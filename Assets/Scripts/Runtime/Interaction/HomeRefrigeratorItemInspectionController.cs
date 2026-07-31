using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
            "home.refrigerator.action.take";
        public const string UseActionKey =
            "home.refrigerator.action.use";
        public const string BackActionKey =
            "home.refrigerator.action.back";
        public const string UnavailableFeedbackKey =
            "home.refrigerator.action.unavailable";
        private const float PreviewDistance = 0.68f;
        private const float BackdropDistance = 0.82f;
        private const float PreviewHeightFraction = 0.46f;
        private const float PreviewWidthFraction = 0.42f;
        private const float MinimumPreviewScale = 0.55f;
        private const float MaximumPreviewScale = 5.5f;
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
        private HomeRefrigeratorItemInspectionTimeline timeline;
        private HomeRefrigeratorItemInspectionView inspectionView;
        private HomeRefrigeratorItemView hoveredItem;
        private HomeRefrigeratorItemView activeItem;
        private HomeRefrigeratorItemDefinition activeDefinition;
        private Transform presentationPivot;
        private GameObject backdropObject;
        private Renderer backdropRenderer;
        private Transform savedParent;
        private int savedSiblingIndex;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private Vector3 savedLocalScale;
        private bool savedSelectionColliderEnabled;
        private Vector3 pivotStartLocalPosition;
        private Quaternion pivotStartLocalRotation;
        private Vector3 pivotTargetLocalPosition;
        private Quaternion pivotTargetLocalRotation;
        private float pivotTargetScale;
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
        public HomeRefrigeratorItemInspectionTimeline Timeline => timeline;
        public Transform PresentationPivot => presentationPivot;
        public Renderer BackdropRenderer => backdropRenderer;
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
            timeline = new HomeRefrigeratorItemInspectionTimeline();
            properties = new MaterialPropertyBlock();
            CaptureRendererColors();
            EnsurePresentationObjects();
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
                HomeRefrigeratorItemInspectionPhase.FlyingOut;
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
            SaveActiveItemTransform();
            PreparePresentationPivot(bounds, definition);
            savedSelectionColliderEnabled =
                item.SelectionCollider != null &&
                item.SelectionCollider.enabled;
            if (item.SelectionCollider != null)
            {
                item.SelectionCollider.enabled = false;
            }

            item.OriginalRoot.SetParent(presentationPivot, true);
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
                HomeRefrigeratorItemInspectionPhase.FlyingOut)
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

        public bool CancelAndRestore()
        {
            bool hadState =
                hoveredItem != null ||
                activeItem != null ||
                timeline != null && timeline.IsActive;
            ClearHoveredItem();
            timeline?.Cancel();
            RestoreActiveItem();
            HideBackdrop();
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

        private void SaveActiveItemTransform()
        {
            Transform itemRoot = activeItem.OriginalRoot;
            savedParent = itemRoot.parent;
            savedSiblingIndex = itemRoot.GetSiblingIndex();
            savedLocalPosition = itemRoot.localPosition;
            savedLocalRotation = itemRoot.localRotation;
            savedLocalScale = itemRoot.localScale;
        }

        private void PreparePresentationPivot(
            Bounds itemBounds,
            HomeRefrigeratorItemDefinition definition)
        {
            presentationPivot.SetParent(targetCamera.transform, true);
            presentationPivot.position = itemBounds.center;
            presentationPivot.rotation = Quaternion.identity;
            presentationPivot.localScale = Vector3.one;
            pivotStartLocalPosition = presentationPivot.localPosition;
            pivotStartLocalRotation = presentationPivot.localRotation;
            pivotTargetLocalPosition =
                new Vector3(0f, 0.035f, PreviewDistance);
            pivotTargetLocalRotation = definition.PreviewLocalRotation;
            pivotTargetScale = CalculatePreviewScale(
                itemBounds,
                definition.PreviewScale);
        }

        private float CalculatePreviewScale(
            Bounds bounds,
            float authoredScale)
        {
            float viewHeight;
            if (targetCamera.orthographic)
            {
                viewHeight = targetCamera.orthographicSize * 2f;
            }
            else
            {
                viewHeight =
                    2f *
                    PreviewDistance *
                    Mathf.Tan(
                        targetCamera.fieldOfView *
                        Mathf.Deg2Rad *
                        0.5f);
            }

            float viewWidth = viewHeight * targetCamera.aspect;
            float heightScale =
                viewHeight * PreviewHeightFraction /
                Mathf.Max(0.01f, bounds.size.y);
            float widthScale =
                viewWidth * PreviewWidthFraction /
                Mathf.Max(0.01f, bounds.size.x);
            return Mathf.Clamp(
                Mathf.Min(heightScale, widthScale) * authoredScale,
                MinimumPreviewScale,
                MaximumPreviewScale);
        }

        private void ApplyInspectionFrame(
            HomeRefrigeratorItemInspectionFrame frame)
        {
            if (activeItem == null || presentationPivot == null)
            {
                HideBackdrop();
                return;
            }

            float blend = Mathf.Clamp01(frame.ItemBlend);
            presentationPivot.localPosition = Vector3.Lerp(
                pivotStartLocalPosition,
                pivotTargetLocalPosition,
                blend);
            Quaternion rotatingTarget =
                pivotTargetLocalRotation *
                Quaternion.Euler(0f, frame.RotationDegrees, 0f);
            presentationPivot.localRotation = Quaternion.Slerp(
                pivotStartLocalRotation,
                rotatingTarget,
                blend);
            float scale = Mathf.Lerp(1f, pivotTargetScale, blend);
            presentationPivot.localScale = Vector3.one * scale;
            ApplyBackdrop(frame.BackdropAlpha);
        }

        private void ApplyBackdrop(float itemBlend)
        {
            if (backdropRenderer == null || targetCamera == null)
            {
                return;
            }

            float reveal = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.62f, 1f, itemBlend));
            if (reveal <= 0.001f)
            {
                HideBackdrop();
                return;
            }

            UpdateBackdropGeometry();
            backdropObject.SetActive(true);
            properties.Clear();
            backdropRenderer.GetPropertyBlock(properties);
            properties.SetColor(
                BaseColorId,
                new Color(0.005f, 0.004f, 0.008f, 0.86f * reveal));
            backdropRenderer.SetPropertyBlock(properties);
        }

        private void UpdateBackdropGeometry()
        {
            float height;
            if (targetCamera.orthographic)
            {
                height = targetCamera.orthographicSize * 2f;
            }
            else
            {
                height =
                    2f *
                    BackdropDistance *
                    Mathf.Tan(
                        targetCamera.fieldOfView *
                        Mathf.Deg2Rad *
                        0.5f);
            }

            float width = height * targetCamera.aspect;
            backdropObject.transform.localPosition =
                new Vector3(0f, 0f, BackdropDistance);
            backdropObject.transform.localRotation = Quaternion.identity;
            backdropObject.transform.localScale =
                new Vector3(width * 1.08f, height * 1.08f, 1f);
        }

        private void RestoreActiveItem()
        {
            if (activeItem == null)
            {
                return;
            }

            Transform itemRoot = activeItem.OriginalRoot;
            if (itemRoot != null && savedParent != null)
            {
                itemRoot.SetParent(savedParent, false);
                itemRoot.SetSiblingIndex(
                    Mathf.Clamp(
                        savedSiblingIndex,
                        0,
                        Mathf.Max(0, savedParent.childCount - 1)));
                itemRoot.localPosition = savedLocalPosition;
                itemRoot.localRotation = savedLocalRotation;
                itemRoot.localScale = savedLocalScale;
            }

            if (activeItem.SelectionCollider != null)
            {
                activeItem.SelectionCollider.enabled =
                    savedSelectionColliderEnabled;
            }

            activeItem = null;
            activeDefinition = default;
            savedParent = null;
            feedbackKey = string.Empty;
            selectedActionIndex = 0;
            HideBackdrop();
        }

        private void EnsurePresentationObjects()
        {
            if (presentationPivot == null)
            {
                var pivotObject = new GameObject(
                    "Home Refrigerator Item Inspection Pivot");
                pivotObject.hideFlags = HideFlags.DontSave;
                presentationPivot = pivotObject.transform;
                presentationPivot.SetParent(targetCamera.transform, false);
            }

            if (backdropObject != null)
            {
                backdropObject.transform.SetParent(
                    targetCamera.transform,
                    false);
                HideBackdrop();
                return;
            }

            backdropObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdropObject.name =
                "Home Refrigerator Item Inspection Backdrop";
            backdropObject.hideFlags = HideFlags.DontSave;
            backdropObject.transform.SetParent(
                targetCamera.transform,
                false);
            Collider backdropCollider =
                backdropObject.GetComponent<Collider>();
            if (backdropCollider != null)
            {
                backdropCollider.enabled = false;
                DestroyOwnedObject(backdropCollider);
            }

            backdropRenderer = backdropObject.GetComponent<Renderer>();
            backdropRenderer.sharedMaterial =
                HomeBalconyResources.GlassMaterial;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;
            backdropRenderer.lightProbeUsage = LightProbeUsage.Off;
            backdropRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            HideBackdrop();
        }

        private void HideBackdrop()
        {
            if (backdropObject != null)
            {
                backdropObject.SetActive(false);
            }
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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool WasBackPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
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
            DestroyOwnedObject(backdropObject);
            if (presentationPivot != null)
            {
                DestroyOwnedObject(presentationPivot.gameObject);
            }

            backdropObject = null;
            backdropRenderer = null;
            presentationPivot = null;
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
