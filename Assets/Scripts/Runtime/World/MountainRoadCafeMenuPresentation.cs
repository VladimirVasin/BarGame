using System;
using System.Collections.Generic;
using BarPromenade.Runtime.World;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The physical open booklet on the mountain-cafe counter. The authored
    /// mesh carries the book, dock and page anchors; runtime adds localized
    /// world lettering and moves the prop between the attendant's hand and
    /// counter without ever turning the choices into a screen-space list.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeMenuPresentation : MonoBehaviour
    {
        public const string PropName = "Menu.Hero";
        public const string DockAnchorName = "MenuDock.Hero";
        public const string SelectionAnchorName = "MenuText.Selection";

        private const string ItemAnchorPrefix = "MenuText.Item.";
        private const float TextLiftMeters = 0.0015f;
        private const float ItemFontSize = 0.15f;
        private const float MarkerGapMeters = 0.016f;

        /// <summary>
        /// The mark is set larger than the lines it points at because it is
        /// a bullet, not a letter: at the line's own size the glyph is a
        /// fifth of an em and comes out a speck across the margin. Bold on
        /// the chosen row stays the primary cue.
        /// </summary>
        private const float MarkerFontSize = 0.24f;

        private static readonly IReadOnlyList<string> ItemKeys =
            MountainRoadCafeMenuItemIds.Ordered;

        private static readonly Color Ink =
            new Color(0.055f, 0.040f, 0.025f, 1f);

        [SerializeField] private MountainRoadCafeAssetRegistry environment;
        [SerializeField] private MountainRoadCafeCastController cast;
        [SerializeField] private Transform propRoot;
        [SerializeField] private Transform gripAnchor;
        [SerializeField] private Transform dockAnchor;
        [SerializeField] private Renderer[] propRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private TMP_Text[] itemLines =
            Array.Empty<TMP_Text>();
        [SerializeField] private TMP_Text selectionMarker;
        [SerializeField] private CounterMenuPageView pageView;

        private readonly Vector3[] itemAnchorLocalPositions =
            new Vector3[3];
        private Vector3 gripLocalPosition;
        private Quaternion gripLocalRotation;
        private Vector3 pageNormalLocal;
        private Vector3 pageUpLocal;
        private Vector3 pageRightLocal;
        private Vector3 dockPosition;
        private Quaternion dockRotation;
        private Vector3 placementStartPosition;
        private Quaternion placementStartRotation;
        private bool hasPlacementStart;
        private bool textVisible;
        private CounterMenuPropMotion propMotion;

        public bool IsConfigured { get; private set; }
        public bool IsVisible { get; private set; }
        public bool IsPlaced { get; private set; }
        public bool IsRestingOnCounter { get; private set; }
        public int SelectedIndex { get; private set; }
        public bool IsConfirmed { get; private set; }
        public IReadOnlyList<TMP_Text> ItemLines => itemLines;
        public TMP_Text SelectionMarker => selectionMarker;
        public Transform PropRoot => propRoot;
        public Transform GripAnchor => gripAnchor;
        public Transform DockAnchor => dockAnchor;
        public CounterMenuPageView Page => pageView;

        public bool RestOnCounter()
        {
            if (!IsConfigured || !IsPlaced)
            {
                return false;
            }

            SnapToDock();
            IsPlaced = true;
            IsRestingOnCounter = true;
            if (pageView != null)
            {
                pageView.SetRestingVisible(true);
                IsVisible = true;
                textVisible = false;
            }
            else
            {
                SetVisible(true, false);
            }

            return true;
        }

        public bool ReopenOnCounter()
        {
            if (!IsConfigured || !IsPlaced || !IsRestingOnCounter)
            {
                return false;
            }

            SnapToDock();
            IsRestingOnCounter = false;
            SetVisible(true, true);
            return true;
        }

        public bool IsLookingAtRestingMenu(Camera camera)
        {
            return IsRestingOnCounter &&
                   pageView != null &&
                   pageView.IsLookingAtRestingProp(camera);
        }

        public Pose ResolveCameraFocusPose(Vector3 viewerPosition)
        {
            if (pageView != null)
            {
                return pageView.ResolveCameraFocusPose(viewerPosition);
            }

            if (!IsConfigured || propRoot == null)
            {
                throw new InvalidOperationException(
                    "The cafe menu focus requires a configured world page.");
            }

            MountainRoadCafeSeatViewPlan.EvaluateMenuCamera(
                dockPosition,
                dockRotation * pageNormalLocal,
                dockRotation * pageUpLocal,
                viewerPosition,
                out Vector3 position,
                out Quaternion rotation);
            return new Pose(position, rotation);
        }

        public static MountainRoadCafeMenuPresentation CreateAndBind(
            MountainRoadCafeAssetRegistry configuredEnvironment,
            MountainRoadCafeCastController configuredCast)
        {
            if (configuredEnvironment == null)
            {
                throw new ArgumentNullException(nameof(configuredEnvironment));
            }

            if (configuredCast == null || !configuredCast.IsInitialized ||
                configuredCast.AttendantMenuHandSocket == null)
            {
                throw new ArgumentException(
                    "Cafe menu requires the initialized attendant cast.",
                    nameof(configuredCast));
            }

            if (!configuredEnvironment.TryGetProp(
                    PropName,
                    out MountainRoadCafeDynamicPropBinding prop) ||
                prop == null ||
                prop.PropRoot == null ||
                prop.GripAnchor == null ||
                prop.Renderers.Count != 2)
            {
                throw new InvalidOperationException(
                    "The authored cafe menu prop is incomplete.");
            }

            Transform dock = RequireAnchor(
                configuredEnvironment,
                DockAnchorName);
            var presentation = configuredEnvironment.gameObject.AddComponent<
                MountainRoadCafeMenuPresentation>();
            presentation.Configure(
                configuredEnvironment,
                configuredCast,
                prop,
                dock);
            return presentation;
        }

        public void SetSelection(int selectedIndex, bool confirmed)
        {
            if (pageView != null)
            {
                pageView.SetSelection(selectedIndex, confirmed);
                SelectedIndex = pageView.SelectedIndex;
                IsConfirmed = confirmed;
                return;
            }

            if (!IsConfigured || itemLines.Length == 0)
            {
                return;
            }

            SelectedIndex = Mathf.Clamp(
                selectedIndex,
                0,
                itemLines.Length - 1);
            IsConfirmed = confirmed;
            for (int index = 0; index < itemLines.Length; index++)
            {
                if (itemLines[index] != null)
                {
                    itemLines[index].fontStyle = index == SelectedIndex
                        ? FontStyles.Bold
                        : FontStyles.Normal;
                }
            }

            if (selectionMarker == null)
            {
                return;
            }

            selectionMarker.text = confirmed ? "X" : "•";
            PlaceSelectionMarker();
        }

        /// <summary>
        /// The mark is set beside the chosen line's own left edge rather
        /// than at a fixed margin: the rows are centred and their lengths
        /// differ by four centimetres, so a fixed mark drifts away from the
        /// short ones and reads as a speck on the paper instead of as a
        /// cursor pointing at a dish.
        /// </summary>
        private void PlaceSelectionMarker()
        {
            TMP_Text chosen = itemLines[SelectedIndex];
            if (chosen == null)
            {
                return;
            }

            chosen.ForceMeshUpdate();
            float halfLine = chosen.GetRenderedValues(false).x * 0.5f;
            selectionMarker.transform.localPosition =
                itemAnchorLocalPositions[SelectedIndex] +
                (pageNormalLocal * TextLiftMeters) -
                (pageRightLocal * (halfLine + MarkerGapMeters));
        }

        private void Configure(
            MountainRoadCafeAssetRegistry configuredEnvironment,
            MountainRoadCafeCastController configuredCast,
            MountainRoadCafeDynamicPropBinding prop,
            Transform configuredDock)
        {
            environment = configuredEnvironment;
            cast = configuredCast;
            propRoot = prop.PropRoot;
            gripAnchor = prop.GripAnchor;
            dockAnchor = configuredDock;
            propRenderers = new Renderer[prop.Renderers.Count];
            for (int index = 0; index < prop.Renderers.Count; index++)
            {
                propRenderers[index] = prop.Renderers[index];
            }

            dockPosition = propRoot.position;
            dockRotation = propRoot.rotation;
            ResolvePageBasis(configuredEnvironment);
            gripLocalPosition = propRoot.InverseTransformPoint(
                gripAnchor.position);
            gripLocalRotation = Quaternion.Inverse(propRoot.rotation) *
                                gripAnchor.rotation;
            propMotion = new CounterMenuPropMotion(
                propRoot,
                gripAnchor,
                new Pose(dockPosition, dockRotation),
                cast.AttendantMenuHandSocket);
            BuildPageText();
            IsConfigured = true;
            SnapToDock();
            SetVisible(false, false);
            SetSelection(0, false);
        }

        private static string ItemAnchorName(int index)
        {
            return ItemAnchorPrefix + index.ToString("00");
        }

        /// <summary>
        /// The page plane, measured from the authored anchors themselves.
        ///
        /// Every anchor also carries a `unity_local_forward` / `_up` pair,
        /// but those are written in UNITY axes while the imported model
        /// root's own local space is the model's - pushing them through
        /// <c>ModelRoot.TransformDirection</c> lands 89.5 degrees away, and
        /// the lettering stood upright INSIDE the paper instead of lying on
        /// it, each line showing only the sliver that cleared the page.
        /// Three anchors that are not in a line pin the same plane exactly
        /// and stay right through any re-export: the selection mark and item
        /// 00 share a line, items 00 and 01 share a column.
        /// </summary>
        private void ResolvePageBasis(
            MountainRoadCafeAssetRegistry registry)
        {
            Vector3 lineStart = RequireAnchorBinding(
                registry,
                SelectionAnchorName).Anchor.position;
            Vector3 firstItem = RequireAnchorBinding(
                registry,
                ItemAnchorName(0)).Anchor.position;
            Vector3 secondItem = RequireAnchorBinding(
                registry,
                ItemAnchorName(1)).Anchor.position;
            Vector3 right = (firstItem - lineStart).normalized;
            Vector3 up = Vector3.ProjectOnPlane(
                firstItem - secondItem,
                right).normalized;
            if (right.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f)
            {
                throw new InvalidOperationException(
                    "The authored cafe menu anchors do not span a page.");
            }

            Vector3 normal = Vector3.Cross(up, right).normalized;
            Quaternion inverseDockRotation = Quaternion.Inverse(dockRotation);
            pageRightLocal = inverseDockRotation * right;
            pageUpLocal = inverseDockRotation * up;
            pageNormalLocal = inverseDockRotation * normal;
        }

        private void BuildPageText()
        {
            if (TryBuildSharedPageText())
            {
                return;
            }

            TMP_FontAsset font = CemeteryPlaqueFont.Get();
            if (font == null)
            {
                itemLines = Array.Empty<TMP_Text>();
                return;
            }

            itemLines = new TMP_Text[ItemKeys.Count];
            for (int index = 0; index < ItemKeys.Count; index++)
            {
                string anchorName = ItemAnchorName(index);
                MountainRoadCafeAnchorBinding binding =
                    RequireAnchorBinding(environment, anchorName);
                itemAnchorLocalPositions[index] =
                    propRoot.InverseTransformPoint(binding.Anchor.position);
                // Centred, not left: the item anchors sit on the RIGHT
                // page's own centre line, so a left-aligned box hangs its
                // text off toward the spine - crowding the gutter, leaving
                // eight blank centimetres at the outer edge, and printing
                // the first letter straight over the selection mark that
                // the margin anchor exists to hold.
                itemLines[index] = CreateText(
                    anchorName,
                    LocalizationService.Get(ItemKeys[index]),
                    font,
                    ItemFontSize,
                    new Vector2(0.195f, 0.044f),
                    TextAlignmentOptions.Center);
            }

            selectionMarker = CreateText(
                SelectionAnchorName,
                "•",
                font,
                MarkerFontSize,
                new Vector2(0.028f, 0.044f),
                TextAlignmentOptions.Center);
        }

        private bool TryBuildSharedPageText()
        {
            Vector3 right = dockRotation * pageRightLocal;
            Vector3 up = dockRotation * pageUpLocal;
            Vector3 normal = dockRotation * pageNormalLocal;
            var anchors = new CounterMenuPageTextAnchor[ItemKeys.Count];
            var lines = new string[ItemKeys.Count];
            for (int index = 0; index < ItemKeys.Count; index++)
            {
                MountainRoadCafeAnchorBinding binding =
                    RequireAnchorBinding(
                        environment,
                        ItemAnchorName(index));
                anchors[index] = new CounterMenuPageTextAnchor(
                    binding.Anchor,
                    right,
                    up,
                    normal);
                lines[index] = LocalizationService.Get(ItemKeys[index]);
            }

            Transform selection = RequireAnchorBinding(
                environment,
                SelectionAnchorName).Anchor;
            pageView = gameObject.AddComponent<CounterMenuPageView>();
            pageView.Initialize(
                propRoot,
                propRenderers,
                anchors,
                selection,
                lines,
                dockPosition,
                normal,
                up,
                CounterMenuPageStyle.Cafe);
            itemLines = new TMP_Text[pageView.ItemLines.Count];
            for (int index = 0; index < itemLines.Length; index++)
            {
                itemLines[index] = pageView.ItemLines[index];
            }

            selectionMarker = pageView.SelectionMarker;
            return true;
        }

        private TMP_Text CreateText(
            string anchorName,
            string value,
            TMP_FontAsset font,
            float fontSize,
            Vector2 size,
            TextAlignmentOptions alignment)
        {
            MountainRoadCafeAnchorBinding binding =
                RequireAnchorBinding(environment, anchorName);
            Vector3 normal = dockRotation * pageNormalLocal;
            Vector3 up = dockRotation * pageUpLocal;
            var host = new GameObject(anchorName + " Text");
            host.transform.SetPositionAndRotation(
                binding.Anchor.position + normal * TextLiftMeters,
                Quaternion.LookRotation(-normal, up));
            host.transform.SetParent(propRoot, true);
            var text = host.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontSizeMin = fontSize * 0.62f;
            text.fontSizeMax = fontSize;
            text.enableAutoSizing = true;
            text.color = Ink;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.text = value ?? string.Empty;
            text.rectTransform.sizeDelta = size;
            Renderer renderer = text.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return text;
        }

        private void LateUpdate()
        {
            RefreshFromServiceFrame();
        }

        /// <summary>
        /// Applies the cast's current deterministic service frame. Runtime
        /// invokes this after the animated hand has been sampled; focused
        /// tests can also refresh immediately after manually stepping time.
        /// </summary>
        public void RefreshFromServiceFrame()
        {
            if (!IsConfigured || cast == null)
            {
                return;
            }

            MountainRoadCafeServiceFrame frame = cast.ServiceFrame;
            switch (frame.Phase)
            {
                case MountainRoadCafeServicePhase.WalkToHero:
                    IsPlaced = false;
                    SetCarriedClosedVisible();
                    AttachToHand();
                    RememberPlacementStart();
                    break;
                case MountainRoadCafeServicePhase.PlaceMenu:
                    IsPlaced = false;
                    SetVisible(true, false);
                    PlaceFromCarry(frame.PhaseNormalized);
                    break;
                case MountainRoadCafeServicePhase.TakeMenu:
                    IsPlaced = false;
                    SetRestingOrOpenVisible();
                    TakeToHand(frame.PhaseNormalized);
                    break;
                case MountainRoadCafeServicePhase.CarryMenuBack:
                    IsPlaced = false;
                    SetRestingOrOpenVisible();
                    AttachToHand();
                    break;
                default:
                    if (frame.HeroMenuPlaced)
                    {
                        IsPlaced = true;
                        SnapToDock();
                        SetRestingOrOpenVisible();
                    }
                    else
                    {
                        IsPlaced = false;
                        IsRestingOnCounter = false;
                        hasPlacementStart = false;
                        SnapToDock();
                        SetVisible(false, false);
                    }
                    break;
            }
        }

        private void SetRestingOrOpenVisible()
        {
            if (IsRestingOnCounter && pageView != null)
            {
                pageView.SetRestingVisible(true);
                IsVisible = true;
                textVisible = false;
                return;
            }

            SetVisible(true, !IsRestingOnCounter);
        }

        private void SetCarriedClosedVisible()
        {
            if (pageView != null)
            {
                pageView.SetRestingVisible(true);
                IsVisible = true;
                textVisible = false;
                return;
            }

            SetVisible(true, false);
        }

        private void AttachToHand()
        {
            if (propMotion != null)
            {
                propMotion.AttachToCarrier();
                return;
            }

            Transform hand = cast.AttendantMenuHandSocket;
            if (hand == null)
            {
                SnapToDock();
                return;
            }

            ResolveRootForGrip(
                hand.position,
                hand.rotation,
                out Vector3 position,
                out Quaternion rotation);
            propRoot.SetPositionAndRotation(position, rotation);

            // Imported FBX hierarchies can carry a small scale/axis
            // conversion residue. Finish against the authored contact
            // transform itself so the visible booklet cannot hover beside
            // the attendant's hand while it is being carried.
            Quaternion rotationCorrection = hand.rotation *
                Quaternion.Inverse(gripAnchor.rotation);
            propRoot.rotation = rotationCorrection * propRoot.rotation;
            propRoot.position += hand.position - gripAnchor.position;
        }

        private void RememberPlacementStart()
        {
            if (propMotion != null)
            {
                propMotion.BeginDelivery();
                return;
            }

            placementStartPosition = propRoot.position;
            placementStartRotation = propRoot.rotation;
            hasPlacementStart = true;
        }

        private void PlaceFromCarry(float phaseNormalized)
        {
            float placementNormalized = MountainRoadCafeServiceTimeline
                .ResolveMenuPlacementNormalized(phaseNormalized);
            if (propMotion != null)
            {
                propMotion.EvaluateDelivery(placementNormalized);
                return;
            }

            if (placementNormalized <= 0f)
            {
                AttachToHand();
                RememberPlacementStart();
                return;
            }

            if (!hasPlacementStart)
            {
                AttachToHand();
                RememberPlacementStart();
            }

            float amount = Mathf.SmoothStep(
                0f,
                1f,
                placementNormalized);
            propRoot.SetPositionAndRotation(
                Vector3.Lerp(placementStartPosition, dockPosition, amount),
                Quaternion.Slerp(
                    placementStartRotation,
                    dockRotation,
                    amount));
        }

        private void TakeToHand(float phaseNormalized)
        {
            if (propMotion != null)
            {
                propMotion.EvaluateRetrieval(
                    MountainRoadCafeServiceTimeline
                        .ResolveMenuPickupNormalized(phaseNormalized));
                return;
            }

            Transform hand = cast.AttendantMenuHandSocket;
            if (hand == null)
            {
                SnapToDock();
                return;
            }

            ResolveRootForGrip(
                hand.position,
                hand.rotation,
                out Vector3 carriedPosition,
                out Quaternion carriedRotation);
            float amount = Mathf.SmoothStep(
                0f,
                1f,
                MountainRoadCafeServiceTimeline
                    .ResolveMenuPickupNormalized(phaseNormalized));
            propRoot.SetPositionAndRotation(
                Vector3.Lerp(dockPosition, carriedPosition, amount),
                Quaternion.Slerp(dockRotation, carriedRotation, amount));
        }

        private void ResolveRootForGrip(
            Vector3 gripPosition,
            Quaternion gripRotation,
            out Vector3 rootPosition,
            out Quaternion rootRotation)
        {
            rootRotation = gripRotation * Quaternion.Inverse(
                gripLocalRotation);
            Vector3 scaledGripOffset = Vector3.Scale(
                gripLocalPosition,
                propRoot.lossyScale);
            rootPosition = gripPosition -
                rootRotation * scaledGripOffset;
        }

        private void SnapToDock()
        {
            if (propMotion != null)
            {
                propMotion.SnapToDock();
                return;
            }

            if (propRoot != null)
            {
                propRoot.SetPositionAndRotation(dockPosition, dockRotation);
            }
        }

        private void SetVisible(bool visible, bool showText)
        {
            if (pageView != null)
            {
                pageView.SetVisible(visible, showText);
                IsVisible = visible;
                textVisible = showText;
                return;
            }

            for (int index = 0; index < propRenderers.Length; index++)
            {
                if (propRenderers[index] != null)
                {
                    propRenderers[index].enabled = visible;
                }
            }

            IsVisible = visible;
            if (textVisible == showText)
            {
                return;
            }

            textVisible = showText;
            for (int index = 0; index < itemLines.Length; index++)
            {
                SetTextRenderer(itemLines[index], showText);
            }

            SetTextRenderer(selectionMarker, showText);
        }

        private static void SetTextRenderer(TMP_Text text, bool visible)
        {
            Renderer renderer = text != null
                ? text.GetComponent<Renderer>()
                : null;
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }

        private static Transform RequireAnchor(
            MountainRoadCafeAssetRegistry registry,
            string anchorName)
        {
            if (registry.TryGetAnchor(anchorName, out Transform anchor) &&
                anchor != null)
            {
                return anchor;
            }

            throw new InvalidOperationException(
                $"Authored cafe anchor '{anchorName}' is missing.");
        }

        private static MountainRoadCafeAnchorBinding RequireAnchorBinding(
            MountainRoadCafeAssetRegistry registry,
            string anchorName)
        {
            if (registry.TryGetAnchorBinding(
                    anchorName,
                    out MountainRoadCafeAnchorBinding binding) &&
                binding != null && binding.Anchor != null)
            {
                return binding;
            }

            throw new InvalidOperationException(
                $"Authored cafe anchor binding '{anchorName}' is missing.");
        }

        private void OnDisable()
        {
            if (!IsConfigured)
            {
                return;
            }

            SnapToDock();
            if (IsPlaced)
            {
                SetRestingOrOpenVisible();
            }
            else
            {
                SetVisible(false, false);
            }
        }
    }
}
