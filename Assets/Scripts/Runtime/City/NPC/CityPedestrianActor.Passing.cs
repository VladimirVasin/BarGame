using UnityEngine;

namespace BarPromenade
{
    internal static class CityPedestrianPassingGround
    {
        internal const float MaximumKerbStep = .32f;

        internal static bool TryKerb(CityStreetSupportPlan streets, CityPedestrianVehicleTraffic traffic,
            Vector3 centre, Vector3 desired, float radius, bool returning, out Vector3 feet)
        {
            feet = desired;
            if (streets == null || traffic == null) return false;
            var point = new Vector2(desired.x, desired.z);
            var lane = new Vector2(centre.x, centre.z);
            if (!streets.TrySampleTop(lane, FootstepGroundKind.Stone, out float pavementTop) ||
                Mathf.Abs(pavementTop - centre.y) > .13f ||
                !streets.TrySampleTop(point, out float top, out var kind) || Mathf.Abs(top - pavementTop) > .18f)
                return false;
            feet.y = top;
            if (kind == FootstepGroundKind.Stone) return true;
            if (kind != FootstepGroundKind.Concrete ||
                !streets.TrySampleTop(Vector2.MoveTowards(point, lane, MaximumKerbStep), FootstepGroundKind.Stone, out _))
                return false;
            return !traffic.Blocks(feet, radius) || returning && !traffic.PhysicallyBlocks(feet, radius);
        }

        internal static Vector3 PassingFacing(Vector3 travel, Vector3 step)
        {
            step.y = 0f;
            return step.sqrMagnitude > .000001f
                ? Vector3.RotateTowards(travel, step.normalized, 30f * Mathf.Deg2Rad, 0f) : travel;
        }
    }

    public sealed partial class CityPedestrianActor
    {
        // The feet stay on the paved strip. The physical capsule may overhang
        // a kerb slightly, with its full collision against walls and people.
        private const float PassingEdgeMargin = .10f;
        private float requestedPassingLane;
        private bool holdPassingProgress, returningToLaneCentre, requestedPassingEdge, requestedKerb, steppingOffKerb;

        /// <summary>Metres to the right of this actor's direction along its route.</summary>
        internal void SetPassingLane(float signedMetres, bool holdForward, bool alongEdge = false, bool kerb = false)
        {
            requestedPassingLane = signedMetres;
            holdPassingProgress = holdForward;
            requestedPassingEdge = alongEdge;
            requestedKerb = kerb;
        }

        private bool HasPassingRequest => holdPassingProgress || requestedPassingLane != 0f;

        internal bool TryGetPassingFrame(out Vector3 centre, out Vector3 travel)
        {
            if (TryGetPassingLeg(out var leg))
            {
                centre = leg.Sample(leg.Progress, out travel);
                return true;
            }
            centre = Position;
            travel = Vector3.zero;
            return false;
        }

        private bool TryGetPassingLeg(out PassingLeg leg)
        {
            leg = default;
            bool eligible = MotionState == CityPedestrianMotionState.Walking ||
                MotionState == CityPedestrianMotionState.WaitingAtDestination ||
                MotionState == CityPedestrianMotionState.ApproachingBench ||
                MotionState == CityPedestrianMotionState.ApproachingStop;
            if (!eligible || plan == null || previousNodeIndex < 0 || targetNodeIndex < 0 ||
                previousNodeIndex == targetNodeIndex || TryGetDirectWaitSlot(Position, out _)) return false;

            TryGetCurvedWalkingLink(out var link);
            leg = new PassingLeg(plan.Nodes[previousNodeIndex].Position, plan.Nodes[targetNodeIndex].Position,
                link, link != null && targetNodeIndex == link.SecondNodeIndex, Position);
            return leg.Length > .001f;
        }

        private readonly struct PassingLeg
        {
            private readonly Vector3 start, end;
            private readonly CityPedestrianLink link;
            private readonly bool forward;
            public readonly float Length, Progress;

            public PassingLeg(Vector3 start, Vector3 end, CityPedestrianLink link, bool forward, Vector3 current)
            {
                this.start = start;
                this.end = end;
                this.link = link;
                this.forward = forward;
                var point = new Vector2(current.x, current.z);
                if (link != null)
                {
                    Length = link.Path.Length;
                    float along = link.Path.Project(point).DistanceAlong;
                    Progress = forward ? along : Length - along;
                }
                else
                {
                    Vector2 delta = new Vector2(end.x - start.x, end.z - start.z);
                    Length = delta.magnitude;
                    Progress = Mathf.Clamp(Vector2.Dot(point - new Vector2(start.x, start.z), delta.normalized), 0f, Length);
                }
            }

            public Vector3 Sample(float along, out Vector3 direction)
            {
                if (link == null)
                {
                    direction = end - start;
                    direction.y = 0f;
                    direction.Normalize();
                    return Vector3.Lerp(start, end, along / Length);
                }
                CityRoadSample sample = link.Path.SampleDistance(forward ? along : Length - along);
                Vector2 tangent = forward ? sample.Tangent : -sample.Tangent;
                direction = new Vector3(tangent.x, 0f, tangent.y);
                return new Vector3(sample.Position.x, Height(sample.Position, along), sample.Position.y);
            }

            // Height samplers own the surveyed centreline. A lateral target may belong
            // to a different support patch, or have no support at all near a corner.
            private float Height(Vector2 point, float along) => link?.PathHeightSampler != null
                ? link.PathHeightSampler(point) : Mathf.Lerp(start.y, end.y, along / Length);
        }

        // Scenery and support decide which shoulder leads somewhere; the director
        // negotiates the moving capsules separately. Probe the authored curve,
        // rather than extending a tangent through the next railing or corner.
        internal bool CanWalkPassingLane(float signedMetres, float lookAhead, bool alongEdge = false,
            CityPedestrianVehicleTraffic roadTraffic = null)
        {
            if (!TryGetPassingLeg(out var leg)) return false;
            float footRadius = alongEdge ? Mathf.Max(.12f, agentRadius - PassingEdgeMargin) : agentRadius;
            Vector3 centre = leg.Sample(leg.Progress, out Vector3 direction);
            Vector3 previous = centre + Vector3.Cross(Vector3.up, direction) * signedMetres;
            if (!Probe(Position, ref previous, centre)) return false;
            float end = Mathf.Min(leg.Length, leg.Progress + lookAhead);
            const int probes = 4;
            for (int i = 1; i <= probes; i++)
            {
                centre = leg.Sample(Mathf.Lerp(leg.Progress, end, i / (float)probes), out direction);
                Vector3 next = centre + Vector3.Cross(Vector3.up, direction) * signedMetres;
                if (!Probe(previous, ref next, centre)) return false;
                previous = next;
            }
            return true;

            bool Probe(Vector3 from, ref Vector3 target, Vector3 routeCentre)
            {
                if (roadTraffic == null) return CanWalkVehicleSegment(from, target, true, footRadius);
                var streets = plan.TerraceLayout != null ? CityStreetSupportPlan.ForLayout(plan.TerraceLayout) : null;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, target) / .16f));
                Vector3 last = from;
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(from, target, i / (float)steps);
                    if (!walkableArea.Contains(point, footRadius) &&
                        !CityPedestrianPassingGround.TryKerb(streets, roadTraffic, routeCentre, point, agentRadius, false, out point))
                        return false;
                    if (Mathf.Abs(point.y - last.y) > .23f || !ClearVehicleWalkCapsule(last, point, true)) return false;
                    last = point;
                }
                target = last;
                return true;
            }
        }

        private bool TryAdvancePassingLane(float deltaTime, bool shouldYield, CityPedestrianVehicleTraffic traffic, out bool moving)
        {
            moving = false;
            if (!TryGetPassingLeg(out var leg))
            {
                returningToLaneCentre = false;
                steppingOffKerb = false;
                return false;
            }
            if (deltaTime <= 0f) return true;
            bool hold = holdPassingProgress || shouldYield || MotionState == CityPedestrianMotionState.WaitingAtDestination;
            if (!hold && leg.Length - leg.Progress <= .025f)
            {
                // Actors on parallel lanes pass the end of the segment beside its node.
                // Requiring both capsules to touch that same centre would merge the lanes.
                ReachTargetNode();
                return true;
            }

            float stride = speed * deltaTime;
            float next = Mathf.Min(leg.Length, leg.Progress + (hold ? 0f : stride * requestedSpeedScale));
            Vector3 centre = leg.Sample(next, out Vector3 direction);
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            Vector3 desired = centre + right * requestedPassingLane;
            Vector3 current = Position;
            Vector2 currentXZ = new Vector2(current.x, current.z);
            Vector2 moved = Vector2.MoveTowards(currentXZ, new Vector2(desired.x, desired.z), stride);
            desired = new Vector3(moved.x, centre.y, moved.y);
            bool alongEdge = requestedPassingEdge || returningToLaneCentre && !walkableArea.Contains(current, agentRadius);
            float footRadius = alongEdge ? Mathf.Max(.12f, agentRadius - PassingEdgeMargin) : agentRadius;
            bool allowKerb = requestedKerb || steppingOffKerb;
            bool onKerb = allowKerb && !walkableArea.Contains(desired, footRadius);
            Vector3 constrained;
            if (onKerb && plan.TerraceLayout != null && CityPedestrianPassingGround.TryKerb(
                CityStreetSupportPlan.ForLayout(plan.TerraceLayout), traffic, centre, desired, agentRadius,
                steppingOffKerb && !requestedKerb, out Vector3 kerb)) constrained = kerb;
            else constrained = walkableArea.Constrain(current, desired, footRadius);
            if (CollisionEnabled) characterController.Move(constrained - current);
            else transform.position = constrained;

            LastDisplacement = Position - current;
            lateralOffset = Vector3.Dot(Position - centre, right);
            returningToLaneCentre = Mathf.Abs(lateralOffset) > .04f;
            steppingOffKerb = allowKerb && !walkableArea.Contains(Position, footRadius);
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(CityPedestrianPassingGround.PassingFacing(direction, LastDisplacement), Vector3.up),
                TurnSpeedDegrees * deltaTime);
            Vector3 step = LastDisplacement;
            step.y = 0f;
            moving = step.sqrMagnitude > .000001f;
            // A requested shoulder is not proof that both capsules fit. Keep
            // the existing jam escape live when courtesy produces no movement.
            blockedTime = moving || MotionState == CityPedestrianMotionState.WaitingAtDestination
                ? 0f : blockedTime + deltaTime;
            return true;
        }

        private void OnDisable()
        {
            SetPassingLane(0f, false);
            returningToLaneCentre = steppingOffKerb = false;
            ResetPassingRetreat();
        }
    }
}
