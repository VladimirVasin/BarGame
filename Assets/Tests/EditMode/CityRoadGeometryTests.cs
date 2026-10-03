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

            foreach (BuildingLot lot in layout.BuildingLots.Where(lot => layout.RoadGeometry.IsAffectedCell(lot.Cell)))
            {
                Assert.That(lot.IsOrdinaryBuilding, Is.True, "The pilot must not consume a built landmark.");
                Assert.That(layout.PrimaryLandmarkCells.Values, Has.No.Member(lot.Cell));
                foreach (Vector2[] building in lot.CreateCollisionPolygons())
                    foreach (RoadEdge edge in layout.RoadGeometry.CurvedEdges)
                        foreach (Vector2[] road in layout.RoadGeometry.Get(edge).Ribbon(layout.RoadWidth))
                        {
                            var overlap = new System.Collections.Generic.List<Vector2>(building);
                            for (int i = 0; i < road.Length; i++)
                                overlap = CityRoadPolygon.Clip(overlap, road[i], road[(i + 1) % road.Length]);
                            Assert.That(overlap.Count < 3 || Mathf.Abs(CityRoadPolygon.Area(overlap)) < .001f, Is.True,
                                $"Building {lot.Cell} intersects the curved street.");
                        }
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                var ground = layout.RoadGeometry.GetGroundPolygons(lot.Cell);
                var streets = layout.RoadEdges.SelectMany(edge => layout.RoadGeometry.Get(edge).Ribbon(layout.GetTravelWidth(edge))).ToList();
                // Interior probes avoid exact seams: the rendered ground is the complement of the real corridor.
                for (float x = cell.xMin + .37f; x < cell.xMax; x += 1.37f)
                    for (float z = cell.yMin + .53f; z < cell.yMax; z += 1.43f)
                    {
                        Vector2 point = new Vector2(x, z);
                        // Containment includes a millimetre seam tolerance;
                        // shared border points legitimately belong to both skins.
                        if (streets.Any(polygon => NearBoundary(polygon, point))) continue;
                        bool onRoad = streets.Any(polygon => CityRoadPolygon.Contains(polygon, point));
                        bool onNode = layout.Nodes.Any(node => {
                            Vector3 p = layout.GetNodeWorldPosition(node);
                            return Mathf.Abs(x - p.x) < layout.RoadWidth * .5f && Mathf.Abs(z - p.z) < layout.RoadWidth * .5f;
                        });
                        Assert.That(ground.Any(polygon => CityRoadPolygon.Contains(polygon, point)), Is.EqualTo(!onRoad && !onNode),
                            $"Road/ground partition failed at {point} in {lot.Cell}.");
                    }
            }
            Assert.That(layout.BuildingLots.Count(lot => layout.RoadGeometry.IsAffectedCell(lot.Cell) && lot.HasFacadeRotation),
                Is.GreaterThanOrEqualTo(2), "The rigid frontage poses must follow the replanned streets.");
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
            foreach (RoadEdge edge in layout.RoadEdges)
                Assert.That(layout.GetRoadLength(edge), Is.EqualTo(layout.SpatialPlan.GetNodeSpan(edge)).Within(.001f));
        }
    }
}
