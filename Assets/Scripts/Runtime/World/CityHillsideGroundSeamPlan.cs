using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // Upstream geometry only: bus placement consumes the completed ground grid.
    // A narrow soil shoulder meets each actual outer pavement, including its
    // widened bends. Its outside height follows the ground at the same station.
    internal sealed class CityHillsideGroundSeamPlan
    {
        internal const float BlendDistance = 1.5f;
        internal const float SurfaceRecess = .012f;
        private const float VergeWidth = 2.35f;
        private const float VergeGrade = .70f;
        private const float VergeGradeReach = 3.65f;
        private const float FoundationReach = 11f;
        private const float CellSize = 8f;
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideGroundSeamPlan> Plans = new();
        private readonly Dictionary<Vector2Int, List<Segment>> cells = new();
        private readonly List<Segment> segments = new();
        private readonly IReadOnlyList<CityHillsideBuilding> houses;
        private readonly CityStreetSupportPlan pavement;

        private readonly struct Segment
        {
            internal readonly Vector2 A, B;
            internal readonly Vector2 Delta;
            internal readonly float InverseLengthSquared;
            internal readonly float TopA, TopB;
            internal readonly bool IsPublic;
            internal Segment(Vector2 a, Vector2 b, float topA, float topB, bool isPublic)
            {
                A = a; B = b; TopA = topA; TopB = topB;
                Delta = b - a;
                InverseLengthSquared = 1f / Delta.sqrMagnitude;
                IsPublic = isPublic;
            }

            internal float DistanceSquared(Vector2 point, out float along)
            {
                Vector2 offset = point - A;
                along = Mathf.Clamp01(Vector2.Dot(offset, Delta) * InverseLengthSquared);
                return (offset - Delta * along).sqrMagnitude;
            }
        }

        internal static CityHillsideGroundSeamPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideGroundSeamPlan(value));

        private CityHillsideGroundSeamPlan(CityLayout layout)
        {
            houses = CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings;
            var roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            pavement = CityStreetSupportPlan.ForLayout(layout);
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
                Add(a, b, first, last, true);
            }
            foreach (var link in quarter.Links)
            {
                for (int i = 1; i < link.Centreline.Count; i++)
                {
                    float a = link.Centreline[i - 1].x, b = link.Centreline[i].x;
                    Add(new Vector2(a, link.Bounds.yMin), new Vector2(b, link.Bounds.yMin),
                        link.PavementTopAt(a), link.PavementTopAt(b), true);
                    Add(new Vector2(a, link.Bounds.yMax), new Vector2(b, link.Bounds.yMax),
                        link.PavementTopAt(a), link.PavementTopAt(b), true);
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
                Add(a, b, first, last, false);
            }
        }

        private void Add(Vector2 a, Vector2 b, float topA, float topB, bool isPublic)
        {
            if ((b - a).sqrMagnitude < .000001f) return;
            var segment = new Segment(a, b, topA, topB, isPublic);
            segments.Add(segment);
            float reach = isPublic ? FoundationReach : BlendDistance;
            Vector2 min = Vector2.Min(segment.A, segment.B) - Vector2.one * reach;
            Vector2 max = Vector2.Max(segment.A, segment.B) + Vector2.one * reach;
            Vector2Int first = Cell(min), last = Cell(max);
            for (int z = first.y; z <= last.y; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<Segment>());
                list.Add(segment);
            }
        }

        // A house on the next tier can be only a few metres from this curb.
        // Keep its cut or fill shallow at the street and restore it gradually
        // toward the lot. Entrances still use their surveyed apron heights.
        internal float LimitFoundationAtVerge(Vector2 point, float bank, float developed)
        {
            if (Mathf.Abs(developed - bank) <= .12f ||
                !cells.TryGetValue(Cell(point), out var nearby)) return developed;
            float squared = FoundationReach * FoundationReach;
            foreach (var edge in nearby)
                if (edge.IsPublic) squared = Mathf.Min(squared, edge.DistanceSquared(point, out _));
            float distance = Mathf.Sqrt(squared);
            float limit = .12f + .55f * Mathf.Max(0f, distance - VergeWidth);
            float graded = Mathf.MoveTowards(bank, developed, limit);
            return Mathf.SmoothStep(graded, developed, Mathf.InverseLerp(7f, FoundationReach, distance));
        }

        internal float Apply(Vector2 point, float original)
        {
            // This includes the rock below a bridge, whose XZ is otherwise a road.
            if (CityHillsideQuarterRavine.Drop(point) > .0001f) return original;
            float height = BlendToPaving(point, original);
            // Recess only under the actual pavement. A fixed centreline radius
            // cut a second ledge through the soil beyond the outside edge.
            return pavement.TrySampleTop(point, out float roadTop, out _)
                ? Mathf.Min(height, roadTop - SurfaceRecess) : height;
        }

        private float BlendToPaving(Vector2 point, float original)
        {
            foreach (var house in houses)
                if (house.TrySampleApproach(point, out float apron)) return apron - SurfaceRecess;
            if (!cells.TryGetValue(Cell(point), out var nearby)) return original;
            float strongest = 0f, heightSum = 0f, weightSum = 0f;
            float curbDistanceSquared = VergeGradeReach * VergeGradeReach, curbTop = original;
            foreach (var segment in nearby)
            {
                float squared = segment.DistanceSquared(point, out float t);
                if (segment.IsPublic && squared < curbDistanceSquared)
                {
                    curbDistanceSquared = squared;
                    curbTop = Mathf.Lerp(segment.TopA, segment.TopB, t) - SurfaceRecess;
                }
                if (squared >= BlendDistance * BlendDistance) continue;
                float influence = Mathf.SmoothStep(1f, 0f, Mathf.Sqrt(squared) / BlendDistance);
                float weight = influence / Mathf.Max(.000001f, squared);
                heightSum += (Mathf.Lerp(segment.TopA, segment.TopB, t) - SurfaceRecess) * weight;
                weightSum += weight;
                strongest = Mathf.Max(strongest, influence);
            }
            // Adjacent edges share a continuous blend where their shoulders meet.
            // The correction goes to zero on the local soil at the outside edge.
            float height = weightSum > 0f ? Mathf.Lerp(original, heightSum / weightSum, strongest) : original;
            float curbDistance = Mathf.Sqrt(curbDistanceSquared);
            float rise = curbDistance * VergeGrade;
            float gradeLimited = Mathf.Clamp(height, curbTop - rise, curbTop + rise);
            return Mathf.SmoothStep(gradeLimited, height, Mathf.InverseLerp(VergeWidth, VergeGradeReach, curbDistance));
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
