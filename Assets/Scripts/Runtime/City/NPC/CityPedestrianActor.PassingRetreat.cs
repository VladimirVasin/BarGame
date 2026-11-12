using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianActor
    {
        // Most encounters only need a lateral step. A single-file flight can span
        // several links before there is room to step off the other person's line.
        private const float RetreatProbeInterval = .75f;
        private const int MaximumRetreatPoints = 64;
        private Vector3[] retreatPoints;
        private int retreatCount, retreatIndex;
        private Transform retreatGuest;
        private CityPedestrianActor retreatPeer;
        private string retreatPeerSpawn;
        private Vector3 guestAtEncounter, bayForward;
        private Vector3 bayCentre;
        private float retreatClearance, nextRetreatProbe;
        private bool returningFromBay, guestReachedBay;

        internal bool IsPassingRetreat => retreatCount > 0;

        internal bool TryBeginPassingRetreat(Transform priorityWalker, float clearance)
        {
            if (IsPassingRetreat) return true;
            if (nextRetreatProbe > 0f || priorityWalker == null || !CanRetreat()) return false;
            if (!TryGetPassingFrame(out _, out Vector3 routeForward)) return false;
            // An overtaking hero approaches from behind. Walk ahead to a turnout
            // in that case, rather than backing the yielding body into the hero.
            bool retreatBack = Vector3.Dot(priorityWalker.position - Position, routeForward) >= 0f;
            retreatPoints ??= new Vector3[MaximumRetreatPoints];
            retreatPoints[0] = Position;
            int count = 1;
            if (!SurveyRetreat(priorityWalker.position, clearance, retreatBack, ref count))
            {
                nextRetreatProbe = .65f;
                return false;
            }
            retreatCount = count;
            retreatIndex = 1;
            retreatGuest = priorityWalker;
            retreatPeer = priorityWalker.GetComponent<CityPedestrianActor>();
            retreatPeerSpawn = retreatPeer != null ? retreatPeer.SpawnAnchorId : null;
            guestAtEncounter = priorityWalker.position;
            retreatClearance = clearance;
            return true;
        }

        private bool CanRetreat() => CollisionEnabled && !IsYieldingToVehicle &&
            !IsPersonalSpaceReacting && previousNodeIndex >= 0 && targetNodeIndex >= 0 &&
            targetNodeIndex != previousNodeIndex &&
            (MotionState == CityPedestrianMotionState.Walking || MotionState == CityPedestrianMotionState.ApproachingStop ||
             MotionState == CityPedestrianMotionState.ApproachingBench || MotionState == CityPedestrianMotionState.WaitingAtDestination);

        private bool SurveyRetreat(Vector3 guest, float clearance, bool retreatBack, ref int count)
        {
            int toward = retreatBack ? previousNodeIndex : targetNodeIndex;
            int away = retreatBack ? targetNodeIndex : previousNodeIndex;
            Vector3 start = Position;
            // A committed walker may already occupy the painted crossing.
            // Clear that same chain to a pavement bay; never enter a fresh
            // crossing or park a yielding capsule in the carriageway.
            bool clearingCrossing=IsCrossingRoad;
            for (int legs = 0; legs < 12 && count < MaximumRetreatPoints - 1; legs++)
            {
                CityPedestrianLink link = null;
                foreach (int id in plan.GetLinkIndices(toward))
                    if (plan.Links[id].Other(toward) == away) { link = plan.Links[id]; break; }
                if (link == null || link.Kind == CityPedestrianLinkKind.Crosswalk && !clearingCrossing) return false;

                Vector3 from = plan.Nodes[link.FirstNodeIndex].Position;
                Vector3 to = plan.Nodes[link.SecondNodeIndex].Position;
                Vector2 axis = XZ(to) - XZ(from);
                float length = link.Path?.Length ?? axis.magnitude;
                if (length < .001f) return false;
                float sign = toward == link.FirstNodeIndex ? -1f : 1f;
                float end = sign < 0f ? 0f : length;
                float along = link.Path != null ? link.Path.Project(XZ(start)).DistanceAlong :
                    Mathf.Clamp(Vector2.Dot(XZ(start) - XZ(from), axis.normalized), 0f, length);
                Vector2 backwards;
                do
                {
                    along = Mathf.MoveTowards(along, end, RetreatProbeInterval);
                    Vector2 xz = Vector2.Lerp(XZ(from), XZ(to), along / length);
                    backwards = axis.normalized * sign;
                    if (link.Path != null)
                    {
                        CityRoadSample sample = link.Path.SampleDistance(along);
                        xz = sample.Position;
                        backwards = sample.Tangent * sign;
                    }
                    Vector3 centre = new Vector3(xz.x, RetreatHeight(link, xz, along / length), xz.y);
                    if (count >= MaximumRetreatPoints - 1) return false;
                    if (Vector2.Distance(XZ(retreatPoints[count - 1]), xz) > .035f)
                        retreatPoints[count++] = centre;
                    if(link.Kind==CityPedestrianLinkKind.Crosswalk) continue;
                    Vector3 side = new Vector3(-backwards.y, 0f, backwards.x);
                    if (Vector3.Dot(guest - centre, side) > 0f) side = -side;
                    for (int attempt = 0; attempt < 2; attempt++, side = -side)
                    {
                        Vector3 pocket = centre + side * (clearance + .06f);
                        // A route's height sampler is defined on its centreline. Validate
                        // the transverse step at that level against the live navigation and physics.
                        if (!CanWalkVehicleSegment(centre, pocket)) continue;
                        // Enter diagonally as soon as the opening admits the
                        // capsule instead of making a square turn in front of the follower.
                        if (count > 1 && CanWalkVehicleSegment(retreatPoints[count - 2], pocket))
                            retreatPoints[count - 1] = pocket;
                        else
                            retreatPoints[count++] = pocket;
                        bayCentre = centre;
                        bayForward = new Vector3(-backwards.x, 0f, -backwards.y);
                        return true;
                    }
                }
                while (along != end);

                if(plan.Nodes[toward].IsCrosswalkEntry) clearingCrossing=false;
                int next = -1;
                float best = -1f;
                foreach (int id in plan.GetLinkIndices(toward))
                {
                    CityPedestrianLink candidate = plan.Links[id];
                    int other = candidate.Other(toward);
                    if (other == away || candidate.Kind == CityPedestrianLinkKind.Crosswalk && !clearingCrossing) continue;
                    if (candidate.Kind == CityPedestrianLinkKind.HomeEntrance &&
                        link.Kind != CityPedestrianLinkKind.HomeEntrance) continue;
                    Vector2 direction = (XZ(plan.Nodes[other].Position) - XZ(plan.Nodes[toward].Position)).normalized;
                    float alignment = Vector2.Dot(backwards, direction);
                    if (alignment > best) { next = other; best = alignment; }
                }
                if (next < 0) return false;
                start = plan.Nodes[toward].Position;
                away = toward;
                toward = next;
            }
            return false;
        }

        private static Vector2 XZ(Vector3 world) => new Vector2(world.x, world.z);

        private float RetreatHeight(CityPedestrianLink link, Vector2 point, float fraction) =>
            link.PathHeightSampler != null ? link.PathHeightSampler(point) :
            Mathf.Lerp(plan.Nodes[link.FirstNodeIndex].Position.y, plan.Nodes[link.SecondNodeIndex].Position.y, fraction);

        private bool AdvancePassingRetreat(float deltaTime, CityPedestrianVehicleTraffic traffic, out bool moving)
        {
            moving = false;
            nextRetreatProbe = Mathf.Max(0f, nextRetreatProbe - deltaTime);
            if (!IsPassingRetreat) return false;
            if (!CanRetreat()) { ResetPassingRetreat(); return false; }
            if (deltaTime <= 0f) return true;

            if (!returningFromBay && GuestClearedRoute())
            {
                returningFromBay = true;
                retreatIndex = Mathf.Clamp(retreatIndex - 1, 0, retreatCount - 1);
            }

            if (retreatIndex >= retreatCount) return true; // Waiting in the bay.
            Vector3 target = retreatPoints[retreatIndex];
            Vector2 current = XZ(Position);
            float distance = Vector2.Distance(current, XZ(target));
            if (distance <= .04f)
            {
                retreatIndex += returningFromBay ? -1 : 1;
                if (retreatIndex < 0) ResetPassingRetreat();
                return true;
            }

            // The capsule still owns world collision. Its layer ignores other walkers,
            // so also sweep this short physical step against anyone newly in its way.
            float step = Mathf.Min(distance, speed * deltaTime);
            Vector3 next = Vector3.Lerp(Position, target, step / distance);
            if (!CanWalkVehicleSegment(Position, next) ||
                traffic != null && traffic.BlocksEscapeSegment(Position, next, agentRadius + .1f)) return true;
            moving = MoveForVehicle(target, deltaTime, traffic);
            return true;
        }

        private bool GuestClearedRoute()
        {
            if (retreatGuest == null || !retreatGuest.gameObject.activeInHierarchy ||
                retreatPeer != null && (!retreatPeer.IsSpawned || retreatPeer.SpawnAnchorId != retreatPeerSpawn))
                return true;

            Vector3 guest = retreatGuest.position;
            Vector3 fromBay = guest - bayCentre;
            if (XZ(fromBay).sqrMagnitude < 2.25f && Mathf.Abs(fromBay.y) < CollisionHeight)
                guestReachedBay = true;
            bool passed = guestReachedBay && Vector3.Dot(fromBay, bayForward) < -retreatClearance;
            float gap = retreatClearance + .22f;
            if (!passed)
            {
                // Wait while the guest approaches the turnout, even if the
                // few metres we have already backed along are temporarily clear.
                Vector3 from = guestAtEncounter;
                for (int i = 0; i < retreatCount - 1; i++)
                {
                    if (OccupiesCorridor(guest, from, retreatPoints[i], gap)) return false;
                    from = retreatPoints[i];
                }
                if (OccupiesCorridor(guest, from, bayCentre, gap)) return false;
            }
            Vector3 here = Position;
            for (int i = Mathf.Min(retreatIndex - 1, retreatCount - 1); i >= 0; i--)
            {
                if (OccupiesCorridor(guest, here, retreatPoints[i], gap)) return false;
                here = retreatPoints[i];
            }
            return true;
        }

        private static bool OccupiesCorridor(Vector3 guest, Vector3 from, Vector3 to, float gap)
        {
            Vector2 leg = XZ(to - from);
            float t = leg.sqrMagnitude > .00001f
                ? Mathf.Clamp01(Vector2.Dot(XZ(guest - from), leg) / leg.sqrMagnitude) : 0f;
            Vector3 offset = guest - Vector3.Lerp(from, to, t);
            return Mathf.Abs(offset.y) < CollisionHeight && XZ(offset).sqrMagnitude < gap * gap;
        }

        private void ResetPassingRetreat()
        {
            retreatCount = 0;
            retreatIndex = 0;
            retreatGuest = null;
            retreatPeer = null;
            retreatPeerSpawn = null;
            returningFromBay = false;
            guestReachedBay = false;
            nextRetreatProbe = 0f;
        }
    }
}
