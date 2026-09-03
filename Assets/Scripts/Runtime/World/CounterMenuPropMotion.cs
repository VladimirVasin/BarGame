using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared physical delivery/retrieval of a menu prop between an authored
    /// hand grip and its counter dock. Scene timelines supply normalized
    /// progress; this class owns all grip alignment and interpolation math.
    /// </summary>
    public sealed class CounterMenuPropMotion
    {
        private readonly Transform propRoot;
        private readonly Transform gripAnchor;
        private readonly Vector3 gripLocalPosition;
        private readonly Quaternion gripLocalRotation;
        private readonly Pose dockPose;

        private Transform carrier;
        private Vector3 deliveryStartPosition;
        private Quaternion deliveryStartRotation;
        private bool hasDeliveryStart;
        private Vector3 retrievalStartPosition;
        private Quaternion retrievalStartRotation;
        private bool hasRetrievalStart;

        public CounterMenuPropMotion(
            Transform configuredPropRoot,
            Transform configuredGripAnchor,
            Pose configuredDockPose,
            Transform configuredCarrier = null)
        {
            propRoot = configuredPropRoot != null
                ? configuredPropRoot
                : throw new ArgumentNullException(
                    nameof(configuredPropRoot));
            gripAnchor = configuredGripAnchor != null
                ? configuredGripAnchor
                : throw new ArgumentNullException(
                    nameof(configuredGripAnchor));
            if (!gripAnchor.IsChildOf(propRoot))
            {
                throw new ArgumentException(
                    "The menu grip must belong to its prop root.",
                    nameof(configuredGripAnchor));
            }

            dockPose = configuredDockPose;
            gripLocalPosition = propRoot.InverseTransformPoint(
                gripAnchor.position);
            gripLocalRotation = Quaternion.Inverse(propRoot.rotation) *
                                gripAnchor.rotation;
            carrier = configuredCarrier;
        }

        public Transform PropRoot => propRoot;
        public Transform GripAnchor => gripAnchor;
        public Transform Carrier => carrier;
        public Pose DockPose => dockPose;

        public void SetCarrier(Transform configuredCarrier)
        {
            carrier = configuredCarrier;
        }

        public void BeginDelivery()
        {
            AttachToCarrier();
            deliveryStartPosition = propRoot.position;
            deliveryStartRotation = propRoot.rotation;
            hasDeliveryStart = true;
            hasRetrievalStart = false;
        }

        public void EvaluateDelivery(float normalized)
        {
            float progress = Mathf.Clamp01(normalized);
            if (!hasDeliveryStart)
            {
                BeginDelivery();
            }

            float amount = Mathf.SmoothStep(0f, 1f, progress);
            propRoot.SetPositionAndRotation(
                Vector3.Lerp(
                    deliveryStartPosition,
                    dockPose.position,
                    amount),
                Quaternion.Slerp(
                    deliveryStartRotation,
                    dockPose.rotation,
                    amount));
        }

        public void BeginRetrieval()
        {
            retrievalStartPosition = propRoot.position;
            retrievalStartRotation = propRoot.rotation;
            hasRetrievalStart = true;
            hasDeliveryStart = false;
        }

        public void EvaluateRetrieval(float normalized)
        {
            if (!hasRetrievalStart)
            {
                BeginRetrieval();
            }

            ResolveCarrierPose(
                out Vector3 carriedPosition,
                out Quaternion carriedRotation);
            float amount = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(normalized));
            propRoot.SetPositionAndRotation(
                Vector3.Lerp(
                    retrievalStartPosition,
                    carriedPosition,
                    amount),
                Quaternion.Slerp(
                    retrievalStartRotation,
                    carriedRotation,
                    amount));
        }

        public void AttachToCarrier()
        {
            if (carrier == null)
            {
                SnapToDock();
                return;
            }

            ResolveCarrierPose(
                out Vector3 position,
                out Quaternion rotation);
            propRoot.SetPositionAndRotation(position, rotation);

            // Imported FBX hierarchies can retain a tiny scale/axis residue.
            // Finish against the actual authored grip contact.
            Quaternion correction = carrier.rotation *
                Quaternion.Inverse(gripAnchor.rotation);
            propRoot.rotation = correction * propRoot.rotation;
            propRoot.position += carrier.position - gripAnchor.position;
            hasRetrievalStart = false;
        }

        public void SnapToDock()
        {
            propRoot.SetPositionAndRotation(
                dockPose.position,
                dockPose.rotation);
            hasDeliveryStart = false;
            hasRetrievalStart = false;
        }

        private void ResolveCarrierPose(
            out Vector3 position,
            out Quaternion rotation)
        {
            if (carrier == null)
            {
                position = dockPose.position;
                rotation = dockPose.rotation;
                return;
            }

            rotation = carrier.rotation * Quaternion.Inverse(
                gripLocalRotation);
            Vector3 scaledGripOffset = Vector3.Scale(
                gripLocalPosition,
                propRoot.lossyScale);
            position = carrier.position - rotation * scaledGripOffset;
        }
    }
}
