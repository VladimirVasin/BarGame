using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two public roads on the dry saddle behind the original river cave.</summary>
    public sealed class CityHillsideQuarterPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideQuarterPlan> Plans = new();
        private readonly CityLayout layout;
        private readonly CityHillsideStreetRoute west, east;
        public IReadOnlyList<CityHillsideQuarterLink> Links { get; }
        public Rect TerrainBounds { get; }
        public IReadOnlyList<CityHillsideQuarterBridge> Bridges { get; } = Array.Empty<CityHillsideQuarterBridge>();
        public IReadOnlyList<Vector2> RavineOutline { get; } = Array.Empty<Vector2>();
        public IReadOnlyList<Vector2[]> ExposedRavinePolygons { get; } = Array.Empty<Vector2[]>();

        private CityHillsideQuarterPlan(CityLayout layout)
        {
            this.layout = layout;
            foreach (var route in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (route.Bank == CityTerraceTransportBank.West) west = route;
                else east = route;
            if (west == null || east == null)
            {
                Links = Array.Empty<CityHillsideQuarterLink>();
                return;
            }
            var westLand = CityHillsideNeighborhoodPlan.TerrainBounds(west);
            var eastLand = CityHillsideNeighborhoodPlan.TerrainBounds(east);
            TerrainBounds = Rect.MinMaxRect(westLand.xMax, westLand.yMin, eastLand.xMin, west.GetLaneZ(4) + 11f);
            Links = Array.AsReadOnly(new[] {
                new CityHillsideQuarterLink(west, east, false),
                new CityHillsideQuarterLink(west, east, true) });
            Bridges = Array.AsReadOnly(new[] { new CityHillsideQuarterBridge(this, Links[0]),
                new CityHillsideQuarterBridge(this, Links[1]) });
            RavineOutline = CityHillsideQuarterRavine.Outline();
            ExposedRavinePolygons = CityHillsideQuarterRavine.OpenPolygons(Bridges);
        }

        public static CityHillsideQuarterPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideQuarterPlan(value));

        public bool IsExposedRavine(Vector2 point, float radius = 0f)
        {
            if (Bridges.Count == 0 || point.x < CityHillsideQuarterRavine.MeshWest - radius ||
                point.x > CityHillsideQuarterRavine.MeshEast + radius ||
                point.y < TerrainBounds.yMin - radius || point.y > TerrainBounds.yMax + radius) return false;
            foreach (var piece in ExposedRavinePolygons)
            {
                if (CityRoadPolygon.Contains(piece, point)) return true;
                if (radius <= 0f) continue;
                for (int i = 0; i < piece.Length; i++)
                {
                    Vector2 a = piece[i], d = piece[(i + 1) % piece.Length] - a;
                    Vector2 nearest = a + d * Mathf.Clamp01(Vector2.Dot(point - a, d) / d.sqrMagnitude);
                    if ((point - nearest).sqrMagnitude < radius * radius) return true;
                }
            }
            return false;
        }

        public float SampleSoil(Vector2 p)
        {
            // The existing bank meshes own the two vertical seams. Make the shared
            // court level below the passage and start climbing behind its far door.
            float across = Mathf.InverseLerp(TerrainBounds.xMin, TerrainBounds.xMax, p.x);
            float westTop = CityHillsideNeighborhoodPlan.SampleBank(layout, west, new Vector2(TerrainBounds.xMin, p.y));
            float eastTop = CityHillsideNeighborhoodPlan.SampleBank(layout, east, new Vector2(TerrainBounds.xMax, p.y));
            float seam = Mathf.Lerp(westTop, eastTop, across);
            float low = Links[0].RoadTopAt(p.x) - .20f;
            float high = Links[1].RoadTopAt(p.x) - .20f;
            float terrace = Mathf.Lerp(low, high, Mathf.InverseLerp(Links[0].WestGateWorld.z - 20f,
                Links[1].WestGateWorld.z + CityHillsideStreetRoute.OuterHalfWidth, p.y));
            float inset = Mathf.Min(p.x - TerrainBounds.xMin, TerrainBounds.xMax - p.x);
            return Mathf.Lerp(seam, terrace, Mathf.Clamp01(inset / 9f)) - CityHillsideQuarterRavine.Drop(p);
        }
    }

    public sealed class CityHillsideQuarterLink
    {
        public const float CarriagewayWidth = 2f * CityHillsideStreetRoute.RoadHalfWidth;
        private const float Shoulder = CityHillsideStreetRoute.OuterHalfWidth;
        private const float Curb = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;
        private const float FlatThroat = 8f;
        private readonly CityHillsideStreetRoute west, east;
        public string Id { get; }
        public Rect Bounds { get; }
        public Vector3 WestGateWorld { get; }
        public Vector3 EastGateWorld { get; }
        public IReadOnlyList<Vector3> Centreline { get; }
        public IReadOnlyList<Vector3> NorthSidewalk { get; }
        public IReadOnlyList<Vector3> SouthSidewalk { get; }
        public IReadOnlyList<CityStreetSupportPatch> SurfacePatches { get; }

        internal CityHillsideQuarterLink(CityHillsideStreetRoute west, CityHillsideStreetRoute east, bool summit)
        {
            this.west = west;
            this.east = east;
            Id = summit ? "summit" : "court";
            WestGateWorld = Gate(west, summit);
            EastGateWorld = Gate(east, summit);
            float z = WestGateWorld.z;
            float min = WestGateWorld.x, max = EastGateWorld.x;
            float gradeRun = max - min - 2f * FlatThroat;
            if (gradeRun <= 0f || Mathf.Abs(EastGateWorld.y - WestGateWorld.y) * 100f >
                CityElevationPlan.MaximumBusGradePercent * gradeRun)
                throw new InvalidOperationException($"The {Id} link does not fit the city carriageway grade.");
            Bounds = Rect.MinMaxRect(min, z - Shoulder, max, z + Shoulder);
            Centreline = Course(WestGateWorld, EastGateWorld, false);
            NorthSidewalk = Course(PavementGate(west, z + 4f, summit), PavementGate(east, z + 4f, summit), true);
            SouthSidewalk = Course(PavementGate(west, z - 4f, summit), PavementGate(east, z - 4f, summit), true);

            var frame = new CityRoadPath(new[] { new Vector2(min, z), new Vector2(max, z) });
            var existing = new List<Vector2[]>();
            if (!summit)
            {
                CollectContours(west);
                CollectContours(east);
            }
            var patches = new List<CityStreetSupportPatch>();
            for (int span = 1; span < Centreline.Count; span++)
            {
                float start = Centreline[span - 1].x, end = Centreline[span].x;
                AddStrip(start, end, -Shoulder, -CarriagewayWidth * .5f, true);
                AddStrip(start, end, -CarriagewayWidth * .5f, CarriagewayWidth * .5f, false);
                AddStrip(start, end, CarriagewayWidth * .5f, Shoulder, true);
            }
            SurfacePatches = patches.AsReadOnly();

            void CollectContours(CityHillsideStreetRoute route)
            {
                foreach (Vector2[] contour in route.WalkablePolygons)
                    if (CityRoadPolygon.Bounds(contour).Overlaps(Bounds)) existing.Add(contour);
            }

            void AddStrip(float start, float end, float lo, float hi, bool pavement)
            {
                var pieces = new List<Vector2[]> {
                    CityRoadPolygon.Rectangle(Rect.MinMaxRect(start, z + lo, end, z + hi)) };
                // Both surfaces meet at the actual faceted outer edge of each
                // hairpin. No overlaid deck or concrete curb across the mouth.
                foreach (Vector2[] occupied in existing)
                {
                    Rect occupiedBounds = CityRoadPolygon.Bounds(occupied);
                    for (int i = pieces.Count - 1; i >= 0; i--)
                    {
                        Vector2[] piece = pieces[i];
                        if (!occupiedBounds.Overlaps(CityRoadPolygon.Bounds(piece))) continue;
                        pieces.RemoveAt(i);
                        pieces.AddRange(CityRoadPolygon.Subtract(piece, occupied));
                    }
                }
                foreach (Vector2[] piece in pieces)
                {
                    var top = new Vector3[piece.Length];
                    for (int i = 0; i < piece.Length; i++)
                        top[i] = new Vector3(piece[i].x, RoadTopAt(piece[i].x) + (pavement ? Curb : 0f), piece[i].y);
                    patches.Add(new CityStreetSupportPatch(top, pavement ? CityStreetSupportKind.Sidewalk : CityStreetSupportKind.Asphalt,
                        pavement ? .30f : .20f, pavement ? 30 : 20, roadFrame: frame));
                }
            }
        }

        private IReadOnlyList<Vector3> Course(Vector3 first, Vector3 last, bool pavement)
        {
            float w = WestGateWorld.x + FlatThroat, e = EastGateWorld.x - FlatThroat;
            return Array.AsReadOnly(new[] { first, new Vector3(w, RoadTopAt(w) + (pavement ? Curb : 0f), first.z),
                new Vector3(e, RoadTopAt(e) + (pavement ? Curb : 0f), last.z), last });
        }

        public float RoadTopAt(float x) => Mathf.Lerp(WestGateWorld.y, EastGateWorld.y,
            Mathf.InverseLerp(WestGateWorld.x + FlatThroat, EastGateWorld.x - FlatThroat, x));
        public float PavementTopAt(float x) => RoadTopAt(x) + Curb;

        public bool TrySampleTop(Vector2 p, out float top, out CityStreetSupportKind kind)
        {
            top = 0f;
            kind = CityStreetSupportKind.Asphalt;
            // A summit pedestrian gate is inset one metre into the existing
            // terminal pavement. Only that short approach can extend past the link.
            if (p.y < Bounds.yMin - .001f || p.y > Bounds.yMax + .001f ||
                p.x < Bounds.xMin - 1.001f || p.x > Bounds.xMax + 1.001f) return false;
            if (p.x < WestGateWorld.x + FlatThroat && west.TrySampleTop(p, out top, out kind)) return true;
            if (p.x > EastGateWorld.x - FlatThroat && east.TrySampleTop(p, out top, out kind)) return true;
            if (!CityHillsideBuilding.Inside(Bounds, p)) return false;
            bool pavement = Mathf.Abs(p.y - WestGateWorld.z) >= CarriagewayWidth * .5f;
            kind = pavement ? CityStreetSupportKind.Sidewalk : CityStreetSupportKind.Asphalt;
            top = RoadTopAt(p.x) + (pavement ? Curb : 0f);
            return true;
        }

        private static Vector3 Gate(CityHillsideStreetRoute route, bool summit)
        {
            int i = summit ? route.Path.Vertices.Count - 1 : route.CourtApex;
            Vector2 p = route.Path.Vertices[i];
            return new Vector3(p.x, route.Profile.Stations[i].Height, p.y);
        }

        private static Vector3 PavementGate(CityHillsideStreetRoute route, float z, bool summit)
        {
            if (summit)
            {
                Vector3 a = route.SidewalkPath[route.SidewalkPath.Count - 1];
                Vector3 b = route.OppositeSidewalkPath[route.OppositeSidewalkPath.Count - 1];
                return Mathf.Abs(a.z - z) <= Mathf.Abs(b.z - z) ? a : b;
            }
            // At the outward face of the inner hairpin both footways cross
            // this Z in just one segment. Select the side closest to the quarter.
            bool west = route.Bank == CityTerraceTransportBank.West;
            Vector3 best = new Vector3(west ? float.NegativeInfinity : float.PositiveInfinity, 0f, z);
            Search(route.SidewalkPath);
            Search(route.OppositeSidewalkPath);
            if (float.IsInfinity(best.x)) throw new InvalidOperationException("The quarter has no hairpin pavement connection.");
            return best;

            void Search(IReadOnlyList<Vector3> path)
            {
                for (int i = route.CourtApex - 8; i < route.CourtApex + 8; i++)
                {
                    Vector3 a = path[i], b = path[i + 1];
                    if ((z - a.z) * (z - b.z) > 0f || Mathf.Abs(b.z - a.z) < .0001f) continue;
                    Vector3 p = Vector3.Lerp(a, b, (z - a.z) / (b.z - a.z));
                    if (west ? p.x > best.x : p.x < best.x) best = p;
                }
            }
        }
    }
}
