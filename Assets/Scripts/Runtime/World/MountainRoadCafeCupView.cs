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
        private bool attachedToHand;

        public bool IsConfigured { get; private set; }
        public MountainRoadCafeCastRole Role => role;
        public Transform LiquidTransform => liquidTransform;
        public Renderer LiquidRenderer => liquidRenderer;
        public Transform GripAnchor => gripAnchor;
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
                    "Only the three authored patrons may own cafe cups.");
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
