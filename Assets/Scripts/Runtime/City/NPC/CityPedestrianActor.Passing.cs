using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianActor
    {
        private float requestedPassingLane;
        private bool holdPassingProgress, returningToLaneCentre;

        /// <summary>Metres to the right of this actor's direction along its route.</summary>
        internal void SetPassingLane(float signedMetres, bool holdForward)
        {
            requestedPassingLane = signedMetres;
            holdPassingProgress = holdForward;
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

        /// <summary>Reject clipped or unsupported side positions before promising a clear lane.</summary>
        internal bool CanOccupyPassingPoint(Vector3 candidate)
        {
            if (!walkableArea.Contains(candidate, agentRadius) || !TryGetPassingLeg(out var leg)) return false;
            candidate.y = leg.Sample(leg.Progress, out _).y;
            return CanWalkVehicleSegment(Position, candidate);
        }

        private bool TryAdvancePassingLane(float deltaTime, bool shouldYield, out bool moving)
        {
            moving = false;
            if (!TryGetPassingLeg(out var leg))
            {
                returningToLaneCentre = false;
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
            Vector3 constrained = walkableArea.Constrain(current, desired, agentRadius);
            if (CollisionEnabled) characterController.Move(constrained - current);
            else transform.position = constrained;

            LastDisplacement = Position - current;
            lateralOffset = Vector3.Dot(Position - centre, right);
            returningToLaneCentre = Mathf.Abs(lateralOffset) > .04f;
            // Keep an ordinary forward-facing gait on the route, even while stepping aside.
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), TurnSpeedDegrees * deltaTime);
            Vector3 step = LastDisplacement;
            step.y = 0f;
            moving = step.sqrMagnitude > .000001f;
            blockedTime = 0f;
            return true;
        }

        private void OnDisable()
        {
            SetPassingLane(0f, false);
            returningToLaneCentre = false;
            ResetPassingRetreat();
        }
    }
}
