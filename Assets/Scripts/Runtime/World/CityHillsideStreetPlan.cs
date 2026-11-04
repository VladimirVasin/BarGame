using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two southern streets on distinct hillside shelves. Every bend is a
    /// level turning platform. Ground, street meshes and travel use the same sections.</summary>
    public sealed class CityHillsideStreetPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideStreetPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityHillsideStreetPlan>();

        private CityHillsideStreetPlan(CityLayout layout)
        {
            var routes = new List<CityHillsideStreetRoute>(2);
            Routes = routes.AsReadOnly();
            if (!CityCoastalAscentPlan.IsEnabled(layout)) return;
            foreach (CityTerraceTransportBank bank in new[] { CityTerraceTransportBank.West, CityTerraceTransportBank.East })
            {
                var mouth = new Vector2Int(bank == CityTerraceTransportBank.West ? 1 : 12, 0);
                if (!layout.HasRoad(new RoadEdge(mouth - Vector2Int.right, mouth)) ||
                    !layout.HasRoad(new RoadEdge(mouth, mouth + Vector2Int.right))) continue;
                routes.Add(new CityHillsideStreetRoute(layout, bank, mouth));
            }
        }

        public IReadOnlyList<CityHillsideStreetRoute> Routes { get; }
        public static CityHillsideStreetPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideStreetPlan(value));
    }

    public sealed class CityHillsideStreetRoute
    {
        public const float RoadHalfWidth = 3f, OuterHalfWidth = 5f;
        public const int LaneCount = 7;
        public const float SummitTop = 40f;
        private const float EntryBendWidening = .90f, HairpinWidening = .75f;
        private const float FirstLaneZ = -190f, TurnRadius = 13f;
        private const float SidewalkRise = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;
        private readonly int[] laneStarts = new int[LaneCount], laneEnds = new int[LaneCount];
        private readonly CityStreetSupportPlan supports;

        internal CityHillsideStreetRoute(CityLayout layout, CityTerraceTransportBank bank, Vector2Int mouth)
        {
            Bank = bank;
            MouthNode = mouth;
            CoastApproachNodes = SurveyCoastApproach(bank, layout);
            bool west = bank == CityTerraceTransportBank.West;
            LaneMinX = west ? -146f : 70f;
            LaneMaxX = west ? -54f : 162f;
            Vector2 node = layout.RoadGeometry.GetNodePosition(mouth);
            float streetTop = layout.ElevationPlan.GetNodeElevation(mouth) + CityStreetSurfacePlanner.RoadTop;
            var points = new List<Vector2>(128);
            var sloping = new List<bool>(128);
            var bends = new List<(int first, int last, bool outsideRight, float widening)>(LaneCount);
            int direction = west ? 1 : -1;
            Add(new Vector2(node.x, node.y - RoadHalfWidth), false);
            Add(new Vector2(node.x, node.y - 10f), false);
            // A tangent quarter turn leaves the public street on a level throat.
            const float entryRadius = 9f;
            Vector2 entryCentre = new Vector2(node.x + direction * entryRadius, FirstLaneZ + entryRadius);
            Add(new Vector2(node.x, entryCentre.y + 10f), true);
            Add(new Vector2(node.x, entryCentre.y + 4f), true);
            Add(new Vector2(node.x, entryCentre.y), true);
            int arcStart = points.Count - 1;
            for (int step = 1; step <= 8; step++)
            {
                float a = Mathf.PI * .5f * step / 8;
                Add(entryCentre + new Vector2(-direction * Mathf.Cos(a), -Mathf.Sin(a)) * entryRadius, false);
            }
            bends.Add((arcStart, points.Count - 1, west, EntryBendWidening));
            for (int lane = 0; lane < LaneCount; lane++)
            {
                laneStarts[lane] = points.Count - 1;
                float end = direction > 0 ? LaneMaxX : LaneMinX;
                float z = GetLaneZ(lane);
                float start = points[points.Count - 1].x;
                Add(new Vector2(start + direction * 4f, z), false);
                Add(new Vector2(start + direction * 10f, z), true);
                if (lane < LaneCount - 1) Add(new Vector2(end - direction * 10f, z), true);
                Add(new Vector2(end - direction * (lane == LaneCount - 1 ? 16f : 4f), z), true);
                Add(new Vector2(end, z), false);
                laneEnds[lane] = points.Count - 1;
                if (lane == LaneCount - 1) break;
                Vector2 bend = new Vector2(end, z - TurnRadius);
                arcStart = points.Count - 1;
                for (int step = 1; step <= 16; step++)
                {
                    float a = Mathf.PI * step / 16;
                    Add(bend + new Vector2(direction * Mathf.Sin(a), Mathf.Cos(a)) * TurnRadius, false);
                }
                bends.Add((arcStart, points.Count - 1, direction < 0, HairpinWidening));
                direction = -direction;
            }

            Path = new CityRoadPath(points);
            var distances = new float[points.Count];
            float run = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float span = Vector2.Distance(points[i - 1], points[i]);
                distances[i] = distances[i - 1] + span;
                if (sloping[i]) run += span;
            }
            var stations = new CityRoadElevationStation[points.Count];
            float travelled = 0f;
            for (int i = 0; i < stations.Length; i++)
            {
                if (sloping[i]) travelled += distances[i] - distances[i - 1];
                stations[i] = new CityRoadElevationStation(distances[i], Mathf.Lerp(streetTop, SummitTop, travelled / run));
            }
            Profile = new CityRoadElevationProfile(stations, CityElevationPlan.MaximumBusGradePercent);
            var miter = Path.Offset(1f).Vertices;
            var left = new Vector2[points.Count]; var right = new Vector2[points.Count];
            var innerLeft = new Vector2[points.Count]; var innerRight = new Vector2[points.Count];
            var sidewalk = new Vector3[points.Count]; var opposite = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                float leftWidening = 0f, rightWidening = 0f;
                foreach (var bend in bends)
                {
                    float distance = Mathf.Max(distances[bend.first] - distances[i], distances[i] - distances[bend.last]);
                    float amount = bend.widening * Mathf.Clamp01((10f - distance) / 6f);
                    if (bend.outsideRight) rightWidening = Mathf.Max(rightWidening, amount);
                    else leftWidening = Mathf.Max(leftWidening, amount);
                }
                // Only the outside of a curved lane needs the bus overhang apron;
                // each footway retains its full clear width on the varying contour.
                Vector2 normal = miter[i] - points[i];
                left[i] = points[i] - normal * (OuterHalfWidth + leftWidening);
                right[i] = points[i] + normal * (OuterHalfWidth + rightWidening);
                innerLeft[i] = points[i] - normal * (RoadHalfWidth + leftWidening);
                innerRight[i] = points[i] + normal * (RoadHalfWidth + rightWidening);
            }
            var patches = new List<CityStreetSupportPatch>((points.Count - 1) * 3);
            var corridor = new List<Vector2[]>(points.Count - 1);
            for (int i = 0; i < points.Count; i++)
            {
                sidewalk[i] = Point((right[i] + innerRight[i]) * .5f, i, SidewalkRise);
                opposite[i] = Point((left[i] + innerLeft[i]) * .5f, i, SidewalkRise);
                if (i == 0) continue;
                corridor.Add(CityRoadPolygon.CounterClockwise(new[] { left[i - 1], right[i - 1], right[i], left[i] }));
                Band(innerLeft, innerRight, 0f, CityStreetSupportKind.Asphalt, .20f, 20);
                Band(left, innerLeft, SidewalkRise, CityStreetSupportKind.Sidewalk, .30f, 30);
                Band(innerRight, right, SidewalkRise, CityStreetSupportKind.Sidewalk, .30f, 30);

                void Band(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b, float rise,
                    CityStreetSupportKind kind, float thickness, int priority)
                {
                    patches.Add(new CityStreetSupportPatch(new[] { Point(a[i - 1], i - 1, rise),
                        Point(b[i - 1], i - 1, rise), Point(b[i], i, rise), Point(a[i], i, rise) },
                        kind, thickness, priority, roadFrame: Path));
                }
            }
            SurfacePatches = patches.AsReadOnly();
            WalkablePolygons = corridor.AsReadOnly();
            supports = new CityStreetSupportPlan(SurfacePatches);
            // Keep the mapped arrival a full capsule inside the terminal pavement.
            int endIndex = sidewalk.Length - 1;
            sidewalk[endIndex] = Vector3.MoveTowards(sidewalk[endIndex], sidewalk[endIndex - 1], 1f);
            opposite[endIndex] = Vector3.MoveTowards(opposite[endIndex], opposite[endIndex - 1], 1f);
            SidewalkPath = Array.AsReadOnly(sidewalk);
            OppositeSidewalkPath = Array.AsReadOnly(opposite);
            Rect bounds = CityRoadPolygon.Bounds(left);
            foreach (Vector2 v in right) bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, v.x), Mathf.Min(bounds.yMin, v.y),
                Mathf.Max(bounds.xMax, v.x), Mathf.Max(bounds.yMax, v.y));
            WorldBounds = bounds;
            MouthCut = Rect.MinMaxRect(node.x - OuterHalfWidth, node.y - OuterHalfWidth - .02f,
                node.x + OuterHalfWidth, points[0].y);

            Vector3 Point(Vector2 xz, int index, float rise) => new Vector3(xz.x, stations[index].Height + rise, xz.y);
            void Add(Vector2 p, bool climb) { points.Add(p); sloping.Add(climb); }
        }

        public CityTerraceTransportBank Bank { get; }
        public Vector2Int MouthNode { get; }
        public IReadOnlyList<Vector2Int> CoastApproachNodes { get; }
        public CityRoadPath Path { get; }
        public CityRoadElevationProfile Profile { get; }
        public Rect WorldBounds { get; }
        public IReadOnlyList<Vector2[]> WalkablePolygons { get; }
        public IReadOnlyList<CityStreetSupportPatch> SurfacePatches { get; }
        public IReadOnlyList<Vector3> SidewalkPath { get; }
        public IReadOnlyList<Vector3> OppositeSidewalkPath { get; }
        public Vector3 SummitArrivalWorld => SidewalkPath[SidewalkPath.Count - 1];
        public float LaneMinX { get; }
        public float LaneMaxX { get; }
        internal Rect MouthCut { get; }
        public float GetLaneZ(int lane) => FirstLaneZ - 2f * TurnRadius * lane;
        public float SampleLaneTop(int lane, float worldX)
        {
            int first = laneStarts[lane], last = laneEnds[lane];
            float delta = (worldX - Path.Vertices[first].x) * Mathf.Sign(Path.Vertices[last].x - Path.Vertices[first].x);
            return Profile.SampleDistance(Mathf.Clamp(Profile.Stations[first].HorizontalDistance + delta,
                Profile.Stations[first].HorizontalDistance, Profile.Stations[last].HorizontalDistance)).Height;
        }
        // The old grid remains a through neighbourhood. This section follows
        // its public carriageways from the same 4.92 m coastal datum on each bank.
        private static IReadOnlyList<Vector2Int> SurveyCoastApproach(CityTerraceTransportBank bank, CityLayout layout)
        {
            bool east = bank == CityTerraceTransportBank.East;
            var route = east ? new List<Vector2Int> {
                new Vector2Int(10, 12), new Vector2Int(11, 12), new Vector2Int(11, 11),
                new Vector2Int(12, 11), new Vector2Int(13, 11) } : new List<Vector2Int> {
                new Vector2Int(3, 12), new Vector2Int(2, 12), new Vector2Int(1, 12), new Vector2Int(0, 12) };
            for (int z = east ? 10 : 11; z >= 0; z--) route.Add(new Vector2Int(east ? 13 : 0, z));
            route.Add(new Vector2Int(east ? 12 : 1, 0));
            for (int i = 1; i < route.Count; i++)
            {
                var edge = new RoadEdge(route[i - 1], route[i]);
                if (!layout.HasRoad(edge) || layout.GetPathKind(edge) != CityPathKind.Street)
                    return Array.Empty<Vector2Int>();
            }
            return route.AsReadOnly();
        }

        public bool TrySampleTop(Vector2 point, out float top, out CityStreetSupportKind kind)
        {
            bool found = supports.TrySampleTop(point, out top, out FootstepGroundKind material);
            kind = material == FootstepGroundKind.Stone ? CityStreetSupportKind.Sidewalk : CityStreetSupportKind.Asphalt;
            return found;
        }
    }
}
