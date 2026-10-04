using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCourtyardBlockKind
    {
        LRecess,
        OffsetPair,
        Passage,
        Ensemble,
        OpenCourt,
        ServiceCourt
    }

    public sealed class CityCourtyardBlock
    {
        internal CityCourtyardBlock(BuildingLot primary, BuildingLot rear,
            IList<Vector2[]> ground, CityRoadPath route, Vector3 court, Vector3 passage,
            CityCourtyardBlockKind kind = CityCourtyardBlockKind.LRecess)
        {
            Primary = primary; RearBuilding = rear;
            GroundPolygons = new ReadOnlyCollection<Vector2[]>(ground);
            Route = route; CourtCenter = court; PassageCenter = passage;
            Kind = kind;
        }
        public Vector2Int Cell => Primary.Cell;
        public CityCourtyardBlockKind Kind { get; }
        public BuildingLot Primary { get; }
        public BuildingLot RearBuilding { get; }
        public IReadOnlyList<Vector2[]> GroundPolygons { get; }
        public CityRoadPath Route { get; }
        public Vector3 CourtCenter { get; }
        public Vector3 PassageCenter { get; }
    }

    public sealed class CityCourtyardConnection
    {
        internal CityCourtyardConnection(CityCourtyardBlock first, CityCourtyardBlock second, CityRoadPath path)
        { First = first; Second = second; Path = path; }
        public CityCourtyardBlock First { get; }
        public CityCourtyardBlock Second { get; }
        public Vector2Int FirstCell => First.Cell;
        public Vector2Int SecondCell => Second.Cell;
        public CityRoadPath Path { get; }
    }

    /// <summary>
    /// Ordinary street fronts, internal courts and secondary wings share one
    /// fixed-metre layout. Physical rear bodies introduce no semantic lot or
    /// gameplay entrance. Missing road arms can join neighbouring courts.
    /// </summary>
    public static class CityCourtyardBlockPlanner
    {
        public const float PassageWidth = 2.2f;
        public const float OffsetPairPassageWidth = 3f;
        public const int WingVariant = 4;
        public static Vector2Int OffsetPairCell => new Vector2Int(0, 9);
        private const float RouteRadius = .4f;
        private const float WingClearance = .65f;
        private static readonly Vector2[] CapsuleRim = CreateCapsuleRim();

        internal static bool SupportsOffsetPair(CityGenerationSettings settings, Vector2Int cell)
        {
            if (cell != OffsetPairCell || settings.RoadGeometry?.ObliqueJunction == null)
                return false;
            Vector2 span = settings.GetCellSpan(cell);
            if (span.x < 40f || span.y < 34f) return false;
            RoadEdge east = RoadEdge.ForCellFrontage(cell, Vector2Int.right);
            RoadEdge west = RoadEdge.ForCellFrontage(cell, Vector2Int.left);
            foreach (RoadEdge edge in settings.RoadGeometry.Edges)
                if (edge.Equals(east) || edge.Equals(west)) return true;
            return false;
        }

        internal static Vector3 ResolveOffsetRearCenter(Vector3 primary, Vector3 facing)
        {
            Vector3 longHouse = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 1);
            Vector3 compact = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 0);
            return primary - facing * (longHouse.z * .5f + OffsetPairPassageWidth + compact.z * .5f)
                - Vector3.forward * 4.5f;
        }

        internal static Vector3 ResolveRearCenter(Vector3 primary, Vector2[] envelope)
        {
            return new Vector3(CityRoadPolygon.Bounds(envelope).xMin - PassageWidth - 6.75f,
                primary.y, primary.z);
        }

        public static IReadOnlyList<CityCourtyardBlock> Create(CityLayout layout)
        {
            var result = new List<CityCourtyardBlock>();
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (lot.Cell == OffsetPairCell && layout.RoadGeometry.ObliqueJunction != null &&
                    lot.District == CityDistrictKind.OldTown &&
                    layout.GetCellWorldBounds(lot.Cell).width >= 40f && layout.GetCellWorldBounds(lot.Cell).height >= 34f &&
                    lot.IsOrdinaryBuilding && lot.BuildingVariant == 1 && lot.FrontageDirection.x != 0 &&
                    layout.HasRoad(RoadEdge.ForCellFrontage(lot.Cell, lot.FrontageDirection)) &&
                    layout.GetPathKind(RoadEdge.ForCellFrontage(lot.Cell, lot.FrontageDirection)) == CityPathKind.Street &&
                    (!layout.PrimaryLandmarkCells.TryGetValue(CityDistrictKind.OldTown, out Vector2Int landmark) || landmark != lot.Cell))
                {
                    result.Add(CreateOffsetPair(layout, lot));
                    continue;
                }
                if (!layout.RoadGeometry.IsAffectedCell(lot.Cell))
                {
                    if (SupportsDistrictCourt(layout, lot) &&
                        TryCreateDistrictCourt(layout, lot, out CityCourtyardBlock block))
                        result.Add(block);
                    continue;
                }
                if (!lot.IsOrdinaryBuilding || lot.BuildingVariant != 2)
                    throw new InvalidOperationException("An OldTown courtyard requires its authored L mass.");
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                var bodies = new List<Vector2[]>(lot.CreateCollisionPolygons());
                BuildingLot rear = null;
                Vector3 court = pose.TransformPoint(new Vector3(2.5f, 0f, -3.5f));
                Vector3 passage = court;
                if (lot.Cell.x == 0)
                {
                    var envelope = new Vector2[4];
                    Vector3[] corners = { new Vector3(-7.5f, 0f, -7f), new Vector3(7.5f, 0f, -7f),
                        new Vector3(7.5f, 0f, 7f), new Vector3(-7.5f, 0f, 7f) };
                    for (int i = 0; i < 4; i++) envelope[i] = XZ(pose.TransformPoint(corners[i]));
                    Vector3 center = ResolveRearCenter(lot.Center, envelope);
                    Vector3 door = center + Vector3.right * 6.75f;
                    float height = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 0).y;
                    rear = new BuildingLot(lot.Cell, center, new Vector2(13.5f, 14f), height,
                        lot.Color, lot.AreaId, lot.District, CityLandUseKind.Building,
                        false, false, false, string.Empty, lot.BarActivity, Vector2Int.right,
                        door, lot.ReturnPosition, lot.SidewalkArrivalPosition, 0, Vector3.right);
                    bodies.AddRange(rear.CreateCollisionPolygons());
                    passage = new Vector3(door.x + PassageWidth * .5f,
                        lot.Center.y, lot.Center.z + 4.5f);
                }
                List<Vector2[]> ground = CreateGround(layout, lot.Cell, bodies);
                Vector3 front = lot.DoorPosition + lot.FacadeForward * .5f;
                Vector3 left = pose.TransformPoint(new Vector3(-8.1f, 0f, 1f));
                Vector3 right = pose.TransformPoint(new Vector3(8.1f, 0f, -1.5f));
                var goals = new List<Vector2> { XZ(front), XZ(left) };
                if (rear != null) goals.Add(XZ(passage));
                goals.Add(XZ(court)); goals.Add(XZ(right)); goals.Add(XZ(front));
                List<Vector2> route = RouteAroundMasses(goals, ground, bodies, lot.Cell);
                route.Insert(0, XZ(lot.SidewalkArrivalPosition));
                route.Add(XZ(lot.SidewalkArrivalPosition));
                result.Add(new CityCourtyardBlock(lot, rear, ground,
                    new CityRoadPath(route), Grounded(layout, court), Grounded(layout, passage)));
            }
            return new ReadOnlyCollection<CityCourtyardBlock>(result);
        }

        private static bool SupportsDistrictCourt(CityLayout layout, BuildingLot lot)
        {
            if (layout.SpatialPlan.IsUniform || !lot.IsOrdinaryBuilding ||
                !lot.HasRoadFrontage || lot.BuildingVariant == WingVariant ||
                !layout.TryGetFrontageEdge(lot, out RoadEdge frontage) ||
                layout.GetPathKind(frontage) != CityPathKind.Street ||
                layout.ElevationPlan.TryGetSignatureStair(frontage, out _) ||
                layout.PrimaryLandmarkCells.TryGetValue(lot.District, out Vector2Int landmark) && landmark == lot.Cell)
                return false;
            // These small lots contain the existing authored east-exit precinct
            // and its work docks; their ground remains with that owner.
            return !(lot.Cell.x == 10 && lot.Cell.y == 5) &&
                !(lot.Cell.x == 11 && lot.Cell.y >= 3 && lot.Cell.y <= 5);
        }

        private static bool TryCreateDistrictCourt(CityLayout layout, BuildingLot primary,
            out CityCourtyardBlock block)
        {
            block = null;
            CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(primary);
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(primary.District, primary.BuildingVariant);
            var primaryBodies = new List<Vector2[]>(primary.CreateCollisionPolygons());
            List<Vector2[]> primaryGround = CreateGround(layout, primary.Cell, primaryBodies);
            float courtDepth = primary.District == CityDistrictKind.Residential ? 5.5f
                : primary.District == CityDistrictKind.Industrial ? 6f
                : primary.District == CityDistrictKind.Nightlife ? 3.8f : 3.3f;

            // A row becomes an ensemble only when the entire authored wing and
            // its walking margin fit the actual ground complement. The open
            // residential court deliberately has the deepest separation.
            Vector3 wingEnvelope = CityBuildingAssetProvider.GetExpectedEnvelope(primary.District, WingVariant);
            float lateral = Mathf.Max(0f, envelope.x * .5f - wingEnvelope.x * .5f);
            int firstSide = ((primary.Cell.x + primary.Cell.y) & 1) == 0 ? -1 : 1;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                BuildingLot wing = null;
                if (attempt < 2)
                {
                    int side = attempt == 0 ? firstSide : -firstSide;
                    Vector3 center = pose.TransformPoint(new Vector3(side * lateral, 0f,
                        -envelope.z * .5f - courtDepth - wingEnvelope.z * .5f));
                    if (!TryCreateWing(layout, primary, center, primaryGround,
                        out wing)) continue;
                }
                var bodies = new List<Vector2[]>(primaryBodies);
                if (wing != null) bodies.AddRange(wing.CreateCollisionPolygons());
                List<Vector2[]> ground = CreateGround(layout, primary.Cell, bodies);
                if (!TryResolveCourt(pose, envelope, primary.District, ground, bodies,
                    out Vector2 court)) continue;
                Vector2 passage;
                if (primary.BuildingVariant == 3)
                    passage = XZ(pose.TransformPoint(Vector3.zero));
                else if (primary.BuildingVariant == 2)
                    passage = XZ(pose.TransformPoint(new Vector3(2.5f, 0f, -3.5f)));
                else
                    passage = XZ(pose.TransformPoint(new Vector3(
                        firstSide * (envelope.x * .5f + .8f), 0f, -envelope.z * .35f)));
                if (!Clear(passage, ground, bodies)) passage = court;

                List<Vector2[]> walkingGround = CreateAccessGround(layout, primary, ground);
                Vector2 gate = XZ(primary.SidewalkArrivalPosition);
                Vector2 front = XZ(primary.DoorPosition + primary.FacadeForward * .65f);
                var goals = new List<Vector2> { gate };
                AppendDistinct(goals, front);
                AppendDistinct(goals, passage);
                AppendDistinct(goals, court);
                if (wing != null)
                {
                    Vector2 wingDoor = XZ(wing.DoorPosition + wing.FacadeForward * .65f);
                    if (!Clear(wingDoor, ground, bodies)) continue;
                    AppendDistinct(goals, wingDoor);
                }
                AppendDistinct(goals, gate);
                if (!TryRouteAroundMasses(goals, walkingGround, bodies, out List<Vector2> route,
                    true, layout)) continue;
                CityCourtyardBlockKind kind = primary.BuildingVariant == 3
                    ? CityCourtyardBlockKind.Passage
                    : primary.District == CityDistrictKind.Residential ? CityCourtyardBlockKind.OpenCourt
                    : primary.District == CityDistrictKind.Industrial ? CityCourtyardBlockKind.ServiceCourt
                    : CityCourtyardBlockKind.Ensemble;
                block = new CityCourtyardBlock(primary, wing, ground, new CityRoadPath(route),
                    Grounded(layout, new Vector3(court.x, primary.Center.y, court.y)),
                    Grounded(layout, new Vector3(passage.x, primary.Center.y, passage.y)), kind);
                return true;
            }
            return false;
        }

        private static void AppendDistinct(List<Vector2> points, Vector2 point)
        {
            if ((points[points.Count - 1] - point).sqrMagnitude > .0001f) points.Add(point);
        }

        private static bool TryCreateWing(CityLayout layout, BuildingLot primary,
            Vector3 center, IReadOnlyList<Vector2[]> ground,
            out BuildingLot wing)
        {
            wing = null;
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(primary.District, WingVariant);
            Vector3 right = primary.FacadeRotation * Vector3.right;
            Vector3 forward = primary.FacadeForward;
            // Subtract the whole ground union from the padded rigid wing.
            // Centre/corner sampling misses a thin curved road cut through a
            // rotated body, and triangulation seams are not physical borders.
            float halfWidth = envelope.x * .5f + WingClearance;
            float halfDepth = envelope.z * .5f + WingClearance;
            var padded = new[] { XZ(center - right * halfWidth - forward * halfDepth),
                XZ(center + right * halfWidth - forward * halfDepth),
                XZ(center + right * halfWidth + forward * halfDepth),
                XZ(center - right * halfWidth + forward * halfDepth) };
            if (!RegionFitsGround(CityRoadPolygon.CounterClockwise(padded), ground)) return false;
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(layout, XZ(center), out float top, out _)) return false;
            center.y = top - CityElevationPlan.GroundTopOffset;
            Vector3 door = center + forward * (envelope.z * .5f);
            Vector2 size = primary.FrontageDirection.x != 0
                ? new Vector2(envelope.z, envelope.x) : new Vector2(envelope.x, envelope.z);
            wing = new BuildingLot(primary.Cell, center, size, envelope.y,
                primary.Color, primary.AreaId, primary.District, CityLandUseKind.Building,
                false, false, false, string.Empty, primary.BarActivity, primary.FrontageDirection,
                door, primary.ReturnPosition, primary.SidewalkArrivalPosition, WingVariant, forward);
            return true;
        }

        private static bool RegionFitsGround(Vector2[] region, IReadOnlyList<Vector2[]> ground)
        {
            // Ground subtraction already returns disjoint convex pieces. Sum
            // their clipped area instead of subtracting this entire union
            // from another union: unrelated supporting planes otherwise
            // subdivide its remainder repeatedly and grow it exponentially.
            Rect regionBounds = CityRoadPolygon.Bounds(region);
            float covered = 0f;
            foreach (Vector2[] piece in ground)
            {
                if (!regionBounds.Overlaps(CityRoadPolygon.Bounds(piece))) continue;
                var intersection = new List<Vector2>(region);
                for (int side = 0; side < piece.Length && intersection.Count >= 3; side++)
                    intersection = CityRoadPolygon.Clip(intersection, piece[side], piece[(side + 1) % piece.Length]);
                covered += CityRoadPolygon.Area(intersection);
            }
            return covered >= CityRoadPolygon.Area(region) - .001f;
        }

        private static bool TryResolveCourt(CityBuildingPrototypePose pose, Vector3 envelope,
            CityDistrictKind district, IReadOnlyList<Vector2[]> ground,
            IReadOnlyList<Vector2[]> bodies, out Vector2 court)
        {
            float depth = district == CityDistrictKind.Residential ? 2.8f
                : district == CityDistrictKind.Industrial ? 3f : 1.8f;
            // Rear land belongs to this frontage, rather than remaining an
            // anonymous moat. Search metre positions, preserving a broad open
            // central court before considering the narrower side alternatives.
            foreach (float shift in new[] { 0f, -2f, 2f, -4f, 4f })
                for (int step = 0; step < 3; step++)
                {
                    Vector2 point = XZ(pose.TransformPoint(new Vector3(shift, 0f,
                        -envelope.z * .5f - depth - step * .6f)));
                    if (Clear(point, ground, bodies)) { court = point; return true; }
                }
            court = default;
            return false;
        }

        private static List<Vector2[]> CreateAccessGround(CityLayout layout,
            BuildingLot primary, IReadOnlyList<Vector2[]> ground)
        {
            var walkable = new List<Vector2[]>(ground);
            RoadEdge frontage = RoadEdge.ForCellFrontage(primary.Cell, primary.FrontageDirection);
            CityRoadPath street = layout.RoadGeometry.Get(frontage);
            Vector2 gate = XZ(primary.SidewalkArrivalPosition);
            CityRoadProjection projection = street.Project(gate);
            float side = Mathf.Sign(projection.SignedLateral);
            float offset = layout.RoadWidth * .5f - CityStreetSurfacePlanner.SidewalkWidth * .5f;
            walkable.AddRange(street.Ribbon(CityStreetSurfacePlanner.SidewalkWidth, side * offset));
            return walkable;
        }

        private static CityCourtyardBlock CreateOffsetPair(CityLayout layout, BuildingLot primary)
        {
            Vector3 compact = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 0);
            Vector3 longHouse = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 1);
            Vector3 facing = primary.FacadeForward;
            Vector3 rearCenter = ResolveOffsetRearCenter(primary.Center, facing);
            Vector3 rearDoor = rearCenter + facing * (compact.z * .5f);
            var rear = new BuildingLot(primary.Cell, rearCenter, new Vector2(compact.z, compact.x), compact.y,
                primary.Color, primary.AreaId, primary.District, CityLandUseKind.Building,
                false, false, false, string.Empty, primary.BarActivity, primary.FrontageDirection,
                rearDoor, primary.ReturnPosition, primary.SidewalkArrivalPosition, 0, facing);
            var bodies = new List<Vector2[]>(primary.CreateCollisionPolygons());
            bodies.AddRange(rear.CreateCollisionPolygons());
            List<Vector2[]> ground = CreateGround(layout, primary.Cell, bodies);
            Vector3 passage = rearDoor + facing * (OffsetPairPassageWidth * .5f) + Vector3.forward * 2f;
            Vector3 court = rearCenter + facing * 2f + Vector3.forward * (compact.x * .5f + 4.5f);
            Vector3 front = primary.DoorPosition + primary.FacadeForward * .5f;
            Vector3 south = primary.Center - facing * (longHouse.z * .5f + .6f) - Vector3.forward * (longHouse.x * .5f + .6f);
            Vector3 north = primary.Center + Vector3.forward * (longHouse.x * .5f + .6f);
            var goals = new List<Vector2> { XZ(front), XZ(south), XZ(passage), XZ(court), XZ(north), XZ(front) };
            List<Vector2> route = RouteAroundMasses(goals, ground, bodies, primary.Cell);
            route.Insert(0, XZ(primary.SidewalkArrivalPosition));
            route.Add(XZ(primary.SidewalkArrivalPosition));
            return new CityCourtyardBlock(primary, rear, ground, new CityRoadPath(route),
                Grounded(layout, court), Grounded(layout, passage), CityCourtyardBlockKind.OffsetPair);
        }

        private static List<Vector2[]> CreateGround(CityLayout layout, Vector2Int cell, IReadOnlyList<Vector2[]> bodies)
        {
            var ground = new List<Vector2[]>(layout.RoadGeometry.GetGroundPolygons(cell));
            foreach (Vector2[] body in bodies)
            {
                var remaining = new List<Vector2[]>();
                Rect bodyBounds = CityRoadPolygon.Bounds(body);
                foreach (Vector2[] piece in ground)
                    if (CityRoadPolygon.Bounds(piece).Overlaps(bodyBounds))
                        remaining.AddRange(CityRoadPolygon.Subtract(piece, body));
                    else remaining.Add(piece);
                ground = remaining;
            }
            return ground;
        }

        internal static IReadOnlyList<CityCourtyardConnection> CreateConnections(CityLayout layout)
        {
            var result = new List<CityCourtyardConnection>();
            IReadOnlyList<CityCourtyardBlock> blocks = layout.CourtyardBlocks;
            for (int a = 0; a < blocks.Count; a++)
                for (int b = a + 1; b < blocks.Count; b++)
                {
                    CityCourtyardBlock first = blocks[a], second = blocks[b];
                    Vector2Int direction = second.Cell - first.Cell;
                    if (Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1 ||
                        layout.HasRoad(RoadEdge.ForCellFrontage(first.Cell, direction))) continue;
                    // A missing road arm leaves ordinary ground across this
                    // boundary. Never create an undeclared road crossing.
                    var ground = new List<Vector2[]>(first.GroundPolygons);
                    ground.AddRange(second.GroundPolygons);
                    var bodies = new List<Vector2[]>(first.Primary.CreateCollisionPolygons());
                    if (first.RearBuilding != null) bodies.AddRange(first.RearBuilding.CreateCollisionPolygons());
                    bodies.AddRange(second.Primary.CreateCollisionPolygons());
                    if (second.RearBuilding != null) bodies.AddRange(second.RearBuilding.CreateCollisionPolygons());
                    if (!TryRouteAroundMasses(new List<Vector2> {
                        XZ(first.CourtCenter), XZ(second.CourtCenter) }, ground, bodies,
                        out List<Vector2> route, false, layout)) continue;
                    result.Add(new CityCourtyardConnection(first, second, new CityRoadPath(route)));
                }
            return new ReadOnlyCollection<CityCourtyardConnection>(result);
        }

        private static Vector3 Grounded(CityLayout layout, Vector3 point)
        {
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, XZ(point),
                    out float top, out _)) point.y = top;
            return point;
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

        // A bounded visibility graph uses actual rectangular solids and the
        // union of ground pieces. It never treats a triangulation seam as a
        // wall, and every link has room for the production hero's capsule.
        private static List<Vector2> RouteAroundMasses(List<Vector2> goals,
            IReadOnlyList<Vector2[]> ground, IReadOnlyList<Vector2[]> bodies, Vector2Int cell, bool closed = true)
        {
            if (!TryRouteAroundMasses(goals, ground, bodies, out List<Vector2> route, closed))
                throw new InvalidOperationException($"Courtyard {cell} has no continuous capsule route.");
            return route;
        }

        private static bool TryRouteAroundMasses(List<Vector2> goals,
            IReadOnlyList<Vector2[]> ground, IReadOnlyList<Vector2[]> bodies,
            out List<Vector2> route, bool closed = true, CityLayout layout = null)
        {
            route = null;
            var nodes = new List<Vector2>();
            var clearance = new ClearanceQuery(ground, bodies);
            int goalCount = closed ? goals.Count - 1 : goals.Count;
            if (goalCount < 2) return false;
            for (int i = 0; i < goalCount; i++)
            {
                if (!clearance.IsClear(goals[i])) return false;
                nodes.Add(goals[i]);
            }
            foreach (Vector2[] body in bodies)
                for (int i = 0; i < body.Length; i++)
                {
                    Vector2 incoming = (body[i] - body[(i + body.Length - 1) % body.Length]).normalized;
                    Vector2 outgoing = (body[(i + 1) % body.Length] - body[i]).normalized;
                    Vector2 corner = body[i] + new Vector2(incoming.y + outgoing.y,
                        -incoming.x - outgoing.x) * .6f;
                    if (clearance.IsClear(corner)) nodes.Add(corner);
                }
            var heights = layout == null ? null : new GroundHeightQuery(layout, nodes);
            var links = new float[nodes.Count, nodes.Count];
            for (int a = 0; a < nodes.Count; a++)
                for (int b = a + 1; b < nodes.Count; b++)
                {
                    float length = Vector2.Distance(nodes[a], nodes[b]);
                    bool safe = length > .001f && SweptCapsuleClear(nodes[a], nodes[b], bodies);
                    for (float d = .2f; safe && d < length; d += .2f)
                        safe = clearance.IsClear(Vector2.Lerp(nodes[a], nodes[b], d / length));
                    if (safe && heights != null) safe = GroundGradeClear(heights, nodes[a], nodes[b]);
                    if (safe) links[a, b] = links[b, a] = length;
                }
            var shoulderChecked = new bool[nodes.Count, nodes.Count];
            route = new List<Vector2> { nodes[0] };
            int legCount = closed ? goalCount : goalCount - 1;
            for (int leg = 0; leg < legCount; leg++)
            {
                int destination = (leg + 1) % goalCount;
                // Keep the bounded centre-grade visibility graph. Expensive
                // terrain shoulders matter only on selected links; a rejected
                // link is removed in both directions before trying a detour.
                bool found = false;
                int maximumAttempts = nodes.Count * (nodes.Count - 1) / 2 + 1;
                for (int attempt = 0; attempt < maximumAttempts; attempt++)
                {
                    if (!TryFindRoute(links, leg, destination, out List<int> selected)) break;
                    bool blocked = false;
                    for (int step = 1; step < selected.Count; step++)
                    {
                        int a = selected[step - 1], b = selected[step];
                        if (heights == null || shoulderChecked[a, b]) continue;
                        shoulderChecked[a, b] = shoulderChecked[b, a] = true;
                        if (GroundShoulderClear(heights, nodes[a], nodes[b])) continue;
                        links[a, b] = links[b, a] = 0f;
                        blocked = true;
                    }
                    if (blocked) continue;
                    for (int step = 1; step < selected.Count; step++) route.Add(nodes[selected[step]]);
                    found = true;
                    break;
                }
                if (!found) { route = null; return false; }
            }
            return true;
        }

        private static bool TryFindRoute(float[,] links, int start, int destination,
            out List<int> route)
        {
            int count = links.GetLength(0);
            var cost = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
            cost[start] = 0f;
            for (int step = 0; step < count; step++)
            {
                int nearest = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && (nearest < 0 || cost[i] < cost[nearest])) nearest = i;
                if (nearest < 0 || float.IsPositiveInfinity(cost[nearest])) break;
                if (nearest == destination) break;
                visited[nearest] = true;
                for (int i = 0; i < count; i++)
                    if (links[nearest, i] > 0f && cost[nearest] + links[nearest, i] < cost[i])
                    { cost[i] = cost[nearest] + links[nearest, i]; previous[i] = nearest; }
            }
            route = null;
            if (previous[destination] < 0) return false;
            route = new List<int>();
            for (int cursor = destination; cursor != start; cursor = previous[cursor]) route.Add(cursor);
            route.Add(start);
            route.Reverse();
            return true;
        }

        private static bool SweptCapsuleClear(Vector2 first, Vector2 second,
            IReadOnlyList<Vector2[]> bodies)
        {
            foreach (Vector2[] body in bodies)
                for (int side = 0; side < body.Length; side++)
                    if (SegmentDistanceSquared(first, second, body[side], body[(side + 1) % body.Length]) <
                        RouteRadius * RouteRadius) return false;
            return true;
        }

        private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a, cd = d - c;
            float denominator = CityRoadPolygon.Cross(ab, cd);
            if (Mathf.Abs(denominator) > .00001f)
            {
                float first = CityRoadPolygon.Cross(c - a, cd) / denominator;
                float second = CityRoadPolygon.Cross(c - a, ab) / denominator;
                if (first >= 0f && first <= 1f && second >= 0f && second <= 1f) return 0f;
            }
            return Mathf.Min(Mathf.Min(PointSegmentDistanceSquared(a, c, d),
                PointSegmentDistanceSquared(b, c, d)), Mathf.Min(PointSegmentDistanceSquared(c, a, b),
                PointSegmentDistanceSquared(d, a, b)));
        }

        private static float PointSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float amount = delta.sqrMagnitude > .00001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
            return (point - a - delta * amount).sqrMagnitude;
        }

        private static bool GroundGradeClear(GroundHeightQuery heights, Vector2 first, Vector2 second)
        {
            float length = Vector2.Distance(first, second);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / .25f));
            bool previousSampled = false;
            float previous = 0f;
            for (int step = 0; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(first, second, step / (float)steps);
                bool sampled = heights.TrySampleGroundTop(point, out float top);
                if (sampled && previousSampled && Mathf.Abs(top - previous) > length / steps *
                    (CityElevationPlan.MaximumPedestrianGradePercent + .05f) * .01f) return false;
                // The raised frontage pavement has its own sampler and curb;
                // interior/shared legs must follow physical terrain heights.
                previousSampled = sampled; previous = top;
            }
            return true;
        }

        private static bool GroundShoulderClear(GroundHeightQuery heights, Vector2 first, Vector2 second)
        {
            float length = Vector2.Distance(first, second);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / .25f));
            float maximumRise = PlayerFactory.StepOffset + PlayerFactory.GroundedRootOffset;
            for (int step = 0; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(first, second, step / (float)steps);
                if (!heights.TrySampleWalkingTop(point, out float centreTop)) return false;
                foreach (Vector2 offset in CapsuleRim)
                    for (int ring = 1; ring <= 2; ring++)
                        if (heights.TrySampleWalkingTop(point + offset * (ring * .5f), out float shoulderTop) &&
                            shoulderTop - centreTop > maximumRise) return false;
            }
            return true;
        }

        private static bool Clear(Vector2 point, IReadOnlyList<Vector2[]> ground,
            IReadOnlyList<Vector2[]> bodies)
        {
            foreach (Vector2[] body in bodies)
            {
                if (CityRoadPolygon.Contains(body, point)) return false;
                for (int i = 0; i < body.Length; i++)
                {
                    Vector2 start = body[i], delta = body[(i + 1) % body.Length] - start;
                    Vector2 closest = start + delta * Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
                    if ((closest - point).sqrMagnitude < RouteRadius * RouteRadius) return false;
                }
            }
            for (int sample = 0; sample < 16; sample++)
            {
                Vector2 rim = point + CapsuleRim[sample];
                bool contained = false;
                foreach (Vector2[] polygon in ground)
                    if (CityRoadPolygon.Contains(polygon, rim)) { contained = true; break; }
                if (!contained) return false;
            }
            return true;
        }

        private static Vector2[] CreateCapsuleRim()
        {
            var offsets = new Vector2[16];
            for (int sample = 0; sample < offsets.Length; sample++)
            {
                float angle = sample * Mathf.PI / 8f;
                offsets[sample] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RouteRadius;
            }
            return offsets;
        }

        // The graph and its shoulder retries stay inside one small ground
        // region. Retain the authoritative sampler's descriptor order and
        // polygon membership, resolving each surface context only once.
        private sealed class GroundHeightQuery
        {
            private readonly CityLayout layout;
            private readonly GroundHeightSurface[] surfaces;
            private readonly RuntimeOrientedBox[] sidewalks;
            private readonly RuntimeOrientedBox[] streets;
            private readonly CityStreetRibbonDescriptor[] sidewalkRibbons;
            private readonly CityStreetRibbonDescriptor[] streetRibbons;

            internal GroundHeightQuery(CityLayout layout, IReadOnlyList<Vector2> nodes)
            {
                this.layout = layout;
                float minX = nodes[0].x, maxX = minX, minY = nodes[0].y, maxY = minY;
                foreach (Vector2 node in nodes)
                {
                    minX = Mathf.Min(minX, node.x); maxX = Mathf.Max(maxX, node.x);
                    minY = Mathf.Min(minY, node.y); maxY = Mathf.Max(maxY, node.y);
                }
                float margin = RouteRadius + .001f;
                var nearby = new List<GroundHeightSurface>();
                foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                {
                    Rect bounds = surface.WorldBounds;
                    if (surface.IsWater || bounds.xMax < minX - margin || bounds.xMin > maxX + margin ||
                        bounds.yMax < minY - margin || bounds.yMin > maxY + margin) continue;
                    nearby.Add(new GroundHeightSurface(layout, surface));
                }
                surfaces = nearby.ToArray();
                Rect queryBounds = Rect.MinMaxRect(minX - margin, minY - margin, maxX + margin, maxY + margin);
                CityStreetSurfacePlan paving = CityStreetSurfacePlanner.CreateCourtyardPaving(layout);
                sidewalks = NearbyBoxes(paving.SidewalkGeometry, queryBounds);
                streets = NearbyBoxes(paving.StreetGeometry, queryBounds);
                sidewalkRibbons = NearbyRibbons(paving.CurvedSidewalkRibbons, queryBounds);
                streetRibbons = NearbyRibbons(paving.CurvedStreetRibbons, queryBounds);
            }

            internal bool TrySampleGroundTop(Vector2 point, out float top)
            {
                foreach (GroundHeightSurface surface in surfaces)
                    if (surface.Contains(point))
                    {
                        top = CityTerrainSurfacePlan.SampleTop(layout, surface.Descriptor, point, surface.Context);
                        return true;
                    }
                top = 0f;
                return false;
            }

            internal bool TrySampleWalkingTop(Vector2 point, out float top)
            {
                // Match physical layer priority: sidewalk, street, then soil.
                // Within one paving layer the highest exposed plane wins.
                // A road datum with a sidewalk offset cannot identify the
                // real strip footprint, especially at a raised slab edge.
                if (TrySamplePavingTop(sidewalks, sidewalkRibbons, point, out top) ||
                    TrySamplePavingTop(streets, streetRibbons, point, out top))
                    return true;
                return TrySampleGroundTop(point, out top);
            }

            private bool TrySamplePavingTop(RuntimeOrientedBox[] boxes,
                CityStreetRibbonDescriptor[] ribbons, Vector2 point, out float top)
            {
                top = float.NegativeInfinity;
                var position = new Vector3(point.x, 0f, point.y);
                foreach (RuntimeOrientedBox box in boxes)
                    if (box.TrySampleTop(position, out float boxTop)) top = Mathf.Max(top, boxTop);
                foreach (CityStreetRibbonDescriptor ribbon in ribbons)
                    foreach (Vector2[] polygon in ribbon.Polygons)
                    {
                        if (!CityRoadPolygon.Contains(polygon, point)) continue;
                        CityRoadPath path = layout.RoadGeometry.Get(ribbon.Edge);
                        float datum = ribbon.FlatNode.HasValue
                            ? layout.ElevationPlan.GetNodeElevation(ribbon.FlatNode.Value)
                            : layout.ElevationPlan.SampleRoadDatum(ribbon.Edge, path.Project(point).DistanceAlong / path.Length);
                        top = Mathf.Max(top, datum + ribbon.TopOffset);
                        break;
                    }
                return !float.IsNegativeInfinity(top);
            }

            private static RuntimeOrientedBox[] NearbyBoxes(IReadOnlyList<RuntimeOrientedBox> boxes, Rect queryBounds)
            {
                var nearby = new List<RuntimeOrientedBox>();
                foreach (RuntimeOrientedBox box in boxes)
                {
                    Vector3 half = box.Size * .5f;
                    Vector3 right = box.Rotation * Vector3.right;
                    Vector3 up = box.Rotation * Vector3.up;
                    Vector3 forward = box.Rotation * Vector3.forward;
                    float x = Mathf.Abs(right.x) * half.x + Mathf.Abs(up.x) * half.y + Mathf.Abs(forward.x) * half.z;
                    float z = Mathf.Abs(right.z) * half.x + Mathf.Abs(up.z) * half.y + Mathf.Abs(forward.z) * half.z;
                    Rect bounds = Rect.MinMaxRect(box.Center.x - x, box.Center.z - z, box.Center.x + x, box.Center.z + z);
                    if (bounds.Overlaps(queryBounds)) nearby.Add(box);
                }
                return nearby.ToArray();
            }

            private static CityStreetRibbonDescriptor[] NearbyRibbons(
                IReadOnlyList<CityStreetRibbonDescriptor> ribbons, Rect queryBounds)
            {
                var nearby = new List<CityStreetRibbonDescriptor>();
                foreach (CityStreetRibbonDescriptor ribbon in ribbons)
                {
                    var polygons = new List<Vector2[]>();
                    foreach (Vector2[] polygon in ribbon.Polygons)
                        if (CityRoadPolygon.Bounds(polygon).Overlaps(queryBounds)) polygons.Add(polygon);
                    if (polygons.Count > 0) nearby.Add(new CityStreetRibbonDescriptor(ribbon.Edge, polygons,
                        ribbon.TopOffset, ribbon.Thickness, ribbon.FlatNode));
                }
                return nearby.ToArray();
            }
        }

        private readonly struct GroundHeightSurface
        {
            internal readonly CitySurfaceDescriptor Descriptor;
            internal readonly CityTerrainSurfacePlan.SurfaceContext Context;
            private readonly IReadOnlyList<Vector2[]> polygons;

            internal GroundHeightSurface(CityLayout layout, CitySurfaceDescriptor descriptor)
            {
                Descriptor = descriptor;
                Context = CityTerrainSurfacePlan.ResolveSurfaceContext(layout, descriptor);
                polygons = layout.RoadGeometry.IsReplannedCell(descriptor.Cell)
                    ? layout.RoadGeometry.GetGroundPolygons(descriptor.Cell) : null;
            }

            internal bool Contains(Vector2 point)
            {
                const float tolerance = .001f;
                Rect bounds = Descriptor.WorldBounds;
                if (point.x < bounds.xMin - tolerance || point.x > bounds.xMax + tolerance ||
                    point.y < bounds.yMin - tolerance || point.y > bounds.yMax + tolerance) return false;
                if (polygons == null) return true;
                foreach (Vector2[] polygon in polygons)
                    if (CityRoadPolygon.Contains(polygon, point)) return true;
                return false;
            }
        }

        // One visibility graph probes the same polygons thousands of times.
        // Cache their bounds, validity and edge metrics once; this changes no
        // geometry, Contains tolerance, capsule radius or grade selection.
        private sealed class ClearanceQuery
        {
            private readonly ClearancePolygon[] ground;
            private readonly ClearancePolygon[] bodies;

            internal ClearanceQuery(IReadOnlyList<Vector2[]> ground, IReadOnlyList<Vector2[]> bodies)
            {
                this.ground = CreatePolygons(ground);
                this.bodies = CreatePolygons(bodies);
            }

            internal bool IsClear(Vector2 point)
            {
                foreach (ClearancePolygon body in bodies)
                {
                    if (!body.BoundsContain(point, RouteRadius + body.ContainmentPadding)) continue;
                    if (body.Contains(point)) return false;
                    foreach (ClearanceEdge edge in body.Edges)
                        if (edge.DistanceSquared(point) < RouteRadius * RouteRadius) return false;
                }
                foreach (Vector2 offset in CapsuleRim)
                {
                    Vector2 rim = point + offset;
                    bool contained = false;
                    foreach (ClearancePolygon polygon in ground)
                        if (polygon.Contains(rim)) { contained = true; break; }
                    if (!contained) return false;
                }
                return true;
            }

            private static ClearancePolygon[] CreatePolygons(IReadOnlyList<Vector2[]> polygons)
            {
                var result = new ClearancePolygon[polygons.Count];
                for (int index = 0; index < result.Length; index++)
                    result[index] = new ClearancePolygon(polygons[index]);
                return result;
            }
        }

        private sealed class ClearancePolygon
        {
            private readonly Rect bounds;
            private readonly bool valid;
            internal readonly ClearanceEdge[] Edges;
            internal readonly float ContainmentPadding;

            internal ClearancePolygon(Vector2[] vertices)
            {
                bounds = CityRoadPolygon.Bounds(vertices);
                valid = vertices.Length >= 3 && CityRoadPolygon.Area(vertices) > .00001f;
                Edges = new ClearanceEdge[vertices.Length];
                float padding = .0011f;
                for (int side = 0; side < Edges.Length; side++)
                {
                    Edges[side] = new ClearanceEdge(vertices[side], vertices[(side + 1) % vertices.Length]);
                    Vector2 incoming = (vertices[side] - vertices[(side + vertices.Length - 1) % vertices.Length]).normalized;
                    Vector2 outgoing = (vertices[(side + 1) % vertices.Length] - vertices[side]).normalized;
                    Vector2 firstNormal = new Vector2(incoming.y, -incoming.x);
                    Vector2 secondNormal = new Vector2(outgoing.y, -outgoing.x);
                    Vector2 bisector = (firstNormal + secondNormal).normalized;
                    float denominator = Mathf.Abs(Vector2.Dot(bisector, firstNormal));
                    padding = Mathf.Max(padding, denominator > .000001f
                        ? .001f / denominator + .0001f : float.PositiveInfinity);
                }
                ContainmentPadding = padding;
            }

            internal bool BoundsContain(Vector2 point, float padding) =>
                point.x >= bounds.xMin - padding && point.x <= bounds.xMax + padding &&
                point.y >= bounds.yMin - padding && point.y <= bounds.yMax + padding;

            internal bool Contains(Vector2 point)
            {
                // Match CityRoadPolygon.Contains' one-millimetre half-plane
                // tolerance, including points just beyond a polygon's AABB.
                if (!valid || !BoundsContain(point, ContainmentPadding)) return false;
                foreach (ClearanceEdge edge in Edges)
                    if (!edge.Contains(point)) return false;
                return true;
            }
        }

        private readonly struct ClearanceEdge
        {
            private readonly Vector2 start;
            private readonly Vector2 delta;
            private readonly float lengthSquared;
            private readonly float containsTolerance;

            internal ClearanceEdge(Vector2 start, Vector2 end)
            {
                this.start = start;
                delta = end - start;
                lengthSquared = delta.sqrMagnitude;
                containsTolerance = -.001f * delta.magnitude;
            }

            internal bool Contains(Vector2 point) =>
                CityRoadPolygon.Cross(delta, point - start) >= containsTolerance;

            internal float DistanceSquared(Vector2 point)
            {
                Vector2 closest = start + delta * Mathf.Clamp01(Vector2.Dot(point - start, delta) / lengthSquared);
                return (closest - point).sqrMagnitude;
            }
        }
    }
}
