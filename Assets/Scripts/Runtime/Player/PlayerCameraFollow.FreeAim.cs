using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerCameraFollow
    {
        private object freeAimOwner;
        private Transform freeAimShoulder;
        private int freeAimInputFrame = -1;

        public bool FreeAimActive => freeAimOwner != null && freeAimShoulder != null &&
            (!(freeAimOwner is Object owner) || owner != null);

        /// <summary>Mouse-controlled shoulder view, independent of any opponent.</summary>
        public bool SetFreeAim(object owner, Transform shoulder)
        {
            if (owner == null || shoulder == null || controlledCamera == null || followTarget == null ||
                !shoulder.IsChildOf(followTarget) || fixedPoseActive || TargetLockActive ||
                (FreeAimActive && !ReferenceEquals(owner, freeAimOwner))) return false;
            if (ReferenceEquals(owner, freeAimOwner)) return true;
            targetYaw = controlledCamera.transform.eulerAngles.y;
            targetPitch = ClampOrbitPitch(Mathf.DeltaAngle(0f, controlledCamera.transform.eulerAngles.x));
            freeAimOwner = owner;
            freeAimShoulder = shoulder;
            freeAimInputFrame = -1;
            return true;
        }

        public void ClearFreeAim(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, freeAimOwner)) return;
            freeAimOwner = null;
            freeAimShoulder = null;
            freeAimInputFrame = -1;
            // Resume the chase camera from the accepted look direction.
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
            Vector2 input = SampleOrbitInputDegrees(Time.unscaledDeltaTime);
            RotateYaw(input.x);
            RotatePitch(input.y);
            UpdateFreeAimPose();
        }

        private void UpdateFreeAimPose()
        {
            Quaternion rotation = Quaternion.Euler(targetPitch, targetYaw, 0f);
            Vector3 origin = followTarget.position;
            origin.y = Mathf.Max(freeAimShoulder.position.y + .2f,
                origin.y + collisionRadius + collisionPadding);
            Vector3 side = rotation * Vector3.right;
            Vector3 pivot = origin + side * GetCameraClearance(origin, side, .8f, null);
            Vector3 back = -(rotation * Vector3.forward);
            Vector3 position = pivot + back * GetCameraClearance(pivot, back, 1.9f, null);
            controlledCamera.transform.SetPositionAndRotation(position, rotation);
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = TargetLockFieldOfView;
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            currentFocusPoint = origin;
            previousTargetPosition = followTarget.position;
        }
    }
}
