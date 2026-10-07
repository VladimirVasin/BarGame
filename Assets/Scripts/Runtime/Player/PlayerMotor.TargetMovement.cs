using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PlayerMotor
    {
        private const float SideStepSpeed = 1.5f;
        private object movementTargetOwner;
        private Transform movementTarget;
        private bool inertialTargetMovement, movementTargetFrozen;
        private float targetYawVelocity;

        public bool MovementTargetActive => movementTargetOwner != null && movementTarget != null &&
            (!(movementTargetOwner is Object unityOwner) || unityOwner != null);

        /// <summary>Optional target-facing movement; ordinary actors retain tank controls.</summary>
        public bool SetMovementTarget(object owner, Transform target, bool useInertia = false)
        {
            if (owner == null || target == null || target == transform || target.IsChildOf(transform) ||
                (MovementTargetActive && !ReferenceEquals(owner, movementTargetOwner))) return false;
            if (!ReferenceEquals(owner, movementTargetOwner) || movementTarget != target ||
                inertialTargetMovement != useInertia)
            {
                targetYawVelocity = 0f;
                movementTargetFrozen = false;
            }
            movementTargetOwner = owner; movementTarget = target;
            inertialTargetMovement = useInertia;
            return true;
        }

        /// <summary>Holds a combat target owner's movement and momentum on its contact frame.</summary>
        public void SetMovementTargetFrozen(object owner, bool frozen)
        {
            if (owner == null || !ReferenceEquals(owner, movementTargetOwner) || !inertialTargetMovement) return;
            movementTargetFrozen = frozen;
        }

        public void ClearMovementTarget(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, movementTargetOwner)) return;
            movementTargetOwner = null; movementTarget = null;
            inertialTargetMovement = movementTargetFrozen = false;
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
            float remainingYaw = Vector3.SignedAngle(transform.forward, forward, Vector3.up);
            yawDelta = inertialTargetMovement
                ? AdvanceInertialYaw(remainingYaw, canFace ? turnSpeed * ownedTurnScale : 0f,
                    Time.deltaTime, ref targetYawVelocity)
                : Mathf.Clamp(remainingYaw, -maximumTurn, maximumTurn);
            transform.Rotate(0f, yawDelta, 0f);
            turnInput = turnSpeed * Time.deltaTime > .0001f ? yawDelta / (turnSpeed * Time.deltaTime) : 0f;
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float forwardSpeed = input.y >= 0f
                ? SnowSpeed(sprintRequested ? RunSpeed : MoveSpeed) : SnowSpeed(BackwardMoveSpeed, true);
            return (forward * (input.y * forwardSpeed) + right * (input.x * SnowSpeed(SideStepSpeed, true))) *
                (speedMultiplier * ownedMoveScale);
        }

        /// <summary>Reserve room only from voluntary inward travel, including its braking tail.
        /// Tangential/backward motion and the separate physical drift/push passes stay free.</summary>
        private Vector3 LimitOwnedApproach(Vector3 current, Vector3 travel)
        {
            if (ownedSpacingTarget == null || ownedMinimumSpacing <= 0f) return travel;
            Vector3 toward = ownedSpacingTarget.position - current;
            toward.y = 0f;
            float distance = toward.magnitude;
            if (distance <= .000001f) return travel;
            toward /= distance;
            float inward = Vector3.Dot(travel, toward);
            if (inward <= 0f) return travel;
            // A conservative radial bound also keeps the whole movement segment
            // outside the reserved circle without shortening its lateral component.
            float allowed = Mathf.Max(0f, distance - ownedMinimumSpacing);
            return travel - toward * Mathf.Max(0f, inward - allowed);
        }

        /// <summary>Bounded acceleration with a stopping-distance speed cap; no overshoot at the target.</summary>
        internal static float AdvanceInertialYaw(float angle, float maximumSpeed, float seconds, ref float velocity,
            bool retainSpeedWhileBraking = false, float braking = 900f)
        {
            const float acceleration = 600f;
            if (maximumSpeed <= 0f && !retainSpeedWhileBraking) { velocity = 0f; return 0f; }
            if (seconds <= 0f) return 0f;
            float desiredSpeed = Mathf.Sign(angle) * Mathf.Min(maximumSpeed,
                Mathf.Sqrt(2f * braking * Mathf.Abs(angle)));
            float rate = velocity * desiredSpeed < 0f || Mathf.Abs(desiredSpeed) < Mathf.Abs(velocity)
                ? braking : acceleration;
            // A combat phase may lower its requested rate while the planted body
            // is still turning. Brake that earned velocity instead of clipping it.
            float previous = retainSpeedWhileBraking ? velocity : Mathf.Clamp(velocity, -maximumSpeed, maximumSpeed);
            velocity = Mathf.MoveTowards(previous, desiredSpeed, rate * seconds);
            float delta = (previous + velocity) * .5f * seconds;
            if (Mathf.Abs(angle) <= .001f || (delta * angle >= 0f && Mathf.Abs(delta) >= Mathf.Abs(angle)))
            {
                velocity = 0f;
                return angle;
            }
            return delta;
        }
    }
}
