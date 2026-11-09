using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // The toe is sampled from the original ridge plan. Ground rendering, collision and
    // arrival heights then use the same graded apron, even across terrain-owner seams.
    internal sealed class CityMountainTalusPlan
    {
        internal const float MaximumReach = 3.6f;
        private const float Clearance = 1.8f;
        private const float ServiceToeRise = .09f;
        private const float CellSize = 12f;
        private static readonly ConditionalWeakTable<CityLayout, CityMountainTalusPlan> Plans = new();
        private readonly List<Segment> segments = new();
        private readonly Dictionary<Vector2Int, Cell> cells = new();
        private readonly float phase;

        private sealed class Segment
        {
            internal Vector2 A, B, Direction, Inward;
            internal float Length, RiseA, RiseB;
            internal bool ServiceA, ServiceB;
        }

        private sealed class Cell
        {
            internal readonly List<Segment> Segments = new();
            internal readonly List<Rect> Reservations = new();
        }

        internal static CityMountainTalusPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityMountainTalusPlan(value));

        private CityMountainTalusPlan(CityLayout layout)
        {
            var rim = CityMountainBoundaryPlanner.Create(layout);
            if (rim.Ridges.Count == 0) return;
            phase = (layout.Seed & 1023) * .17f;
            var adjoining = new Dictionary<Vector2Int, int>();
            var serviceEnds = new HashSet<Vector2Int>();
            foreach (var ridge in rim.Ridges)
            for (int i = 1; i < ridge.Stations.Count; i++)
            {
                var first = ridge.Stations[i - 1];
                var last = ridge.Stations[i];
                Vector2 delta = last.WorldXZ - first.WorldXZ;
                float length = delta.magnitude;
                if (length < .001f) continue;
                var direction = delta / length;
                var inward = new Vector2(-direction.y, direction.x);
                var outside = first.OutwardNormal + last.OutwardNormal;
                if (Vector2.Dot(inward, new Vector2(outside.x, outside.z)) > 0f) inward = -inward;
                var segment = new Segment { A = first.WorldXZ, B = last.WorldXZ, Length = length,
                    Direction = direction, Inward = inward };
                segments.Add(segment);
                CountEnd(segment.A);
                CountEnd(segment.B);
                if (layout.SpatialPlan.IsCompactCoastal &&
                    (ridge.SourceAreaId == CityMountainBoundaryDefinition.WestNorthAreaId ||
                     ridge.SourceAreaId == CityMountainBoundaryDefinition.WestSouthAreaId))
                {
                    // Utility buildings can use the whole western work sheet up to its
                    // surveyed maintenance gap. The wider loose stone texture continues.
                    serviceEnds.Add(EndKey(segment.A));
                    serviceEnds.Add(EndKey(segment.B));
                }
                var min = CellKey(Vector2.Min(segment.A, segment.B) - Vector2.one * MaximumReach);
                var max = CellKey(Vector2.Max(segment.A, segment.B) + Vector2.one * MaximumReach);
                for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!cells.TryGetValue(key, out var cell)) cells.Add(key, cell = new Cell());
                    cell.Segments.Add(segment);
                }
            }

            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                Reserve(house.FoundationBounds);
                Reserve(house.ApproachBounds);
                Reserve(house.CourtBounds);
            }
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                foreach (var part in stair.WalkableBounds) Reserve(part);
            foreach (var pad in CityHillsideFurniturePlan.GroundPads(layout)) Reserve(pad.Bounds);
            foreach (var ramp in CityHillsideFurniturePlan.GroundRamps(layout)) Reserve(ramp.Bounds);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                foreach (var polygon in road.WalkablePolygons) Reserve(CityRoadPolygon.Bounds(polygon));
            foreach (var link in CityHillsideQuarterPlan.ForLayout(layout).Links) Reserve(link.Bounds);
            foreach (var access in layout.OpenAreaAccesses) Reserve(access.ApproachBounds);
            if (rim.HasRiverCave) Reserve(rim.RiverCave.ApproachBounds);
            if (rim.HasTunnel) Reserve(rim.Tunnel.ApproachBounds);

            foreach (var segment in segments)
            {
                segment.RiseA = CrestRise(segment.A);
                segment.RiseB = CrestRise(segment.B);
                segment.ServiceA = serviceEnds.Contains(EndKey(segment.A));
                segment.ServiceB = serviceEnds.Contains(EndKey(segment.B));
            }

            void CountEnd(Vector2 point)
            {
                var key = EndKey(point);
                adjoining.TryGetValue(key, out int count);
                adjoining[key] = count + 1;
            }

            float CrestRise(Vector2 point)
            {
                // Two segments meeting here share the same top, including between separately
                // named ridges. An actual open end runs gently into the unchanged terrain.
                if (adjoining[EndKey(point)] < 2) return 0f;
                float variation = Mathf.Sin(point.x * .12f + point.y * .17f + phase);
                return (.78f + .17f * variation) * ReservationWeight(point, cells[CellKey(point)]);
            }
        }

        private void Reserve(Rect bounds)
        {
            var min = CellKey(bounds.min - Vector2.one * Clearance);
            var max = CellKey(bounds.max + Vector2.one * Clearance);
            for (int z = min.y; z <= max.y; z++)
            for (int x = min.x; x <= max.x; x++)
                if (cells.TryGetValue(new Vector2Int(x, z), out var cell)) cell.Reservations.Add(bounds);
        }

        private static float ReservationWeight(Vector2 point, Cell cell)
        {
            float distance = Clearance;
            foreach (var bounds in cell.Reservations)
            {
                float dx = Mathf.Max(bounds.xMin - point.x, point.x - bounds.xMax);
                float dz = Mathf.Max(bounds.yMin - point.y, point.y - bounds.yMax);
                distance = Mathf.Min(distance, Mathf.Max(dx, dz));
            }
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, Clearance, distance));
        }

        internal float ApplyHeight(Vector2 point, float original)
        {
            Sample(point, out float rise, out _);
            return original + rise;
        }

        internal float Influence(Vector2 point)
        {
            Sample(point, out _, out float weight);
            return weight;
        }

        private void Sample(Vector2 point, out float rise, out float weight)
        {
            rise = weight = 0f;
            if (!cells.TryGetValue(CellKey(point), out var cell)) return;
            float nearestSquared = MaximumReach * MaximumReach;
            Segment nearest = null;
            float along = 0f;
            foreach (var segment in cell.Segments)
            {
                Vector2 offset = point - segment.A;
                if (Vector2.Dot(offset, segment.Inward) < -.0001f) continue;
                float t = Mathf.Clamp(Vector2.Dot(offset, segment.Direction), 0f, segment.Length);
                float squared = (offset - segment.Direction * t).sqrMagnitude;
                if (squared >= nearestSquared) continue;
                nearestSquared = squared;
                nearest = segment;
                along = t;
            }
            if (nearest == null) return;
            Vector2 foot = nearest.A + nearest.Direction * along;
            float reach = 2.60f + .66f * Mathf.Sin(foot.x * .19f + foot.y * .16f + phase)
                                + .34f * Mathf.Sin(foot.x * .43f - foot.y * .38f - phase);
            float distance = Mathf.Sqrt(nearestSquared);
            float profile = Mathf.SmoothStep(1f, 0f, distance / reach);
            if (profile <= 0f) return;
            float tAlong = along / nearest.Length;
            float crest = Mathf.Lerp(nearest.RiseA, nearest.RiseB, tAlong);
            float clear = ReservationWeight(point, cell);
            weight = profile * Mathf.Min(Mathf.Clamp01(crest / .60f), clear);
            if (nearest.ServiceA || nearest.ServiceB)
            {
                float gap = CityCoastalInfrastructurePlan.FacilityMaintenanceGap;
                float physicalReach = Mathf.Lerp(nearest.ServiceA ? gap : reach,
                    nearest.ServiceB ? gap : reach, tAlong);
                profile = Mathf.SmoothStep(1f, 0f, distance / physicalReach);
                crest = Mathf.Lerp(nearest.ServiceA ? Mathf.Min(nearest.RiseA, ServiceToeRise) : nearest.RiseA,
                    nearest.ServiceB ? Mathf.Min(nearest.RiseB, ServiceToeRise) : nearest.RiseB, tAlong);
            }
            rise = profile * Mathf.Min(crest, clear);
        }

        // The existing banks use rectilinear grids. Densify only gaps: the lower
        // work sheets can otherwise span an entire 22 m ridge station in one quad.
        internal void AddBreaks(Rect bounds, SortedSet<float> xs, SortedSet<float> zs)
        {
            var alongCuts = new List<float>();
            foreach (var segment in segments)
            {
                Vector2 lo = Vector2.Min(segment.A, segment.B) - Vector2.one * MaximumReach;
                Vector2 hi = Vector2.Max(segment.A, segment.B) + Vector2.one * MaximumReach;
                if (hi.x < bounds.xMin || lo.x > bounds.xMax || hi.y < bounds.yMin || lo.y > bounds.yMax) continue;
                AddAxis(xs, bounds.xMin, bounds.xMax, segment.A.x);
                AddAxis(xs, bounds.xMin, bounds.xMax, segment.B.x);
                AddAxis(zs, bounds.yMin, bounds.yMax, segment.A.y);
                AddAxis(zs, bounds.yMin, bounds.yMax, segment.B.y);
                bool cutsX = Mathf.Abs(segment.Inward.x) >= Mathf.Abs(segment.Inward.y);
                float a = cutsX ? segment.A.x : segment.A.y;
                float b = cutsX ? segment.B.x : segment.B.y;
                float alongA = cutsX ? segment.A.y : segment.A.x;
                float alongB = cutsX ? segment.B.y : segment.B.x;
                float alongMin = Mathf.Max(cutsX ? bounds.yMin : bounds.xMin, Mathf.Min(alongA, alongB));
                float alongMax = Mathf.Min(cutsX ? bounds.yMax : bounds.xMax, Mathf.Max(alongA, alongB));
                float alongStep = 3f * Mathf.Abs(cutsX ? segment.Direction.y : segment.Direction.x);
                if (alongMin < alongMax)
                {
                    var axis = cutsX ? zs : xs;
                    alongCuts.Clear();
                    float previous = alongMin;
                    foreach (float cut in axis.GetViewBetween(alongMin, alongMax))
                    {
                        SplitGap(previous, cut, alongStep, alongCuts);
                        previous = cut;
                    }
                    SplitGap(previous, alongMax, alongStep, alongCuts);
                    foreach (float cut in alongCuts) axis.Add(cut);
                }
                float across = MaximumReach * (cutsX ? segment.Inward.x : segment.Inward.y);
                float min = cutsX ? bounds.xMin : bounds.yMin;
                float max = cutsX ? bounds.xMax : bounds.yMax;
                if (segment.ServiceA && segment.ServiceB)
                {
                    float gap = CityCoastalInfrastructurePlan.FacilityMaintenanceGap;
                    float edge = a + gap * (cutsX ? segment.Inward.x : segment.Inward.y);
                    AddAxis(cutsX ? xs : zs, min, max, edge);
                }
                float start = Mathf.Max(min, Mathf.Min(a, b) + Mathf.Min(0f, across));
                float end = Mathf.Min(max, Mathf.Max(a, b) + Mathf.Max(0f, across));
                const float step = .75f;
                for (int index = Mathf.CeilToInt(start / step); index * step <= end; index++)
                    AddAxis(cutsX ? xs : zs, min, max, index * step);
            }
        }

        private static void SplitGap(float from, float to, float step, List<float> cuts)
        {
            int count = Mathf.CeilToInt((to - from) / step);
            for (int i = 1; i < count; i++) cuts.Add(Mathf.Lerp(from, to, i / (float)count));
        }

        private static void AddAxis(SortedSet<float> axis, float min, float max, float value)
        {
            if (value <= min || value >= max || axis.Contains(value)) return;
            if (axis.GetViewBetween(value - .001f, value + .001f).Count == 0) axis.Add(value);
        }

        private static Vector2Int CellKey(Vector2 p) =>
            new(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
        private static Vector2Int EndKey(Vector2 p) => Vector2Int.RoundToInt(p * 100f);
    }
}
