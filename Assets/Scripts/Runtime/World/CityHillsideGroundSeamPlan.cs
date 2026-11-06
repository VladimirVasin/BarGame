using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // Upstream geometry only: bus placement consumes the completed ground grid.
    // A narrow soil shoulder meets each actual outer pavement, including its
    // widened bends. Nothing here changes a street, threshold or rock datum.
    internal sealed class CityHillsideGroundSeamPlan
    {
        internal const float BlendDistance = 1.5f;
        internal const float SurfaceRecess = .012f;
        private const float CellSize = 8f;
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideGroundSeamPlan> Plans = new();
        private readonly Dictionary<Vector2Int, List<Segment>> cells = new();
        private readonly List<Segment> segments = new();
        private readonly IReadOnlyList<CityHillsideBuilding> houses;
        private readonly CityLayout layout;
        private readonly IReadOnlyList<CityHillsideStreetRoute> roads;
        private readonly CityHillsideQuarterPlan quarter;

        private readonly struct Segment
        {
            internal readonly Vector2 A, B;
            internal readonly Vector2 Outward;
            internal readonly float TopA, TopB, OuterA, OuterB;
            internal Segment(Vector2 a, Vector2 b, Vector2 outward, float topA, float topB, float outerA, float outerB)
            { A = a; B = b; Outward = outward; TopA = topA; TopB = topB; OuterA = outerA; OuterB = outerB; }
        }

        internal static CityHillsideGroundSeamPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideGroundSeamPlan(value));

        private CityHillsideGroundSeamPlan(CityLayout layout)
        {
            this.layout = layout;
            houses = CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings;
            roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
            quarter = CityHillsideQuarterPlan.ForLayout(layout);
            foreach (var road in roads)
            foreach (var polygon in road.WalkablePolygons)
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                Vector2 a = polygon[edge], b = polygon[(edge + 1) % polygon.Length];
                Vector2 delta = b - a, middle = (a + b) * .5f;
                if (delta.sqrMagnitude < .000001f || road.Path.Project(middle).DistanceSquared < 16f) continue;
                Vector2 outward = new Vector2(delta.y, -delta.x).normalized;
                // Interior ribbon stations are not outside boundaries.
                if (road.TrySampleTop(middle + outward * .02f, out _, out _)) continue;
                if (!road.TrySampleTop(a, out float first, out _))
                    first = road.Profile.SampleDistance(road.Path.Project(a).DistanceAlong).Height + CityHillsideBuilding.PavementRise;
                if (!road.TrySampleTop(b, out float last, out _))
                    last = road.Profile.SampleDistance(road.Path.Project(b).DistanceAlong).Height + CityHillsideBuilding.PavementRise;
                Add(a, b, outward, first, last);
            }
            foreach (var link in quarter.Links)
            {
                for (int i = 1; i < link.Centreline.Count; i++)
                {
                    float a = link.Centreline[i - 1].x, b = link.Centreline[i].x;
                    Add(new Vector2(a, link.Bounds.yMin), new Vector2(b, link.Bounds.yMin), Vector2.down,
                        link.PavementTopAt(a), link.PavementTopAt(b));
                    Add(new Vector2(a, link.Bounds.yMax), new Vector2(b, link.Bounds.yMax), Vector2.up,
                        link.PavementTopAt(a), link.PavementTopAt(b));
                }
            }
            foreach (var house in houses)
            {
                Approach(house.ApproachBounds, house);
                Approach(house.CourtBounds, house);
            }
        }

        private void Approach(Rect bounds, CityHillsideBuilding house)
        {
            Vector2[] corners = CityRoadPolygon.Rectangle(bounds);
            for (int edge = 0; edge < corners.Length; edge++)
            {
                Vector2 a = corners[edge], b = corners[(edge + 1) % corners.Length];
                if (!house.TrySampleApproach(a, out float first) || !house.TrySampleApproach(b, out float last)) continue;
                Vector2 delta = b - a;
                Add(a, b, new Vector2(delta.y, -delta.x).normalized, first, last);
            }
        }

        private void Add(Vector2 a, Vector2 b, Vector2 outward, float topA, float topB)
        {
            var segment = new Segment(a, b, outward, topA, topB,
                BaseTop(a + outward * BlendDistance, topA - SurfaceRecess),
                BaseTop(b + outward * BlendDistance, topB - SurfaceRecess));
            segments.Add(segment);
            Vector2 min = Vector2.Min(segment.A, segment.B) - Vector2.one * BlendDistance;
            Vector2 max = Vector2.Max(segment.A, segment.B) + Vector2.one * BlendDistance;
            Vector2Int first = Cell(min), last = Cell(max);
            for (int z = first.y; z <= last.y; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<Segment>());
                list.Add(segment);
            }
        }

        private float BaseTop(Vector2 point, float fallback)
        {
            foreach (var road in roads)
                if (CityHillsideBuilding.Inside(CityHillsideNeighborhoodPlan.TerrainBounds(road), point))
                    return CityHillsideNeighborhoodPlan.SampleBuiltGround(layout, road, point, houses);
            return quarter.Links.Count > 0 && CityHillsideBuilding.Inside(quarter.TerrainBounds, point)
                ? quarter.SampleSoil(point) : fallback;
        }

        internal float Apply(Vector2 point, float original)
        {
            // This includes the rock below a bridge, whose XZ is otherwise a road.
            if (CityHillsideQuarterRavine.Drop(point) > .0001f) return original;
            foreach (var house in houses)
                if (house.TrySampleApproach(point, out float apron)) return apron - SurfaceRecess;
            if (!cells.TryGetValue(Cell(point), out var nearby)) return original;
            float best = BlendDistance * BlendDistance;
            float target = original, outerTop = original;
            bool outside = false;
            bool found = false;
            foreach (var segment in nearby)
            {
                Vector2 delta = segment.B - segment.A;
                float length = delta.sqrMagnitude;
                float t = length > 0f ? Mathf.Clamp01(Vector2.Dot(point - segment.A, delta) / length) : 0f;
                float distance = (point - segment.A - delta * t).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                target = Mathf.Lerp(segment.TopA, segment.TopB, t) - SurfaceRecess;
                outerTop = Mathf.Lerp(segment.OuterA, segment.OuterB, t);
                outside = Vector2.Dot(point - segment.A - delta * t, segment.Outward) >= -.0001f;
                found = true;
            }
            if (!found) return original;
            float across = Mathf.Sqrt(best) / BlendDistance;
            // Interpolate two fixed endpoint datums. Fading a sloping original
            // surface into the curb multiplies its grade and can create a steep
            // rejected strip immediately outside an otherwise passable footway.
            return outside ? Mathf.Lerp(target, outerTop, across)
                : Mathf.Lerp(original, target, 1f - Mathf.SmoothStep(0f, 1f, across));
        }

        internal void AddBreaks(Rect bounds, SortedSet<float> xs, SortedSet<float> zs)
        {
            foreach (var segment in segments)
            {
                if (Mathf.Max(segment.A.x, segment.B.x) + BlendDistance < bounds.xMin ||
                    Mathf.Min(segment.A.x, segment.B.x) - BlendDistance > bounds.xMax ||
                    Mathf.Max(segment.A.y, segment.B.y) + BlendDistance < bounds.yMin ||
                    Mathf.Min(segment.A.y, segment.B.y) - BlendDistance > bounds.yMax) continue;
                if (Mathf.Abs(segment.A.x - segment.B.x) < .0001f)
                    Axis(xs, segment.A.x, bounds.xMin, bounds.xMax);
                if (Mathf.Abs(segment.A.y - segment.B.y) < .0001f)
                    Axis(zs, segment.A.y, bounds.yMin, bounds.yMax);
            }
            static void Axis(SortedSet<float> axis, float edge, float min, float max)
            {
                if (edge > min && edge < max) axis.Add(edge);
                if (edge - BlendDistance > min && edge - BlendDistance < max) axis.Add(edge - BlendDistance);
                if (edge + BlendDistance > min && edge + BlendDistance < max) axis.Add(edge + BlendDistance);
            }
        }

        private static Vector2Int Cell(Vector2 point) => new(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.y / CellSize));
    }
}
