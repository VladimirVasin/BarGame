using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerCameraFollow
    {
        private object freeAimOwner;
        private Transform freeAimShoulder;
        private int freeAimInputFrame = -1;
        private float freeAimShoulderHeight;
        private Vector3 freeAimCameraOffset = new Vector3(.8f, 0f, -1.9f);
        private Quaternion freeAimRotationOffset = Quaternion.identity;
        private float freeAimFieldOfView = TargetLockFieldOfView;
        private float freeAimHitKick;
        private int freeAimHitKickFrame = -1;

        private void AdvanceFreeAimHitKick(float seconds)
        {
            if (freeAimHitKickFrame == Time.frameCount) return;
            freeAimHitKickFrame = Time.frameCount;
            freeAimHitKick = FreeAimActive ? freeAimHitKick * Mathf.Exp(-30f * Mathf.Max(0f, seconds)) : 0f;
        }

        public bool FreeAimActive => freeAimOwner != null && freeAimShoulder != null &&
            (!(freeAimOwner is Object owner) || owner != null);

        /// <summary>Mouse-controlled shoulder view, independent of any opponent.</summary>
        public bool SetFreeAim(object owner, Transform shoulder, bool preserveCurrentPose = false)
        {
            if (owner == null || shoulder == null || controlledCamera == null || followTarget == null ||
                !shoulder.IsChildOf(followTarget) || fixedPoseActive || TargetLockActive ||
                (FreeAimActive && !ReferenceEquals(owner, freeAimOwner))) return false;
            if (ReferenceEquals(owner, freeAimOwner)) return true;
            targetYaw = controlledCamera.transform.eulerAngles.y;
            targetPitch = ClampOrbitPitch(Mathf.DeltaAngle(0f, controlledCamera.transform.eulerAngles.x));
            freeAimOwner = owner;
            freeAimShoulder = shoulder;
            freeAimShoulderHeight = shoulder.position.y - followTarget.position.y;
            freeAimCameraOffset = new Vector3(.8f, 0f, -1.9f);
            freeAimRotationOffset = Quaternion.identity;
            freeAimFieldOfView = TargetLockFieldOfView;
            if (preserveCurrentPose)
            {
                // Focus normally has zero roll and FOV55, but its look pitch
                // need not lie inside the orbit limits. Preserve that residual
                // rotation too, rather than making the handoff a hidden snap.
                Quaternion accepted = Quaternion.Euler(targetPitch, targetYaw, 0f);
                freeAimRotationOffset = Quaternion.Inverse(accepted) * controlledCamera.transform.rotation;
                freeAimCameraOffset = Quaternion.Inverse(controlledCamera.transform.rotation) *
                    (controlledCamera.transform.position - GetFreeAimOrigin());
                freeAimFieldOfView = controlledCamera.fieldOfView;
            }
            // Input sampled before this ownership handoff belonged to the lock.
            // Begin consuming free look on the next frame, keeping this pose exact.
            freeAimInputFrame = preserveCurrentPose ? Time.frameCount : -1;
            freeAimHitKick = 0f;
            return true;
        }

        public void ClearFreeAim(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, freeAimOwner)) return;
            freeAimOwner = null;
            freeAimShoulder = null;
            freeAimInputFrame = -1;
            // Resume the chase camera from the accepted look direction.
            freeAimHitKick = 0f;
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            yawVelocity = pitchVelocity = 0f;
            currentFocusPoint = GetTargetFocusPoint();
        }

        /// <summary>Consume orbit once before a shot; LateUpdate only follows the completed body pose.</summary>
        public void PrepareFreeAimFrame(object owner)
        {
            if (!FreeAimActive || !ReferenceEquals(owner, freeAimOwner) || freeAimInputFrame == Time.frameCount) return;
            freeAimInputFrame = Time.frameCount;
            AdvanceFreeAimHitKick(Time.deltaTime);
            Vector2 input = SampleOrbitInputDegrees(Time.unscaledDeltaTime);
            RotateYaw(input.x);
            RotatePitch(input.y);
            UpdateFreeAimPose();
        }

        private void UpdateFreeAimPose()
        {
            Quaternion rotation = Quaternion.Euler(targetPitch + freeAimHitKick,
                targetYaw, -freeAimHitKick * .2f) * freeAimRotationOffset;
            Vector3 origin = GetFreeAimOrigin();
            // Keep the accepted framing for this aim lease, including a focus
            // handoff's vertical/lateral offset. Both real path segments still
            // stop at walls; the camera never recentres because a shot landed.
            Vector3 offset = rotation * new Vector3(freeAimCameraOffset.x, freeAimCameraOffset.y, 0f);
            float offsetLength = offset.magnitude;
            Vector3 pivot = offsetLength > .0001f ? origin + offset / offsetLength *
                GetCameraClearance(origin, offset / offsetLength, offsetLength) : origin;
            Vector3 back = rotation * new Vector3(0f, 0f, freeAimCameraOffset.z);
            float backLength = back.magnitude;
            Vector3 position = backLength > .0001f ? pivot + back / backLength *
                GetCameraClearance(pivot, back / backLength, backLength) : pivot;
            controlledCamera.transform.SetPositionAndRotation(position, rotation);
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = freeAimFieldOfView;
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            currentFocusPoint = origin;
            previousTargetPosition = followTarget.position;
        }

        private Vector3 GetFreeAimOrigin()
        {
            // Arm/torso recoil belongs to the character, not the camera boom
            // or the next centre ray used to aim the gun.
            Vector3 origin = followTarget.position;
            origin.y += Mathf.Max(freeAimShoulderHeight + .2f, collisionRadius + collisionPadding);
            return origin;
        }
    }
}
