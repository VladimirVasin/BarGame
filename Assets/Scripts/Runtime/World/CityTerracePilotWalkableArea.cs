using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Capsule clearance against the exposed boundary of the whole pilot.
    /// Shared floor/ramp seams remain open; physics owns vertical motion.
    /// </summary>
    public sealed class CityTerracePilotWalkableArea : IWalkableArea
    {
        private const float Epsilon = .0001f;
        private const float SweepSpacing = .1f;
        private const int MaximumSweepSteps = 256;
        private readonly CityTerracePilotPlan plan;
        private readonly Rect[] supports;
        private readonly Edge[] boundaries;

        private readonly struct Edge
        {
            internal readonly Vector2 Start, End;
            internal Edge(Vector2 start, Vector2 end) { Start = start; End = end; }
            internal float SquaredDistance(Vector2 point)
            {
                Vector2 delta = End - Start;
                float along = Mathf.Clamp01(Vector2.Dot(point - Start, delta) / delta.sqrMagnitude);
                return (point - (Start + delta * along)).sqrMagnitude;
            }
            internal float SquaredDistance(Edge other)
            {
                Vector2 a = End - Start, b = other.End - other.Start;
                float denominator = Cross(a, b);
                if (Mathf.Abs(denominator) > .000001f)
                {
                    Vector2 offset = other.Start - Start;
                    float t = Cross(offset, b) / denominator, u = Cross(offset, a) / denominator;
                    if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
                }
                return Mathf.Min(Mathf.Min(SquaredDistance(other.Start), SquaredDistance(other.End)),
                    Mathf.Min(other.SquaredDistance(Start), other.SquaredDistance(End)));
            }
            private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        }

        public CityTerracePilotWalkableArea(CityTerracePilotPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            supports = new Rect[plan.Floors.Count + plan.Flights.Count];
            for (int i = 0; i < plan.Floors.Count; i++) supports[i] = plan.Floors[i].Bounds;
            for (int i = 0; i < plan.Flights.Count; i++)
                supports[plan.Floors.Count + i] = CityTerracePilotPlan.FlightBounds(plan.Flights[i]);
            boundaries = CreateExposedEdges();
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(position) || !CityTerracePilotPlan.Finite(radius) || radius < 0f)
                return false;
            var point = new Vector2(position.x, position.z);
            if (!InsideUnion(point)) return false;
            float clearance = Mathf.Max(0f, radius - Epsilon);
            float squaredClearance = clearance * clearance;
            for (int i = 0; i < boundaries.Length && radius > 0f; i++)
                if (boundaries[i].SquaredDistance(point) < squaredClearance) return false;
            return true;
        }

        public bool TryGetSurfaceHeight(Vector3 position, out float height, float radius = 0f)
        {
            height = 0f;
            return Contains(position, radius) && plan.TryGetSurfaceHeight(position, out height);
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(desiredPosition) || !Contains(currentPosition, radius))
                return currentPosition;
            Vector3 result = Sweep(currentPosition, desiredPosition, radius);
            if ((new Vector2(result.x - desiredPosition.x, result.z - desiredPosition.z)).sqrMagnitude <= Epsilon * Epsilon)
                return result;

            // Keep movement along a courtyard edge, while sweeping both slide
            // candidates as well; no desired endpoint can skip an empty gap.
            Vector3 alongX = Sweep(result, new Vector3(desiredPosition.x, desiredPosition.y, result.z), radius);
            Vector3 alongZ = Sweep(result, new Vector3(result.x, desiredPosition.y, desiredPosition.z), radius);
            return (alongX - desiredPosition).sqrMagnitude <= (alongZ - desiredPosition).sqrMagnitude ? alongX : alongZ;
        }

        private Vector3 Sweep(Vector3 start, Vector3 desired, float radius)
        {
            Vector3 delta = new Vector3(desired.x - start.x, 0f, desired.z - start.z);
            if (!CityTerracePilotPlan.Finite(delta)) return start;
            float distance = delta.magnitude;
            if (!CityTerracePilotPlan.Finite(distance)) return start;
            if (distance < Epsilon) return new Vector3(start.x, desired.y, start.z);
            float boundedDistance = Mathf.Min(distance, MaximumSweepSteps * SweepSpacing);
            Vector3 end = start + delta * (boundedDistance / distance);
            end.y = desired.y;
            int steps = Mathf.Max(1, Mathf.CeilToInt(boundedDistance / SweepSpacing));
            Vector3 lastValid = start;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 candidate = Vector3.Lerp(start, end, i / (float)steps);
                if (SegmentClear(lastValid, candidate, radius)) { lastValid = candidate; continue; }
                Vector3 invalid = candidate;
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    Vector3 middle = (lastValid + invalid) * .5f;
                    if (SegmentClear(lastValid, middle, radius)) lastValid = middle;
                    else invalid = middle;
                }
                lastValid.y = desired.y;
                return lastValid;
            }
            return end;
        }

        private bool SegmentClear(Vector3 start, Vector3 end, float radius)
        {
            if (!Contains(end, radius)) return false;
            var from = new Vector2(start.x, start.z);
            var to = new Vector2(end.x, end.z);
            if ((to - from).sqrMagnitude < Epsilon * Epsilon) return true;
            if (radius > Epsilon)
            {
                var movement = new Edge(from, to);
                float clearance = radius - Epsilon;
                for (int i = 0; i < boundaries.Length; i++)
                    if (boundaries[i].SquaredDistance(movement) < clearance * clearance) return false;
                return true;
            }

            // A point-sized query can cross the four-centimetre gap between
            // courts within one sweep sample. Merge rectangle intersections
            // along this segment, rather than trusting only its endpoints.
            Vector2 delta = to - from;
            float covered = 0f;
            for (int pass = 0; pass < supports.Length; pass++)
            {
                float previous = covered;
                foreach (Rect rectangle in supports)
                {
                    float first = 0f, last = 1f;
                    if (!Clip(from.x, delta.x, rectangle.xMin, rectangle.xMax, ref first, ref last) ||
                        !Clip(from.y, delta.y, rectangle.yMin, rectangle.yMax, ref first, ref last)) continue;
                    if (first <= covered + .000001f) covered = Mathf.Max(covered, last);
                }
                if (covered >= 1f - .000001f) return true;
                if (covered <= previous) return false;
            }
            return false;
        }

        private static bool Clip(float start, float delta, float minimum, float maximum,
            ref float first, ref float last)
        {
            if (Mathf.Abs(delta) < .000001f) return start >= minimum - Epsilon && start <= maximum + Epsilon;
            float a = (minimum - start) / delta, b = (maximum - start) / delta;
            first = Mathf.Max(first, Mathf.Min(a, b));
            last = Mathf.Min(last, Mathf.Max(a, b));
            return first <= last;
        }

        private bool InsideUnion(Vector2 point)
        {
            for (int i = 0; i < supports.Length; i++)
                if (CityTerracePilotPlan.Inside(supports[i], point.x, point.y)) return true;
            return false;
        }

        private Edge[] CreateExposedEdges()
        {
            var edges = new List<Edge>(64);
            var cuts = new List<float>(supports.Length * 2 + 2);
            foreach (Rect rectangle in supports)
            {
                AppendEdge(new Vector2(rectangle.xMin, rectangle.yMin), Vector2.right,
                    rectangle.width, Vector2.down, cuts, edges);
                AppendEdge(new Vector2(rectangle.xMin, rectangle.yMax), Vector2.right,
                    rectangle.width, Vector2.up, cuts, edges);
                AppendEdge(new Vector2(rectangle.xMin, rectangle.yMin), Vector2.up,
                    rectangle.height, Vector2.left, cuts, edges);
                AppendEdge(new Vector2(rectangle.xMax, rectangle.yMin), Vector2.up,
                    rectangle.height, Vector2.right, cuts, edges);
            }
            return edges.ToArray();
        }

        private void AppendEdge(Vector2 start, Vector2 direction, float length, Vector2 outward,
            List<float> cuts, List<Edge> edges)
        {
            cuts.Clear(); cuts.Add(0f); cuts.Add(length);
            foreach (Rect rectangle in supports)
            {
                float first = Vector2.Dot(rectangle.min - start, direction);
                float last = Vector2.Dot(rectangle.max - start, direction);
                if (first > Epsilon && first < length - Epsilon) cuts.Add(first);
                if (last > Epsilon && last < length - Epsilon) cuts.Add(last);
            }
            cuts.Sort();
            for (int i = 1; i < cuts.Count; i++)
            {
                float from = cuts[i - 1], to = cuts[i];
                if (to - from < Epsilon) continue;
                Vector2 midpoint = start + direction * ((from + to) * .5f);
                if (InsideUnion(midpoint + outward * .002f)) continue;
                edges.Add(new Edge(start + direction * from, start + direction * to));
            }
        }
    }
}
