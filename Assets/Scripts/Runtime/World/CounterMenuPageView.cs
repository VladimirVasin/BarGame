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
    /// and places all world text, marker state, visibility and camera focus.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class CounterMenuPageView : MonoBehaviour
    {
        private const float TextLiftMeters = 0.0015f;

        [SerializeField] private Transform propRoot;
        [SerializeField] private Renderer[] propRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private TMP_Text[] itemLines =
            Array.Empty<TMP_Text>();
        [SerializeField] private TMP_Text selectionMarker;

        private Vector3[] rowLocalPositions = Array.Empty<Vector3>();
        private Vector3[] rowRightLocal = Array.Empty<Vector3>();
        private Vector3[] rowUpLocal = Array.Empty<Vector3>();
        private Vector3[] rowNormalLocal = Array.Empty<Vector3>();
        private string[] rowObjectNames = Array.Empty<string>();
        private Vector3 focusLocalPosition;
        private Vector3 focusUpLocal;
        private Vector3 focusNormalLocal;
        private CounterMenuPageStyle style;

        public bool IsConfigured { get; private set; }
        public bool IsPropVisible { get; private set; }
        public bool IsTextVisible { get; private set; }
        public int SelectedIndex { get; private set; }
        public bool IsConfirmed { get; private set; }
        public IReadOnlyList<TMP_Text> ItemLines => itemLines;
        public TMP_Text SelectionMarker => selectionMarker;
        public Transform PropRoot => propRoot;

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
            for (int index = 0; index < propRenderers.Length; index++)
            {
                if (propRenderers[index] != null)
                {
                    propRenderers[index].enabled = propVisible;
                }
            }

            IsPropVisible = propVisible;
            IsTextVisible = propVisible && textVisible;
            for (int index = 0; index < itemLines.Length; index++)
            {
                SetTextRenderer(itemLines[index], IsTextVisible);
            }

            SetTextRenderer(selectionMarker, IsTextVisible);
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
    }
}
