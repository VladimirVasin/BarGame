using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical doorway and its finite, continuous route to public pavement.</summary>
    public sealed class CityResidentialEntranceDescriptor
    {
        internal CityResidentialEntranceDescriptor(BuildingLot lot, Vector3 threshold, Vector3 outward,
            IList<Vector3> points, IList<CityEntranceForecourtPlan> paving)
        {
            Lot = lot; Threshold = threshold; Outward = outward;
            Points = new ReadOnlyCollection<Vector3>(points);
            Paving = new ReadOnlyCollection<CityEntranceForecourtPlan>(paving);
            var footprints = new List<Rect>(paving.Count);
            foreach (CityEntranceForecourtPlan strip in paving) footprints.Add(strip.Footprint);
            Footprints = footprints.AsReadOnly();
        }
        public BuildingLot Lot { get; }
        public Vector3 Threshold { get; }
        public Vector3 Outward { get; }
        public IReadOnlyList<Vector3> Points { get; }
        public IReadOnlyList<CityEntranceForecourtPlan> Paving { get; }
        public IReadOnlyList<Rect> Footprints { get; }
    }

    /// <summary>
    /// Final, immutable entrance access. It reads allocated courtyard masses and
    /// existing public paving and upstream terrain, never final entrance-supported
    /// terrain, decoration or physics.
    /// Builders prime it after infill allocation; early planners only read the
    /// primary reservations or an already cached final plan.
    /// </summary>
    public sealed class CityResidentialEntrancePlan
    {
        public const float PathWidth = 1.5f;
        public const float ThroatWidth = 1.3f;
        private const float RouteRadius = PathWidth * .5f + .01f;
        private const float ApronRun = 1.4f;
        private const float Thickness = .4f;
        private const float SampleBucketSize = 4f;
        private const int MaximumConnectedCells = 12;
        private static readonly ConditionalWeakTable<CityLayout, CityResidentialEntrancePlan> Plans =
            new ConditionalWeakTable<CityLayout, CityResidentialEntrancePlan>();
        private static readonly ConditionalWeakTable<CityLayout, PrimaryReservations> Reservations =
            new ConditionalWeakTable<CityLayout, PrimaryReservations>();

        private readonly Dictionary<Vector2Int, List<int>> sampleBuckets = new Dictionary<Vector2Int, List<int>>();
        private readonly IReadOnlyList<CityStreetSupportPatch> supportPatches;

        private CityResidentialEntrancePlan(IList<CityResidentialEntranceDescriptor> entrances)
        {
            Entrances = new ReadOnlyCollection<CityResidentialEntranceDescriptor>(entrances);
            var patches = new List<CityStreetSupportPatch>();
            foreach (CityResidentialEntranceDescriptor entry in entrances)
            foreach (CityEntranceForecourtPlan strip in entry.Paving)
            for (int row = 0; row < strip.Rows - 1; row++)
            for (int column = 0; column < strip.Columns - 1; column++)
            {
                int a = row * strip.Columns + column;
                var face = new[] { strip.Top[a], strip.Top[a + 1],
                    strip.Top[a + strip.Columns + 1], strip.Top[a + strip.Columns] };
                var patch = new CityStreetSupportPatch(face, CityStreetSupportKind.Sidewalk, Thickness, 31);
                Add(patch);
            }
            SurfacePatches = patches.AsReadOnly();
            var allSupport = new List<CityStreetSupportPatch>(patches);
            foreach (CityResidentialEntranceDescriptor entry in entrances)
            {
                Vector3 side = Vector3.Cross(Vector3.up, entry.Outward) * (CityBuildingAssetProvider.EntranceClearWidth * .5f);
                Vector3 back = entry.Threshold - entry.Outward * CityBuildingAssetProvider.EntranceDepth;
                // The imported model owns this visible floor. Only its plane
                // is retained here for terrain exclusion/contact queries.
                var floor = new CityStreetSupportPatch(new[] { entry.Threshold - side, entry.Threshold + side,
                    back + side, back - side }, CityStreetSupportKind.Sidewalk, .08f, 31);
                int index = allSupport.Count; allSupport.Add(floor); Index(floor, index);
            }
            supportPatches = allSupport.AsReadOnly();

            void Add(CityStreetSupportPatch patch)
            {
                int index = patches.Count; patches.Add(patch); Index(patch, index);
            }

            void Index(CityStreetSupportPatch patch, int index)
            {
                Rect bounds = Expanded(patch.Bounds, .002f);
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!sampleBuckets.TryGetValue(key, out List<int> entries)) sampleBuckets.Add(key, entries = new List<int>());
                    entries.Add(index);
                }
            }
        }

        public IReadOnlyList<CityResidentialEntranceDescriptor> Entrances { get; }
        public IReadOnlyList<CityStreetSupportPatch> SurfacePatches { get; }

        public static CityResidentialEntrancePlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, Create);
        }

        public static bool TrySampleCachedTop(CityLayout layout, Vector2 point, out float top)
        {
            top = float.NegativeInfinity;
            return Plans.TryGetValue(layout, out CityResidentialEntrancePlan plan) && plan.TrySampleTop(point, out top);
        }

        public bool TrySampleTop(Vector2 point, out float top)
        {
            top = float.NegativeInfinity;
            if (!sampleBuckets.TryGetValue(new Vector2Int(Bucket(point.x), Bucket(point.y)), out List<int> nearby)) return false;
            foreach (int index in nearby)
            {
                if (!supportPatches[index].TrySampleTop(point, out float height)) continue;
                // The ground compositor retains the first equal-priority
                // source. Contact queries must choose that same triangle.
                top = height; return true;
            }
            return false;
        }

        public bool Intersects(Rect footprint)
        {
            foreach (CityResidentialEntranceDescriptor entry in Entrances)
                foreach (Rect path in entry.Footprints)
                    if (path.Overlaps(footprint)) return true;
            return false;
        }

        public static bool IntersectsReservedApproach(CityLayout layout, Rect footprint)
        {
            if (Plans.TryGetValue(layout, out CityResidentialEntrancePlan plan) && plan.Intersects(footprint)) return true;
            return IntersectsPrimaryApproach(layout, footprint);
        }

        public static bool IntersectsPrimaryApproach(CityLayout layout, Rect footprint)
        {
            foreach (Rect reserve in Reservations.GetValue(layout, value => new PrimaryReservations(value)).Footprints)
                if (reserve.Overlaps(footprint)) return true;
            return false;
        }

        private sealed class PrimaryReservations
        {
            internal readonly List<Rect> Footprints = new List<Rect>();
            internal PrimaryReservations(CityLayout layout)
            {
                // This deliberately never reads BuildingMasses or terrain:
                // those allocate the laundry and invoke decoration themselves.
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (!IsResidential(lot)) continue;
                    CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                    Vector3 threshold = pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                    Vector3 front = threshold + lot.FacadeForward * ApronRun;
                    Footprints.Add(SegmentBounds(XZ(threshold), XZ(front), PathWidth * .5f));
                    if (lot.HasRoadFrontage)
                        Footprints.Add(SegmentBounds(XZ(front), XZ(lot.SidewalkArrivalPosition), PathWidth * .5f));
                }
            }
        }

        private static CityResidentialEntrancePlan Create(CityLayout layout)
        {
            // Finish courtyard allocation before an infill lookup can enter
            // its ground sampler. Subsequent queries read the same identities.
            IReadOnlyList<BuildingLot> masses = layout.BuildingMasses;
            var obstacles = new List<Vector2[]>();
            foreach (BuildingLot lot in masses)
                if (lot.HasBuilding || lot.IsCafe) obstacles.AddRange(lot.CreateCollisionPolygons());
            CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled) foreach (Rect body in laundry.BodyFootprints) obstacles.Add(CityRoadPolygon.Rectangle(body));
            CityDogParkPlan dogs = CityDogParkPlan.ForLayout(layout);
            if (dogs.IsEnabled) obstacles.Add(CityRoadPolygon.Rectangle(dogs.Footprint));
            // These fixtures already own physical street ground. Read their
            // deterministic placement after courts/infill have been cached;
            // a doorway route must bend around their actual lower solids.
            CityNightFixturePlan night = CityNightFixturePlanner.CreatePlan(layout);
            foreach (StreetLampDescriptor lamp in night.StreetLamps) AddPole(lamp.Position);
            foreach (TrafficSignalDescriptor signal in night.TrafficSignals) AddPole(signal.Position);
            foreach (CityBusStopDescriptor stop in CityBusPlanner.CreateRoadRouting(layout).Stops)
                obstacles.Add(CityRoadPolygon.Rectangle(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop)));

            var grade = new SharedGradeField(layout, masses);
            var result = new List<CityResidentialEntranceDescriptor>();
            foreach (BuildingLot lot in masses)
            {
                if (!IsResidential(lot)) continue;
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                Vector3 threshold = pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                Vector3 outward = pose.Rotation * CityBuildingAssetProvider.GetExpectedEntranceOutward(lot.BuildingVariant);
                Vector2 apron = XZ(threshold + outward * ApronRun);
                var query = new RouteQuery(layout, lot, obstacles);
                List<Vector2> route = query.Route(apron);
                if (route == null)
                    throw new InvalidOperationException($"Residential entrance {lot.Cell}/variant {lot.BuildingVariant} has no continuous pavement route from {threshold}: {query.Failure}.");
                var points = new List<Vector3> { threshold };
                Vector2 end = route[route.Count - 1];
                if (!CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, end, out _))
                    throw new InvalidOperationException($"Residential entrance {lot.Cell} does not end on public support.");
                foreach (Vector2 point in route)
                    points.Add(new Vector3(point.x, grade.Sample(point), point.y));
                List<CityEntranceForecourtPlan> paving = CreatePaving(layout, points, grade);
                foreach (CityEntranceForecourtPlan strip in paving)
                {
                    Vector2[] contour = CityRoadPolygon.CounterClockwise(new[] { XZ(strip.Top[0]), XZ(strip.Top[strip.Columns - 1]),
                        XZ(strip.Top[strip.Rows * strip.Columns - 1]), XZ(strip.Top[(strip.Rows - 1) * strip.Columns]) });
                    foreach (Vector2[] body in obstacles)
                    {
                        if (!strip.Footprint.Overlaps(CityRoadPolygon.Bounds(body))) continue;
                        var clipped = new List<Vector2>(contour);
                        for (int side = 0; side < body.Length && clipped.Count >= 3; side++)
                            clipped = CityRoadPolygon.Clip(clipped, body[side], body[(side + 1) % body.Length]);
                        if (CityRoadPolygon.Area(clipped) > .0001f)
                            throw new InvalidOperationException($"Residential entrance {lot.Cell}/variant {lot.BuildingVariant} paving shoulder intersects a physical mass.");
                    }
                }
                result.Add(new CityResidentialEntranceDescriptor(lot, threshold, outward, points, paving));
            }
            return new CityResidentialEntrancePlan(result);

            void AddPole(Vector3 position)
            {
                Bounds solid = CityStaticCollisionBuilder.CreateLowerPoleBounds(position);
                obstacles.Add(CityRoadPolygon.Rectangle(Rect.MinMaxRect(solid.min.x, solid.min.z, solid.max.x, solid.max.z)));
            }
        }

        private static List<CityEntranceForecourtPlan> CreatePaving(CityLayout layout, IReadOnlyList<Vector3> points, SharedGradeField grade)
        {
            var left = new Vector3[points.Count]; var right = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 before = XZ(points[i] - points[Mathf.Max(0, i - 1)]).normalized;
                Vector2 after = XZ(points[Mathf.Min(points.Count - 1, i + 1)] - points[i]).normalized;
                if (i == 0) before = after; if (i == points.Count - 1) after = before;
                Vector2 tangent = (before + after).normalized;
                if (tangent.sqrMagnitude < .5f) tangent = after;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                Vector2 legNormal = new Vector2(-before.y, before.x);
                float width = i == 0 ? ThroatWidth : PathWidth;
                float reach = width * .5f / Mathf.Max(.65f, Vector2.Dot(normal, legNormal));
                Vector3 offset = new Vector3(normal.x, 0f, normal.y) * reach;
                left[i] = points[i] + offset; right[i] = points[i] - offset;
                if (i != points.Count - 1) continue;
                // The whole shared edge, including public crossfall, matches
                // the actual triangulated pavement instead of a guessed datum.
                if (CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, XZ(left[i]), out float a)) left[i].y = a;
                if (CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, XZ(right[i]), out float b)) right[i].y = b;
            }
            var paving = new List<CityEntranceForecourtPlan>(points.Count - 1);
            for (int leg = 1; leg < points.Count; leg++)
            {
                int columns = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(XZ(points[leg - 1]), XZ(points[leg])) / .3f) + 1, 2, 193);
                int rows = Mathf.CeilToInt(PathWidth / .3f) + 1;
                var top = new Vector3[columns * rows]; var bottom = new Vector3[top.Length];
                for (int row = 0; row < rows; row++)
                {
                    float across = row / (float)(rows - 1);
                    Vector3 start = Vector3.Lerp(left[leg - 1], right[leg - 1], across);
                    Vector3 end = Vector3.Lerp(left[leg], right[leg], across);
                    for (int column = 0; column < columns; column++)
                    {
                        Vector3 point = Vector3.Lerp(start, end, column / (float)(columns - 1));
                        point.y = grade.Sample(XZ(point));
                        int index = row * columns + column;
                        top[index] = point; bottom[index] = point - Vector3.up * Thickness;
                    }
                }
                paving.Add(new CityEntranceForecourtPlan(top, bottom, rows, columns, true));
            }
            return paving;
        }

        // All routes sample the same field. Intersecting walkways therefore
        // inherit the existing ground grade rather than two unrelated ramps
        // between different houses. Only the short real threshold pad departs
        // from that shared ground, and every overlapping route sees that pad.
        private sealed class SharedGradeField
        {
            private readonly CityLayout layout;
            private readonly List<GradeAnchor> anchors = new List<GradeAnchor>();
            private readonly Dictionary<Vector2, float> samples = new Dictionary<Vector2, float>();
            private readonly List<CitySurfaceDescriptor> ground = new List<CitySurfaceDescriptor>();
            internal SharedGradeField(CityLayout source, IReadOnlyList<BuildingLot> masses)
            {
                layout = source;
                foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                    if (CityTerrainSurfacePlan.UsesContinuousTop(surface)) ground.Add(surface);
                foreach (BuildingLot lot in masses)
                {
                    if (!IsResidential(lot)) continue;
                    CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                    anchors.Add(new GradeAnchor(pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant)),
                        pose.Rotation * CityBuildingAssetProvider.GetExpectedEntranceOutward(lot.BuildingVariant)));
                }
            }

            internal float Sample(Vector2 point)
            {
                if (samples.TryGetValue(point, out float saved)) return saved;
                float top;
                if (CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, point, out top))
                { samples.Add(point, top); return top; }
                bool found = false;
                foreach (CitySurfaceDescriptor surface in ground)
                {
                    if (!surface.WorldBounds.Contains(point)) continue;
                    top = CityTerrainSurfacePlan.SampleTopWithoutRigidSupports(layout, surface, point);
                    found = true; break;
                }
                if (!found)
                    throw new InvalidOperationException($"Residential paving has no upstream ground support at {point}.");
                float correction = 0f, weightSum = 0f;
                foreach (GradeAnchor anchor in anchors)
                {
                    Vector2 delta = point - XZ(anchor.Threshold);
                    if (delta.sqrMagnitude > 9f) continue;
                    Vector2 forward = XZ(anchor.Outward);
                    float along = Vector2.Dot(delta, forward);
                    float across = Mathf.Abs(CityRoadPolygon.Cross(forward, delta));
                    if (along < -CityBuildingAssetProvider.EntranceDepth || along > ApronRun || across > 1.1f) continue;
                    float weight = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, ApronRun, Mathf.Max(0f, along)))) *
                        (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CityBuildingAssetProvider.EntranceClearWidth * .5f, 1.1f, across)));
                    correction += (anchor.Threshold.y - top) * weight; weightSum += weight;
                }
                if (weightSum > 0f) top += correction / Mathf.Max(1f, weightSum);
                samples.Add(point, top); return top;
            }

            private readonly struct GradeAnchor
            {
                internal GradeAnchor(Vector3 threshold, Vector3 outward) { Threshold = threshold; Outward = outward; }
                internal Vector3 Threshold { get; }
                internal Vector3 Outward { get; }
            }
        }

        private sealed class RouteQuery
        {
            private readonly CityLayout layout;
            private readonly List<Vector2[]> ground = new List<Vector2[]>();
            private readonly List<Vector2[]> bodies = new List<Vector2[]>();
            private readonly List<Rect> groundBounds = new List<Rect>();
            private readonly List<Rect> bodyClearanceBounds = new List<Rect>();
            private readonly Dictionary<Vector2, bool> clearMemo = new Dictionary<Vector2, bool>();
            private readonly List<Vector2> destinations = new List<Vector2>();
            private readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            private readonly List<RoadEdge> streets = new List<RoadEdge>();
            private readonly Rect searchBounds;
            private readonly Vector2Int sourceCell;
            internal string Failure { get; private set; } = string.Empty;

            internal RouteQuery(CityLayout source, BuildingLot lot, IReadOnlyList<Vector2[]> obstacles)
            {
                layout = source;
                sourceCell = lot.Cell;
                var pending = new Queue<Vector2Int>(); pending.Enqueue(lot.Cell); cells.Add(lot.Cell);
                Vector2Int[] directions = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
                while (pending.Count > 0)
                {
                    Vector2Int cell = pending.Dequeue();
                    ground.AddRange(layout.RoadGeometry.GetGroundPolygons(cell));
                    foreach (Vector2Int direction in directions)
                    {
                        RoadEdge edge = RoadEdge.ForCellFrontage(cell, direction);
                        if (layout.HasRoad(edge))
                        {
                            if (layout.GetPathKind(edge) == CityPathKind.Street && !streets.Contains(edge) &&
                                !layout.TryGetBridge(edge, out _) && !layout.ElevationPlan.TryGetSignatureStair(edge, out _)) streets.Add(edge);
                            continue;
                        }
                        Vector2Int next = cell + direction;
                        if (cells.Count >= MaximumConnectedCells || cells.Contains(next) ||
                            next.x < 0 || next.y < 0 || next.x >= layout.BlockCount.x || next.y >= layout.BlockCount.y ||
                            !layout.Blueprint.TryGetArea(next, out CityAreaDefinition area) || !area.SupportsOrdinaryBuildings) continue;
                        cells.Add(next); pending.Enqueue(next);
                    }
                }
                Rect reach = layout.GetCellWorldBounds(lot.Cell);
                foreach (Vector2Int cell in cells) reach = Union(reach, layout.GetCellWorldBounds(cell));
                reach = Expanded(reach, 1f);
                searchBounds = Expanded(reach, 2f);
                foreach (Vector2[] body in obstacles)
                    if (reach.Overlaps(CityRoadPolygon.Bounds(body)))
                    {
                        bodies.Add(body);
                        bodyClearanceBounds.Add(Expanded(CityRoadPolygon.Bounds(body), RouteRadius));
                    }
                foreach (Vector2[] polygon in ground) groundBounds.Add(Expanded(CityRoadPolygon.Bounds(polygon), .002f));
                streets.Sort(RoadEdge.Compare);
            }

            internal List<Vector2> Route(Vector2 start)
            {
                if (!Clear(start)) { Failure = $"apron {start} is blocked"; return null; }
                foreach (RoadEdge edge in streets)
                {
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    CityRoadProjection projection = path.Project(start);
                    float inset = Mathf.Min(layout.RoadWidth * .5f + 1f, path.Length * .4f);
                    foreach (float shift in new[] { 0f, -2f, 2f, -5f, 5f })
                    {
                        CityRoadSample sample = path.SampleDistance(Mathf.Clamp(projection.DistanceAlong + shift, inset, path.Length - inset));
                        float side = Mathf.Sign(Vector2.Dot(start - sample.Position, sample.Right));
                        foreach (float distance in new[] { 4f, 3.85f, 3.5f, 4.35f })
                        {
                            if (distance > CityStreetSurfacePlanner.GetPublicSidewalkOuterDistance(layout) - .05f) continue;
                            Vector2 candidate = sample.Position + sample.Right * (side * distance);
                            if (!CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, candidate, out _) || !Clear(candidate)) continue;
                            destinations.Add(candidate); break;
                        }
                    }
                }
                destinations.Sort((a, b) => (a - start).sqrMagnitude.CompareTo((b - start).sqrMagnitude));
                if (destinations.Count > 20) destinations.RemoveRange(20, destinations.Count - 20);
                foreach (Vector2 destination in destinations)
                    if (ClearSegment(start, destination) && DestinationSeamClear(start, destination))
                        return new List<Vector2> { start, destination };
                var nodes = new List<Vector2> { start }; nodes.AddRange(destinations);
                foreach (Vector2[] body in bodies)
                    for (int i = 0; i < body.Length; i++)
                    {
                        Vector2 incoming = (body[i] - body[(i + body.Length - 1) % body.Length]).normalized;
                        Vector2 outgoing = (body[(i + 1) % body.Length] - body[i]).normalized;
                        Vector2 corner = body[i] + new Vector2(incoming.y + outgoing.y, -incoming.x - outgoing.x) * (RouteRadius + .08f);
                        if (Clear(corner)) nodes.Add(corner);
                    }
                if (nodes.Count > 384) throw new InvalidOperationException("Residential entrance visibility graph exceeds its finite cell budget.");
                var cost = new float[nodes.Count]; var previous = new int[nodes.Count]; var visited = new bool[nodes.Count];
                for (int i = 0; i < nodes.Count; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
                cost[0] = 0f;
                for (int step = 0; step < nodes.Count; step++)
                {
                    int nearest = -1;
                    for (int i = 0; i < nodes.Count; i++) if (!visited[i] && (nearest < 0 || cost[i] < cost[nearest])) nearest = i;
                    if (nearest < 0 || float.IsPositiveInfinity(cost[nearest])) break;
                    if (nearest > 0 && nearest <= destinations.Count)
                    {
                        var route = new List<Vector2>();
                        for (int cursor = nearest; cursor >= 0; cursor = previous[cursor]) route.Add(nodes[cursor]);
                        route.Reverse(); return route;
                    }
                    visited[nearest] = true;
                    for (int i = 1; i < nodes.Count; i++)
                    {
                        if (visited[i]) continue;
                        float nextCost = cost[nearest] + Vector2.Distance(nodes[nearest], nodes[i]);
                        if (nextCost >= cost[i] || !ClearSegment(nodes[nearest], nodes[i]) ||
                            i <= destinations.Count && !DestinationSeamClear(nodes[nearest], nodes[i])) continue;
                        cost[i] = nextCost; previous[i] = nearest;
                    }
                }
                // A pole beside a facade can put all its expanded corners
                // beyond the ground boundary. The free corridor still exists,
                // but a body-corner-only visibility graph cannot describe it.
                return RouteGrid(start);
            }

            private List<Vector2> RouteGrid(Vector2 start)
            {
                const float spacing = .5f;
                const int maximumNodes = 8192;
                const float maximumReach = 48f;
#if UNITY_EDITOR
                Debug.Log($"Residential access fallback {sourceCell}: apron {start}, destinations {destinations.Count}, bodies {bodies.Count}, ground {ground.Count}.");
#endif
                var ids = new Dictionary<Vector2Int, int>();
                var positions = new List<Vector2Int>();
                var costs = new List<float>();
                var previous = new List<int>();
                var closed = new List<bool>();
                var open = new SortedSet<GridOpen>();
                var clearance = new Dictionary<Vector2Int, bool>();
                int serial = 0;
                Add(Vector2Int.zero, 0f, -1);
                Vector2Int[] offsets = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down,
                    new Vector2Int(-1,-1), new Vector2Int(-1,1), new Vector2Int(1,-1), new Vector2Int(1,1) };
                while (open.Count > 0)
                {
                    GridOpen next = open.Min; open.Remove(next);
                    int current = next.Id;
                    if (closed[current]) continue;
                    closed[current] = true;
                    Vector2 point = World(positions[current]);
                    if (previous[current] >= 0 &&
                        CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, point, out _) &&
                        DestinationSeamClear(World(positions[previous[current]]), point))
                    {
                        var route = new List<Vector2>();
                        for (int cursor = current; cursor >= 0; cursor = previous[cursor]) route.Add(World(positions[cursor]));
                        route.Reverse();
#if UNITY_EDITOR
                        Debug.Log($"Residential access fallback {sourceCell}: public pavement reached after {positions.Count} grid nodes.");
#endif
                        return Simplify(route);
                    }
                    foreach (Vector2Int offset in offsets)
                    {
                        Vector2Int cell = positions[current] + offset;
                        Vector2 candidate = World(cell);
                        if (!searchBounds.Contains(candidate) || (candidate - start).sqrMagnitude > maximumReach * maximumReach) continue;
                        if (!clearance.TryGetValue(cell, out bool clear))
                        { clear = Clear(candidate); clearance.Add(cell, clear); }
                        if (!clear || !ClearSegment(point, candidate)) continue;
                        float cost = costs[current] + Vector2.Distance(point, candidate);
                        if (ids.TryGetValue(cell, out int id))
                        {
                            if (closed[id] || cost >= costs[id]) continue;
                            costs[id] = cost; previous[id] = current;
                            open.Add(new GridOpen(cost + Heuristic(candidate), serial++, id));
                        }
                        else if (positions.Count < maximumNodes) Add(cell, cost, current);
                    }
                }
                Failure = $"apron {start}; {destinations.Count} public destinations; grid visited {positions.Count}/{maximumNodes}";
                return null;

                Vector2 World(Vector2Int cell) => start + new Vector2(cell.x * spacing, cell.y * spacing);
                float Heuristic(Vector2 point)
                {
                    float distance = float.PositiveInfinity;
                    foreach (Vector2 target in destinations) distance = Mathf.Min(distance, Vector2.Distance(point, target));
                    return float.IsPositiveInfinity(distance) ? 0f : distance;
                }
                void Add(Vector2Int cell, float cost, int parent)
                {
                    int id = positions.Count;
                    ids.Add(cell, id); positions.Add(cell); costs.Add(cost); previous.Add(parent); closed.Add(false);
                    open.Add(new GridOpen(cost + Heuristic(World(cell)), serial++, id));
                }
            }

            private List<Vector2> Simplify(IReadOnlyList<Vector2> source)
            {
                var result = new List<Vector2> { source[0] };
                int cursor = 0;
                while (cursor < source.Count - 1)
                {
                    int selected = cursor + 1;
                    for (int next = source.Count - 1; next > cursor + 1; next--)
                    {
                        if (!ClearSegment(source[cursor], source[next]) ||
                            next == source.Count - 1 && !DestinationSeamClear(source[cursor], source[next])) continue;
                        selected = next; break;
                    }
                    result.Add(source[selected]); cursor = selected;
                }
                return result;
            }

            private readonly struct GridOpen : IComparable<GridOpen>
            {
                internal GridOpen(float score, int serial, int id) { Score = score; Serial = serial; Id = id; }
                private float Score { get; }
                private int Serial { get; }
                internal int Id { get; }
                public int CompareTo(GridOpen other)
                {
                    int order = Score.CompareTo(other.Score);
                    return order != 0 ? order : Serial.CompareTo(other.Serial);
                }
            }

            private bool ClearSegment(Vector2 a, Vector2 b)
            {
                float length = Vector2.Distance(a, b);
                if (length < .01f) return false;
                int samples = Mathf.CeilToInt(length / .3f);
                for (int i = 0; i <= samples; i++) if (!Clear(Vector2.Lerp(a, b, i / (float)samples))) return false;
                return true;
            }

            private bool DestinationSeamClear(Vector2 previous, Vector2 destination)
            {
                Vector2 direction = (destination - previous).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * (PathWidth * .5f);
                return CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, destination + normal, out _) &&
                    CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, destination - normal, out _);
            }

            private bool Clear(Vector2 point)
            {
                if (clearMemo.TryGetValue(point, out bool cached)) return cached;
                bool clear = ClearUncached(point);
                // Visibility and fallback links repeatedly share endpoints and
                // half-metre stations. This finite build-local memo disappears
                // with the query; no world/frame cache grows with exploration.
                if (clearMemo.Count < 65536) clearMemo.Add(point, clear);
                return clear;
            }

            private bool ClearUncached(Vector2 point)
            {
                for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                {
                    if (!bodyClearanceBounds[bodyIndex].Contains(point)) continue;
                    Vector2[] body = bodies[bodyIndex];
                    if (CityRoadPolygon.Contains(body, point)) return false;
                    for (int i = 0; i < body.Length; i++)
                    {
                        Vector2 first = body[i], delta = body[(i + 1) % body.Length] - first;
                        Vector2 nearest = first + delta * Mathf.Clamp01(Vector2.Dot(point - first, delta) / delta.sqrMagnitude);
                        if ((point - nearest).sqrMagnitude < RouteRadius * RouteRadius) return false;
                    }
                }
                for (int sample = 0; sample < 8; sample++)
                {
                    float angle = sample * Mathf.PI * .25f;
                    Vector2 rim = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RouteRadius;
                    bool contained = false;
                    for (int polygon = 0; polygon < ground.Count; polygon++)
                        if (groundBounds[polygon].Contains(rim) && CityRoadPolygon.Contains(ground[polygon], rim))
                        { contained = true; break; }
                    if (!contained && !CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout, rim, out _)) return false;
                }
                return true;
            }
        }

        private static bool IsResidential(BuildingLot lot) => lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential;
        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static int Bucket(float value) => Mathf.FloorToInt(value / SampleBucketSize);
        private static Rect Expanded(Rect r, float amount) => Rect.MinMaxRect(r.xMin - amount, r.yMin - amount, r.xMax + amount, r.yMax + amount);
        private static Rect SegmentBounds(Vector2 a, Vector2 b, float radius) => Rect.MinMaxRect(Mathf.Min(a.x, b.x) - radius,
            Mathf.Min(a.y, b.y) - radius, Mathf.Max(a.x, b.x) + radius, Mathf.Max(a.y, b.y) + radius);
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }
}
