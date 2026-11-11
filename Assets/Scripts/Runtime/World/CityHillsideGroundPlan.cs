using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The usable earth between Ridge streets, yards and the physical mountain rim.
    /// Safe cells are joined before the shared walk mask erodes the outside by a body radius.</summary>
    public sealed class CityHillsideGroundPlan
    {
        public const float MaximumSlopeDegrees = 40f;
        private static readonly float MinimumNormalY = Mathf.Cos(MaximumSlopeDegrees * Mathf.Deg2Rad);
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideGroundPlan> Cached = new();
        private readonly IReadOnlyList<CityHillsideGroundGrid> banks;
        private readonly CityHillsideQuarterGroundGrid quarter;
        private readonly RoadWalkableArea safeArea = new();
        public IReadOnlyList<Vector2[]> WalkablePolygons { get; }

        public static CityHillsideGroundPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Cached.GetValue(layout, value => new CityHillsideGroundPlan(value));
        }

        private CityHillsideGroundPlan(CityLayout layout)
        {
            banks = CityHillsideGroundGrid.ForLayout(layout);
            quarter = CityHillsideQuarterGroundGrid.ForLayout(layout);
            var polygons = new List<Vector2[]>();
            foreach (var bank in banks) AppendBank(bank, polygons);
            AppendQuarter(quarter, polygons);

            // These are only exclusions from earth. Existing authored stair and
            // covered passage surfaces retain their own level and movement owner.
            var solids = new List<Vector2[]>();
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                solids.AddRange(house.Lot.CreateCollisionPolygons());
            foreach (var site in CityHillsideSiteDetails.ForLayout(layout))
            {
                if (site.Part == CityHillsideSiteDetails.Kind.RetainingEnd ||
                    site.Part == CityHillsideSiteDetails.Kind.CourtEdge ||
                    site.Part == CityHillsideSiteDetails.Kind.YardShelter)
                    solids.Add(CityRoadPolygon.Rectangle(site.Bounds));
                else if (site.Part == CityHillsideSiteDetails.Kind.YardTree)
                    solids.Add(CityHillsideSiteDetails.TrunkFootprint(site.Origin, site.Rotation, site.Scale));
            }
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk)) solids.Add(CityRoadPolygon.Rectangle(kiosk.Bounds));
            // The enclosing rim trims the soil's rectangular corners. A flat
            // triangle under its rock is not a valid map or walking destination.
            foreach (var ridge in CityMountainBoundaryPlanner.Create(layout).Ridges)
            for (int i = 1; i < ridge.Stations.Count; i++)
            {
                var a = ridge.Stations[i - 1]; var b = ridge.Stations[i];
                solids.Add(CityRoadPolygon.CounterClockwise(new[] { XZ(a.Toe), XZ(b.Toe), XZ(b.OuterFoot) }));
                solids.Add(CityRoadPolygon.CounterClockwise(new[] { XZ(a.Toe), XZ(b.OuterFoot), XZ(a.OuterFoot) }));
            }
            foreach (Vector2[] cut in solids)
            {
                Rect bounds = CityRoadPolygon.Bounds(cut);
                var next = new List<Vector2[]>(polygons.Count);
                foreach (Vector2[] polygon in polygons)
                    if (bounds.Overlaps(CityRoadPolygon.Bounds(polygon))) next.AddRange(CityRoadPolygon.Subtract(polygon, cut));
                    else next.Add(polygon);
                polygons = next;
            }
            WalkablePolygons = polygons.AsReadOnly();
            safeArea.AddPolygons(WalkablePolygons);
        }

        public bool Contains(Vector2 point, float radius = 0f) =>
            safeArea.Contains(new Vector3(point.x, 0f, point.y), radius);

        // Sampling covers physical earth, including areas deliberately omitted
        // from traversal. Consumers wanting a destination must also use Contains.
        public bool TrySampleTop(Vector2 point, out float top)
        {
            top = 0f;
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
            foreach (var bank in banks) if (bank.TryHeight(point, out top)) return true;
            return quarter.TryHeight(point, out top);
        }

        internal static float ApplyPads(Vector2 point, float top, IReadOnlyList<CityHillsideGroundPad> pads)
        {
            foreach (var pad in pads)
            {
                Rect bounds = pad.Bounds;
                float distance = Mathf.Max(Mathf.Max(0f, Mathf.Max(bounds.xMin - point.x, point.x - bounds.xMax)),
                    Mathf.Max(bounds.yMin - point.y, point.y - bounds.yMax));
                if (distance >= pad.BlendDistance) continue;
                float amount = 1f - Mathf.SmoothStep(0f, 1f, distance / pad.BlendDistance);
                top = Mathf.Lerp(top, pad.TopY, amount);
            }
            return top;
        }

        internal static void AddPadBreaks(Rect bounds, IReadOnlyList<CityHillsideGroundPad> pads,
            SortedSet<float> xs, SortedSet<float> zs)
        {
            foreach (var pad in pads)
            {
                Rect area = pad.Bounds;
                float blend = pad.BlendDistance;
                if (!bounds.Overlaps(Rect.MinMaxRect(area.xMin - blend, area.yMin - blend, area.xMax + blend, area.yMax + blend))) continue;
                Add(xs, bounds.xMin, bounds.xMax, area.xMin - blend, area.xMin, area.xMax, area.xMax + blend);
                Add(zs, bounds.yMin, bounds.yMax, area.yMin - blend, area.yMin, area.yMax, area.yMax + blend);
            }
            static void Add(SortedSet<float> axis, float min, float max, params float[] values)
            {
                foreach (float value in values) if (value > min && value < max) axis.Add(value);
            }
        }

        internal static float ApplyRamps(Vector2 point, float top, IReadOnlyList<CityHillsideGroundRamp> ramps)
        {
            foreach (var ramp in ramps)
            {
                // The road keeps its buried skin. The authored shoulder starts
                // exactly on its outside edge, at the same recessed datum.
                if (point.y < ramp.StartZ) continue;
                Rect bounds = ramp.Bounds;
                float distance = Mathf.Max(Mathf.Max(0f, Mathf.Max(bounds.xMin - point.x, point.x - bounds.xMax)),
                    Mathf.Max(bounds.yMin - point.y, point.y - bounds.yMax));
                if (distance >= ramp.BlendDistance) continue;
                float target = Mathf.Lerp(ramp.StartY, ramp.EndY, Mathf.InverseLerp(ramp.StartZ, ramp.EndZ, point.y));
                float amount = 1f - Mathf.SmoothStep(0f, 1f, distance / ramp.BlendDistance);
                top = Mathf.Lerp(top, target, amount);
            }
            return top;
        }

        internal static void AddRampBreaks(Rect bounds, IReadOnlyList<CityHillsideGroundRamp> ramps,
            SortedSet<float> xs, SortedSet<float> zs)
        {
            foreach (var ramp in ramps)
            {
                Rect area = ramp.Bounds;
                float blend = ramp.BlendDistance;
                if (!bounds.Overlaps(Rect.MinMaxRect(area.xMin - blend, area.yMin, area.xMax + blend, area.yMax + blend))) continue;
                Add(xs, bounds.xMin, bounds.xMax, area.xMin - blend, area.xMin, area.xMax, area.xMax + blend);
                Add(zs, bounds.yMin, bounds.yMax, area.yMin,
                    ramp.StartZ - blend, ramp.StartZ, ramp.StartZ + blend,
                    ramp.EndZ - blend, ramp.EndZ, ramp.EndZ + blend,
                    area.yMax, area.yMax + blend);
            }
            static void Add(SortedSet<float> axis, float min, float max, params float[] values)
            {
                foreach (float value in values) if (value > min && value < max) axis.Add(value);
            }
        }

        private static bool Safe(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 first = Vector3.Cross(b - a, c - a), second = Vector3.Cross(b - c, d - c);
            // Grade is independent of area: closely spaced authored breaks must
            // not turn a flat sliver into an invisible gap in the walkable union.
            float cosineSquared = MinimumNormalY * MinimumNormalY;
            return first.y > 0f && second.y > 0f &&
                first.y * first.y >= cosineSquared * first.sqrMagnitude &&
                second.y * second.y >= cosineSquared * second.sqrMagnitude;
        }

        private static void AppendBank(CityHillsideGroundGrid grid, List<Vector2[]> result)
        {
            // A run is an actual safe set of mesh cells, not a terrain-bounds
            // rectangle. Coalesce matching runs through consecutive rows.
            var active = new Dictionary<(int Start, int End), Rect>();
            for (int row = 0; row + 1 < grid.Rows; row++)
            {
                var next = new Dictionary<(int Start, int End), Rect>();
                int col = 0;
                while (col + 1 < grid.Columns)
                {
                    if (!Cell(col)) { col++; continue; }
                    int start = col++;
                    while (col + 1 < grid.Columns && Cell(col)) col++;
                    var key = (start, col);
                    Rect bounds = Rect.MinMaxRect(grid.Vertex(start, row).x, grid.Vertex(start, row).z,
                        grid.Vertex(col, row + 1).x, grid.Vertex(col, row + 1).z);
                    if (active.TryGetValue(key, out Rect previous)) { bounds.yMin = previous.yMin; active.Remove(key); }
                    next.Add(key, bounds);
                }
                foreach (Rect done in active.Values) result.Add(CityRoadPolygon.Rectangle(done));
                active = next;
                bool Cell(int at) => grid.CellSurface(at, row) != CityHillsideGroundGrid.Surface.Cut &&
                    Safe(grid.Vertex(at, row), grid.Vertex(at, row + 1), grid.Vertex(at + 1, row), grid.Vertex(at + 1, row + 1));
            }
            foreach (Rect done in active.Values) result.Add(CityRoadPolygon.Rectangle(done));
        }

        private static void AppendQuarter(CityHillsideQuarterGroundGrid grid, List<Vector2[]> result)
        {
            for (int row = 0; row + 1 < grid.Rows; row++)
            {
                int col = 0;
                while (col + 1 < grid.Columns)
                {
                    if (!Cell(col)) { col++; continue; }
                    int start = col++;
                    while (col + 1 < grid.Columns && Cell(col)) col++;
                    // Bending rows are trapezoids. Joining their interior cells
                    // leaves only the outside edge subject to radius clearance.
                    result.Add(new[] { XZ(grid.Vertex(start, row)), XZ(grid.Vertex(col, row)),
                        XZ(grid.Vertex(col, row + 1)), XZ(grid.Vertex(start, row + 1)) });
                }
                bool Cell(int at) => grid.CellSurface(at, row) == CityHillsideQuarterGroundGrid.Surface.Earth &&
                    Safe(grid.Vertex(at, row), grid.Vertex(at, row + 1), grid.Vertex(at + 1, row), grid.Vertex(at + 1, row + 1));
            }
        }

        private static Vector2 XZ(Vector3 point) => new(point.x, point.z);
    }
}
