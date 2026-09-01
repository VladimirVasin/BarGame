using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One authored cup's only mutable visual: the coffee surface. Moving a
    /// dedicated transform keeps the ceramic and saucer untouched and never
    /// instantiates a material.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCupView : MonoBehaviour
    {
        [SerializeField] private MountainRoadCafeCastRole role;
        [SerializeField] private Transform cupRoot;
        [SerializeField] private Transform gripAnchor;
        [SerializeField] private Transform liquidTransform;
        [SerializeField] private Renderer liquidRenderer;
        [SerializeField] private Transform pourTarget;
        [SerializeField] private Vector3 emptyLocalPosition;
        [SerializeField] private Vector3 fullLocalPosition;

        private Transform authoredCupParent;
        private Vector3 authoredCupPosition;
        private Quaternion authoredCupRotation;
        private Vector3 authoredCupScale;
        private Vector3 authoredCupLossyScale;
        private Vector3 authoredGripLocalPosition;
        private Vector3 authoredOpeningLocalDirection;
        private Vector3 authoredHandleLocalDirection;
        private bool attachedToHand;
        private Transform boundHandSocket;
        private Transform boundMouthSocket;
        private Transform boundOwnerRoot;
        private Vector3 boundOutward;

        public bool IsConfigured { get; private set; }
        public MountainRoadCafeCastRole Role => role;
        public Transform LiquidTransform => liquidTransform;
        public Renderer LiquidRenderer => liquidRenderer;
        public Transform GripAnchor => gripAnchor;
        public Transform CupRoot => cupRoot;
        public Vector3 OpeningDirection => cupRoot != null
            ? cupRoot.TransformDirection(
                authoredOpeningLocalDirection).normalized
            : Vector3.up;
        public Transform PourTarget => pourTarget != null
            ? pourTarget
            : liquidTransform;
        public float Fill01 { get; private set; }

        public void Configure(
            MountainRoadCafeCastRole configuredRole,
            Transform configuredLiquidTransform,
            Renderer configuredLiquidRenderer,
            Vector3 configuredEmptyLocalPosition,
            Vector3 configuredFullLocalPosition,
            Transform configuredPourTarget = null,
            Transform configuredCupRoot = null,
            Transform configuredGripAnchor = null)
        {
            if (!MountainRoadCafeServiceTimeline.IsPatronWithCup(
                    configuredRole))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuredRole),
                    "Only the authored pair may own cafe cups.");
            }

            if (configuredLiquidTransform == null)
            {
                throw new ArgumentNullException(
                    nameof(configuredLiquidTransform));
            }

            if (configuredLiquidRenderer == null)
            {
                throw new ArgumentNullException(
                    nameof(configuredLiquidRenderer));
            }

            if (configuredCupRoot == null ||
                configuredGripAnchor == null ||
                !configuredGripAnchor.IsChildOf(configuredCupRoot))
            {
                throw new ArgumentException(
                    "A cafe cup requires its authored lift root and a Grip " +
                    "anchor below that root.");
            }

            if (Vector3.SqrMagnitude(
                    configuredFullLocalPosition -
                    configuredEmptyLocalPosition) <= 0.00000001f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuredFullLocalPosition),
                    "A cup's full and empty liquid positions must differ.");
            }

            role = configuredRole;
            cupRoot = configuredCupRoot;
            gripAnchor = configuredGripAnchor;
            liquidTransform = configuredLiquidTransform;
            liquidRenderer = configuredLiquidRenderer;
            pourTarget = configuredPourTarget;
            emptyLocalPosition = configuredEmptyLocalPosition;
            fullLocalPosition = configuredFullLocalPosition;
            authoredCupParent = cupRoot.parent;
            authoredCupPosition = cupRoot.localPosition;
            authoredCupRotation = cupRoot.localRotation;
            authoredCupScale = cupRoot.localScale;
            authoredCupLossyScale = cupRoot.lossyScale;
            authoredGripLocalPosition =
                cupRoot.InverseTransformPoint(gripAnchor.position);
            Vector3 openingInLiquidParent =
                configuredFullLocalPosition -
                configuredEmptyLocalPosition;
            authoredOpeningLocalDirection = cupRoot.InverseTransformDirection(
                liquidTransform.parent.TransformDirection(
                    openingInLiquidParent)).normalized;
            authoredHandleLocalDirection = Vector3.ProjectOnPlane(
                authoredGripLocalPosition,
                authoredOpeningLocalDirection).normalized;
            if (authoredOpeningLocalDirection.sqrMagnitude < 0.999f ||
                authoredHandleLocalDirection.sqrMagnitude < 0.999f)
            {
                throw new InvalidOperationException(
                    "A cafe cup requires measurable opening and handle " +
                    "directions below its lift root.");
            }
            IsConfigured = true;
            SetFill01(1f);
        }

        public void SetDrinkPose(
            bool drinking,
            float normalized,
            Transform handSocket)
        {
            if (!IsConfigured)
            {
                return;
            }

            bool shouldAttach = drinking &&
                                handSocket != null &&
                                normalized >= 0.16f &&
                                normalized < 0.84f;
            if (shouldAttach)
            {
                if (boundHandSocket == handSocket &&
                    boundMouthSocket != null &&
                    boundOwnerRoot != null)
                {
                    SetMeasuredDrinkPose(normalized);
                    attachedToHand = true;
                    return;
                }

                // Compatibility path for isolated tests and callers that do
                // not bind the full cafe rig. Production binds mouth/root and
                // therefore never inherits the FBX bone's arbitrary axes.
                if (!attachedToHand || cupRoot.parent != handSocket)
                {
                    cupRoot.SetParent(handSocket, false);
                    attachedToHand = true;
                }
                cupRoot.localScale = ResolveLocalScaleForParent(
                    authoredCupLossyScale,
                    handSocket.lossyScale);
                cupRoot.localRotation = Quaternion.Inverse(
                    gripAnchor.localRotation);
                Vector3 scaledGrip = Vector3.Scale(
                    gripAnchor.localPosition,
                    cupRoot.localScale);
                cupRoot.localPosition = -(
                    cupRoot.localRotation * scaledGrip);
                return;
            }

            RestoreDock();
        }

        public bool BindDrinkRig(
            Transform handSocket,
            Transform mouthSocket,
            Transform ownerRoot)
        {
            if (!IsConfigured || handSocket == null ||
                mouthSocket == null || ownerRoot == null)
            {
                return false;
            }

            // The docked handle is the authored side contract. Inferring the
            // side from an idle hand relative to the mouth can flip it when
            // a staged rig has a yawed mark or an asymmetric idle. The Grip
            // still reaches the hand in that failure, but the cup body swings
            // around it and lands beside the saucer.
            Vector3 authoredWorldHandle = Vector3.ProjectOnPlane(
                cupRoot.TransformDirection(authoredHandleLocalDirection),
                Vector3.up);
            if (authoredWorldHandle.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            boundOutward = authoredWorldHandle.normalized;
            boundHandSocket = handSocket;
            boundMouthSocket = mouthSocket;
            boundOwnerRoot = ownerRoot;
            return true;
        }

        public void SetFill01(float value)
        {
            if (!IsConfigured)
            {
                return;
            }

            Fill01 = Mathf.Clamp01(value);
            liquidTransform.localPosition = Vector3.Lerp(
                emptyLocalPosition,
                fullLocalPosition,
                Fill01);
            liquidRenderer.enabled = Fill01 > 0.001f;
        }

        public void ResetExact()
        {
            RestoreDock();
            SetFill01(1f);
        }

        private void RestoreDock()
        {
            if (!IsConfigured)
            {
                return;
            }

            if (cupRoot.parent != authoredCupParent)
            {
                cupRoot.SetParent(authoredCupParent, false);
            }

            cupRoot.localPosition = authoredCupPosition;
            cupRoot.localRotation = authoredCupRotation;
            cupRoot.localScale = authoredCupScale;
            attachedToHand = false;
        }

        private void SetMeasuredDrinkPose(float normalized)
        {
            if (cupRoot.parent != authoredCupParent)
            {
                cupRoot.SetParent(authoredCupParent, false);
            }

            Vector3 up = Vector3.up;
            Vector3 towardMouth = Vector3.ProjectOnPlane(
                boundMouthSocket.position - boundHandSocket.position,
                up).normalized;
            if (towardMouth.sqrMagnitude < 0.999f)
            {
                towardMouth = -Vector3.ProjectOnPlane(
                    boundOwnerRoot.forward,
                    up).normalized;
            }

            float tipAmount = ResolveSipAmount(normalized);
            const float MaximumTipDegrees = 32f;
            Vector3 tiltAxis = Vector3.Cross(
                up,
                towardMouth).normalized;
            Vector3 tippedOpening = Quaternion.AngleAxis(
                MaximumTipDegrees * tipAmount,
                tiltAxis) * up;
            Vector3 worldHandle = Vector3.ProjectOnPlane(
                boundOutward,
                tippedOpening).normalized;
            Vector3 worldForward = Vector3.Cross(
                worldHandle,
                tippedOpening).normalized;
            Vector3 localForward = Vector3.Cross(
                authoredHandleLocalDirection,
                authoredOpeningLocalDirection).normalized;
            Quaternion localBasis = Quaternion.LookRotation(
                localForward,
                authoredOpeningLocalDirection);
            Quaternion worldBasis = Quaternion.LookRotation(
                worldForward,
                tippedOpening);
            Quaternion worldRotation = worldBasis *
                                       Quaternion.Inverse(localBasis);

            cupRoot.localScale = authoredCupScale;
            cupRoot.rotation = worldRotation;
            cupRoot.position = boundHandSocket.position -
                               cupRoot.TransformVector(
                                   authoredGripLocalPosition);
        }

        private static float ResolveSipAmount(float normalized)
        {
            if (normalized <= 0.34f || normalized >= 0.76f)
            {
                return 0f;
            }

            if (normalized < 0.48f)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.34f, 0.48f, normalized));
            }

            if (normalized <= 0.62f)
            {
                return 1f;
            }

            return Mathf.SmoothStep(
                1f,
                0f,
                Mathf.InverseLerp(0.62f, 0.76f, normalized));
        }

        private static Vector3 ResolveLocalScaleForParent(
            Vector3 desiredLossyScale,
            Vector3 parentLossyScale)
        {
            return new Vector3(
                SafeDivide(desiredLossyScale.x, parentLossyScale.x),
                SafeDivide(desiredLossyScale.y, parentLossyScale.y),
                SafeDivide(desiredLossyScale.z, parentLossyScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.000001f
                ? value / divisor
                : value;
        }
    }
}
