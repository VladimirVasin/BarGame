using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRoadGeometryTests
    {
        [TestCase(20260727)]
        [TestCase(17)]
        public void OldTownPilot_KeepsAnchorsAndPartitionsGroundWithoutBuildingOverlap(int seed)
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, seed);
            Assert.That(layout.BuildingLots.Count, Is.EqualTo(144));
            Assert.That(layout.BuildingMasses.Count, Is.EqualTo(146));
            Assert.That(layout.CourtyardBlocks.Select(block => block.Cell), Is.EquivalentTo(new[] {
                new Vector2Int(0, 7), new Vector2Int(1, 7), new Vector2Int(0, 8), new Vector2Int(1, 8) }));
            Assert.That(layout.CourtyardBlocks.Count(block => block.RearBuilding != null), Is.EqualTo(2));
            Assert.That(layout.RoadGeometry.CurvedEdges.Count, Is.EqualTo(3));
            foreach (RoadEdge edge in CityRoadGeometryPlan.PilotEdges)
            {
                Assert.That(layout.GetPathKind(edge), Is.EqualTo(CityPathKind.Street));
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Assert.That(path.Vertices[0], Is.EqualTo(new Vector2(start.x, start.z)));
                Assert.That(path.Vertices[path.Vertices.Count - 1], Is.EqualTo(new Vector2(end.x, end.z)));
                Assert.That(path.Length, Is.GreaterThan(Vector2.Distance(path.Vertices[0], path.Vertices[path.Vertices.Count - 1])));
                Vector2 axis = (path.Vertices[path.Vertices.Count - 1] - path.Vertices[0]).normalized;
                Vector2 approach = path.SampleDistance(0).Tangent;
                Assert.That(Vector2.Distance(path.SampleDistance(6).Position, path.Vertices[0] + approach * 6), Is.LessThan(.001f));
                Assert.That(Vector2.SignedAngle(axis, approach), Is.EqualTo(edge.Equals(CityRoadGeometryPlan.PilotEdges[1]) ? -12f : 0f).Within(.01f));
                Assert.That(Vector2.Distance(path.SampleDistance(path.Length - 6).Position,
                    path.Vertices[path.Vertices.Count - 1] - axis * 6), Is.LessThan(.001f));
                for (float distance = 0; distance <= path.Length; distance += .5f)
                {
                    CityRoadSample sample = path.SampleDistance(distance);
                    Assert.That(layout.ElevationPlan.TrySampleSurface(sample.Position,
                        CitySurfaceRole.RoadDatum, out float height, out _), Is.True);
                    Assert.That(height, Is.EqualTo(layout.ElevationPlan.SampleRoadDatum(edge, distance / path.Length)).Within(.001f));
                    Assert.That(path.Project(sample.Position).DistanceAlong, Is.EqualTo(distance).Within(.001f));
                }
            }
            if (seed == 20260727)
                Assert.That(layout.RoadEdges.Count(edge => edge.Contains(new Vector2Int(1, 8))), Is.EqualTo(3));
            CityRoadJunction junction = layout.RoadGeometry.ObliqueJunction;
            Assert.That(junction.Node, Is.EqualTo(new Vector2Int(1, 8)));
            foreach (CityRoadPath sidewalk in junction.SidewalkPaths)
                for (float s = 0f; s <= sidewalk.Length; s += .25f)
                {
                    Vector2 point = sidewalk.SampleDistance(s).Position;
                    Assert.That(junction.SidewalkPolygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)), Is.True,
                        $"Junction pavement must contain its shared walk path at {point}.");
                    Assert.That(layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadDatum, out float height, out _), Is.True);
                    Assert.That(height, Is.EqualTo(layout.ElevationPlan.GetNodeElevation(junction.Node)).Within(.001f));
                }

            var streets = layout.RoadEdges.SelectMany(edge =>
                layout.RoadGeometry.Get(edge).Ribbon(layout.GetTravelWidth(edge))).ToList();
            foreach (Vector2Int node in layout.Nodes)
            {
                if (node == junction.Node) { streets.AddRange(junction.RoadPolygons); continue; }
                Vector3 p = layout.GetNodeWorldPosition(node);
                float half = layout.RoadWidth * .5f;
                streets.Add(new[] { new Vector2(p.x - half, p.z - half),
                    new Vector2(p.x + half, p.z - half), new Vector2(p.x + half, p.z + half),
                    new Vector2(p.x - half, p.z + half) });
            }
            foreach (BuildingLot lot in layout.BuildingMasses.Where(lot => layout.RoadGeometry.IsAffectedCell(lot.Cell)))
            {
                Assert.That(lot.IsOrdinaryBuilding, Is.True, "The pilot must not consume a built landmark.");
                Assert.That(layout.PrimaryLandmarkCells.Values, Has.No.Member(lot.Cell));
                foreach (Vector2[] building in lot.CreateCollisionPolygons())
                    foreach (Vector2[] road in streets)
                        AssertNoPolygonOverlap(building, road, $"Building {lot.Cell} intersects the actual street or junction.");
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                var ground = layout.RoadGeometry.GetGroundPolygons(lot.Cell);
                // Interior probes avoid exact seams: the rendered ground is the complement of the real corridor.
                for (float x = cell.xMin + .37f; x < cell.xMax; x += 1.37f)
                    for (float z = cell.yMin + .53f; z < cell.yMax; z += 1.43f)
                    {
                        Vector2 point = new Vector2(x, z);
                        // Containment includes a millimetre seam tolerance;
                        // shared border points legitimately belong to both skins.
                        if (streets.Any(polygon => NearBoundary(polygon, point))) continue;
                        bool onRoad = streets.Any(polygon => CityRoadPolygon.Contains(polygon, point));
                        Assert.That(ground.Any(polygon => CityRoadPolygon.Contains(polygon, point)), Is.EqualTo(!onRoad),
                            $"Road/ground partition failed at {point} in {lot.Cell}.");
                    }
            }
            Assert.That(layout.BuildingLots.Count(lot => layout.RoadGeometry.IsAffectedCell(lot.Cell) && lot.HasFacadeRotation),
                Is.GreaterThanOrEqualTo(2), "The rigid frontage poses must follow the replanned streets.");
            AssertCourtyardRoutes(layout);
        }

        private static void AssertCourtyardRoutes(CityLayout layout)
        {
            var bodies = layout.BuildingMasses.SelectMany(lot => lot.CreateCollisionPolygons()).ToList();
            RoadWalkableArea heroArea = RoadWalkableArea.FromLayout(layout);
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                Assert.That(block.Primary, Is.SameAs(layout.BuildingLots.Single(lot => lot.Cell == block.Cell)));
                Assert.That(block.Primary.BuildingVariant, Is.EqualTo(2), "A court uses the authored L house, without changing its metres.");
                Assert.That(block.Primary.CreateCollisionPolygons().Count, Is.EqualTo(2));
                Assert.That(block.RearBuilding != null, Is.EqualTo(block.Cell.x == 0));
                var blockMasses = new List<BuildingLot> { block.Primary };
                if (block.RearBuilding != null)
                {
                    blockMasses.Add(block.RearBuilding);
                    Assert.That(block.RearBuilding.BuildingVariant, Is.Zero);
                    Assert.That(block.RearBuilding.Height, Is.EqualTo(42f));
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
                // Prove the swept capsule along every complete link, including
                // corners between samples, against all actual building solids.
                for (int link = 1; link < block.Route.Vertices.Count; link++)
                {
                    Vector2 a = block.Route.Vertices[link - 1], b = block.Route.Vertices[link];
                    foreach (Vector2[] body in bodies)
                    {
                        Assert.That(CityRoadPolygon.Contains(body, a) || CityRoadPolygon.Contains(body, b), Is.False);
                        for (int side = 0; side < body.Length; side++)
                            Assert.That(SegmentDistanceSquared(a, b, body[side], body[(side + 1) % body.Length]),
                                Is.GreaterThanOrEqualTo(.35f * .35f - .0001f),
                                $"Hero capsule route crosses a building at {block.Cell}, link {link}.");
                    }
                }
                for (float distance = 0f; distance < block.Route.Length + .25f; distance += .25f)
                {
                    Vector2 point = block.Route.SampleDistance(distance).Position;
                    Assert.That(heroArea.Contains(new Vector3(point.x, 0f, point.y), .35f), Is.True,
                        $"Courtyard {block.Cell} route leaves the actual hero mask at {point}.");
                }
            }
        }

        private static void AssertNoPolygonOverlap(Vector2[] first, Vector2[] second, string message)
        {
            var overlap = new List<Vector2>(first);
            for (int i = 0; i < second.Length && overlap.Count >= 3; i++)
                overlap = CityRoadPolygon.Clip(overlap, second[i], second[(i + 1) % second.Length]);
            Assert.That(overlap.Count < 3 || Mathf.Abs(CityRoadPolygon.Area(overlap)) < .001f, Is.True, message);
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
            Assert.That(layout.BuildingMasses.Count, Is.EqualTo(layout.BuildingLots.Count));
            foreach (RoadEdge edge in layout.RoadEdges)
                Assert.That(layout.GetRoadLength(edge), Is.EqualTo(layout.SpatialPlan.GetNodeSpan(edge)).Within(.001f));
        }
    }
}
