using UnityEngine;

namespace BarPromenade
{
    /// <summary>Local walking and right of way for the collider-free street figures.</summary>
    internal sealed class CityAmbientWalking
    {
        internal const float Radius = .25f;
        private const float PassingOffset = .34f;
        private const float BodyMargin = .07f;
        private const float EncounterRange = 1.85f;
        private const float HeightTolerance = 1.1f;

        private readonly CityAmbientWalkPlan plan;
        private readonly RoadWalkableArea pavement;

        internal struct Route
        {
            public int Link, Direction;
            public float Along, Offset;
            public float DesiredOffset, NextProbe, Stopped;
            public int YieldTo, Blocker;
            public bool Backing, InPocket, SurfaceBlocked;

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

        internal CityAmbientWalking(CityAmbientWalkPlan plan, RoadWalkableArea pavement)
        {
            this.plan = plan;
            this.pavement = pavement;
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

            int direction = walk.Backing ? -walk.Direction : walk.Direction;
            float end = direction > 0 ? plan.Length(walk.Link) : 0f;
            if (!walk.InPocket && !walk.SurfaceBlocked && Mathf.Abs(end - walk.Along) < .02f)
            {
                // Keep an occupied lane usable. Its population cap applies to
                // new appearances, while actual clearance governs walking.
                if (Continue(ref walk, direction, feet))
                    direction = walk.Direction;
                else if (!walk.Backing)
                    walk.Direction = direction = -direction;
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
            walk.Stopped = moving ? 0f : walk.Stopped + delta;
            if (walk.SurfaceBlocked && walk.Blocker == 0 && walk.Stopped > 1.7f)
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
            Contact peer = FindEncounter(walk, encounter, centre, travel, contacts, count);
            walk.SurfaceBlocked = false;
            walk.Blocker = peer.Id;
            if (peer.Id == 0)
            {
                walk.YieldTo = 0;
                walk.Backing = walk.InPocket = false;
                if (Fits(walk, 0f, .55f)) walk.DesiredOffset = 0f;
                else if (Fits(walk, walk.Direction * PassingOffset, .55f)) walk.DesiredOffset = walk.Direction * PassingOffset;
                else if (Fits(walk, -walk.Direction * PassingOffset, .55f)) walk.DesiredOffset = -walk.Direction * PassingOffset;
                else
                {
                    walk.DesiredOffset = walk.Offset;
                    walk.SurfaceBlocked = true;
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
            bool hasShoulder = TryFindPocket(walk, peerSide, separation, out walk.DesiredOffset);
            walk.InPocket = hasShoulder && !Fits(walk, walk.DesiredOffset, .55f);
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
                    Mathf.Abs(Vector3.Dot(offset, right)) > gap + PassingOffset)
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

        private bool TryFindPocket(Route walk, float peerSide, float gap, out float offset)
        {
            for (int side = peerSide > 0f ? -1 : 1, probe = 0; probe < 2; probe++, side = -side)
            {
                offset = peerSide + side * gap;
                if (Mathf.Abs(offset) <= .85f && Fits(walk, offset, 0f)) return true;
            }
            offset = 0f;
            return false;
        }

        private bool Fits(Route walk, float offset, float lookAhead)
        {
            Vector3 current = Point(walk.Link, walk.Along, offset);
            if (!HasClearance(current)) return false;
            if (Mathf.Abs(walk.Offset - offset) > .12f &&
                !HasClearance(Point(walk.Link, walk.Along, (walk.Offset + offset) * .5f))) return false;
            if (lookAhead == 0f) return true;
            float ahead = Mathf.Clamp(walk.Along + lookAhead * walk.Direction, 0f, plan.Length(walk.Link));
            return HasClearance(Point(walk.Link, (walk.Along + ahead) * .5f, offset)) &&
                HasClearance(Point(walk.Link, ahead, offset));
        }

        private bool TryStep(ref Route walk, float along, float offset, Vector3 feet, float stride,
            int encounter, Contact[] contacts, int count, out Vector3 position)
        {
            position = feet;
            Vector3 target = Point(walk.Link, along, offset);
            Vector3 next = Vector3.MoveTowards(feet, target, stride);
            if (PlanarSquared(next - feet) < .0000001f || !pavement.Contains(next, Radius))
                return false;
            for (int i = 0; i < count; i++)
            {
                Contact peer = contacts[i];
                if (peer.Id == 0 || peer.Id == encounter || Mathf.Abs(next.y - peer.Feet.y) > HeightTolerance)
                    continue;
                float gap = Radius + peer.Radius + BodyMargin;
                float proposed = PlanarSquared(next - peer.Feet);
                if (proposed < gap * gap && proposed <= PlanarSquared(feet - peer.Feet))
                {
                    walk.Blocker = peer.Id;
                    return false;
                }
            }

            position = next;
            walk.Along = plan.ProjectDistance(walk.Link, next);
            Vector3 centre = plan.Sample(walk.Link, walk.Along, out Vector3 axis);
            walk.Offset = Vector3.Dot(next - centre, Vector3.Cross(Vector3.up, axis));
            return true;
        }

        private bool Continue(ref Route walk, int direction, Vector3 feet)
        {
            plan.Sample(walk.Link, walk.Along, out Vector3 incoming);
            incoming *= direction;
            int best = -1, nextDirection = 1;
            float bestDot = -.45f;
            var neighbours = plan.GetContinuations(walk.Link, direction, out int node);
            for (int i = 0; i < neighbours.Count; i++)
            {
                int link = neighbours[i];
                if (link == walk.Link || plan.Length(link) <= 0f) continue;
                int candidateDirection = plan.DirectionFrom(link, node);
                float distance = candidateDirection > 0 ? 0f : plan.Length(link);
                plan.Sample(link, distance, out Vector3 axis);
                float dot = Vector3.Dot(incoming, axis * candidateDirection);
                if (dot <= bestDot || !HasClearance(Point(link, distance + candidateDirection * .25f, 0f))) continue;
                best = link;
                bestDot = dot;
                nextDirection = candidateDirection;
            }
            if (best < 0) return false;
            walk = new Route(best, nextDirection, plan.ProjectDistance(best, feet));
            Vector3 centre = plan.Sample(best, walk.Along, out Vector3 tangent);
            walk.Offset = Vector3.Dot(feet - centre, Vector3.Cross(Vector3.up, tangent));
            return true;
        }

        private Vector3 Point(int link, float along, float offset)
        {
            Vector3 centre = plan.Sample(link, Mathf.Clamp(along, 0f, plan.Length(link)), out Vector3 axis);
            return centre + Vector3.Cross(Vector3.up, axis) * offset;
        }

        internal bool HasClearance(Vector3 feet) =>
            pavement.Contains(feet, Radius) && !Physics.CheckCapsule(
                feet + Vector3.up * (Radius + .08f), feet + Vector3.up * 1.43f, Radius,
                CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);

        private static float PlanarSquared(Vector3 offset) => offset.x * offset.x + offset.z * offset.z;
    }
}
