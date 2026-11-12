using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Local walking and right of way for the collider-free street figures.</summary>
    internal sealed class CityAmbientWalking
    {
        internal const float Radius = .25f;
        private const float PassingOffset = .34f;
        private const float BodyMargin = .07f;
        private const float TightBodyMargin = .015f;
        private const float MaximumShoulder = .95f;
        private const float EncounterRange = 1.85f;
        private const float HeightTolerance = 1.1f;

        private readonly CityAmbientWalkPlan plan;
        private readonly RoadWalkableArea pavement;
        private readonly CityStreetSupportPlan streetSupport;
        private readonly CityPedestrianVehicleTraffic traffic;
        private Collider[] clearanceHits = new Collider[8];

        internal struct Route
        {
            public int Link, Direction;
            public float Along, Offset;
            public float DesiredOffset, NextProbe, Stopped;
            public int YieldTo, Blocker;
            public bool Backing, InPocket, Aligning, WaitingForRetreat, SurfaceBlocked, TightPassing, Kerb;

            public bool NeedsAlignment => Aligning && Mathf.Abs(Offset - DesiredOffset) > .02f;

            public Route(int link, int direction, float along) : this()
            {
                Link = link;
                Direction = direction;
                Along = along;
            }
        }

        // Positive IDs are background leases, in arrival order. The hero and
        // physical residents have negative IDs and always get right of way.
        internal readonly struct Contact
        {
            public readonly int Id;
            public readonly Vector3 Feet;
            public readonly float Radius;
            public readonly Route Walk;

            public Contact(int id, Vector3 feet, float radius, Route walk = default)
            {
                Id = id;
                Feet = feet;
                Radius = radius;
                Walk = walk;
            }
        }

        internal CityAmbientWalking(CityAmbientWalkPlan plan, RoadWalkableArea pavement,
            CityStreetSupportPlan streetSupport = null, CityPedestrianVehicleTraffic traffic = null)
        {
            this.plan = plan;
            this.pavement = pavement;
            this.streetSupport = streetSupport;
            this.traffic = traffic;
        }

        internal bool Advance(ref Route walk, int encounter, Vector3 feet, float speed, float delta,
            Contact[] contacts, int count, out Vector3 position, out Vector3 facing)
        {
            walk.NextProbe -= delta;
            if (walk.NextProbe <= 0f)
            {
                ChooseMotion(ref walk, encounter, feet, contacts, count);
                walk.NextProbe = .16f;
            }
            if (walk.Kerb && traffic != null && traffic.Blocks(feet, Radius))
            {
                walk.DesiredOffset = 0f;
                walk.SurfaceBlocked = true;
            }

            bool holdProgress = walk.InPocket || walk.WaitingForRetreat || walk.NeedsAlignment;
            int direction = walk.Backing ? -walk.Direction : walk.Direction;
            float end = direction > 0 ? plan.Length(walk.Link) : 0f;
            if (!holdProgress && Mathf.Abs(end - walk.Along) < .02f)
            {
                if (Continue(ref walk, encounter, direction, feet, out bool connected))
                    direction = walk.Backing ? -walk.Direction : walk.Direction;
                else if (!connected && !walk.Backing)
                    walk.Direction = direction = -direction;
                else
                    walk.SurfaceBlocked = true;
            }

            plan.Sample(walk.Link, walk.Along, out Vector3 axis);
            facing = axis * direction;
            float lateral = Mathf.MoveTowards(walk.Offset, walk.DesiredOffset, delta * .9f);
            float stride = speed * delta;
            float next = Mathf.Clamp(walk.Along + direction * stride, 0f, plan.Length(walk.Link));
            position = feet;
            bool moving = (!holdProgress && !walk.SurfaceBlocked &&
                    TryStep(ref walk, next, lateral, feet, stride * 1.22f, encounter, contacts, count, out position)) ||
                TryStep(ref walk, walk.Along, lateral, feet, stride, encounter, contacts, count, out position);
            if (moving) facing = CityPedestrianPassingGround.PassingFacing(facing, position - feet);
            walk.Stopped = moving ? 0f : walk.Stopped + delta;
            if (walk.SurfaceBlocked && walk.Blocker == 0 && walk.Stopped > 2.5f)
            {
                // A genuine scenery obstruction can send a short walk back.
                // A person retains priority until the encounter is clear.
                walk.Direction = -walk.Direction;
                walk.NextProbe = walk.Stopped = 0f;
            }
            return moving;
        }

        private void ChooseMotion(ref Route walk, int encounter, Vector3 feet, Contact[] contacts, int count)
        {
            Vector3 centre = plan.Sample(walk.Link, walk.Along, out Vector3 axis);
            Vector3 travel = axis * walk.Direction;
            Vector3 right = Vector3.Cross(Vector3.up, axis);
            Contact peer = FindEncounter(walk, encounter, feet, travel, contacts, count);
            bool finishAlignment = walk.Aligning && walk.YieldTo == peer.Id;
            walk.Backing = walk.InPocket = walk.Aligning = walk.WaitingForRetreat = false;
            walk.SurfaceBlocked = false;
            walk.TightPassing = false;
            walk.Blocker = peer.Id;
            if (peer.Id == 0)
            {
                walk.YieldTo = 0;
                if (Fits(walk, feet, 0f, .55f)) walk.DesiredOffset = 0f;
                else if (!TryFindShoulder(walk, feet, 0f, .20f, .55f, out walk.DesiredOffset))
                {
                    if (TryFindShoulder(walk, feet, 0f, .35f, .55f, out walk.DesiredOffset, kerb: true)) walk.Kerb = true;
                    else { walk.DesiredOffset = walk.Offset; walk.SurfaceBlocked = true; }
                }
                return;
            }

            if (walk.YieldTo == 0 && peer.Id > 0 && !peer.Walk.Backing && peer.Walk.YieldTo == 0)
            {
                plan.Sample(peer.Walk.Link, peer.Walk.Along, out Vector3 peerAxis);
                if (Vector3.Dot(travel, peerAxis * peer.Walk.Direction) < -.5f)
                {
                    var self = new Contact(encounter, feet, Radius, walk);
                    // Both walkers must be able to reach their shoulder from
                    // where they are now, even when they meet off centre.
                    for (int side = 1; side >= -1; side -= 2)
                    {
                        float offset = side * walk.Direction * PassingOffset;
                        if (!Fits(walk, feet, offset, .55f, peer) ||
                            !Fits(peer.Walk, peer.Feet, side * peer.Walk.Direction * PassingOffset, .55f, self))
                            continue;
                        walk.DesiredOffset = offset;
                        return;
                    }
                }
            }

            if (peer.Id > encounter)
            {
                walk.YieldTo = 0;
                float ahead = Vector3.Dot(peer.Feet - feet, travel);
                float room = Radius + peer.Radius + BodyMargin + .18f;
                // Following a retreating walker at the same pace closes the
                // space they need to change sides, even if both are moving.
                walk.WaitingForRetreat = peer.Walk.YieldTo == encounter &&
                    (peer.Walk.Backing || peer.Walk.NeedsAlignment) && ahead >= 0f && ahead < room;
                walk.DesiredOffset = !walk.WaitingForRetreat && Fits(walk, feet, 0f, .55f, peer) ? 0f : walk.Offset;
                return;
            }

            float peerSide = Vector3.Dot(peer.Feet - centre, right);
            float longitudinal = Vector3.Dot(peer.Feet - feet, travel);
            float separation = Radius + peer.Radius + BodyMargin + .025f;
            walk.YieldTo = peer.Id;
            bool canPass = TryFindShoulder(walk, feet, peerSide, separation, .55f, out walk.DesiredOffset, peer);
            if (!canPass && peer.Id < 0)
            {
                float tight = Radius + peer.Radius + TightBodyMargin + .025f;
                canPass = TryFindShoulder(walk, feet, peerSide, tight, .55f, out walk.DesiredOffset, peer, TightBodyMargin);
                walk.TightPassing = canPass;
                if (!canPass && TryFindShoulder(walk, feet, peerSide, separation, .55f, out walk.DesiredOffset, peer, kerb: true))
                    canPass = walk.Kerb = true;
            }
            if (canPass)
            {
                // If the open shoulder is across the peer's track, finish
                // changing sides before using up the gap opened by backing.
                bool crossingTrack = (walk.Offset - peerSide) * (walk.DesiredOffset - peerSide) <= 0f;
                walk.Aligning = finishAlignment || crossingTrack && Mathf.Abs(longitudinal) < separation + .3f;
                return;
            }
            walk.InPocket = TryFindShoulder(walk, feet, peerSide, separation, 0f, out walk.DesiredOffset, peer);
            if (walk.InPocket) return;

            walk.Backing = longitudinal >= 0f;
            walk.DesiredOffset = walk.Offset;
            walk.SurfaceBlocked = !Fits(walk, feet, walk.Offset, walk.Backing ? -.4f : .4f, peer, kerb: walk.Kerb);
        }

        private Contact FindEncounter(Route walk, int encounter, Vector3 feet, Vector3 travel,
            Contact[] contacts, int count)
        {
            Contact nearest = default;
            float best = EncounterRange * EncounterRange;
            Vector3 right = Vector3.Cross(Vector3.up, travel);
            for (int i = 0; i < count; i++)
            {
                Contact peer = contacts[i];
                if (peer.Id == 0 || peer.Id == encounter) continue;
                Vector3 offset = peer.Feet - feet;
                float gap = Radius + peer.Radius + BodyMargin;
                if (Mathf.Abs(offset.y) > HeightTolerance) continue;
                float distance = PlanarSquared(offset);
                if (peer.Id == walk.Blocker && walk.Stopped >= .24f && distance < (gap + .1f) * (gap + .1f))
                    return peer;
                float along = Vector3.Dot(offset, travel);
                if (along < -gap - .05f ||
                    Mathf.Abs(Vector3.Dot(offset, right)) > gap + (walk.Kerb ? 1.1f : PassingOffset))
                    continue;
                if (peer.Id == walk.YieldTo && distance < 7f * 7f) return peer;
                if (distance >= best) continue;
                if (peer.Id > 0 && !peer.Walk.Backing)
                {
                    plan.Sample(peer.Walk.Link, peer.Walk.Along, out Vector3 peerAxis);
                    if (Vector3.Dot(travel, peerAxis * peer.Walk.Direction) > .5f) continue;
                }
                nearest = peer;
                best = distance;
            }
            return nearest;
        }

        private bool TryFindShoulder(Route walk, Vector3 feet, float blockerSide, float gap, float lookAhead,
            out float offset, Contact peer = default, float margin = BodyMargin, bool kerb = false)
        {
            // Keep a working shoulder and probe intermediate widths on both
            // sides. A kerb or a post can rule out the nominal offset alone.
            offset = walk.DesiredOffset;
            if (kerb && (traffic == null || streetSupport == null || plan.IsStair(walk.Link))) return false;
            if (Mathf.Abs(offset - blockerSide) >= gap && Fits(walk, feet, offset, lookAhead, peer, margin, kerb)) return true;
            int preferred = blockerSide > walk.Offset ? -1 : blockerSide < walk.Offset ? 1 : walk.Direction;
            for (int probe = 0; probe < 7; probe++)
            {
                float distance = gap + probe * (kerb ? .20f : .12f);
                for (int side = preferred, attempt = 0; attempt < 2; attempt++, side = -side)
                {
                    offset = blockerSide + side * distance;
                    if (Mathf.Abs(offset) <= (kerb ? 1.65f : MaximumShoulder) &&
                        Fits(walk, feet, offset, lookAhead, peer, margin, kerb)) return true;
                }
            }
            offset = 0f;
            return false;
        }

        private bool Fits(Route walk, Vector3 feet, float offset, float lookAhead,
            Contact peer = default, float margin = BodyMargin, bool kerb = false)
        {
            Vector3 current = Point(walk.Link, walk.Along, offset);
            Vector3 centre = Point(walk.Link, walk.Along, 0f);
            bool returning = walk.Kerb && Mathf.Abs(offset) < Mathf.Abs(walk.Offset);
            if (!ClearsBody(feet, current, peer, margin) || !CanTraverse(feet, current, centre, kerb || returning, returning))
                return false;
            if (lookAhead == 0f) return true;
            float ahead = Mathf.Clamp(walk.Along + lookAhead * walk.Direction, 0f, plan.Length(walk.Link));
            return CanTraverse(current, Point(walk.Link, ahead, offset), centre, kerb);
        }

        private bool TryStep(ref Route walk, float along, float offset, Vector3 feet, float stride,
            int encounter, Contact[] contacts, int count, out Vector3 position)
        {
            position = feet;
            Vector3 target = Point(walk.Link, along, offset);
            Vector2 moved = Vector2.MoveTowards(new Vector2(feet.x, feet.z), new Vector2(target.x, target.z), stride);
            Vector3 next = new Vector3(moved.x, feet.y, moved.y);
            float progress = plan.ProjectDistance(walk.Link, next);
            Vector3 centre = plan.Sample(walk.Link, progress, out Vector3 axis);
            next.y = centre.y;
            if (PlanarSquared(next - feet) < .0000001f || !TryFooting(next, centre, walk.Kerb,
                Mathf.Abs(offset) < Mathf.Abs(walk.Offset), out next))
                return false;
            for (int i = 0; i < count; i++)
            {
                Contact peer = contacts[i];
                if (peer.Id == encounter) continue;
                float margin = walk.TightPassing && peer.Id == walk.YieldTo ? TightBodyMargin : BodyMargin;
                if (!ClearsBody(feet, next, peer, margin))
                {
                    walk.Blocker = peer.Id;
                    return false;
                }
            }

            position = next;
            walk.Along = progress;
            walk.Offset = Vector3.Dot(next - centre, Vector3.Cross(Vector3.up, axis));
            if (walk.Kerb && Mathf.Abs(walk.Offset) < .2f) walk.Kerb = false;
            return true;
        }

        private static bool ClearsBody(Vector3 from, Vector3 to, Contact peer, float margin)
        {
            if (peer.Id == 0 || Mathf.Abs(to.y - peer.Feet.y) > HeightTolerance) return true;
            Vector3 offset = from - peer.Feet;
            Vector3 step = to - from;
            float toward = -(offset.x * step.x + offset.z * step.z);
            // A peer that has already stepped inside our margin can still
            // be cleared by a step whose distance only increases.
            if (toward <= 0f) return true;
            float closest = Mathf.Min(1f, toward / PlanarSquared(step));
            float gap = Radius + peer.Radius + margin;
            return PlanarSquared(offset + step * closest) >= gap * gap;
        }

        private bool Continue(ref Route walk, int encounter, int direction, Vector3 feet, out bool connected)
        {
            plan.Sample(walk.Link, walk.Along, out Vector3 incoming);
            incoming *= direction;
            int best = -1, nextDirection = 1;
            float bestScore = float.NegativeInfinity, bestOffset = 0f;
            bool bestKerb = false;
            connected = false;
            bool keepLane = walk.YieldTo != 0 || walk.Kerb;
            var neighbours = plan.GetContinuations(walk.Link, direction, out int node);
            for (int i = 0; i < neighbours.Count; i++)
            {
                int link = neighbours[i];
                if (link == walk.Link || plan.Length(link) <= 0f) continue;
                connected = true;
                int candidateDirection = plan.DirectionFrom(link, node);
                float distance = candidateDirection > 0 ? 0f : plan.Length(link);
                plan.Sample(link, distance, out Vector3 axis);
                float score = Vector3.Dot(incoming, axis * candidateDirection);
                uint choice = CityPedestrianStableHash.Combine((uint)encounter, (uint)node);
                if (!walk.Backing && (choice & 3u) == 0 && plan.IsStair(link) && !plan.IsStair(walk.Link)) score += 1.15f;
                if (score <= bestScore || !TryJoin(link, distance + candidateDirection * .32f, feet, keepLane, walk.Kerb,
                    out float offset, out bool kerb)) continue;
                best = link;
                bestScore = score;
                bestOffset = offset;
                bestKerb = kerb;
                nextDirection = candidateDirection;
            }
            if (best < 0) return false;
            // A short link can end before the yielding walker has made
            // enough room. Keep the encounter and the intended direction
            // while backing across that join.
            if (!keepLane) walk = default;
            walk.Link = best;
            walk.Direction = walk.Backing ? -nextDirection : nextDirection;
            walk.Along = plan.ProjectDistance(best, feet);
            Vector3 centre = plan.Sample(best, walk.Along, out Vector3 tangent);
            walk.Offset = Vector3.Dot(feet - centre, Vector3.Cross(Vector3.up, tangent));
            walk.DesiredOffset = bestOffset;
            walk.Kerb = bestKerb;
            walk.SurfaceBlocked = false;
            walk.NextProbe = keepLane ? 0f : .16f;
            return true;
        }

        private bool TryJoin(int link, float along, Vector3 feet, bool keepLane, bool leavingKerb, out float offset, out bool kerb)
        {
            kerb = false;
            if (keepLane)
            {
                bool allowKerb = leavingKerb && !plan.IsStair(link);
                Vector3 centre = plan.Sample(link, Mathf.Clamp(along, 0f, plan.Length(link)), out Vector3 tangent);
                float lateral = Vector3.Dot(feet - centre, Vector3.Cross(Vector3.up, tangent));
                if (Mathf.Abs(lateral) <= (allowKerb ? 1.65f : MaximumShoulder) &&
                    CanTraverse(feet, Point(link, along, lateral), centre, allowKerb))
                {
                    offset = lateral;
                    kerb = allowKerb;
                    return true;
                }
            }
            offset = 0f;
            if (CanTraverse(feet, Point(link, along, 0f))) return true;
            for (int i = 1; i <= 4; i++)
            for (int side = 1; side >= -1; side -= 2)
            {
                offset = side * i * .18f;
                if (CanTraverse(feet, Point(link, along, offset))) return true;
            }
            return false;
        }

        private bool CanTraverse(Vector3 from, Vector3 to, Vector3 centre = default, bool kerb = false, bool returning = false)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .2f));
            for (int step = 1; step <= steps; step++)
                if (!TryFooting(Vector3.Lerp(from, to, step / (float)steps), centre, kerb, returning, out _)) return false;
            return true;
        }

        private Vector3 Point(int link, float along, float offset)
        {
            Vector3 centre = plan.Sample(link, Mathf.Clamp(along, 0f, plan.Length(link)), out Vector3 axis);
            return centre + Vector3.Cross(Vector3.up, axis) * offset;
        }

        internal bool HasClearance(Vector3 feet) => TryFooting(feet, default, false, false, out _);

        private bool TryFooting(Vector3 desired, Vector3 centre, bool kerb, bool returning, out Vector3 feet)
        {
            feet = desired;
            if (!pavement.Contains(feet, Radius) && (!kerb || !CityPedestrianPassingGround.TryKerb(
                streetSupport, traffic, centre, desired, Radius, returning, out feet))) return false;
            Vector3 lower = feet + Vector3.up * (Radius + .08f), upper = feet + Vector3.up * 1.43f;
            int count;
            while ((count = Physics.OverlapCapsuleNonAlloc(lower, upper, Radius, clearanceHits,
                       CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore)) == clearanceHits.Length)
                Array.Resize(ref clearanceHits, clearanceHits.Length * 2);
            for (int i = 0; i < count; i++)
                if (!(clearanceHits[i] is CharacterController)) return false;
            return true;
        }

        private static float PlanarSquared(Vector3 offset) => offset.x * offset.x + offset.z * offset.z;
    }
}
