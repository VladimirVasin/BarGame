using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerCameraFollow
    {
        private object freeAimOwner;
        private Transform freeAimShoulder;
        private int freeAimInputFrame = -1;
        private int freeAimOrbitFrame = -1;
        private float freeAimShoulderHeight;
        private Vector3 freeAimCameraOffset = new Vector3(.8f, 0f, -1.9f);
        private Quaternion freeAimRotationOffset = Quaternion.identity;
        private float freeAimFieldOfView = TargetLockFieldOfView;
        private float freeAimHitKick;
        private int freeAimHitKickFrame = -1;
        private bool aimTransitionActive;
        private int aimTransitionFrame = -1;
        private float aimTransitionElapsed, aimTransitionFieldOfView;
        private Vector3 aimTransitionOriginOffset, aimTransitionCameraOffset;
        private Quaternion aimTransitionRotationOffset;

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
            if (preserveCurrentPose) CancelAimTransition();
            else BeginAimTransition(GetFreeAimOrigin());
            // Input sampled before this ownership handoff belonged to the lock.
            // Begin consuming free look on the next frame, keeping this pose exact.
            freeAimInputFrame = preserveCurrentPose ? Time.frameCount : -1;
            if (preserveCurrentPose) freeAimOrbitFrame = Time.frameCount;
            freeAimHitKick = 0f;
            return true;
        }

        public void ClearFreeAim(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, freeAimOwner)) return;
            bool smoothReturn = isActiveAndEnabled && !fixedPoseActive && !TargetLockActive &&
                (!(owner is Behaviour behaviour) || (behaviour != null && behaviour.isActiveAndEnabled));
            freeAimOwner = null;
            freeAimShoulder = null;
            freeAimInputFrame = -1;
            // Resume the chase camera from the accepted look direction.
            freeAimHitKick = 0f;
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            yawVelocity = pitchVelocity = 0f;
            currentFocusPoint = GetTargetFocusPoint();
            if (smoothReturn) BeginAimTransition(currentFocusPoint);
            else CancelAimTransition();
        }

        /// <summary>Consume orbit once before a shot; LateUpdate only follows the completed body pose.</summary>
        public void PrepareFreeAimFrame(object owner)
        {
            if (!FreeAimActive || !ReferenceEquals(owner, freeAimOwner) || freeAimInputFrame == Time.frameCount) return;
            freeAimInputFrame = Time.frameCount;
            AdvanceFreeAimHitKick(Time.deltaTime);
            if (freeAimOrbitFrame != Time.frameCount)
            {
                freeAimOrbitFrame = Time.frameCount;
                Vector2 input = SampleOrbitInputDegrees(Time.unscaledDeltaTime);
                RotateYaw(input.x);
                RotatePitch(input.y);
            }
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
            ApplyAimTransition(position, rotation, freeAimFieldOfView, origin);
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            currentFocusPoint = origin;
            previousTargetPosition = followTarget.position;
        }

        private void BeginAimTransition(Vector3 origin)
        {
            CancelAimTransition();
            if (controlledCamera == null || followTarget == null || aimTransitionDuration <= 0f) return;
            Quaternion orbit = Quaternion.Euler(targetPitch, targetYaw, 0f);
            Quaternion inverseOrbit = Quaternion.Inverse(orbit);
            aimTransitionOriginOffset = origin - followTarget.position;
            aimTransitionCameraOffset = inverseOrbit * (controlledCamera.transform.position - origin);
            aimTransitionRotationOffset = inverseOrbit * controlledCamera.transform.rotation;
            aimTransitionFieldOfView = controlledCamera.fieldOfView;
            aimTransitionFrame = Time.frameCount;
            aimTransitionActive = true;
        }

        private void CancelAimTransition()
        {
            aimTransitionActive = false;
            aimTransitionElapsed = 0f;
            aimTransitionFrame = -1;
        }

        private void OnDisable() => CancelAimTransition();

        private void ApplyAimTransition(Vector3 position, Quaternion rotation, float fieldOfView, Vector3 origin)
        {
            if (aimTransitionActive && ShouldSnapForTeleport()) CancelAimTransition();
            if (aimTransitionActive)
            {
                // PrepareFreeAimFrame applies this before the centre ray is read.
                // LateUpdate can follow the completed body without ticking twice.
                if (aimTransitionFrame != Time.frameCount &&
                    !GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused)
                {
                    aimTransitionFrame = Time.frameCount;
                    // Framing and the aimed rig share the same presentation clock.
                    aimTransitionElapsed += Mathf.Max(0f, Time.deltaTime);
                }
                float progress = aimTransitionDuration > 0f
                    ? Mathf.Clamp01(aimTransitionElapsed / aimTransitionDuration) : 1f;
                float weight = progress * progress * (3f - 2f * progress);
                // Apply accepted look input to both ends. Only the change of
                // framing is eased; mouse aim keeps its existing response.
                Quaternion orbit = Quaternion.Euler(targetPitch, targetYaw, 0f);
                Vector3 start = followTarget.position + aimTransitionOriginOffset +
                    orbit * aimTransitionCameraOffset;
                position = Vector3.Lerp(start, position, weight);
                rotation = Quaternion.Slerp(orbit * aimTransitionRotationOffset, rotation, weight);
                fieldOfView = Mathf.Lerp(aimTransitionFieldOfView, fieldOfView, weight);
                position = ResolveAimTransitionClearance(origin, position, rotation);
                if (progress >= 1f) CancelAimTransition();
            }
            controlledCamera.transform.SetPositionAndRotation(position, rotation);
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = fieldOfView;
        }

        private Vector3 ResolveAimTransitionClearance(Vector3 origin, Vector3 position, Quaternion rotation)
        {
            // Safe endpoints alone do not make an interpolation safe beside a
            // corner. Sweep its actual lateral/vertical reach and backward boom.
            Vector3 local = Quaternion.Inverse(rotation) * (position - origin);
            Vector3 side = rotation * new Vector3(local.x, local.y, 0f);
            float sideLength = side.magnitude;
            Vector3 pivot = sideLength > .0001f ? origin + side / sideLength *
                GetCameraClearance(origin, side / sideLength, sideLength) : origin;
            Vector3 back = rotation * new Vector3(0f, 0f, local.z);
            float backLength = back.magnitude;
            return backLength > .0001f ? pivot + back / backLength *
                GetCameraClearance(pivot, back / backLength, backLength) : pivot;
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
