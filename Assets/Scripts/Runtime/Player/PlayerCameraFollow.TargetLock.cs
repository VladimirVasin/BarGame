using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerCameraFollow
    {
        private const float TargetLockShoulderOffset = 1f;
        private const float TargetLockHeightOffset = .2f;
        private const float TargetLockDistance = 1.9f;
        private const float TargetLockFieldOfView = 55f;
        private const float TargetLockSmoothTime = .12f;
        private object targetLockOwner;
        private Transform targetLockRoot, targetLockAim, targetLockShoulder;
        private Vector3 targetLockAnchorPoint, targetLockAnchorVelocity;
        private float targetLockYaw, targetLockYawVelocity, targetLockDistance, targetLockDistanceVelocity;
        private bool targetLockInitialized;

        public bool TargetLockActive => targetLockOwner != null &&
            (!(targetLockOwner is Object unityOwner) || unityOwner != null) &&
            followTarget != null && targetLockRoot != null && targetLockAim != null && targetLockShoulder != null;

        /// <summary>Owns a right-shoulder view aimed at a second actor's live pose.</summary>
        public bool SetTargetLock(object owner, Transform opponentRoot, Transform opponentAim, Transform shoulderAnchor)
        {
            if (owner == null || controlledCamera == null || followTarget == null ||
                opponentRoot == null || opponentAim == null || shoulderAnchor == null ||
                !opponentAim.IsChildOf(opponentRoot) || !shoulderAnchor.IsChildOf(followTarget) ||
                opponentRoot == followTarget || (TargetLockActive && !ReferenceEquals(owner, targetLockOwner)))
                return false;
            if (ReferenceEquals(owner, targetLockOwner) && targetLockRoot == opponentRoot &&
                targetLockAim == opponentAim && targetLockShoulder == shoulderAnchor) return true;
            targetLockOwner = owner;
            targetLockRoot = opponentRoot; targetLockAim = opponentAim; targetLockShoulder = shoulderAnchor;
            targetLockInitialized = false;
            Snap();
            return true;
        }

        public void ClearTargetLock(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, targetLockOwner)) return;
            ResetTargetLockState();
            if (controlledCamera != null)
            {
                targetYaw = controlledCamera.transform.eulerAngles.y;
                targetPitch = ClampOrbitPitch(Mathf.DeltaAngle(0f, controlledCamera.transform.eulerAngles.x));
            }
            Snap();
        }

        private void ResetTargetLockState()
        {
            targetLockOwner = null;
            targetLockRoot = targetLockAim = targetLockShoulder = null;
            targetLockInitialized = false;
            targetLockAnchorVelocity = Vector3.zero;
            targetLockYawVelocity = targetLockDistanceVelocity = 0f;
        }

        private void UpdateTargetLock(float deltaTime, bool snap)
        {
            Vector3 aim = targetLockAim.position;
            Vector3 anchor = targetLockShoulder.position + Vector3.up * TargetLockHeightOffset;
            if (!IsFinite(aim) || !IsFinite(anchor)) return;
            // Keep the camera sphere clear of the support plane even when the
            // visible shoulder drops with a physical body while its root stays put.
            anchor.y = Mathf.Max(anchor.y, followTarget.position.y + collisionRadius + collisionPadding);
            snap |= !targetLockInitialized || ShouldSnapForTeleport();
            Vector3 axis = aim - targetLockShoulder.position;
            axis.y = 0f;
            float desiredYaw = axis.sqrMagnitude > .04f ? Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg :
                targetLockInitialized ? targetLockYaw : followTarget.eulerAngles.y;
            if (snap)
            {
                targetLockAnchorPoint = anchor; targetLockAnchorVelocity = Vector3.zero;
                targetLockYaw = desiredYaw; targetLockYawVelocity = targetLockDistanceVelocity = 0f;
            }
            else
            {
                targetLockAnchorPoint = Vector3.SmoothDamp(targetLockAnchorPoint, anchor,
                    ref targetLockAnchorVelocity, TargetLockSmoothTime, Mathf.Infinity, deltaTime);
                targetLockAnchorPoint = anchor + Vector3.ClampMagnitude(targetLockAnchorPoint - anchor, .05f);
                targetLockYaw = Mathf.SmoothDampAngle(targetLockYaw, desiredYaw,
                    ref targetLockYawVelocity, TargetLockSmoothTime, Mathf.Infinity, deltaTime);
                // Translation and yaw lag add together during a quick side step.
                // Bound both so the smoothed view still clears the raised arm,
                // even while the opponent remains exactly centred.
                targetLockYaw = desiredYaw + Mathf.Clamp(Mathf.DeltaAngle(desiredYaw, targetLockYaw), -1.5f, 1.5f);
            }
            Quaternion orientation = Quaternion.Euler(0f, targetLockYaw, 0f);
            Vector3 forward = orientation * Vector3.forward;
            float duelDistance = Mathf.Max(.2f, axis.magnitude);
            float boom = Mathf.Lerp(1.5f, TargetLockDistance, Mathf.InverseLerp(.7f, 1.8f, duelDistance));
            // At melee range a narrow shoulder offset aims through the hero's
            // torso. Keep that sight line outside his silhouette in open space.
            float sideOffset = Mathf.Clamp(.72f * (boom + duelDistance) / duelDistance,
                TargetLockShoulderOffset, 2.1f);
            Vector3 desiredPivot = targetLockAnchorPoint + orientation * Vector3.right * sideOffset;
            // Sweep the lateral shoulder offset too: a safe backward arm alone
            // could start on the far side of a wall beside the player's capsule.
            Vector3 safeOrigin = followTarget.position;
            safeOrigin.y = anchor.y;
            Vector3 offset = desiredPivot - safeOrigin;
            float offsetLength = offset.magnitude;
            Vector3 pivot = safeOrigin;
            if (offsetLength > .0001f)
                pivot += offset / offsetLength * GetCameraClearance(safeOrigin, offset / offsetLength,
                    offsetLength, targetLockRoot);
            float allowedDistance = GetCameraClearance(pivot, -forward, boom, targetLockRoot);
            if (snap || allowedDistance <= targetLockDistance)
            { targetLockDistance = allowedDistance; targetLockDistanceVelocity = 0f; }
            else
                targetLockDistance = Mathf.SmoothDamp(targetLockDistance, allowedDistance,
                    ref targetLockDistanceVelocity, distanceRecoverySmoothTime, Mathf.Infinity, deltaTime);
            Vector3 position = pivot - forward * targetLockDistance;
            Vector3 look = aim - position;
            if (look.sqrMagnitude < .0001f) look = forward;
            controlledCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look, Vector3.up));
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = TargetLockFieldOfView;
            currentFocusPoint = aim;
            previousTargetPosition = followTarget.position;
            targetLockInitialized = true;
        }
    }
}
