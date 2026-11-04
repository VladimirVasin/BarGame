using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianActor
    {
        private const float RefugeStep = .5f;
        private const float MaximumRefugeDistance = 5f;
        private const float RefugeRetrySeconds = .4f;
        private const float YieldArrivalDistance = .025f;
        private RaycastHit[] vehicleYieldHits;
        private Collider[] vehicleYieldOverlaps;
        private Vector3 vehicleRefuge;
        private bool hasVehicleRefuge;
        private float refugeRetry;
        private float vehicleMoveBlocked;

        public bool IsYieldingToVehicle { get; private set; }
        public bool IsReturningFromVehicle { get; private set; }
        public Vector3 VehicleReturnPosition { get; private set; }

        // Keep the graph state and wait/bench/resident destination intact. An
        // excursion owns movement until the actor has walked back to its feet.
        private bool AdvanceVehicleYield(float deltaTime,
            CityPedestrianVehicleTraffic traffic, out bool moving)
        {
            moving = false;
            bool eligible = MotionState == CityPedestrianMotionState.Walking ||
                MotionState == CityPedestrianMotionState.ApproachingStop ||
                MotionState == CityPedestrianMotionState.ApproachingBench ||
                MotionState == CityPedestrianMotionState.WaitingAtStop ||
                MotionState == CityPedestrianMotionState.WaitingAtDestination;
            if (!eligible || IsPersonalSpaceReacting) return false;
            if (!IsYieldingToVehicle)
            {
                if (traffic == null || deltaTime <= 0f ||
                    !traffic.Blocks(Position, agentRadius)) return false;
                VehicleReturnPosition = Position;
                IsYieldingToVehicle = true;
                refugeRetry = 0f;
            }
            if (deltaTime <= 0f) return true;

            bool returnBlocked = traffic != null &&
                traffic.Blocks(VehicleReturnPosition, agentRadius + .1f);
            bool feetBlocked = traffic != null && traffic.Blocks(Position, agentRadius);
            if (!returnBlocked && !feetBlocked)
            {
                IsReturningFromVehicle = true;
                moving = MoveForVehicle(VehicleReturnPosition, deltaTime, traffic);
                Vector3 remaining = Position - VehicleReturnPosition;
                remaining.y = 0f;
                if (remaining.sqrMagnitude <= YieldArrivalDistance * YieldArrivalDistance)
                {
                    // Finish at the interrupted point before any graph advance.
                    // MoveForVehicle clamps its step and never teleports there.
                    ResetVehicleYield();
                    IsYielding = false;
                }
                return true;
            }

            IsReturningFromVehicle = false;
            refugeRetry = Mathf.Max(0f, refugeRetry - deltaTime);
            if (hasVehicleRefuge && traffic.Blocks(vehicleRefuge, agentRadius + .1f))
                hasVehicleRefuge = false;
            if (!hasVehicleRefuge && refugeRetry <= 0f)
            {
                hasVehicleRefuge = TryFindVehicleRefuge(traffic, out vehicleRefuge);
                refugeRetry = RefugeRetrySeconds;
            }
            if (hasVehicleRefuge)
            {
                moving = MoveForVehicle(vehicleRefuge, deltaTime, null);
                Vector3 remaining = vehicleRefuge - Position;
                remaining.y = 0f;
                vehicleMoveBlocked = !moving && remaining.sqrMagnitude > .01f
                    ? vehicleMoveBlocked + deltaTime : 0f;
                if (vehicleMoveBlocked >= RefugeRetrySeconds)
                {
                    hasVehicleRefuge = false;
                    vehicleMoveBlocked = 0f;
                }
            }
            // No reachable refuge: retain the capsule and the vehicle's normal
            // obstacle braking, rather than forcing a way through scenery.
            return true;
        }

        private bool TryFindVehicleRefuge(CityPedestrianVehicleTraffic traffic,
            out Vector3 refuge)
        {
            Vector3 outward = traffic.EscapeDirection(Position);
            outward.y = 0f;
            if (outward.sqrMagnitude < .0001f) outward = transform.right;
            outward.Normalize();
            for (float distance = RefugeStep; distance <= MaximumRefugeDistance; distance += RefugeStep)
            {
                // Prefer the nearest verge, then its opposite. Diagonals let
                // someone beside a railing reach the end of the obstruction.
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = direction == 0 ? 0f : direction == 1 ? 180f :
                        direction == 2 ? 45f : direction == 3 ? -45f :
                        direction == 4 ? 135f : direction == 5 ? -135f :
                        direction == 6 ? 90f : -90f;
                    Vector3 target = Position + Quaternion.AngleAxis(angle, Vector3.up) * outward * distance;
                    Vector3 supported = walkableArea.Constrain(Position, target, agentRadius);
                    Vector3 supportShift = supported - target;
                    supportShift.y = 0f;
                    if (supportShift.sqrMagnitude > .0001f) continue;
                    target = supported;
                    if (traffic.Blocks(target, agentRadius + .1f) ||
                        traffic.BlocksEscapeSegment(Position, target, agentRadius) ||
                        !CanWalkVehicleSegment(Position, target)) continue;
                    refuge = target;
                    return true;
                }
            }
            refuge = Position;
            return false;
        }

        private bool CanWalkVehicleSegment(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            int steps = Mathf.Max(1, Mathf.CeilToInt(offset.magnitude / .2f));
            Vector3 previous = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 next = Vector3.Lerp(from, to, i / (float)steps);
                if (!walkableArea.Contains(next, agentRadius)) return false;
                Vector3 constrained = walkableArea.Constrain(previous, next, agentRadius);
                Vector3 difference = constrained - next;
                difference.y = 0f;
                if (difference.sqrMagnitude > .0001f || Mathf.Abs(constrained.y - previous.y) > .28f)
                    return false;
                previous = constrained;
            }
            float length = offset.magnitude;
            CapsuleAt(from, out Vector3 bottom, out Vector3 top, out float radius);
            if (length > .0001f)
            {
                if (vehicleYieldHits == null) vehicleYieldHits = new RaycastHit[16];
                int count;
                while (true)
                {
                    count = Physics.CapsuleCastNonAlloc(bottom, top, radius,
                        offset / length, vehicleYieldHits, length, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore);
                    if (count < vehicleYieldHits.Length) break;
                    Array.Resize(ref vehicleYieldHits, vehicleYieldHits.Length * 2);
                }
                for (int i = 0; i < count; i++)
                    if (IsVehicleWalkObstacle(vehicleYieldHits[i].collider)) return false;
            }
            CapsuleAt(to, out bottom, out top, out radius);
            if (vehicleYieldOverlaps == null) vehicleYieldOverlaps = new Collider[16];
            int overlaps;
            while (true)
            {
                overlaps = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
                    vehicleYieldOverlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (overlaps < vehicleYieldOverlaps.Length) break;
                Array.Resize(ref vehicleYieldOverlaps, vehicleYieldOverlaps.Length * 2);
            }
            for (int i = 0; i < overlaps; i++)
                if (IsVehicleWalkObstacle(vehicleYieldOverlaps[i])) return false;
            return true;
        }

        private bool IsVehicleWalkObstacle(Collider hit) => hit != null &&
            hit != characterController && !hit.transform.IsChildOf(transform);

        private void CapsuleAt(Vector3 position, out Vector3 bottom,
            out Vector3 top, out float radius)
        {
            radius = Mathf.Max(.05f, agentRadius - .02f);
            float height = characterController != null ? characterController.height : CollisionHeight;
            float centre = characterController != null ? characterController.center.y : CollisionCenterHeight;
            // A small sole lift excludes the supporting floor/kerb from the
            // horizontal clearance query; the controller still owns contact.
            bottom = position + Vector3.up * (centre - height * .5f + radius + .12f);
            top = position + Vector3.up * Mathf.Max(bottom.y - position.y,
                centre + height * .5f - radius);
        }

        private bool MoveForVehicle(Vector3 target, float deltaTime,
            CityPedestrianVehicleTraffic traffic)
        {
            Vector3 from = Position;
            Vector3 offset = target - from;
            offset.y = 0f;
            if (offset.sqrMagnitude <= .000001f) return false;
            Vector3 direction = offset.normalized;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), TurnSpeedDegrees * deltaTime);
            float facing = Mathf.Clamp01(1f - Vector3.Angle(transform.forward, direction) / 90f);
            Vector3 desired = Vector3.MoveTowards(from, target, speed * facing * deltaTime);
            if (traffic != null && (traffic.Blocks(desired, agentRadius + .1f) ||
                traffic.BlocksEscapeSegment(from, desired, agentRadius + .1f))) return false;
            Vector3 constrained = walkableArea.Constrain(from, desired, agentRadius);
            if (CollisionEnabled) characterController.Move(constrained - from);
            else transform.position = constrained;
            LastDisplacement = Position - from;
            return LastDisplacement.sqrMagnitude > .000001f;
        }

        private void ResetVehicleYield()
        {
            IsYieldingToVehicle = false;
            IsReturningFromVehicle = false;
            VehicleReturnPosition = Vector3.zero;
            hasVehicleRefuge = false;
            refugeRetry = 0f;
            vehicleMoveBlocked = 0f;
        }
    }
}
