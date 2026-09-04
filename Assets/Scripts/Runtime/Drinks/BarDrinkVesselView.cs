using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One reusable three-dimensional vessel and its bottom-anchored liquid
    /// volume. Geometry is created once; animation only changes transforms and
    /// per-renderer property blocks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkVesselView : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private BarDrinkVesselKind kind;
        [SerializeField] private Renderer glassRenderer;
        [SerializeField] private Transform liquidRoot;
        [SerializeField] private Renderer liquidRenderer;
        [SerializeField] private Transform pourTarget;
        [SerializeField] private Transform gripAnchor;
        [SerializeField] private Renderer interactionHighlightRenderer;

        private MaterialPropertyBlock liquidProperties;
        private Vector3 liquidFullScale;
        private Transform originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private Quaternion gripRotationInVessel = Quaternion.identity;
        private float targetFill;
        private bool initialized;

        public BarDrinkVesselKind Kind => kind;
        public Renderer GlassRenderer => glassRenderer;
        public Transform LiquidRoot => liquidRoot;
        public Renderer LiquidRenderer => liquidRenderer;
        public Transform PourTarget => pourTarget;
        public Transform GripAnchor => gripAnchor;
        public Renderer InteractionHighlightRenderer =>
            interactionHighlightRenderer;
        public Vector3 PourTargetWorldPosition =>
            pourTarget != null ? pourTarget.position : transform.position;
        public Vector3 GripWorldPosition =>
            gripAnchor != null ? gripAnchor.position : transform.position;
        public bool IsInteractionHighlighted =>
            interactionHighlightRenderer != null &&
            interactionHighlightRenderer.enabled;
        public float TargetFill => targetFill;
        public float FillProgress { get; private set; }
        public float DisplayedFill => FillProgress * targetFill;

        internal void Initialize(
            BarDrinkVesselKind newKind,
            Renderer newGlassRenderer,
            Transform newLiquidRoot,
            Renderer newLiquidRenderer,
            Transform newPourTarget,
            Transform newGripAnchor,
            Renderer newInteractionHighlightRenderer = null)
        {
            if (newKind == BarDrinkVesselKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newKind),
                    "A vessel view requires a concrete kind.");
            }

            if (newGlassRenderer == null ||
                !newGlassRenderer.transform.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The glass renderer must belong to the vessel root.",
                    nameof(newGlassRenderer));
            }

            if (newLiquidRoot == null ||
                !newLiquidRoot.IsChildOf(transform) ||
                newLiquidRenderer == null ||
                !newLiquidRenderer.transform.IsChildOf(newLiquidRoot))
            {
                throw new ArgumentException(
                    "The liquid volume must belong to the vessel root.",
                    nameof(newLiquidRoot));
            }

            if (newPourTarget == null ||
                !newPourTarget.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The pour target must belong to the vessel root.",
                    nameof(newPourTarget));
            }

            if (newGripAnchor == null ||
                !newGripAnchor.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The grip anchor must belong to the vessel root.",
                    nameof(newGripAnchor));
            }

            if (newInteractionHighlightRenderer != null &&
                !newInteractionHighlightRenderer.transform
                    .IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The interaction highlight must belong to the vessel root.",
                    nameof(newInteractionHighlightRenderer));
            }

            kind = newKind;
            glassRenderer = newGlassRenderer;
            liquidRoot = newLiquidRoot;
            liquidRenderer = newLiquidRenderer;
            pourTarget = newPourTarget;
            gripAnchor = newGripAnchor;
            interactionHighlightRenderer =
                newInteractionHighlightRenderer;
            liquidProperties = new MaterialPropertyBlock();
            liquidFullScale = liquidRoot.localScale;
            originalParent = transform.parent;
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            originalLocalScale = transform.localScale;
            gripRotationInVessel = Quaternion.Inverse(transform.rotation) *
                                   gripAnchor.rotation;
            targetFill = 1f;
            initialized = true;
            SetInteractionHighlight(false);
            SetFillProgress(0f);
        }

        public void ConfigureLiquid(Color color, float newTargetFill)
        {
            if (!initialized)
            {
                return;
            }

            if (float.IsNaN(newTargetFill) ||
                float.IsInfinity(newTargetFill) ||
                newTargetFill <= 0f ||
                newTargetFill > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newTargetFill),
                    "Target fill must be in the range (0, 1].");
            }

            targetFill = newTargetFill;
            Color displayed = color;
            displayed.a = Mathf.Clamp(displayed.a, 0.72f, 0.94f);
            liquidRenderer.GetPropertyBlock(liquidProperties);
            liquidProperties.SetColor(BaseColorId, displayed);
            liquidProperties.SetColor(ColorId, displayed);
            liquidRenderer.SetPropertyBlock(liquidProperties);
            SetFillProgress(0f);
        }

        public void SetFillProgress(float progress)
        {
            if (!initialized)
            {
                return;
            }

            FillProgress = Mathf.Clamp01(progress);
            float heightScale = targetFill * FillProgress;
            liquidRoot.localScale = new Vector3(
                liquidFullScale.x,
                liquidFullScale.y * Mathf.Max(heightScale, 0.001f),
                liquidFullScale.z);
            liquidRoot.gameObject.SetActive(FillProgress > 0.001f);
        }

        public void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Keeps the authored glass grip exactly on an animated hand socket.
        /// The vessel stays under the scale-free service root; parenting it
        /// to an imported FBX bone would inherit that hierarchy's 100x scale.
        /// </summary>
        public bool AlignGripTo(Transform carrier)
        {
            if (!initialized || carrier == null || gripAnchor == null)
            {
                return false;
            }

            Quaternion vesselRotation = carrier.rotation *
                Quaternion.Inverse(gripRotationInVessel);
            transform.SetPositionAndRotation(
                carrier.position,
                vesselRotation);
            transform.position += carrier.position - gripAnchor.position;
            return true;
        }

        public float ResolveGripError(Transform carrier)
        {
            return initialized && carrier != null && gripAnchor != null
                ? Vector3.Distance(gripAnchor.position, carrier.position)
                : float.PositiveInfinity;
        }

        /// <summary>
        /// Uses the same angular-bounds gaze rule as the folded counter menu.
        /// The caller owns interaction priority and drives prompt/highlight
        /// from this single predicate.
        /// </summary>
        public bool IsLookingAt(
            Camera camera,
            float maximumDistance = 2.5f)
        {
            if (!initialized || !gameObject.activeInHierarchy ||
                camera == null || glassRenderer == null ||
                maximumDistance <= 0f)
            {
                return false;
            }

            Bounds bounds = glassRenderer.bounds;
            Vector3 toVessel = bounds.center - camera.transform.position;
            float distance = toVessel.magnitude;
            if (distance <= 0.001f || distance > maximumDistance)
            {
                return false;
            }

            float apparentRadius = Mathf.Asin(Mathf.Clamp01(
                bounds.extents.magnitude / distance));
            float gazeAllowance = apparentRadius + 3f * Mathf.Deg2Rad;
            return Vector3.Dot(
                       camera.transform.forward,
                       toVessel / distance) >= Mathf.Cos(gazeAllowance);
        }

        public void SetInteractionHighlight(bool highlighted)
        {
            if (interactionHighlightRenderer != null)
            {
                interactionHighlightRenderer.enabled = highlighted;
            }
        }

        public void SetLocalPose(
            BarDrinkServicePose pose,
            Transform referenceSpace)
        {
            if (referenceSpace == null)
            {
                SetWorldPose(pose.Position, pose.Rotation);
                return;
            }

            SetWorldPose(
                referenceSpace.TransformPoint(pose.Position),
                referenceSpace.rotation * pose.Rotation);
        }

        public void ResetFill()
        {
            SetFillProgress(0f);
        }

        public void ResetExact()
        {
            if (!initialized)
            {
                return;
            }

            ResetFill();
            SetInteractionHighlight(false);
            if (transform.parent != originalParent &&
                originalParent != null &&
                gameObject.activeInHierarchy &&
                originalParent.gameObject.activeInHierarchy)
            {
                transform.SetParent(originalParent, false);
            }

            if (transform.parent == originalParent)
            {
                transform.localPosition = originalLocalPosition;
                transform.localRotation = originalLocalRotation;
                transform.localScale = originalLocalScale;
            }
        }

        private void OnDisable()
        {
            ResetExact();
        }
    }
}
