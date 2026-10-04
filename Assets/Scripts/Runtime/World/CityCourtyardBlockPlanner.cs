using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCourtyardBlockKind
    {
        LRecess,
        OffsetPair
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
    /// Ordinary OldTown courts reuse authored houses at fixed metre scale:
    /// four L recesses and a staggered long/compact pair to their north.
    /// Physical rear bodies introduce no semantic lot or gameplay entrance.
    /// </summary>
    public static class CityCourtyardBlockPlanner
    {
        public const float PassageWidth = 2.2f;
        public const float OffsetPairPassageWidth = 3f;
        public static Vector2Int OffsetPairCell => new Vector2Int(0, 9);
        private const float RouteRadius = .4f;

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
                if (!layout.RoadGeometry.IsAffectedCell(lot.Cell)) continue;
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
                    rear = new BuildingLot(lot.Cell, center, new Vector2(13.5f, 14f), 42f,
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
                foreach (Vector2[] piece in ground) remaining.AddRange(CityRoadPolygon.Subtract(piece, body));
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
                    if (first.Kind != CityCourtyardBlockKind.LRecess || second.Kind != CityCourtyardBlockKind.LRecess)
                        continue;
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
                    List<Vector2> route = RouteAroundMasses(new List<Vector2> {
                        XZ(first.CourtCenter), XZ(second.CourtCenter) }, ground, bodies, first.Cell, false);
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
            var nodes = new List<Vector2>();
            int goalCount = closed ? goals.Count - 1 : goals.Count;
            for (int i = 0; i < goalCount; i++)
            {
                if (!Clear(goals[i], ground, bodies))
                    throw new InvalidOperationException($"Courtyard {cell} waypoint has no capsule clearance: {goals[i]}.");
                nodes.Add(goals[i]);
            }
            foreach (Vector2[] body in bodies)
                for (int i = 0; i < body.Length; i++)
                {
                    Vector2 incoming = (body[i] - body[(i + body.Length - 1) % body.Length]).normalized;
                    Vector2 outgoing = (body[(i + 1) % body.Length] - body[i]).normalized;
                    Vector2 corner = body[i] + new Vector2(incoming.y + outgoing.y,
                        -incoming.x - outgoing.x) * .6f;
                    if (Clear(corner, ground, bodies)) nodes.Add(corner);
                }
            var links = new float[nodes.Count, nodes.Count];
            for (int a = 0; a < nodes.Count; a++)
                for (int b = a + 1; b < nodes.Count; b++)
                {
                    float length = Vector2.Distance(nodes[a], nodes[b]);
                    bool safe = length > .001f;
                    for (float d = .2f; safe && d < length; d += .2f)
                        safe = Clear(Vector2.Lerp(nodes[a], nodes[b], d / length), ground, bodies);
                    if (safe) links[a, b] = links[b, a] = length;
                }
            var route = new List<Vector2> { nodes[0] };
            int legCount = closed ? goalCount : goalCount - 1;
            for (int leg = 0; leg < legCount; leg++)
            {
                int destination = (leg + 1) % goalCount;
                var cost = new float[nodes.Count];
                var previous = new int[nodes.Count];
                var visited = new bool[nodes.Count];
                for (int i = 0; i < cost.Length; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
                cost[leg] = 0f;
                for (int step = 0; step < nodes.Count; step++)
                {
                    int nearest = -1;
                    for (int i = 0; i < nodes.Count; i++)
                        if (!visited[i] && (nearest < 0 || cost[i] < cost[nearest])) nearest = i;
                    if (nearest < 0 || float.IsPositiveInfinity(cost[nearest])) break;
                    if (nearest == destination) break;
                    visited[nearest] = true;
                    for (int i = 0; i < nodes.Count; i++)
                        if (links[nearest, i] > 0f && cost[nearest] + links[nearest, i] < cost[i])
                        { cost[i] = cost[nearest] + links[nearest, i]; previous[i] = nearest; }
                }
                if (previous[destination] < 0)
                    throw new InvalidOperationException($"Courtyard {cell} has no continuous route for leg {leg}.");
                var reverse = new List<Vector2>();
                for (int cursor = destination; cursor != leg; cursor = previous[cursor]) reverse.Add(nodes[cursor]);
                reverse.Reverse(); route.AddRange(reverse);
            }
            return route;
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
                float angle = sample * Mathf.PI / 8f;
                Vector2 rim = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RouteRadius;
                bool contained = false;
                foreach (Vector2[] polygon in ground)
                    if (CityRoadPolygon.Contains(polygon, rim)) { contained = true; break; }
                if (!contained) return false;
            }
            return true;
        }
    }
}
