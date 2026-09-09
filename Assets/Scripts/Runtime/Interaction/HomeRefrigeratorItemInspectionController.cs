using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum HomeRefrigeratorItemAction
    {
        Take = 0,
        Use = 1,
        Back = 2
    }

    /// <summary>
    /// Owns pointer selection and the nested item-inspection presentation while
    /// the refrigerator's outer modal interaction remains in its open hold.
    /// It deliberately does not acquire another modal lock.
    /// </summary>
    [DefaultExecutionOrder(270)]
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorItemInspectionController :
        MonoBehaviour
    {
        public const string TakeActionKey =
            WorldItemFoundScreen.TakeActionKey;
        public const string UseActionKey =
            "home.refrigerator.action.use";
        public const string BackActionKey =
            "home.refrigerator.action.back";
        public const string UnavailableFeedbackKey =
            "home.refrigerator.action.unavailable";
        private const float MaximumRayDistance = 12f;
        private const float PointerMoveThresholdSquared = 0.25f;

        private static readonly Color HoverTint =
            new Color(1f, 0.83f, 0.43f, 1f);
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private readonly RaycastHit[] raycastBuffer =
            new RaycastHit[24];
        private readonly Dictionary<Renderer, RendererColorState>
            rendererColors =
                new Dictionary<Renderer, RendererColorState>();
        private MaterialPropertyBlock properties;

        private Camera targetCamera;
        private HomeRefrigeratorView refrigerator;
        private WorldItemInspectionTimeline timeline;
        private HomeRefrigeratorItemInspectionView inspectionView;
        private HomeRefrigeratorItemView hoveredItem;
        private HomeRefrigeratorItemView activeItem;
        private HomeRefrigeratorItemDefinition activeDefinition;
        private WorldItemInspectionPresenter presenter;
        private bool savedSelectionColliderEnabled;
        private Vector2 lastPointerPosition;
        private Vector2 hoverScreenPosition;
        private int keyboardItemIndex = -1;
        private int selectedActionIndex;
        private bool hasPointerPosition;
        private bool keyboardSelectionActive;
        private bool browsingEnabled;
        private string feedbackKey = string.Empty;

        public bool IsInitialized { get; private set; }
        public bool BrowsingEnabled => browsingEnabled;
        public bool IsActive => timeline != null && timeline.IsActive;
        public bool IsInspecting =>
            timeline != null && timeline.IsInspecting;
        public HomeRefrigeratorItemView HoveredItem => hoveredItem;
        public HomeRefrigeratorItemView ActiveItem => activeItem;
        public WorldItemInspectionTimeline Timeline => timeline;
        public Transform PresentationPivot => presenter?.Pivot;
        public Renderer BackdropRenderer => presenter?.BackdropRenderer;
        public Vector2 HoverScreenPosition => hoverScreenPosition;
        public int SelectedActionIndex => selectedActionIndex;
        public string FeedbackKey => feedbackKey;
        public HomeRefrigeratorItemDefinition ActiveDefinition =>
            activeDefinition;

        public void Initialize(
            Camera inspectionCamera,
            HomeRefrigeratorView refrigeratorView)
        {
            if (inspectionCamera == null)
            {
                throw new ArgumentNullException(nameof(inspectionCamera));
            }

            if (refrigeratorView == null)
            {
                throw new ArgumentNullException(
                    nameof(refrigeratorView));
            }

            CancelAndRestore();
            targetCamera = inspectionCamera;
            refrigerator = refrigeratorView;
            timeline = new WorldItemInspectionTimeline();
            properties = new MaterialPropertyBlock();
            CaptureRendererColors();
            presenter?.Dispose();
            presenter = new WorldItemInspectionPresenter(
                targetCamera,
                "Home Refrigerator Item Inspection");
            inspectionView =
                GetComponent<HomeRefrigeratorItemInspectionView>();
            if (inspectionView == null)
            {
                inspectionView =
                    gameObject.AddComponent<
                        HomeRefrigeratorItemInspectionView>();
            }

            inspectionView.Initialize(this);
            browsingEnabled = false;
            selectedActionIndex = 0;
            keyboardItemIndex = -1;
            keyboardSelectionActive = false;
            feedbackKey = string.Empty;
            IsInitialized = true;
        }

        public void SetBrowsingEnabled(bool enabled)
        {
            enabled = enabled && isActiveAndEnabled;
            if (browsingEnabled == enabled)
            {
                return;
            }

            browsingEnabled = enabled;
            if (enabled)
            {
                return;
            }

            CancelAndRestore();
            keyboardItemIndex = -1;
            keyboardSelectionActive = false;
            hasPointerPosition = false;
        }

        /// <summary>
        /// Handles one frame of nested modal input. The return value reports
        /// that an E/Enter/Escape/gamepad action was consumed and therefore
        /// must not also close the refrigerator in the outer interaction.
        /// </summary>
        public bool HandleInput()
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                !browsingEnabled)
            {
                return false;
            }

            if (IsActive)
            {
                return HandleActiveInput();
            }

            RefreshPointerHover(false);
            bool changedKeyboardSelection =
                TryChangeKeyboardItemSelection();
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                RefreshPointerHover(true);
                if (hoveredItem != null)
                {
                    TryBeginInspection(hoveredItem);
                }

                return true;
            }

            if (WasConfirmPressed() &&
                keyboardSelectionActive &&
                hoveredItem != null)
            {
                TryBeginInspection(hoveredItem);
                return true;
            }

            return changedKeyboardSelection && WasCloseLikePressed();
        }

        public void AdvanceInspection(float unscaledDeltaTime)
        {
            if (!IsInitialized || timeline == null)
            {
                return;
            }

            if (!timeline.IsActive)
            {
                if (activeItem != null)
                {
                    RestoreActiveItem();
                }

                return;
            }

            bool wasReturning =
                timeline.Phase ==
                WorldItemInspectionPhase.FlyingOut;
            timeline.Advance(unscaledDeltaTime);
            ApplyInspectionFrame(timeline.CurrentFrame);
            if (wasReturning && timeline.IsBrowsing)
            {
                RestoreActiveItem();
            }
        }

        public bool TryBeginInspection(HomeRefrigeratorItemView item)
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                !browsingEnabled ||
                item == null ||
                !IsRegistered(item) ||
                !timeline.CanBeginInspection)
            {
                return false;
            }

            HomeRefrigeratorItemDefinition definition =
                HomeRefrigeratorItemCatalog.Get(item.Kind);
            Bounds bounds = CalculateWorldBounds(item);
            ClearHoveredItem();

            activeItem = item;
            activeDefinition = definition;
            savedSelectionColliderEnabled =
                item.SelectionCollider != null &&
                item.SelectionCollider.enabled;
            if (item.SelectionCollider != null)
            {
                item.SelectionCollider.enabled = false;
            }

            presenter.Begin(
                item.OriginalRoot,
                bounds,
                definition.PreviewLocalRotation,
                definition.PreviewScale);
            feedbackKey = string.Empty;
            selectedActionIndex = 0;
            if (!timeline.BeginInspection())
            {
                RestoreActiveItem();
                return false;
            }

            ApplyInspectionFrame(timeline.CurrentFrame);
            return true;
        }

        public bool RequestReturn()
        {
            if (!IsInitialized || timeline == null || !timeline.IsActive)
            {
                return false;
            }

            if (timeline.Phase ==
                WorldItemInspectionPhase.FlyingOut)
            {
                return true;
            }

            bool began = timeline.BeginReturn();
            if (began)
            {
                feedbackKey = string.Empty;
                ApplyInspectionFrame(timeline.CurrentFrame);
                if (timeline.IsBrowsing)
                {
                    RestoreActiveItem();
                }
            }

            return began;
        }

        public bool SelectAction(int index)
        {
            if (!IsInspecting || index < 0 || index > 2)
            {
                return false;
            }

            selectedActionIndex = index;
            return true;
        }

        public bool InvokeAction(HomeRefrigeratorItemAction action)
        {
            if (!IsInspecting)
            {
                return false;
            }

            switch (action)
            {
                case HomeRefrigeratorItemAction.Take:
                    selectedActionIndex = (int)action;
                    return TakeActiveItem();
                case HomeRefrigeratorItemAction.Use:
                    feedbackKey = UnavailableFeedbackKey;
                    selectedActionIndex = (int)action;
                    return true;
                case HomeRefrigeratorItemAction.Back:
                    selectedActionIndex = (int)action;
                    return RequestReturn();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        action,
                        "Unknown refrigerator item action.");
            }
        }

        private bool TakeActiveItem()
        {
            if (activeItem == null || refrigerator == null ||
                !HomeRefrigeratorInventoryAdapter.TryGetInventoryItem(
                    activeItem.Kind,
                    out InventoryItemId inventoryItemId))
            {
                feedbackKey = UnavailableFeedbackKey;
                return true;
            }

            string sourceId =
                HomeRefrigeratorInventoryAdapter.GetSourceId(
                    activeItem.SlotId);
            if (!GameSessionState.TryCollectWorldItem(
                    sourceId,
                    inventoryItemId))
            {
                feedbackKey = UnavailableFeedbackKey;
                return true;
            }

            HomeRefrigeratorItemView takenItem = activeItem;
            if (!refrigerator.RemoveItem(takenItem))
            {
                throw new InvalidOperationException(
                    "The inspected refrigerator item was not registered.");
            }

            timeline.Cancel();
            presenter?.Detach();
            activeItem = null;
            activeDefinition = default;
            feedbackKey = string.Empty;
            selectedActionIndex = 0;
            keyboardItemIndex = -1;
            keyboardSelectionActive = false;
            hasPointerPosition = false;
            takenItem.gameObject.SetActive(false);
            DestroyOwnedObject(takenItem.gameObject);
            CaptureRendererColors();
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool CancelAndRestore()
        {
            bool hadState =
                hoveredItem != null ||
                activeItem != null ||
                timeline != null && timeline.IsActive;
            ClearHoveredItem();
            timeline?.Cancel();
            RestoreActiveItem();
            feedbackKey = string.Empty;
            selectedActionIndex = 0;
            return hadState;
        }

        private bool HandleActiveInput()
        {
            bool backPressed = WasBackPressed();
            if (backPressed)
            {
                RequestReturn();
                return true;
            }

            if (!IsInspecting)
            {
                if (WasCloseLikePressed())
                {
                    RequestReturn();
                    return true;
                }

                return false;
            }

            int horizontal = ReadActionNavigation();
            if (horizontal != 0)
            {
                selectedActionIndex =
                    WrapIndex(selectedActionIndex + horizontal, 3);
                feedbackKey = string.Empty;
            }

            if (WasConfirmPressed())
            {
                InvokeAction(
                    (HomeRefrigeratorItemAction)selectedActionIndex);
                return true;
            }

            return horizontal != 0;
        }

        private void RefreshPointerHover(bool force)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || targetCamera == null)
            {
                if (!keyboardSelectionActive)
                {
                    SetHoveredItem(null, Vector2.zero);
                }

                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            bool moved =
                !hasPointerPosition ||
                (pointer - lastPointerPosition).sqrMagnitude >=
                    PointerMoveThresholdSquared;
            if (!force && !moved)
            {
                return;
            }

            hasPointerPosition = true;
            lastPointerPosition = pointer;
            keyboardSelectionActive = false;
            HomeRefrigeratorItemView selected =
                FindPointedItem(pointer);
            SetHoveredItem(selected, pointer);
        }

        private HomeRefrigeratorItemView FindPointedItem(
            Vector2 screenPosition)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            int count = Physics.RaycastNonAlloc(
                ray,
                raycastBuffer,
                MaximumRayDistance,
                ~0,
                QueryTriggerInteraction.Collide);
            HomeRefrigeratorItemView nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = raycastBuffer[index];
                HomeRefrigeratorItemView item =
                    hit.collider != null
                        ? hit.collider.GetComponentInParent<
                            HomeRefrigeratorItemView>()
                        : null;
                if (item == null ||
                    !IsRegistered(item) ||
                    hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearest = item;
                nearestDistance = hit.distance;
            }

            return nearest;
        }

        private bool TryChangeKeyboardItemSelection()
        {
            int direction = ReadItemNavigation();
            if (direction == 0 || refrigerator.Items.Count == 0)
            {
                return false;
            }

            keyboardSelectionActive = true;
            if (keyboardItemIndex < 0)
            {
                keyboardItemIndex = direction > 0
                    ? 0
                    : refrigerator.Items.Count - 1;
            }
            else
            {
                keyboardItemIndex = WrapIndex(
                    keyboardItemIndex + direction,
                    refrigerator.Items.Count);
            }

            HomeRefrigeratorItemView item =
                refrigerator.Items[keyboardItemIndex];
            Bounds bounds = CalculateWorldBounds(item);
            Vector3 screen = targetCamera.WorldToScreenPoint(bounds.center);
            SetHoveredItem(item, new Vector2(screen.x, screen.y));
            return true;
        }

        private void SetHoveredItem(
            HomeRefrigeratorItemView item,
            Vector2 screenPosition)
        {
            if (ReferenceEquals(hoveredItem, item))
            {
                hoverScreenPosition = screenPosition;
                return;
            }

            ClearHoveredItem();
            hoveredItem = item;
            hoverScreenPosition = screenPosition;
            if (hoveredItem == null)
            {
                return;
            }

            ApplyHoverColors(hoveredItem, true);
        }

        private void ClearHoveredItem()
        {
            if (hoveredItem != null)
            {
                ApplyHoverColors(hoveredItem, false);
            }

            hoveredItem = null;
            hoverScreenPosition = Vector2.zero;
        }

        private void ApplyHoverColors(
            HomeRefrigeratorItemView item,
            bool highlighted)
        {
            for (int index = 0; index < item.Renderers.Count; index++)
            {
                Renderer itemRenderer = item.Renderers[index];
                if (itemRenderer == null ||
                    !rendererColors.TryGetValue(
                        itemRenderer,
                        out RendererColorState original))
                {
                    continue;
                }

                properties.Clear();
                itemRenderer.GetPropertyBlock(properties);
                properties.SetColor(
                    BaseColorId,
                    highlighted
                        ? Tint(original.BaseColor)
                        : original.BaseColor);
                properties.SetColor(
                    ColorId,
                    highlighted
                        ? Tint(original.Color)
                        : original.Color);
                itemRenderer.SetPropertyBlock(properties);
            }
        }

        private void CaptureRendererColors()
        {
            rendererColors.Clear();
            for (int itemIndex = 0;
                 itemIndex < refrigerator.Items.Count;
                 itemIndex++)
            {
                HomeRefrigeratorItemView item =
                    refrigerator.Items[itemIndex];
                for (int rendererIndex = 0;
                     rendererIndex < item.Renderers.Count;
                     rendererIndex++)
                {
                    Renderer itemRenderer =
                        item.Renderers[rendererIndex];
                    if (itemRenderer == null ||
                        rendererColors.ContainsKey(itemRenderer))
                    {
                        continue;
                    }

                    properties.Clear();
                    itemRenderer.GetPropertyBlock(properties);
                    rendererColors.Add(
                        itemRenderer,
                        new RendererColorState(
                            ReadColor(itemRenderer, BaseColorId),
                            ReadColor(itemRenderer, ColorId)));
                }
            }
        }

        private Color ReadColor(Renderer itemRenderer, int propertyId)
        {
            Color color = properties.GetColor(propertyId);
            if (color != default)
            {
                return color;
            }

            Material material = itemRenderer.sharedMaterial;
            return material != null && material.HasProperty(propertyId)
                ? material.GetColor(propertyId)
                : Color.white;
        }

        private void ApplyInspectionFrame(
            WorldItemInspectionFrame frame)
        {
            presenter?.Apply(frame);
        }

        private void RestoreActiveItem()
        {
            if (activeItem == null)
            {
                return;
            }

            presenter?.Restore();
            if (activeItem.SelectionCollider != null)
            {
                activeItem.SelectionCollider.enabled =
                    savedSelectionColliderEnabled;
            }

            activeItem = null;
            activeDefinition = default;
            feedbackKey = string.Empty;
            selectedActionIndex = 0;
        }

        private bool IsRegistered(HomeRefrigeratorItemView item)
        {
            if (refrigerator == null || item == null)
            {
                return false;
            }

            for (int index = 0; index < refrigerator.Items.Count; index++)
            {
                if (ReferenceEquals(refrigerator.Items[index], item))
                {
                    return true;
                }
            }

            return false;
        }

        private static Bounds CalculateWorldBounds(
            HomeRefrigeratorItemView item)
        {
            Bounds bounds = item.Renderers[0].bounds;
            for (int index = 1; index < item.Renderers.Count; index++)
            {
                bounds.Encapsulate(item.Renderers[index].bounds);
            }

            return bounds;
        }

        private static Color Tint(Color original)
        {
            Color tinted = Color.Lerp(original, HoverTint, 0.34f);
            tinted.a = original.a;
            return tinted;
        }

        private static int ReadItemNavigation()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame)
                {
                    return 1;
                }

                if (keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    return -1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    return 1;
                }

                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    return -1;
                }
            }

            return 0;
        }

        private static int ReadActionNavigation()
        {
            return ReadItemNavigation();
        }

        private static bool WasConfirmPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Interact, GameInputContext.Menu);
        }

        private static bool WasBackPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Cancel, GameInputContext.Menu);
        }

        private static bool WasCloseLikePressed()
        {
            return WasConfirmPressed() || WasBackPressed();
        }

        private static int WrapIndex(int index, int count)
        {
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        private void OnDisable()
        {
            browsingEnabled = false;
            CancelAndRestore();
            keyboardItemIndex = -1;
            keyboardSelectionActive = false;
            hasPointerPosition = false;
        }

        private void OnDestroy()
        {
            CancelAndRestore();
            presenter?.Dispose();
            presenter = null;
            IsInitialized = false;
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private readonly struct RendererColorState
        {
            public RendererColorState(Color baseColor, Color color)
            {
                BaseColor = baseColor;
                Color = color;
            }

            public Color BaseColor { get; }
            public Color Color { get; }
        }
    }
}
