using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRoadGeometryTests
    {
        [Test]
        public void CompactStreetGeometry_PreservesMetreTrafficApproachesWithoutTheExpandedPilot()
        {
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityGenerationSettings settings = CityGenerationSettings.Default;
            settings.Blueprint = blueprint;
            settings.SpatialPlan = CitySpatialPlan.Create(blueprint, settings);
            var roads = new HashSet<RoadEdge>();
            foreach (CityBlueprintCell cell in blueprint.Cells.Where(cell => cell.ParticipatesInRoadGrid))
                foreach (Vector2Int direction in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    roads.Add(RoadEdge.ForCellFrontage(cell.Cell, direction));
            Vector2 originOffset = -settings.SpatialPlan.GetCoordinateWorldOffset(blueprint.CenterNode);
            Vector3 origin = new Vector3(originOffset.x, 0f, originOffset.y);
            var ordered = roads.OrderBy(edge => edge.A.y).ThenBy(edge => edge.A.x)
                .ThenBy(edge => edge.B.y).ThenBy(edge => edge.B.x).ToList();
            CityRoadGeometryPlan geometry = CityRoadGeometryPlan.Create(settings, origin, ordered);
            Assert.That(settings.SpatialPlan.IsCompactCoastal, Is.True);
            Assert.That(geometry.ObliqueJunction, Is.Null);
            Assert.That(geometry.CurvedEdges, Is.Empty);
            Assert.That(geometry.ReplannedCells, Is.Empty);
            foreach (RoadEdge edge in ordered)
            {
                CityRoadPath path = geometry.Get(edge);
                Vector2 start = settings.SpatialPlan.GetCoordinateWorldOffset(edge.A) + originOffset;
                Vector2 end = settings.SpatialPlan.GetCoordinateWorldOffset(edge.B) + originOffset;
                Assert.That(path.Vertices[0], Is.EqualTo(start));
                Assert.That(path.Vertices[path.Vertices.Count - 1], Is.EqualTo(end));
                Assert.That(path.Length, Is.EqualTo(26f).Within(.001f));
                for (float station = 0f; station <= path.Length; station += 1f)
                    Assert.That(path.Project(path.SampleDistance(station).Position).DistanceAlong,
                        Is.EqualTo(station).Within(.001f));
            }
        }

        [TestCase(20260727)]
        [TestCase(17)]
        public void CityReplanning_KeepsAnchorsAndPartitionsGroundWithoutBuildingOverlap(int seed)
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, seed);
            Assert.That(layout.BuildingLots.Count, Is.EqualTo(144));
            Assert.That(layout.Districts.Select(district => district.Kind), Is.EquivalentTo(new[] {
                CityDistrictKind.Residential, CityDistrictKind.Industrial, CityDistrictKind.CentralPark }));
            Assert.That(layout.CourtyardBlocks, Is.Not.Empty);
            Assert.That(layout.CourtyardBlocks.Select(block => block.Primary.District).Distinct(),
                Is.EquivalentTo(new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial }));
            Assert.That(layout.CourtyardBlocks.Any(block => block.Kind == CityCourtyardBlockKind.OffsetPair ||
                block.Kind == CityCourtyardBlockKind.LRecess), Is.False);
            Assert.That(layout.BuildingMasses.Count, Is.GreaterThanOrEqualTo(layout.BuildingLots.Count));
            foreach (CityElevationStairDescriptor stair in layout.ElevationPlan.SignatureStairs)
                Assert.That(layout.RoadGeometry.Get(stair.Edge).IsStraight, Is.True, stair.Id);

            var streets = layout.RoadEdges.SelectMany(edge =>
                layout.RoadGeometry.Get(edge).Ribbon(layout.GetTravelWidth(edge))).ToList();
            foreach (Vector2Int node in layout.Nodes)
            {
                Vector3 point = layout.GetNodeWorldPosition(node);
                float half = layout.RoadWidth * .5f;
                streets.Add(CityRoadPolygon.Rectangle(new Rect(point.x - half, point.z - half,
                    layout.RoadWidth, layout.RoadWidth)));
            }
            foreach (BuildingLot lot in layout.BuildingMasses.Where(lot => lot.IsOrdinaryBuilding))
            {
                foreach (Vector2[] building in lot.CreateCollisionPolygons())
                    foreach (Vector2[] road in streets)
                        AssertNoPolygonOverlap(building, road, $"Building {lot.Cell} intersects its street.");
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                IReadOnlyList<Vector2[]> ground = layout.RoadGeometry.GetGroundPolygons(lot.Cell);
                for (float x = cell.xMin + .37f; x < cell.xMax; x += 2.37f)
                    for (float z = cell.yMin + .53f; z < cell.yMax; z += 2.43f)
                    {
                        Vector2 point = new Vector2(x, z);
                        if (streets.Any(polygon => NearBoundary(polygon, point))) continue;
                        bool onRoad = streets.Any(polygon => CityRoadPolygon.Contains(polygon, point));
                        Assert.That(ground.Any(polygon => CityRoadPolygon.Contains(polygon, point)),
                            Is.EqualTo(!onRoad), $"Road/ground partition failed at {point}.");
                    }
            }
            AssertCourtyardRoutes(layout);
            AssertCourtyardConnections(layout, streets);
            if (seed == GameSessionState.DefaultCitySeed)
            {
                CityCanneryTruckRoute truck = CityCanneryTruckRoute.Create(layout, CityCanneryPlan.Create(layout),
                    CityPortAccessPlan.ForLayout(layout));
                Assert.That(truck, Is.Not.Null, "The compact town retains port/factory/shop deliveries.");
                foreach (RoadEdge edge in truck.StreetEdges)
                    Assert.That(layout.RoadGeometry.Get(edge).IsStraight, Is.True, edge.ToString());
            }
        }

        private static IEnumerable<Vector2Int> AssertNorthStreetfronts(CityLayout layout,
            IReadOnlyList<Vector2[]> streets)
        {
            int[] variants = { 0, 1, 0, 1 };
            float[] setbacks = { 2.4f, 1.15f, 1.7f, 1.2f };
            var eligible = new List<BuildingLot>();
            for (int x = 0; x < variants.Length; x++)
            {
                Vector2Int cell = new Vector2Int(x, 11);
                BuildingLot lot = layout.BuildingLots.Single(candidate => candidate.Cell == cell);
                Rect bounds = layout.GetCellWorldBounds(cell);
                RoadEdge frontage = RoadEdge.ForCellFrontage(cell, Vector2Int.up);
                bool street = layout.HasRoad(frontage) && layout.GetPathKind(frontage) == CityPathKind.Street;
                float width = variants[x] == 0 ? 14f : 22f;
                float depth = variants[x] == 0 ? 13.5f : 11.5f;
                Vector2 expectedCenter = new Vector2(bounds.center.x,
                    bounds.yMax - layout.RoadWidth * .5f - setbacks[x] - depth * .5f);
                Rect padded = new Rect(expectedCenter - new Vector2(width + 1.3f, depth + 1.6f) * .5f,
                    new Vector2(width + 1.3f, depth + 1.6f));
                Vector2[] footprint = { new Vector2(padded.xMin, padded.yMin), new Vector2(padded.xMax, padded.yMin),
                    new Vector2(padded.xMax, padded.yMax), new Vector2(padded.xMin, padded.yMax) };
                bool fits = footprint.All(point => bounds.Contains(point)) &&
                    streets.All(road => PolygonOverlapArea(footprint, road) < .001f);
                bool ordinary = lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.OldTown &&
                    !layout.PrimaryLandmarkCells.Values.Contains(cell) &&
                    !layout.RoadGeometry.IsReplannedCell(cell) &&
                    layout.CourtyardBlocks.All(block => block.Cell != cell);
                Debug.Log($"North streetfront {cell}: ordinary={ordinary}, northStreet={street}, fits={fits}, " +
                    $"variant={lot.BuildingVariant}, frontage={lot.FrontageDirection}, center={lot.Center:F3}.");
                if (!ordinary || !street || !fits) continue;
                eligible.Add(lot);
                Assert.That(lot.BuildingVariant, Is.EqualTo(variants[x]), $"Authored streetfront variant at {cell}.");
                Assert.That(lot.Height, Is.EqualTo(CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant).y));
                Assert.That(lot.FrontageDirection, Is.EqualTo(Vector2Int.up));
                Assert.That(Vector3.Angle(lot.FacadeForward, Vector3.forward), Is.LessThan(.001f));
                Assert.That(lot.Center.x, Is.EqualTo(expectedCenter.x).Within(.001f));
                Assert.That(lot.Center.z, Is.EqualTo(expectedCenter.y).Within(.001f));
                var bodies = lot.CreateCollisionPolygons();
                Rect mass = CityRoadPolygon.Bounds(bodies.SelectMany(body => body).ToArray());
                Assert.That(mass.width, Is.EqualTo(width).Within(.001f));
                Assert.That(mass.height, Is.EqualTo(depth).Within(.001f));
                float clearance = CityBusIntersectionSelector.BuildingClearance;
                Vector2[] busBody = { new Vector2(mass.xMin - clearance, mass.yMin - clearance),
                    new Vector2(mass.xMax + clearance, mass.yMin - clearance),
                    new Vector2(mass.xMax + clearance, mass.yMax + clearance),
                    new Vector2(mass.xMin - clearance, mass.yMax + clearance) };
                float halfRoad = layout.RoadWidth * .5f;
                foreach (float cornerX in new[] { bounds.xMin + halfRoad, bounds.xMax - halfRoad - 1f })
                    foreach (float cornerZ in new[] { bounds.yMin + halfRoad, bounds.yMax - halfRoad - 1f })
                    {
                        Vector2[] cornerPad = { new Vector2(cornerX, cornerZ), new Vector2(cornerX + 1f, cornerZ),
                            new Vector2(cornerX + 1f, cornerZ + 1f), new Vector2(cornerX, cornerZ + 1f) };
                        AssertNoPolygonOverlap(busBody, cornerPad,
                            $"Streetfront {cell} consumes a bus corner pad at ({cornerX}, {cornerZ}).");
                    }
                CityRoadProjection front = layout.RoadGeometry.Get(frontage).Project(
                    new Vector2(lot.DoorPosition.x, lot.DoorPosition.z));
                Assert.That(Mathf.Sqrt(front.DistanceSquared) - layout.GetTravelWidth(frontage) * .5f,
                    Is.EqualTo(setbacks[x]).Within(.001f), $"Measured streetfront setback at {cell}.");
                Assert.That(lot.DoorPosition.x, Is.EqualTo(lot.Center.x).Within(.001f));
                Assert.That(lot.DoorPosition.z, Is.EqualTo(mass.yMax).Within(.001f));
                Assert.That(lot.ReturnPosition.x, Is.EqualTo(bounds.center.x).Within(.001f));
                Assert.That(lot.ReturnPosition.z, Is.EqualTo(bounds.yMax).Within(.001f));
                Assert.That(lot.SidewalkArrivalPosition.x, Is.EqualTo(bounds.center.x).Within(.001f));
                Assert.That(lot.SidewalkArrivalPosition.z, Is.EqualTo(bounds.yMax - layout.RoadWidth * .5f +
                    CityStreetSurfacePlanner.SidewalkWidth * .5f).Within(.001f));
                foreach (BuildingLot other in layout.BuildingMasses.Where(other => !ReferenceEquals(other, lot)))
                    foreach (Vector2[] body in bodies)
                        foreach (Vector2[] otherBody in other.CreateCollisionPolygons())
                            AssertNoPolygonOverlap(body, otherBody, $"Streetfront {cell} overlaps another building.");
            }
            return eligible.Select(lot => lot.Cell);
        }

        private static void AssertCourtyardRoutes(CityLayout layout)
        {
            var bodies = layout.BuildingMasses.SelectMany(lot => lot.CreateCollisionPolygons()).ToList();
            RoadWalkableArea heroArea = RoadWalkableArea.FromLayout(layout);
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                Assert.That(block.Primary, Is.SameAs(layout.BuildingLots.Single(lot => lot.Cell == block.Cell)));
                bool offsetPair = block.Kind == CityCourtyardBlockKind.OffsetPair;
                bool retained = offsetPair || block.Kind == CityCourtyardBlockKind.LRecess;
                if (retained)
                {
                    Assert.That(block.Primary.BuildingVariant, Is.EqualTo(offsetPair ? 1 : 2), "The court must retain its authored fixed-metre mass.");
                    Assert.That(block.Primary.CreateCollisionPolygons().Count, Is.EqualTo(offsetPair ? 1 : 2));
                    Assert.That(block.RearBuilding != null, Is.EqualTo(block.Cell.x == 0));
                }
                if (offsetPair) AssertOffsetPairMasses(layout, block);
                var blockMasses = new List<BuildingLot> { block.Primary };
                if (block.RearBuilding != null)
                {
                    blockMasses.Add(block.RearBuilding);
                    Assert.That(block.RearBuilding.BuildingVariant, Is.EqualTo(retained ? 0 : 4));
                    Assert.That(block.RearBuilding.Height, Is.EqualTo(CityBuildingAssetProvider.GetExpectedEnvelope(
                        block.RearBuilding.District, block.RearBuilding.BuildingVariant).y));
                }
                Rect cell = layout.GetCellWorldBounds(block.Cell);
                foreach (BuildingLot mass in blockMasses)
                    foreach (Vector2[] body in mass.CreateCollisionPolygons())
                    {
                        foreach (Vector2 point in body)
                            Assert.That(point.x >= cell.xMin && point.x <= cell.xMax &&
                                point.y >= cell.yMin && point.y <= cell.yMax, Is.True,
                                $"Fixed-metre mass leaves courtyard cell {block.Cell}.");
                        foreach (BuildingLot other in layout.BuildingMasses.Where(other => !ReferenceEquals(other, mass)))
                            foreach (Vector2[] otherBody in other.CreateCollisionPolygons())
                                AssertNoPolygonOverlap(body, otherBody, $"Courtyard mass {block.Cell} overlaps another building.");
                        foreach (Vector2[] ground in block.GroundPolygons)
                            AssertNoPolygonOverlap(body, ground, $"Courtyard ground {block.Cell} contains a building solid.");
                    }
                foreach (Vector3 goal in new[] { block.CourtCenter, block.PassageCenter })
                {
                    Vector2 point = new Vector2(goal.x, goal.z);
                    Assert.That(block.GroundPolygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)), Is.True);
                    Assert.That(block.Route.Project(point).DistanceSquared, Is.LessThan(.0001f), "The continuous route must visit its court and passage.");
                }
                Vector2 gate = new Vector2(block.Primary.SidewalkArrivalPosition.x, block.Primary.SidewalkArrivalPosition.z);
                Assert.That(block.Route.Vertices[0], Is.EqualTo(gate));
                Assert.That(block.Route.Vertices[block.Route.Vertices.Count - 1], Is.EqualTo(gate));
                AssertCapsulePathClear(block.Route, bodies, $"Courtyard {block.Cell}");
                for (float distance = 0f; distance < block.Route.Length + .25f; distance += .25f)
                {
                    Vector2 point = block.Route.SampleDistance(distance).Position;
                    Assert.That(heroArea.Contains(new Vector3(point.x, 0f, point.y), .35f), Is.True,
                        $"Courtyard {block.Cell} route leaves the actual hero mask at {point}.");
                }
            }
        }

        private static void AssertOffsetPairMasses(CityLayout layout, CityCourtyardBlock block)
        {
            Assert.That(block.Cell, Is.EqualTo(new Vector2Int(0, 9)));
            Assert.That(block.RearBuilding, Is.Not.Null);
            Assert.That(block.Primary.IsOrdinaryBuilding && block.RearBuilding.IsOrdinaryBuilding, Is.True);
            Assert.That(layout.PrimaryLandmarkCells.Values, Has.No.Member(block.Cell));
            RoadEdge east = RoadEdge.ForCellFrontage(block.Cell, Vector2Int.right);
            bool facesEast = layout.HasRoad(east) && layout.GetPathKind(east) == CityPathKind.Street;
            Vector2Int frontage = facesEast ? Vector2Int.right : Vector2Int.left;
            Vector3 facing = facesEast ? Vector3.right : Vector3.left;
            Assert.That(block.Primary.FrontageDirection, Is.EqualTo(frontage));
            Assert.That(block.RearBuilding.FrontageDirection, Is.EqualTo(frontage));
            Assert.That(block.Primary.FacadeForward, Is.EqualTo(facing));
            Assert.That(block.RearBuilding.FacadeForward, Is.EqualTo(facing));
            Assert.That(block.Primary.Height, Is.EqualTo(CityBuildingAssetProvider.GetExpectedEnvelope(
                block.Primary.District, block.Primary.BuildingVariant).y));
            Rect primary = CityRoadPolygon.Bounds(block.Primary.CreateCollisionPolygons().SelectMany(body => body).ToArray());
            Rect rear = CityRoadPolygon.Bounds(block.RearBuilding.CreateCollisionPolygons().SelectMany(body => body).ToArray());
            Assert.That(primary.width, Is.EqualTo(11.5f).Within(.001f));
            Assert.That(primary.height, Is.EqualTo(22f).Within(.001f));
            Assert.That(rear.width, Is.EqualTo(13.5f).Within(.001f));
            Assert.That(rear.height, Is.EqualTo(14f).Within(.001f));
            float primaryBack = facesEast ? primary.xMin : primary.xMax;
            float rearFront = facesEast ? rear.xMax : rear.xMin;
            Assert.That(Mathf.Abs(primaryBack - rearFront), Is.EqualTo(3f).Within(.001f), "The two imported masses leave the authored narrow passage.");
            Rect cell = layout.GetCellWorldBounds(block.Cell);
            float setback = facesEast ? cell.xMax - layout.RoadWidth * .5f - primary.xMax :
                primary.xMin - cell.xMin - layout.RoadWidth * .5f;
            Assert.That(setback, Is.EqualTo(1.4f).Within(.001f));
            Assert.That(block.Primary.Center.z - cell.center.y, Is.EqualTo(1f).Within(.001f));
            Assert.That(block.RearBuilding.Center.z - cell.center.y, Is.EqualTo(-3.5f).Within(.001f));
            Assert.That(block.PassageCenter.x, Is.EqualTo((primaryBack + rearFront) * .5f).Within(.001f));
            Assert.That(block.PassageCenter.z, Is.InRange(Mathf.Max(primary.yMin, rear.yMin), Mathf.Min(primary.yMax, rear.yMax)));
            Assert.That(block.CourtCenter.z, Is.GreaterThan(rear.yMax), "The second court opens north of the staggered rear house.");
            RoadEdge street = RoadEdge.ForCellFrontage(block.Cell, frontage);
            Assert.That(layout.HasRoad(street), Is.True);
            Assert.That(layout.GetPathKind(street), Is.EqualTo(CityPathKind.Street));
        }

        private static void AssertCourtyardConnections(CityLayout layout, IReadOnlyList<Vector2[]> streets)
        {
            int expectedConnections = layout.HasRoad(new RoadEdge(new Vector2Int(0, 8), new Vector2Int(1, 8))) ? 0 : 1;
            if (layout.Seed == 20260727) Assert.That(expectedConnections, Is.EqualTo(1));
            Assert.That(layout.CourtyardConnections.Count(connection => connection.First.Kind == CityCourtyardBlockKind.LRecess &&
                connection.Second.Kind == CityCourtyardBlockKind.LRecess), Is.EqualTo(expectedConnections));
            Assert.That(layout.CourtyardPaths.Count(), Is.EqualTo(layout.CourtyardBlocks.Count + layout.CourtyardConnections.Count));
            var bodies = layout.BuildingMasses.SelectMany(lot => lot.CreateCollisionPolygons()).ToList();
            RoadWalkableArea heroArea = RoadWalkableArea.FromLayout(layout);
            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
            {
                if (connection.First.Kind == CityCourtyardBlockKind.LRecess &&
                    connection.Second.Kind == CityCourtyardBlockKind.LRecess)
                {
                    Assert.That(connection.FirstCell, Is.EqualTo(new Vector2Int(0, 7)));
                    Assert.That(connection.SecondCell, Is.EqualTo(new Vector2Int(0, 8)));
                }
                Assert.That(connection.First, Is.SameAs(layout.CourtyardBlocks.Single(block => block.Cell == connection.FirstCell)));
                Assert.That(connection.Second, Is.SameAs(layout.CourtyardBlocks.Single(block => block.Cell == connection.SecondCell)));
                Assert.That(connection.Path.Vertices[0], Is.EqualTo(new Vector2(connection.First.CourtCenter.x, connection.First.CourtCenter.z)));
                Assert.That(connection.Path.Vertices[connection.Path.Vertices.Count - 1],
                    Is.EqualTo(new Vector2(connection.Second.CourtCenter.x, connection.Second.CourtCenter.z)));
                AssertCapsulePathClear(connection.Path, bodies, "Inter-court building clearance");
                AssertCapsulePathClear(connection.Path, streets, "Inter-court route must stay off the street");
                var ground = connection.First.GroundPolygons.Concat(connection.Second.GroundPolygons).ToList();
                float maximumGrade = 0f;
                Vector2 previousPoint = connection.Path.Vertices[0], steepestPoint = previousPoint;
                Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(layout, previousPoint, out float previousTop, out _), Is.True);
                for (float distance = 0f; distance < connection.Path.Length + .25f; distance += .25f)
                {
                    Vector2 point = connection.Path.SampleDistance(distance).Position;
                    Assert.That(heroArea.Contains(new Vector3(point.x, 0f, point.y), .35f), Is.True,
                        $"Inter-court capsule leaves the hero mask at {point}.");
                    Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float top, out _), Is.True);
                    float span = Vector2.Distance(point, previousPoint);
                    float grade = span > .001f ? Mathf.Abs(top - previousTop) / span * 100f : 0f;
                    if (grade > maximumGrade) { maximumGrade = grade; steepestPoint = point; }
                    previousPoint = point; previousTop = top;
                    for (int angle = 0; angle < 16; angle++)
                    {
                        float radians = angle * Mathf.PI / 8f;
                        Vector2 rim = point + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * .35f;
                        Assert.That(ground.Any(polygon => CityRoadPolygon.Contains(polygon, rim)), Is.True,
                            $"Inter-court capsule leaves continuous free ground at {rim}.");
                    }
                }
                Debug.Log($"Courtyard connection {connection.FirstCell}->{connection.SecondCell}: length={connection.Path.Length:F3} m, " +
                    $"maximum sampled terrain grade={maximumGrade:F3}% at {steepestPoint:F3}.");
                Assert.That(maximumGrade, Is.LessThanOrEqualTo(CityElevationPlan.MaximumPedestrianGradePercent + .05f),
                    "The shortcut must use terrain the pedestrian can traverse without a height warp.");
            }
        }

        private static void AssertCapsulePathClear(CityRoadPath path, IReadOnlyList<Vector2[]> obstacles, string label)
        {
            // Complete swept segments catch a clipped corner between samples.
            for (int link = 1; link < path.Vertices.Count; link++)
            {
                Vector2 a = path.Vertices[link - 1], b = path.Vertices[link];
                foreach (Vector2[] body in obstacles)
                {
                    Assert.That(CityRoadPolygon.Contains(body, a) || CityRoadPolygon.Contains(body, b), Is.False, label);
                    for (int side = 0; side < body.Length; side++)
                        Assert.That(SegmentDistanceSquared(a, b, body[side], body[(side + 1) % body.Length]),
                            Is.GreaterThanOrEqualTo(.35f * .35f - .0001f), $"{label}, link {link}.");
                }
            }
        }

        private static void AssertNoPolygonOverlap(Vector2[] first, Vector2[] second, string message)
        {
            Assert.That(PolygonOverlapArea(first, second), Is.LessThan(.001f), message);
        }

        private static float PolygonOverlapArea(Vector2[] first, Vector2[] second)
        {
            var overlap = new List<Vector2>(first);
            for (int i = 0; i < second.Length && overlap.Count >= 3; i++)
                overlap = CityRoadPolygon.Clip(overlap, second[i], second[(i + 1) % second.Length]);
            return overlap.Count < 3 ? 0f : Mathf.Abs(CityRoadPolygon.Area(overlap));
        }

        private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a, cd = d - c, ac = c - a;
            float denominator = ab.x * cd.y - ab.y * cd.x;
            if (Mathf.Abs(denominator) > .000001f)
            {
                float t = (ac.x * cd.y - ac.y * cd.x) / denominator;
                float u = (ac.x * ab.y - ac.y * ab.x) / denominator;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
            }
            return Mathf.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d),
                PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b));
        }

        private static float PointSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float amount = delta.sqrMagnitude < .000001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
            return (point - a - delta * amount).sqrMagnitude;
        }

        private static bool NearBoundary(Vector2[] polygon, Vector2 point)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], delta = polygon[(i + 1) % polygon.Length] - a;
                if (delta.sqrMagnitude < .000001f) continue;
                Vector2 closest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                if ((point - closest).sqrMagnitude < .000004f) return true;
            }
            return false;
        }

        [Test]
        public void LegacyLayout_RetainsStraightRoads()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityGenerationSettings.Default, 17);
            Assert.That(layout.RoadGeometry.CurvedEdges, Is.Empty);
            Assert.That(layout.CourtyardBlocks, Is.Empty);
            Assert.That(layout.CourtyardConnections, Is.Empty);
            Assert.That(layout.CourtyardPaths, Is.Empty);
            Assert.That(layout.BuildingMasses.Count, Is.EqualTo(layout.BuildingLots.Count));
            foreach (RoadEdge edge in layout.RoadEdges)
                Assert.That(layout.GetRoadLength(edge), Is.EqualTo(layout.SpatialPlan.GetNodeSpan(edge)).Within(.001f));
        }
    }
}
