using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed partial class CityTerraceTransportPlanTests
    {
        [Test]
        public void CoastalJunctionsShareFullWidthSeamsAndRetainOriginalCoastHeights()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var original = layout.ElevationPlan.NodeElevations.ToDictionary(pair => pair.Key, pair => pair.Value);
            CityTerraceCoastalJunctionPlan[] baseline = null;
            foreach (float summit in new[] { 35f, 40f, 45f })
            {
                CityTerraceTransportPlan plan = CityTerraceTransportPlan.Create(layout,
                    CityTerraceMasterPlan.Create(layout, summit));
                Assert.That(plan.CoastalJunctions.Count, Is.EqualTo(plan.Routes.Count));
                Assert.That(plan.CoastalJunctions.Select(item => item.Id).Distinct().Count(), Is.EqualTo(2));
                for (int index = 0; index < plan.Routes.Count; index++)
                {
                    CityTerraceTransportRoute route = plan.Routes[index];
                    CityTerraceCoastalJunctionPlan junction = plan.CoastalJunctions.Single(item => item.RouteId == route.Id);
                    Vector3 center = layout.GetNodeWorldPosition(junction.Node);
                    Vector2 centerXZ = new Vector2(center.x, center.z);
                    Vector3 forward = (route.Samples[1].Position - route.Samples[0].Position).normalized;
                    Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                    Assert.That(junction.HalfExtent, Is.EqualTo(5f));
                    Assert.That(route.Segments[0].HorizontalLength, Is.EqualTo(5f).Within(Tolerance),
                        "The route cannot start climbing one metre inside the public pavement mouth.");
                    Assert.That(route.Samples[0].Position, Is.EqualTo(center));
                    Assert.That(junction.DatumY, Is.EqualTo(center.y));
                    Assert.That(junction.IsGradeFeasible, Is.True);
                    Assert.That(plan.IsPlacementReady, Is.False, "A coastal patch does not resolve interior road overlaps.");

                    var polygons = junction.RoadPolygons.Concat(junction.PavementPolygons).ToArray();
                    Assert.That(junction.RoadPolygons.Sum(PolygonAreaDouble), Is.EqualTo(72d).Within(.00001d));
                    Assert.That(junction.PavementPolygons.Sum(PolygonAreaDouble), Is.EqualTo(28d).Within(.00001d));
                    for (int a = 0; a < polygons.Length; a++)
                    {
                        Assert.That(PolygonAreaDouble(polygons[a]), Is.GreaterThan(0d));
                        foreach (Vector2 vertex in polygons[a])
                        {
                            Assert.That(Mathf.Abs(vertex.x - centerXZ.x), Is.LessThanOrEqualTo(5f + Tolerance));
                            Assert.That(Mathf.Abs(vertex.y - centerXZ.y), Is.LessThanOrEqualTo(5f + Tolerance));
                        }
                        for (int b = a + 1; b < polygons.Length; b++)
                            Assert.That(ConvexOverlapArea(polygons[a], polygons[b]), Is.LessThan(.00001d),
                                "Every part of the junction must have exactly one asphalt or pavement owner.");
                    }
                    Assert.Throws<NotSupportedException>(() =>
                        ((IList<Vector2>)junction.RoadPolygons[0])[0] = Vector2.zero);
                    Assert.Throws<NotSupportedException>(() =>
                        ((IList<IReadOnlyList<Vector2>>)junction.PavementPolygons).Clear());

                    Vector3 seam = center + forward * 5f;
                    Assert.That(Vector3.Distance(junction.RouteSeamStart,
                        seam - right * 5f + Vector3.up * CityStreetSurfacePlanner.SidewalkTop), Is.LessThan(Tolerance));
                    Assert.That(Vector3.Distance(junction.RouteSeamEnd,
                        seam + right * 5f + Vector3.up * CityStreetSurfacePlanner.SidewalkTop), Is.LessThan(Tolerance));
                    Assert.That(Vector3.Distance(junction.RouteRoadSeamStart,
                        seam - right * 3f + Vector3.up * CityStreetSurfacePlanner.RoadTop), Is.LessThan(Tolerance));
                    Assert.That(Vector3.Distance(junction.RouteRoadSeamEnd,
                        seam + right * 3f + Vector3.up * CityStreetSurfacePlanner.RoadTop), Is.LessThan(Tolerance));
                    foreach (float lateral in new[] { -4f, -2f, 0f, 2f, 4f })
                    {
                        Vector3 point = seam + right * lateral;
                        IReadOnlyList<IReadOnlyList<Vector2>> surface = Mathf.Abs(lateral) < 3f
                            ? junction.RoadPolygons : junction.PavementPolygons;
                        Assert.That(surface.Any(polygon => ContainsConvex(polygon, new Vector2(point.x, point.z))), Is.True,
                            "The new route mouth needs a complete surface even when no original edge continues inland.");
                    }

                    RoadEdge[] existing = layout.RoadEdges.Where(edge => edge.Contains(junction.Node) &&
                        layout.GetPathKind(edge) == CityPathKind.Street).ToArray();
                    Vector2 routeDirection = new Vector2(forward.x, forward.z);
                    bool AlignedWithRoute(RoadEdge edge)
                    {
                        CityRoadPath path = layout.RoadGeometry.Get(edge);
                        Vector2 direction = edge.A == junction.Node ? path.SampleDistance(0f).Tangent :
                            -path.SampleDistance(path.Length).Tangent;
                        return Vector2.Dot(direction, routeDirection) >= 1f - Tolerance;
                    }
                    int existingContinuations = existing.Count(AlignedWithRoute);
                    Assert.That(existingContinuations, Is.InRange(0, 1));
                    Assert.That(junction.Arms.Count, Is.EqualTo(2 + existingContinuations),
                        "Keep the two coastal arms; the inland mouth may be entirely new.");
                    Assert.That(junction.Arms.Select(arm => arm.Edge), Is.EquivalentTo(existing));
                    Assert.That(junction.Arms.Count(arm => arm.ContinuesAsRoute), Is.EqualTo(existingContinuations));
                    Assert.That(junction.RequiresRemainingEdgeResolution, Is.EqualTo(existingContinuations > 0));
                    foreach (CityTerraceCoastalJunctionArm arm in junction.Arms)
                    {
                        Assert.That(arm.Path.Vertices, Is.EqualTo(layout.RoadGeometry.Get(arm.Edge).Vertices));
                        Assert.That(arm.ContinuesAsRoute, Is.EqualTo(AlignedWithRoute(arm.Edge)));
                        bool nearA = arm.Edge.A == junction.Node;
                        float nearDistance = nearA ? 0f : arm.Path.Length;
                        float seamDistance = nearA ? 5f : arm.Path.Length - 5f;
                        Assert.That(arm.PatchStartDistance, Is.EqualTo(Mathf.Min(nearDistance, seamDistance)).Within(Tolerance));
                        Assert.That(arm.PatchEndDistance, Is.EqualTo(Mathf.Max(nearDistance, seamDistance)).Within(Tolerance));
                        Assert.That(arm.IsGradeFeasible, Is.True);
                        CityRoadSample mouth = arm.Path.SampleDistance(seamDistance);
                        foreach (float lateral in new[] { -4f, -2f, 0f, 2f, 4f })
                        {
                            Vector2 point = mouth.Position + mouth.Right * lateral;
                            IReadOnlyList<IReadOnlyList<Vector2>> surface = Mathf.Abs(lateral) < 3f
                                ? junction.RoadPolygons : junction.PavementPolygons;
                            Assert.That(surface.Any(polygon => ContainsConvex(polygon, point)), Is.True,
                                "The entire road and both pavements must meet every retained arm.");
                            Vector3 top = Mathf.Abs(lateral) < 3f ? junction.SampleRoadTop(point) : junction.SamplePavementTop(point);
                            Assert.That(top.y, Is.EqualTo(center.y + (Mathf.Abs(lateral) < 3f
                                ? CityStreetSurfacePlanner.RoadTop : CityStreetSurfacePlanner.SidewalkTop)).Within(Tolerance));
                        }
                        if (arm.ContinuesAsRoute)
                        {
                            Assert.That(arm.ElevationProfile, Is.Null,
                                "An independent old height would leave a second road below the rising route.");
                            Assert.That(arm.RequiresRemainingEdgeResolution, Is.True);
                            Assert.That(route.ElevationProfile.SampleDistance(5f).Height, Is.EqualTo(center.y).Within(Tolerance));
                            continue;
                        }
                        Assert.That(arm.RequiresRemainingEdgeResolution, Is.False);
                        Assert.That(arm.ElevationProfile.HorizontalLength, Is.EqualTo(arm.Path.Length));
                        Assert.That(arm.ElevationProfile.MaximumGradePercent, Is.LessThanOrEqualTo(6f + Tolerance));
                        Vector2Int farNode = arm.Edge.Other(junction.Node);
                        float farY = original[farNode];
                        float farInset = layout.RoadGeometry.GetEndpointInset(arm.Edge, farNode);
                        Assert.That(farInset, Is.EqualTo(4f));
                        foreach (float amount in new[] { 0f, .5f, 1f })
                        {
                            float distance = Mathf.Lerp(nearDistance, seamDistance, amount);
                            Assert.That(arm.ElevationProfile.SampleDistance(distance).Height, Is.EqualTo(center.y).Within(Tolerance));
                            float farDistance = nearA ? arm.Path.Length - farInset * amount : farInset * amount;
                            Assert.That(arm.ElevationProfile.SampleDistance(farDistance).Height, Is.EqualTo(farY).Within(Tolerance));
                        }
                        float startGradeDistance = nearA ? 5f : farInset;
                        float endGradeDistance = nearA ? arm.Path.Length - farInset : arm.Path.Length - 5f;
                        CityRoadElevationSample middle = arm.ElevationProfile.SampleDistance((startGradeDistance + endGradeDistance) * .5f);
                        Assert.That(middle.Height, Is.EqualTo((center.y + farY) * .5f).Within(Tolerance));
                        Assert.That(middle.SignedGradePercent, Is.EqualTo((nearA ? farY - center.y : center.y - farY) /
                            (arm.Path.Length - 5f - farInset) * 100f).Within(Tolerance));
                    }
                    if (baseline != null)
                    {
                        Assert.That(junction.Id, Is.EqualTo(baseline[index].Id));
                        Assert.That(junction.RouteSeamStart, Is.EqualTo(baseline[index].RouteSeamStart));
                        Assert.That(junction.RouteSeamEnd, Is.EqualTo(baseline[index].RouteSeamEnd));
                        Assert.That(junction.Arms.Select(arm => arm.Edge), Is.EqualTo(baseline[index].Arms.Select(arm => arm.Edge)));
                    }
                }
                if (baseline == null) baseline = plan.CoastalJunctions.ToArray();
            }
            Assert.That(layout.ElevationPlan.NodeElevations, Is.EquivalentTo(original));
            Assert.Throws<ArgumentNullException>(() => CityTerraceCoastalJunctionPlan.Create(null, null));
            Assert.Throws<ArgumentNullException>(() => CityTerraceCoastalJunctionPlan.Create(layout, null));
        }

        private static double PolygonAreaDouble(IReadOnlyList<Vector2> polygon)
        {
            double area = 0d;
            for (int i = 1; i + 1 < polygon.Count; i++)
                area += ((double)polygon[i].x - polygon[0].x) * ((double)polygon[i + 1].y - polygon[0].y) -
                    ((double)polygon[i].y - polygon[0].y) * ((double)polygon[i + 1].x - polygon[0].x);
            return area * .5d;
        }
    }
}
