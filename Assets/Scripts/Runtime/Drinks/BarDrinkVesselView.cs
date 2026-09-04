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
        [SerializeField] private Transform drinkRimAnchor;
        [SerializeField] private Renderer interactionHighlightRenderer;

        private MaterialPropertyBlock liquidProperties;
        private Vector3 liquidFullScale;
        private Transform originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private Quaternion gripRotationInVessel = Quaternion.identity;
        private Vector3 authoredGripLocalPosition;
        private Vector3 authoredDrinkRimLocalPosition;
        private Vector3 authoredOpeningLocalDirection = Vector3.up;
        private Vector3 authoredHandleLocalDirection = Vector3.right;
        private float targetFill;
        private bool initialized;

        public BarDrinkVesselKind Kind => kind;
        public Renderer GlassRenderer => glassRenderer;
        public Transform LiquidRoot => liquidRoot;
        public Renderer LiquidRenderer => liquidRenderer;
        public Transform PourTarget => pourTarget;
        public Transform GripAnchor => gripAnchor;
        public Transform DrinkRimAnchor => drinkRimAnchor;
        public Renderer InteractionHighlightRenderer =>
            interactionHighlightRenderer;
        public Vector3 PourTargetWorldPosition =>
            pourTarget != null ? pourTarget.position : transform.position;
        public Vector3 GripWorldPosition =>
            gripAnchor != null ? gripAnchor.position : transform.position;
        public Vector3 DrinkRimWorldPosition =>
            drinkRimAnchor != null
                ? drinkRimAnchor.position
                : transform.position;
        public Vector3 OpeningDirection =>
            transform.TransformDirection(authoredOpeningLocalDirection)
                .normalized;
        public Vector3 HandleDirection =>
            transform.TransformDirection(authoredHandleLocalDirection)
                .normalized;
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
            Transform newDrinkRimAnchor,
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

            if (newDrinkRimAnchor == null ||
                !newDrinkRimAnchor.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The drinking-rim anchor must belong to the vessel root.",
                    nameof(newDrinkRimAnchor));
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
            drinkRimAnchor = newDrinkRimAnchor;
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
            authoredGripLocalPosition =
                transform.InverseTransformPoint(gripAnchor.position);
            authoredDrinkRimLocalPosition =
                transform.InverseTransformPoint(drinkRimAnchor.position);
            authoredOpeningLocalDirection = Vector3.up;
            authoredHandleLocalDirection = Vector3.ProjectOnPlane(
                authoredGripLocalPosition,
                authoredOpeningLocalDirection);
            if (authoredHandleLocalDirection.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The vessel grip must identify a radial handle side.",
                    nameof(newGripAnchor));
            }

            authoredHandleLocalDirection.Normalize();
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
        /// Resolves a measured mug pose independently from the wrist: the
        /// authored drinking edge reaches the live mouth socket, the opening
        /// tips from upright toward the hero, and the handle stays on the
        /// anatomical right. The hand is solved to the handle afterwards.
        /// </summary>
        public bool TryResolveDrinkPose(
            Transform mouthSocket,
            Transform ownerRoot,
            float tipAmount,
            out Pose pose)
        {
            pose = default;
            if (mouthSocket == null || drinkRimAnchor == null ||
                !TryResolveDrinkRotation(
                    ownerRoot,
                    tipAmount,
                    out Quaternion rotation))
            {
                return false;
            }

            Vector3 scaledRim = Vector3.Scale(
                authoredDrinkRimLocalPosition,
                transform.lossyScale);
            pose = new Pose(
                mouthSocket.position - rotation * scaledRim,
                rotation);
            return true;
        }

        /// <summary>
        /// Keeps the authored vessel origin on the counter while orienting an
        /// upright mug handle toward the hero's anatomical right. This must be
        /// independent of the mirrored bartender-service layout.
        /// </summary>
        public bool TryResolveRightHandledUprightPose(
            Vector3 rootPosition,
            Transform ownerRoot,
            out Pose pose)
        {
            pose = default;
            if (!TryResolveDrinkRotation(
                    ownerRoot,
                    0f,
                    out Quaternion rotation))
            {
                return false;
            }

            pose = new Pose(rootPosition, rotation);
            return true;
        }

        private bool TryResolveDrinkRotation(
            Transform ownerRoot,
            float tipAmount,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!initialized || ownerRoot == null || gripAnchor == null)
            {
                return false;
            }

            Vector3 upright = ownerRoot.up.normalized;
            Vector3 towardMouth = -Vector3.ProjectOnPlane(
                ownerRoot.forward,
                upright);
            if (towardMouth.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            Vector3 worldOpening = Vector3.Slerp(
                    upright,
                    towardMouth.normalized,
                    Mathf.Clamp01(tipAmount))
                .normalized;
            Vector3 worldHandle = Vector3.ProjectOnPlane(
                ownerRoot.right,
                worldOpening);
            if (worldHandle.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            worldHandle.Normalize();
            Vector3 localForward = Vector3.Cross(
                    authoredHandleLocalDirection,
                    authoredOpeningLocalDirection)
                .normalized;
            Vector3 worldForward = Vector3.Cross(
                    worldHandle,
                    worldOpening)
                .normalized;
            rotation = Quaternion.LookRotation(
                           worldForward,
                           worldOpening) *
                       Quaternion.Inverse(
                           Quaternion.LookRotation(
                               localForward,
                               authoredOpeningLocalDirection));
            return true;
        }

        public float ResolveDrinkRimError(Transform mouthSocket)
        {
            return initialized && mouthSocket != null &&
                   drinkRimAnchor != null
                ? Vector3.Distance(
                    drinkRimAnchor.position,
                    mouthSocket.position)
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
