using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerMotor
    {
        private const float SideStepSpeed = 1.5f;
        private object movementTargetOwner;
        private Transform movementTarget;

        public bool MovementTargetActive => movementTargetOwner != null && movementTarget != null &&
            (!(movementTargetOwner is Object unityOwner) || unityOwner != null);

        /// <summary>Optional target-facing movement; ordinary actors retain tank controls.</summary>
        public bool SetMovementTarget(object owner, Transform target)
        {
            if (owner == null || target == null || target == transform || target.IsChildOf(transform) ||
                (MovementTargetActive && !ReferenceEquals(owner, movementTargetOwner))) return false;
            movementTargetOwner = owner; movementTarget = target;
            return true;
        }

        public void ClearMovementTarget(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, movementTargetOwner)) return;
            movementTargetOwner = null; movementTarget = null;
            StopPlanarMotion();
        }

        private Vector3 ResolveTargetMovement(Vector2 input, bool sprintRequested, bool canFace,
            out float yawDelta, out float turnInput)
        {
            Vector3 forward = movementTarget.position - transform.position;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .0004f ? forward.normalized : transform.forward;
            float turnSpeed = TurnSpeedDegreesPerSecond * speedMultiplier * balanceYawScale;
            float maximumTurn = canFace ? turnSpeed * ownedTurnScale * Time.deltaTime : 0f;
            yawDelta = Mathf.Clamp(Vector3.SignedAngle(transform.forward, forward, Vector3.up), -maximumTurn, maximumTurn);
            transform.Rotate(0f, yawDelta, 0f);
            turnInput = turnSpeed * Time.deltaTime > .0001f ? yawDelta / (turnSpeed * Time.deltaTime) : 0f;
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float forwardSpeed = input.y >= 0f ? (sprintRequested ? RunSpeed : MoveSpeed) : BackwardMoveSpeed;
            return (forward * (input.y * forwardSpeed) + right * (input.x * SideStepSpeed)) *
                (speedMultiplier * ownedMoveScale);
        }
    }
}
