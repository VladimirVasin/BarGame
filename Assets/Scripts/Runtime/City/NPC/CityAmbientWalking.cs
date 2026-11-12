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
            public bool Backing, InPocket, SurfaceBlocked, TightPassing, Kerb;

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
                ChooseMotion(ref walk, encounter, contacts, count);
                walk.NextProbe = .16f;
            }
            if (walk.Kerb && traffic != null && traffic.Blocks(feet, Radius))
            {
                walk.DesiredOffset = 0f;
                walk.SurfaceBlocked = true;
            }

            int direction = walk.Backing ? -walk.Direction : walk.Direction;
            float end = direction > 0 ? plan.Length(walk.Link) : 0f;
            if (!walk.InPocket && !walk.Backing && Mathf.Abs(end - walk.Along) < .02f)
            {
                if (Continue(ref walk, encounter, direction, feet, out bool connected))
                    direction = walk.Direction;
                else if (!connected)
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
            bool moving = (!walk.InPocket && !walk.SurfaceBlocked &&
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

        private void ChooseMotion(ref Route walk, int encounter, Contact[] contacts, int count)
        {
            Vector3 centre = plan.Sample(walk.Link, walk.Along, out Vector3 axis);
            Vector3 travel = axis * walk.Direction;
            Vector3 right = Vector3.Cross(Vector3.up, axis);
            Contact peer = FindEncounter(walk, encounter, centre + right * walk.Offset, travel, contacts, count);
            walk.SurfaceBlocked = false;
            walk.TightPassing = false;
            walk.Blocker = peer.Id;
            if (peer.Id == 0)
            {
                walk.YieldTo = 0;
                walk.Backing = walk.InPocket = false;
                if (Fits(walk, 0f, .55f)) walk.DesiredOffset = 0f;
                else if (!TryFindShoulder(walk, 0f, .20f, .55f, out walk.DesiredOffset))
                {
                    if (TryFindShoulder(walk, 0f, .35f, .55f, out walk.DesiredOffset, kerb: true)) walk.Kerb = true;
                    else { walk.DesiredOffset = walk.Offset; walk.SurfaceBlocked = true; }
                }
                return;
            }

            if (walk.YieldTo == 0 && peer.Id > 0 && !peer.Walk.Backing && peer.Walk.YieldTo == 0)
            {
                plan.Sample(peer.Walk.Link, peer.Walk.Along, out Vector3 peerAxis);
                if (Vector3.Dot(travel, peerAxis * peer.Walk.Direction) < -.5f)
                {
                    // "Right" is in each walker's own direction. Both check
                    // the same pair of shoulders before advancing.
                    for (int side = 1; side >= -1; side -= 2)
                    {
                        float offset = side * walk.Direction * PassingOffset;
                        if (!Fits(walk, offset, .55f) || !Fits(peer.Walk, side * peer.Walk.Direction * PassingOffset, .55f))
                            continue;
                        walk.DesiredOffset = offset;
                        return;
                    }
                }
            }

            if (peer.Id > encounter)
            {
                walk.DesiredOffset = 0f;
                return;
            }

            float peerSide = Vector3.Dot(peer.Feet - centre, right);
            float separation = Radius + peer.Radius + BodyMargin + .025f;
            walk.YieldTo = peer.Id;
            bool canPass = TryFindShoulder(walk, peerSide, separation, .55f, out walk.DesiredOffset);
            if (!canPass && peer.Id < 0)
            {
                float tight = Radius + peer.Radius + TightBodyMargin + .025f;
                canPass = TryFindShoulder(walk, peerSide, tight, .55f, out walk.DesiredOffset);
                walk.TightPassing = canPass;
                if (!canPass && TryFindShoulder(walk, peerSide, separation, .55f, out walk.DesiredOffset, kerb: true))
                    canPass = walk.Kerb = true;
            }
            bool hasShoulder = canPass || TryFindShoulder(walk, peerSide, separation, 0f, out walk.DesiredOffset);
            walk.InPocket = hasShoulder && !canPass;
            walk.Backing = !hasShoulder;
            if (walk.Backing && !Fits(walk, 0f, -.4f)) walk.SurfaceBlocked = true;
        }

        private Contact FindEncounter(Route walk, int encounter, Vector3 centre, Vector3 travel,
            Contact[] contacts, int count)
        {
            Contact nearest = default;
            float best = EncounterRange * EncounterRange;
            Vector3 right = Vector3.Cross(Vector3.up, travel);
            for (int i = 0; i < count; i++)
            {
                Contact peer = contacts[i];
                if (peer.Id == 0 || peer.Id == encounter) continue;
                Vector3 offset = peer.Feet - centre;
                float gap = Radius + peer.Radius + BodyMargin;
                float along = Vector3.Dot(offset, travel);
                if (Mathf.Abs(offset.y) > HeightTolerance || along < -gap - .05f ||
                    Mathf.Abs(Vector3.Dot(offset, right)) > gap + (walk.Kerb ? 1.1f : PassingOffset))
                    continue;
                float distance = offset.x * offset.x + offset.z * offset.z;
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

        private bool TryFindShoulder(Route walk, float blockerSide, float gap, float lookAhead, out float offset, bool kerb = false)
        {
            // Keep a working shoulder and probe intermediate widths on both
            // sides. A kerb or a post can rule out the nominal offset alone.
            offset = walk.DesiredOffset;
            if (kerb && (traffic == null || streetSupport == null || plan.IsStair(walk.Link))) return false;
            if (Mathf.Abs(offset - blockerSide) >= gap && Fits(walk, offset, lookAhead, kerb)) return true;
            int preferred = blockerSide > walk.Offset ? -1 : blockerSide < walk.Offset ? 1 : walk.Direction;
            for (int probe = 0; probe < 7; probe++)
            {
                float distance = gap + probe * (kerb ? .20f : .12f);
                for (int side = preferred, attempt = 0; attempt < 2; attempt++, side = -side)
                {
                    offset = blockerSide + side * distance;
                    if (Mathf.Abs(offset) <= (kerb ? 1.65f : MaximumShoulder) && Fits(walk, offset, lookAhead, kerb)) return true;
                }
            }
            offset = 0f;
            return false;
        }

        private bool Fits(Route walk, float offset, float lookAhead, bool kerb = false)
        {
            Vector3 current = Point(walk.Link, walk.Along, offset);
            Vector3 centre = Point(walk.Link, walk.Along, 0f);
            bool returning = walk.Kerb && Mathf.Abs(offset) < Mathf.Abs(walk.Offset);
            if (!CanTraverse(Point(walk.Link, walk.Along, walk.Offset), current, centre, kerb || returning, returning)) return false;
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
                if (peer.Id == 0 || peer.Id == encounter || Mathf.Abs(next.y - peer.Feet.y) > HeightTolerance)
                    continue;
                float margin = walk.TightPassing && peer.Id == walk.YieldTo ? TightBodyMargin : BodyMargin;
                float gap = Radius + peer.Radius + margin;
                float proposed = PlanarSquared(next - peer.Feet);
                if (proposed < gap * gap && proposed <= PlanarSquared(feet - peer.Feet))
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

        private bool Continue(ref Route walk, int encounter, int direction, Vector3 feet, out bool connected)
        {
            plan.Sample(walk.Link, walk.Along, out Vector3 incoming);
            incoming *= direction;
            int best = -1, nextDirection = 1;
            float bestScore = float.NegativeInfinity, bestOffset = 0f;
            bool bestKerb = false;
            connected = false;
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
                if ((choice & 3u) == 0 && plan.IsStair(link) && !plan.IsStair(walk.Link)) score += 1.15f;
                if (score <= bestScore || !TryJoin(link, distance + candidateDirection * .32f, feet, walk.Kerb,
                    out float offset, out bool kerb)) continue;
                best = link;
                bestScore = score;
                bestOffset = offset;
                bestKerb = kerb;
                nextDirection = candidateDirection;
            }
            if (best < 0) return false;
            walk = new Route(best, nextDirection, plan.ProjectDistance(best, feet));
            Vector3 centre = plan.Sample(best, walk.Along, out Vector3 tangent);
            walk.Offset = Vector3.Dot(feet - centre, Vector3.Cross(Vector3.up, tangent));
            walk.DesiredOffset = bestOffset;
            walk.Kerb = bestKerb;
            walk.NextProbe = .16f;
            return true;
        }

        private bool TryJoin(int link, float along, Vector3 feet, bool leavingKerb, out float offset, out bool kerb)
        {
            kerb = false;
            if (leavingKerb && !plan.IsStair(link))
            {
                Vector3 centre = plan.Sample(link, Mathf.Clamp(along, 0f, plan.Length(link)), out Vector3 tangent);
                float lateral = Vector3.Dot(feet - centre, Vector3.Cross(Vector3.up, tangent));
                if (Mathf.Abs(lateral) <= 1.65f && CanTraverse(feet, Point(link, along, lateral), centre, true))
                {
                    offset = lateral;
                    kerb = true;
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
