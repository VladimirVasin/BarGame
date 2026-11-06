using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianDirector
    {
        // Look up one short flight ahead, before two shoulders touch. The spatial
        // index caps this radius at its cell size and returns slots in stable order.
        private const float PassingLookAhead = CityPedestrianSpatialIndex.CellSize;
        private const float HalfLane = .43f;
        private const float FullLane = .77f;
        private const float BodyMargin = .05f;
        private const float ObstacleLookAhead = 1.2f;

        private struct PassingWidth
        {
            public int From, To;
            public Vector3 Centre;
            public double RecheckAt;
            public float Left, Right;
            public bool CentreClear;
            public bool SidesMeasured;
            public float ObstacleLane;
            public int EncounterPlusOne;
            public string EncounterSpawn;
        }

        private PassingWidth[] passingWidths;
        private double passingClock;

        private void SetEncounterMovement(CityPedestrianActor actor, int slot)
        {
            actor.SetAvoidance(1f, 0f);
            actor.SetPassingLane(0f, false);
            if (!actor.IsSpawned || actor.IsAttachedToVehicle || actor.IsPassingRetreat ||
                actor.IsYieldingToVehicle || actor.IsPersonalSpaceReacting) return;

            float heroDistance = PlanarSquaredDistance(actor.Position, player.position);
            int nearby = pedestrianIndex.Query(actor.Position, PassingLookAhead);
            if (!actor.TryGetPassingFrame(out Vector3 centre, out Vector3 travel)) return;
            Vector3 right = Vector3.Cross(Vector3.up, travel);
            ref var memory = ref passingWidths[slot];
            int retained = memory.EncounterPlusOne - 1;
            float retainedRange = PassingLookAhead + FullLane;
            if (retained >= 0 && (!actors[retained].IsSpawned ||
                actors[retained].SpawnAnchorId != memory.EncounterSpawn ||
                PlanarSquaredDistance(actor.Position, actors[retained].Position) > retainedRange * retainedRange))
                retained = -1;
            if (heroDistance > PassingLookAhead * PassingLookAhead && nearby <= 1 && retained < 0)
            {
                memory.EncounterPlusOne = 0;
                memory.EncounterSpawn = null;
                SetObstacleMovement(actor, slot, centre);
                return;
            }
            var heroBody = GetPlayerController();
            if (heroDistance <= PassingLookAhead * PassingLookAhead &&
                VerticalCapsulesOverlap(actor.Position, CityPedestrianActor.CollisionHeight, player,
                    heroBody, CityPedestrianActor.CollisionHeight, CollisionActivationPadding))
            {
                float clearance = actor.AgentRadius + (heroBody != null ? heroBody.radius : .32f) + BodyMargin;
                Vector3 delta = player.position - actor.Position;
                float along = Vector3.Dot(delta, travel);
                // Also yield to the hero catching up from behind. Once abreast,
                // hold the same shoulder until their bodies have cleared each other.
                bool catchingUp = Vector3.Dot(smoothedPlayerVelocity, travel) > actor.MovementSpeed;
                if ((along >= -clearance || catchingUp) && Mathf.Abs(Vector3.Dot(delta, right)) < clearance + FullLane)
                {
                    GiveWayTo(actor, slot, centre, right, player, along, clearance);
                    return;
                }
            }

            int encounter = -1;
            float nearest = float.PositiveInfinity, pace = 1f;
            // A shoulder step can put the same peer just outside the broad-phase
            // radius. Retain that encounter until the bodies clear, rather than
            // alternating a passing lane with a return to the centre every frame.
            for (int i = 0; i < nearby + (retained >= 0 ? 1 : 0); i++)
            {
                int otherSlot = i < nearby ? pedestrianIndex.GetActorIndex(i) : retained;
                var other = actors[otherSlot];
                if (other == actor || !VerticalCapsulesOverlap(actor.Position, CityPedestrianActor.CollisionHeight,
                        other.transform, other.CharacterController, CityPedestrianActor.CollisionHeight, CollisionActivationPadding))
                    continue;
                Vector3 delta = other.Position - actor.Position;
                float along = Vector3.Dot(delta, travel);
                float clearance = actor.AgentRadius + other.AgentRadius + BodyMargin;
                float lateralRange = clearance + FullLane + (otherSlot == retained ? FullLane : 0f);
                if (along < -clearance || Mathf.Abs(Vector3.Dot(delta, right)) > lateralRange) continue;
                bool walking = !other.IsPassingRetreat && !other.IsAtResidentDestination &&
                    other.TryGetPassingFrame(out _, out Vector3 otherTravel) && Vector3.Dot(travel, otherTravel) > .35f;
                if (walking)
                {
                    if (along > 0f && along < PedestrianAvoidanceDistance &&
                        Mathf.Abs(Vector3.Dot(delta, right)) < clearance)
                        pace = Mathf.Min(pace, other.IsYielding ? 0f : other.MovementSpeed / actor.MovementSpeed);
                    continue;
                }
                float distance = PlanarSquaredDistance(other.Position, actor.Position);
                if (distance >= nearest) continue;
                nearest = distance;
                encounter = otherSlot;
            }
            actor.SetAvoidance(pace, 0f);
            memory.EncounterPlusOne = encounter + 1;
            memory.EncounterSpawn = encounter >= 0 ? actors[encounter].SpawnAnchorId : null;
            if (encounter < 0)
            {
                SetObstacleMovement(actor, slot, centre);
                return;
            }
            var peer = actors[encounter];
            float bodyGap = actor.AgentRadius + peer.AgentRadius + BodyMargin;
            float longitudinal = Vector3.Dot(peer.Position - actor.Position, travel);
            bool hold = Mathf.Abs(longitudinal) < bodyGap + .17f &&
                Mathf.Abs(Vector3.Dot(peer.Position - actor.Position, right)) < bodyGap;
            if (!peer.IsPassingRetreat && !peer.IsAtResidentDestination && !actor.IsAtResidentDestination &&
                peer.TryGetPassingFrame(out Vector3 otherCentre, out Vector3 peerTravel) &&
                Vector3.Dot(travel, peerTravel) < -.5f)
            {
                // Both choose their right (or, together, their left). Their two
                // physical shoulders fit even when a whole body-width to one side does not.
                var own = MeasureWidth(actor, slot, centre);
                var theirs = MeasureWidth(peer, encounter, otherCentre);
                int sign = own.Right >= HalfLane && theirs.Right >= HalfLane ? 1 :
                    own.Left >= HalfLane && theirs.Left >= HalfLane ? -1 : 0;
                if (sign != 0)
                {
                    actor.SetPassingLane(sign * HalfLane, hold);
                    return;
                }
            }

            // A stationary worker on a narrow path can also make room. Otherwise
            // the same slot always yields first, so neither side waits for its mirror.
            if (peer.IsPassingRetreat)
                // A turnout may border the guest's next leg at a corner. Let
                // the guest walk around that parked body; a second retreat
                // would make both wait for each other to finish first.
                GiveWayTo(actor, slot, centre, right, peer.transform, longitudinal, bodyGap, false);
            else if (actor.IsAtResidentDestination || slot > encounter ||
                    !peer.TryGetPassingFrame(out _, out _))
                GiveWayTo(actor, slot, centre, right, peer.transform, longitudinal, bodyGap);
            else if (hold)
                actor.SetPassingLane(actor.LateralOffset, true);
        }

        private void GiveWayTo(CityPedestrianActor actor, int slot, Vector3 centre, Vector3 right,
            Transform priority, float along, float clearance, bool allowRetreat = true)
        {
            var room = MeasureWidth(actor, slot, centre);
            float theirSide = Vector3.Dot(priority.position - centre, right);
            // Prefer moving away from their track. On a turn one side may end
            // against a railing, so try the other when it leaves a larger gap.
            bool goRight = room.Right + .01f - theirSide >= room.Left + theirSide;
            float offset = goRight ? room.Right : -room.Left;
            float separation = Mathf.Abs(offset - theirSide);
            bool close = Mathf.Abs(along) <= clearance + .23f;
            // Opposing walkers negotiate paired half-lanes above. Here the
            // priority body's actual track needs a complete capsule gap; a
            // nominal half-lane alone cannot clear a centred hero.
            if (allowRetreat && separation < clearance &&
                actor.TryBeginPassingRetreat(priority, clearance)) return;
            bool hold = close && Mathf.Abs(Vector3.Dot(priority.position - actor.Position, right)) < clearance;
            actor.SetPassingLane(offset, hold);
        }

        private void SetObstacleMovement(CityPedestrianActor actor, int slot, Vector3 centre)
        {
            if (actor.IsAtResidentDestination) return;
            var room = MeasureWidth(actor, slot, centre, false);
            ref var width = ref passingWidths[slot];
            if (room.CentreClear)
            {
                width.ObstacleLane = 0f;
                return;
            }
            // Keep the chosen shoulder until the centre corridor is clear, so
            // approaching a prop does not alternate between left and right.
            float lane = width.ObstacleLane;
            if (lane > 0f && room.Right < lane || lane < 0f && room.Left < -lane) lane = 0f;
            if (lane == 0f)
                lane = room.Right >= HalfLane ? room.Right : room.Left >= HalfLane ? -room.Left : 0f;
            width.ObstacleLane = lane;
            if (lane != 0f) actor.SetPassingLane(lane, false);
        }

        private PassingWidth MeasureWidth(CityPedestrianActor actor, int slot, Vector3 centre,
            bool requireSides = true)
        {
            ref var width = ref passingWidths[slot];
            if (width.RecheckAt > passingClock && width.From == actor.PreviousNodeIndex &&
                width.To == actor.TargetNodeIndex && (width.Centre - centre).sqrMagnitude < .20f * .20f)
            {
                if (!requireSides || width.SidesMeasured) return width;
            }
            else
            {
                if (width.From != actor.PreviousNodeIndex || width.To != actor.TargetNodeIndex)
                    width.ObstacleLane = 0f;
                width.SidesMeasured = false;
                width.CentreClear = actor.CanWalkPassingLane(0f, ObstacleLookAhead);
                width.Left = width.Right = 0f;
            }
            width.From = actor.PreviousNodeIndex;
            width.To = actor.TargetNodeIndex;
            width.Centre = centre;
            width.RecheckAt = passingClock + .18f;
            if (requireSides || !width.CentreClear)
            {
                width.Right = Available(1f);
                width.Left = Available(-1f);
                width.SidesMeasured = true;
            }
            return width;

            float Available(float sign)
            {
                if (actor.CanWalkPassingLane(sign * FullLane, ObstacleLookAhead)) return FullLane;
                if (actor.CanWalkPassingLane(sign * HalfLane, ObstacleLookAhead)) return HalfLane;
                if (actor.CanWalkPassingLane(sign * CityPedestrianActor.MaximumLateralOffset, ObstacleLookAhead))
                    return CityPedestrianActor.MaximumLateralOffset;
                return 0f;
            }
        }
    }
}
