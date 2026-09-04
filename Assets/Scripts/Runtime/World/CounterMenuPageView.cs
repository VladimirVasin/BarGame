using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// One authored text socket on a physical menu page. The convention is
    /// the same as the mountain-cafe booklet and TextMesh Pro: +X reads to
    /// the right, +Y reads up the page and +Z points into the paper, so the
    /// visible TMP face (-Z) points out toward the reader.
    /// </summary>
    public readonly struct CounterMenuPageTextAnchor
    {
        public CounterMenuPageTextAnchor(
            Transform anchor,
            Vector3 right,
            Vector3 up,
            Vector3 outwardNormal)
        {
            Anchor = anchor;
            Right = right;
            Up = up;
            OutwardNormal = outwardNormal;
        }

        public Transform Anchor { get; }
        public Vector3 Right { get; }
        public Vector3 Up { get; }
        public Vector3 OutwardNormal { get; }

        public static CounterMenuPageTextAnchor FromTransform(
            Transform anchor)
        {
            if (anchor == null)
            {
                throw new ArgumentNullException(nameof(anchor));
            }

            return new CounterMenuPageTextAnchor(
                anchor,
                anchor.right,
                anchor.up,
                -anchor.forward);
        }
    }

    public readonly struct CounterMenuPageStyle
    {
        public CounterMenuPageStyle(
            float itemFontSize,
            float minimumFontScale,
            Vector2 itemBoxSize,
            float markerFontSize,
            Vector2 markerBoxSize,
            float markerGapMeters,
            Color ink)
        {
            ItemFontSize = itemFontSize;
            MinimumFontScale = minimumFontScale;
            ItemBoxSize = itemBoxSize;
            MarkerFontSize = markerFontSize;
            MarkerBoxSize = markerBoxSize;
            MarkerGapMeters = markerGapMeters;
            Ink = ink;
        }

        public float ItemFontSize { get; }
        public float MinimumFontScale { get; }
        public Vector2 ItemBoxSize { get; }
        public float MarkerFontSize { get; }
        public Vector2 MarkerBoxSize { get; }
        public float MarkerGapMeters { get; }
        public Color Ink { get; }

        public static CounterMenuPageStyle Cafe =>
            new CounterMenuPageStyle(
                0.15f,
                0.62f,
                new Vector2(0.195f, 0.044f),
                0.24f,
                new Vector2(0.028f, 0.044f),
                0.016f,
                new Color(0.055f, 0.040f, 0.025f, 1f));

        public static CounterMenuPageStyle Bar =>
            new CounterMenuPageStyle(
                0.12f,
                0.48f,
                new Vector2(0.235f, 0.034f),
                0.20f,
                new Vector2(0.025f, 0.034f),
                0.010f,
                new Color(0.055f, 0.040f, 0.025f, 1f));
    }

    public static class CounterMenuCameraPlan
    {
        public const float FocusDistanceMeters = 0.50f;
        public const float SurfaceLiftMeters = 0.018f;
        public const float FocusFieldOfView = 40f;
        public const float FocusBlendSeconds = 0.45f;

        public static void Evaluate(
            Vector3 menuPosition,
            Vector3 pageNormal,
            Vector3 pageUp,
            Vector3 viewerPosition,
            out Vector3 position,
            out Quaternion rotation)
        {
            Evaluate(
                menuPosition,
                pageNormal,
                pageUp,
                viewerPosition,
                FocusDistanceMeters,
                out position,
                out rotation);
        }

        public static void Evaluate(
            Vector3 menuPosition,
            Vector3 pageNormal,
            Vector3 pageUp,
            Vector3 viewerPosition,
            float focusDistanceMeters,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (float.IsNaN(focusDistanceMeters) ||
                float.IsInfinity(focusDistanceMeters) ||
                focusDistanceMeters <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(focusDistanceMeters),
                    focusDistanceMeters,
                    "The menu focus distance must be finite and positive.");
            }

            if (pageNormal.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The menu view needs a page normal.",
                    nameof(pageNormal));
            }

            pageNormal.Normalize();
            if (Vector3.Dot(pageNormal, Vector3.up) < 0f)
            {
                pageNormal = -pageNormal;
            }

            pageUp = Vector3.ProjectOnPlane(pageUp, pageNormal);
            if (pageUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The menu view needs a page up axis.",
                    nameof(pageUp));
            }

            pageUp.Normalize();
            Vector3 target = menuPosition +
                pageNormal * SurfaceLiftMeters;
            Vector3 towardViewer = viewerPosition - target;
            if (towardViewer.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The menu view needs a distinct viewer position.",
                    nameof(viewerPosition));
            }

            position = target +
                towardViewer.normalized * focusDistanceMeters;
            Vector3 forward = (target - position).normalized;
            Vector3 cameraUp = Vector3.ProjectOnPlane(
                Vector3.up,
                forward);
            if (cameraUp.sqrMagnitude < 0.000001f)
            {
                cameraUp = Vector3.ProjectOnPlane(pageUp, forward);
            }

            if (cameraUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The menu view cannot resolve an upright camera.",
                    nameof(pageUp));
            }

            rotation = Quaternion.LookRotation(
                forward,
                cameraUp.normalized);
        }
    }

    /// <summary>
    /// Shared physical-page implementation. Scene adapters only resolve the
    /// authored prop, sockets and localized line values; this view creates
    /// and places all world text, marker state, camera focus and the opaque
    /// two-leaf hinge used while the booklet physically folds and unfolds.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class CounterMenuPageView : MonoBehaviour
    {
        private const float TextLiftMeters = 0.0015f;
        private const float FoldEndpointTolerance = 0.0001f;
        private const float OpenLeafAngleDegrees = 5.5f;
        private const float ClosedLeftLeafAngleDegrees = 174.5f;
        private const float RightLeafAngleDegrees = -5.5f;
        private const float CoverThicknessMeters = 0.006f;
        private const float PageThicknessMeters = 0.004f;
        private const float CoverGapMeters = 0.006f;
        private const float PageGapMeters = 0.010f;
        private const float PageCoverClearanceMeters = 0.001f;

        public const float FoldDurationSeconds = 0.40f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Transform propRoot;
        [SerializeField] private Renderer[] propRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private TMP_Text[] itemLines =
            Array.Empty<TMP_Text>();
        [SerializeField] private TMP_Text selectionMarker;
        [SerializeField] private Transform restingPropRoot;
        [SerializeField] private Renderer restingPropRenderer;
        [SerializeField] private Transform leftFoldHinge;
        [SerializeField] private Transform rightFoldHinge;

        private Vector3[] rowLocalPositions = Array.Empty<Vector3>();
        private Vector3[] rowRightLocal = Array.Empty<Vector3>();
        private Vector3[] rowUpLocal = Array.Empty<Vector3>();
        private Vector3[] rowNormalLocal = Array.Empty<Vector3>();
        private string[] rowObjectNames = Array.Empty<string>();
        private Vector3 focusLocalPosition;
        private Vector3 focusUpLocal;
        private Vector3 focusNormalLocal;
        private CounterMenuPageStyle style;
        private readonly List<Renderer> restingPropRenderers =
            new List<Renderer>();
        private float foldAmount = 1f;
        private float foldTarget = 1f;
        private bool requestedPropVisible;
        private bool requestedTextVisible;

        public bool IsConfigured { get; private set; }
        public bool IsPropVisible { get; private set; }
        public bool IsTextVisible { get; private set; }
        public int SelectedIndex { get; private set; }
        public bool IsConfirmed { get; private set; }
        public IReadOnlyList<TMP_Text> ItemLines => itemLines;
        public TMP_Text SelectionMarker => selectionMarker;
        public Transform PropRoot => propRoot;
        public IReadOnlyList<Renderer> PropRenderers => propRenderers;
        public Renderer RestingPropRenderer => restingPropRenderer;
        public IReadOnlyList<Renderer> RestingPropRenderers =>
            restingPropRenderers;
        public Transform LeftFoldHinge => leftFoldHinge;
        public Transform RightFoldHinge => rightFoldHinge;
        public float FoldAmount => foldAmount;
        public float LeftLeafAngleDegrees => Mathf.Lerp(
            OpenLeafAngleDegrees,
            ClosedLeftLeafAngleDegrees,
            SmootherStep(foldAmount));
        public bool IsFoldTransitionActive => requestedPropVisible &&
            Mathf.Abs(foldAmount - foldTarget) > FoldEndpointTolerance;
        public Vector3 RestingWorldCenter
        {
            get
            {
                return TryResolveRestingBounds(out Bounds bounds)
                    ? bounds.center
                    : propRoot != null
                        ? propRoot.position
                        : transform.position;
            }
        }

        public bool IsRestingVisible => requestedPropVisible &&
            foldTarget > 0.5f && restingPropRoot != null &&
            restingPropRoot.gameObject.activeSelf;

        public void Initialize(
            Transform configuredPropRoot,
            IReadOnlyList<Renderer> configuredRenderers,
            IReadOnlyList<CounterMenuPageTextAnchor> rowAnchors,
            Transform selectionAnchor,
            IReadOnlyList<string> displayLines,
            Vector3 focusPosition,
            Vector3 focusNormal,
            Vector3 focusUp,
            CounterMenuPageStyle configuredStyle)
        {
            if (IsConfigured)
            {
                throw new InvalidOperationException(
                    "The counter menu page is already configured.");
            }

            if (configuredPropRoot == null)
            {
                throw new ArgumentNullException(
                    nameof(configuredPropRoot));
            }

            if (configuredRenderers == null ||
                configuredRenderers.Count == 0)
            {
                throw new ArgumentException(
                    "A counter menu requires physical page renderers.",
                    nameof(configuredRenderers));
            }

            if (rowAnchors == null || displayLines == null ||
                rowAnchors.Count == 0 ||
                rowAnchors.Count != displayLines.Count)
            {
                throw new ArgumentException(
                    "Every counter-menu line requires one authored socket.",
                    nameof(rowAnchors));
            }

            if (selectionAnchor == null)
            {
                throw new ArgumentNullException(nameof(selectionAnchor));
            }

            ValidateDirection(focusNormal, nameof(focusNormal));
            ValidateDirection(focusUp, nameof(focusUp));
            ValidateStyle(configuredStyle);

            propRoot = configuredPropRoot;
            propRenderers = new Renderer[configuredRenderers.Count];
            for (int index = 0; index < configuredRenderers.Count; index++)
            {
                Renderer renderer = configuredRenderers[index];
                if (renderer == null)
                {
                    throw new ArgumentException(
                        "Counter-menu renderers must be non-null.",
                        nameof(configuredRenderers));
                }

                propRenderers[index] = renderer;
            }

            style = configuredStyle;
            rowLocalPositions = new Vector3[rowAnchors.Count];
            rowRightLocal = new Vector3[rowAnchors.Count];
            rowUpLocal = new Vector3[rowAnchors.Count];
            rowNormalLocal = new Vector3[rowAnchors.Count];
            rowObjectNames = new string[rowAnchors.Count];
            for (int index = 0; index < rowAnchors.Count; index++)
            {
                CounterMenuPageTextAnchor row = rowAnchors[index];
                if (row.Anchor == null)
                {
                    throw new ArgumentException(
                        "Counter-menu text sockets must be non-null.",
                        nameof(rowAnchors));
                }

                ValidateDirection(row.Right, nameof(rowAnchors));
                ValidateDirection(row.Up, nameof(rowAnchors));
                ValidateDirection(row.OutwardNormal, nameof(rowAnchors));
                rowLocalPositions[index] =
                    propRoot.InverseTransformPoint(row.Anchor.position);
                rowRightLocal[index] = propRoot
                    .InverseTransformDirection(row.Right).normalized;
                rowUpLocal[index] = propRoot
                    .InverseTransformDirection(row.Up).normalized;
                rowNormalLocal[index] = propRoot
                    .InverseTransformDirection(row.OutwardNormal).normalized;
                rowObjectNames[index] = row.Anchor.name + " Text";
            }

            focusLocalPosition =
                propRoot.InverseTransformPoint(focusPosition);
            focusNormalLocal = propRoot
                .InverseTransformDirection(focusNormal).normalized;
            focusUpLocal = propRoot
                .InverseTransformDirection(focusUp).normalized;
            BuildFoldingProp(focusPosition, focusNormal, focusUp);
            BuildPageText(displayLines, selectionAnchor.name);
            IsConfigured = true;
            SetSelection(0, false);
            SetVisible(false, false);
        }

        public Pose ResolveCameraFocusPose(Vector3 viewerPosition)
        {
            return ResolveCameraFocusPose(
                viewerPosition,
                CounterMenuCameraPlan.FocusDistanceMeters);
        }

        public Pose ResolveCameraFocusPose(
            Vector3 viewerPosition,
            float focusDistanceMeters)
        {
            if (!IsConfigured || propRoot == null)
            {
                throw new InvalidOperationException(
                    "A menu focus requires a configured physical page.");
            }

            CounterMenuCameraPlan.Evaluate(
                propRoot.TransformPoint(focusLocalPosition),
                propRoot.TransformDirection(focusNormalLocal),
                propRoot.TransformDirection(focusUpLocal),
                viewerPosition,
                focusDistanceMeters,
                out Vector3 position,
                out Quaternion rotation);
            return new Pose(position, rotation);
        }

        public void SetSelection(int selectedIndex, bool confirmed)
        {
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

            if (selectionMarker != null)
            {
                selectionMarker.text = confirmed ? "X" : "\u2022";
                PlaceSelectionMarker();
            }
        }

        public void SetVisible(bool propVisible, bool textVisible)
        {
            requestedPropVisible = propVisible;
            requestedTextVisible = propVisible && textVisible;
            if (!propVisible)
            {
                foldAmount = 1f;
                foldTarget = 1f;
            }
            else
            {
                foldTarget = 0f;
            }

            ApplyFoldPose();
            RefreshPresentationVisibility();
        }

        /// <summary>
        /// Starts closing the readable spread around its physical spine. The
        /// opaque cover and page leaves remain parented to the authored prop,
        /// so delivery and post-stand retrieval carry the folded booklet too.
        /// </summary>
        public void SetRestingVisible(bool visible)
        {
            requestedPropVisible = visible;
            requestedTextVisible = false;
            foldTarget = 1f;
            if (!visible)
            {
                foldAmount = 1f;
            }

            ApplyFoldPose();
            RefreshPresentationVisibility();
        }

        public void AdvanceFold(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime),
                    unscaledDeltaTime,
                    "Menu fold time must be finite and non-negative.");
            }

            if (!IsConfigured || !requestedPropVisible ||
                !IsFoldTransitionActive)
            {
                return;
            }

            foldAmount = Mathf.MoveTowards(
                foldAmount,
                foldTarget,
                unscaledDeltaTime / FoldDurationSeconds);
            ApplyFoldPose();
            RefreshPresentationVisibility();
        }

        public bool IsLookingAtRestingProp(
            Camera camera,
            float maximumDistance = 2.5f)
        {
            if (!IsRestingVisible || camera == null ||
                restingPropRenderer == null)
            {
                return false;
            }

            if (!TryResolveRestingBounds(out Bounds bounds))
            {
                return false;
            }

            Vector3 toMenu = bounds.center - camera.transform.position;
            float distance = toMenu.magnitude;
            if (distance <= 0.001f || distance > maximumDistance)
            {
                return false;
            }

            float apparentRadius = Mathf.Asin(Mathf.Clamp01(
                bounds.extents.magnitude / distance));
            float gazeAllowance = apparentRadius + 3f * Mathf.Deg2Rad;
            return Vector3.Dot(
                       camera.transform.forward,
                       toMenu / distance) >= Mathf.Cos(gazeAllowance);
        }

        private void Update()
        {
            AdvanceFold(Time.unscaledDeltaTime);
        }

        private void BuildFoldingProp(
            Vector3 focusPosition,
            Vector3 focusNormal,
            Vector3 focusUp)
        {
            Vector3 normal = focusNormal.normalized;
            if (Vector3.Dot(normal, Vector3.up) < 0f)
            {
                normal = -normal;
            }

            Vector3 up = Vector3.ProjectOnPlane(
                focusUp,
                normal).normalized;
            Vector3 right = Vector3.Cross(normal, up).normalized;
            Renderer coverSource = FindRenderer("cover") ??
                propRenderers[0];
            Renderer pageSource = FindRenderer("page") ?? coverSource;

            float coverWidth = Mathf.Clamp(
                ResolveProjectedSize(coverSource.bounds, right, 0.54f),
                0.30f,
                0.75f);
            float coverDepth = Mathf.Clamp(
                ResolveProjectedSize(coverSource.bounds, up, 0.32f),
                0.18f,
                0.60f);
            float pageWidth = Mathf.Clamp(
                ResolveProjectedSize(pageSource.bounds, right,
                    coverWidth - 0.04f),
                coverWidth * 0.72f,
                coverWidth - 0.012f);
            float pageDepth = Mathf.Clamp(
                ResolveProjectedSize(pageSource.bounds, up,
                    coverDepth - 0.03f),
                coverDepth * 0.72f,
                coverDepth - 0.012f);

            float surfaceProjection = Mathf.Max(
                ResolveProjectedMaximum(coverSource.bounds, normal),
                ResolveProjectedMaximum(pageSource.bounds, normal));
            Vector3 hingePosition = focusPosition + normal *
                (surfaceProjection - Vector3.Dot(focusPosition, normal));

            var foldRootObject = new GameObject(
                "Physical Counter Menu Fold");
            foldRootObject.layer = propRoot.gameObject.layer;
            restingPropRoot = foldRootObject.transform;
            restingPropRoot.SetPositionAndRotation(
                hingePosition,
                Quaternion.LookRotation(up, normal));
            restingPropRoot.SetParent(propRoot, true);

            leftFoldHinge = CreateFoldHinge("Left Leaf Hinge");
            rightFoldHinge = CreateFoldHinge("Right Leaf Hinge");

            Color coverColor = ResolveOpaqueColor(
                coverSource,
                new Color(0.11f, 0.045f, 0.025f, 1f));
            Color pageColor = ResolveOpaqueColor(
                pageSource,
                new Color(0.74f, 0.66f, 0.47f, 1f));
            float coverLeafWidth =
                (coverWidth - CoverGapMeters) * 0.5f;
            float pageLeafWidth =
                (pageWidth - PageGapMeters) * 0.5f;

            Renderer leftCover = CreateFoldPanel(
                "Left Opaque Cover",
                leftFoldHinge,
                new Vector3(
                    -(CoverGapMeters + coverLeafWidth) * 0.5f,
                    -CoverThicknessMeters * 0.5f,
                    0f),
                new Vector3(
                    coverLeafWidth,
                    CoverThicknessMeters,
                    coverDepth),
                coverColor,
                coverSource);
            CreateFoldPanel(
                "Right Opaque Cover",
                rightFoldHinge,
                new Vector3(
                    (CoverGapMeters + coverLeafWidth) * 0.5f,
                    -CoverThicknessMeters * 0.5f,
                    0f),
                new Vector3(
                    coverLeafWidth,
                    CoverThicknessMeters,
                    coverDepth),
                coverColor,
                coverSource);
            CreateFoldPanel(
                "Left Opaque Pages",
                leftFoldHinge,
                new Vector3(
                    -(PageGapMeters + pageLeafWidth) * 0.5f,
                    PageCoverClearanceMeters +
                    PageThicknessMeters * 0.5f,
                    0f),
                new Vector3(
                    pageLeafWidth,
                    PageThicknessMeters,
                    pageDepth),
                pageColor,
                pageSource);
            CreateFoldPanel(
                "Right Opaque Pages",
                rightFoldHinge,
                new Vector3(
                    (PageGapMeters + pageLeafWidth) * 0.5f,
                    PageCoverClearanceMeters +
                    PageThicknessMeters * 0.5f,
                    0f),
                new Vector3(
                    pageLeafWidth,
                    PageThicknessMeters,
                    pageDepth),
                pageColor,
                pageSource);
            CreateFoldPanel(
                "Opaque Menu Spine",
                restingPropRoot,
                new Vector3(
                    0f,
                    -CoverThicknessMeters * 0.5f,
                    0f),
                new Vector3(
                    CoverGapMeters + 0.006f,
                    CoverThicknessMeters,
                    coverDepth),
                coverColor,
                coverSource);

            restingPropRenderer = leftCover;
            foldAmount = 1f;
            foldTarget = 1f;
            ApplyFoldPose();
            foldRootObject.SetActive(false);
        }

        private Transform CreateFoldHinge(string objectName)
        {
            var hinge = new GameObject(objectName);
            hinge.layer = propRoot.gameObject.layer;
            hinge.transform.SetParent(restingPropRoot, false);
            return hinge.transform;
        }

        private Renderer CreateFoldPanel(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Renderer source)
        {
            GameObject panel = RuntimePrimitiveFactory.CreateBox(
                objectName,
                parent,
                localPosition,
                size,
                color,
                source != null ? source.sharedMaterial : null,
                false);
            panel.layer = source != null
                ? source.gameObject.layer
                : propRoot.gameObject.layer;
            Renderer renderer = panel.GetComponent<Renderer>();
            if (source != null)
            {
                renderer.shadowCastingMode = source.shadowCastingMode;
                renderer.receiveShadows = source.receiveShadows;
                renderer.lightProbeUsage = source.lightProbeUsage;
                renderer.reflectionProbeUsage = source.reflectionProbeUsage;
            }

            restingPropRenderers.Add(renderer);
            return renderer;
        }

        private Renderer FindRenderer(string nameFragment)
        {
            for (int index = 0; index < propRenderers.Length; index++)
            {
                Renderer candidate = propRenderers[index];
                if (candidate != null &&
                    candidate.name.IndexOf(
                        nameFragment,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ApplyFoldPose()
        {
            if (leftFoldHinge == null || rightFoldHinge == null)
            {
                return;
            }

            leftFoldHinge.localPosition = Vector3.zero;
            leftFoldHinge.localRotation = Quaternion.AngleAxis(
                LeftLeafAngleDegrees,
                Vector3.forward);
            leftFoldHinge.localScale = Vector3.one;
            rightFoldHinge.localPosition = Vector3.zero;
            rightFoldHinge.localRotation = Quaternion.AngleAxis(
                RightLeafAngleDegrees,
                Vector3.forward);
            rightFoldHinge.localScale = Vector3.one;
        }

        private void RefreshPresentationVisibility()
        {
            bool showAuthoredOpen = requestedPropVisible &&
                foldTarget <= FoldEndpointTolerance &&
                foldAmount <= FoldEndpointTolerance;
            SetAuthoredRenderersEnabled(showAuthoredOpen);
            if (restingPropRoot != null)
            {
                restingPropRoot.gameObject.SetActive(
                    requestedPropVisible && !showAuthoredOpen);
            }

            IsPropVisible = requestedPropVisible;
            IsTextVisible = showAuthoredOpen && requestedTextVisible;
            for (int index = 0; index < itemLines.Length; index++)
            {
                SetTextRenderer(itemLines[index], IsTextVisible);
            }

            SetTextRenderer(selectionMarker, IsTextVisible);
        }

        private void SetAuthoredRenderersEnabled(bool enabled)
        {
            for (int index = 0; index < propRenderers.Length; index++)
            {
                if (propRenderers[index] != null)
                {
                    propRenderers[index].enabled = enabled;
                }
            }
        }

        private bool TryResolveRestingBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int index = 0; index < restingPropRenderers.Count; index++)
            {
                Renderer renderer = restingPropRenderers[index];
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private static float ResolveProjectedMaximum(
            Bounds bounds,
            Vector3 worldAxis)
        {
            Vector3 axis = worldAxis.normalized;
            Vector3 extents = bounds.extents;
            return Vector3.Dot(bounds.center, axis) +
                Mathf.Abs(axis.x) * extents.x +
                Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z;
        }

        private static Color ResolveOpaqueColor(
            Renderer source,
            Color fallback)
        {
            Color result = fallback;
            if (source != null)
            {
                var properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                if (properties.HasColor(BaseColorId))
                {
                    result = properties.GetColor(BaseColorId);
                }
                else if (properties.HasColor(ColorId))
                {
                    result = properties.GetColor(ColorId);
                }
                else if (source.sharedMaterial != null &&
                         source.sharedMaterial.HasProperty(BaseColorId))
                {
                    result = source.sharedMaterial.GetColor(BaseColorId);
                }
                else if (source.sharedMaterial != null &&
                         source.sharedMaterial.HasProperty(ColorId))
                {
                    result = source.sharedMaterial.GetColor(ColorId);
                }
            }

            if (!IsFinite(result.r) || !IsFinite(result.g) ||
                !IsFinite(result.b))
            {
                result = fallback;
            }

            result.a = 1f;
            return result;
        }

        private static float ResolveProjectedSize(
            Bounds bounds,
            Vector3 worldAxis,
            float fallback)
        {
            Vector3 axis = worldAxis.normalized;
            Vector3 extents = bounds.extents;
            float size = 2f * (
                Mathf.Abs(axis.x) * extents.x +
                Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z);
            return float.IsNaN(size) ||
                   float.IsInfinity(size) ||
                   size <= 0.0001f
                ? fallback
                : size;
        }

        private void BuildPageText(
            IReadOnlyList<string> displayLines,
            string markerName)
        {
            TMP_FontAsset font = CemeteryPlaqueFont.Get();
            if (font == null)
            {
                itemLines = Array.Empty<TMP_Text>();
                return;
            }

            itemLines = new TMP_Text[displayLines.Count];
            for (int index = 0; index < displayLines.Count; index++)
            {
                itemLines[index] = CreateText(
                    rowObjectNames[index],
                    displayLines[index],
                    font,
                    style.ItemFontSize,
                    style.ItemBoxSize,
                    index);
            }

            selectionMarker = CreateText(
                string.IsNullOrWhiteSpace(markerName)
                    ? "Counter Menu Selection"
                    : markerName + " Text",
                "\u2022",
                font,
                style.MarkerFontSize,
                style.MarkerBoxSize,
                0);
        }

        private TMP_Text CreateText(
            string objectName,
            string value,
            TMP_FontAsset font,
            float fontSize,
            Vector2 size,
            int rowIndex)
        {
            ResolveRowWorldBasis(
                rowIndex,
                out Vector3 position,
                out Vector3 right,
                out Vector3 up,
                out Vector3 normal);
            var host = new GameObject(objectName);
            host.transform.SetPositionAndRotation(
                position + normal * TextLiftMeters,
                Quaternion.LookRotation(-normal, up));
            host.transform.SetParent(propRoot, true);
            var text = host.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontSizeMin = fontSize * style.MinimumFontScale;
            text.fontSizeMax = fontSize;
            text.enableAutoSizing = true;
            text.color = style.Ink;
            text.alignment = TextAlignmentOptions.Center;
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

        private void PlaceSelectionMarker()
        {
            TMP_Text chosen = itemLines[SelectedIndex];
            if (chosen == null)
            {
                return;
            }

            chosen.ForceMeshUpdate();
            float halfLine = chosen.GetRenderedValues(false).x * 0.5f;
            ResolveRowWorldBasis(
                SelectedIndex,
                out Vector3 position,
                out Vector3 right,
                out Vector3 up,
                out Vector3 normal);
            selectionMarker.transform.SetPositionAndRotation(
                position + normal * TextLiftMeters -
                right * (halfLine + style.MarkerGapMeters),
                Quaternion.LookRotation(-normal, up));
        }

        private void ResolveRowWorldBasis(
            int index,
            out Vector3 position,
            out Vector3 right,
            out Vector3 up,
            out Vector3 normal)
        {
            position = propRoot.TransformPoint(rowLocalPositions[index]);
            right = propRoot.TransformDirection(
                rowRightLocal[index]).normalized;
            normal = propRoot.TransformDirection(
                rowNormalLocal[index]).normalized;
            up = Vector3.ProjectOnPlane(
                propRoot.TransformDirection(rowUpLocal[index]),
                normal).normalized;
            if (Vector3.Dot(Vector3.Cross(up, right), normal) < 0f)
            {
                right = -right;
            }
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

        private static void ValidateDirection(
            Vector3 direction,
            string parameterName)
        {
            if (!IsFinite(direction.x) ||
                !IsFinite(direction.y) ||
                !IsFinite(direction.z) ||
                direction.sqrMagnitude < 0.25f)
            {
                throw new ArgumentException(
                    "Counter-menu page axes must be finite and non-zero.",
                    parameterName);
            }
        }

        private static void ValidateStyle(CounterMenuPageStyle value)
        {
            if (value.ItemFontSize <= 0f ||
                value.MinimumFontScale <= 0f ||
                value.MinimumFontScale > 1f ||
                value.ItemBoxSize.x <= 0f ||
                value.ItemBoxSize.y <= 0f ||
                value.MarkerFontSize <= 0f ||
                value.MarkerBoxSize.x <= 0f ||
                value.MarkerBoxSize.y <= 0f ||
                value.MarkerGapMeters < 0f)
            {
                throw new ArgumentException(
                    "Counter-menu page typography is invalid.",
                    nameof(value));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }
    }
}
