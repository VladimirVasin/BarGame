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

        private struct PassingWidth
        {
            public int From, To;
            public Vector3 Centre;
            public double RecheckAt;
            public float Left, Right;
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
            if (heroDistance > PassingLookAhead * PassingLookAhead && nearby <= 1) return;
            if (!actor.TryGetPassingFrame(out Vector3 centre, out Vector3 travel)) return;
            Vector3 right = Vector3.Cross(Vector3.up, travel);
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
            for (int i = 0; i < nearby; i++)
            {
                int otherSlot = pedestrianIndex.GetActorIndex(i);
                var other = actors[otherSlot];
                if (other == actor || !VerticalCapsulesOverlap(actor.Position, CityPedestrianActor.CollisionHeight,
                        other.transform, other.CharacterController, CityPedestrianActor.CollisionHeight, CollisionActivationPadding))
                    continue;
                Vector3 delta = other.Position - actor.Position;
                float along = Vector3.Dot(delta, travel);
                float clearance = actor.AgentRadius + other.AgentRadius + BodyMargin;
                if (along < -clearance || Mathf.Abs(Vector3.Dot(delta, right)) > clearance + FullLane) continue;
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
            if (encounter < 0) return;
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
                var own = MeasureWidth(actor, slot, centre, right);
                var theirs = MeasureWidth(peer, encounter, otherCentre, Vector3.Cross(Vector3.up, peerTravel));
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
            if (!peer.IsPassingRetreat && (actor.IsAtResidentDestination || slot > encounter ||
                    !peer.TryGetPassingFrame(out _, out _)))
                GiveWayTo(actor, slot, centre, right, peer.transform, longitudinal, bodyGap);
            else if (hold)
                actor.SetPassingLane(actor.LateralOffset, true);
        }

        private void GiveWayTo(CityPedestrianActor actor, int slot, Vector3 centre, Vector3 right,
            Transform priority, float along, float clearance)
        {
            var room = MeasureWidth(actor, slot, centre, right);
            float theirSide = Vector3.Dot(priority.position - centre, right);
            // Prefer moving away from their track. On a turn one side may end
            // against a railing, so try the other when it leaves a larger gap.
            bool goRight = room.Right + .01f - theirSide >= room.Left + theirSide;
            float offset = goRight ? room.Right : -room.Left;
            float separation = Mathf.Abs(offset - theirSide);
            bool close = Mathf.Abs(along) <= clearance + .23f;
            // A two-way flight can fit a walker on either half although the
            // handrail prevents a whole body-width of offset from its centre.
            // Hold that shoulder for the other walker. Backtrack only in single file.
            if (Mathf.Abs(offset) < HalfLane && separation < clearance &&
                actor.TryBeginPassingRetreat(priority, clearance)) return;
            bool hold = close && Mathf.Abs(Vector3.Dot(priority.position - actor.Position, right)) < clearance;
            actor.SetPassingLane(offset, hold);
        }

        private PassingWidth MeasureWidth(CityPedestrianActor actor, int slot, Vector3 centre, Vector3 right)
        {
            ref var width = ref passingWidths[slot];
            if (width.RecheckAt > passingClock && width.From == actor.PreviousNodeIndex &&
                width.To == actor.TargetNodeIndex && (width.Centre - centre).sqrMagnitude < .20f * .20f) return width;
            width.From = actor.PreviousNodeIndex;
            width.To = actor.TargetNodeIndex;
            width.Centre = centre;
            width.RecheckAt = passingClock + .18f;
            width.Right = Available(1f);
            width.Left = Available(-1f);
            return width;

            float Available(float sign)
            {
                Vector3 axis = right * sign;
                if (actor.CanOccupyPassingPoint(centre + axis * FullLane)) return FullLane;
                if (actor.CanOccupyPassingPoint(centre + axis * HalfLane)) return HalfLane;
                if (actor.CanOccupyPassingPoint(centre + axis * CityPedestrianActor.MaximumLateralOffset))
                    return CityPedestrianActor.MaximumLateralOffset;
                return 0f;
            }
        }
    }
}
