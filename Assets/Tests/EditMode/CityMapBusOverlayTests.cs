using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapBusOverlayTests
    {
        private const float GeometryTolerance = 0.002f;

        [Test]
        public void ProductionRing_BuildsClosedSimplifiedMapOverlayAndStops()
        {
            CreateProductionContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            var mapObject = new GameObject("City Map Bus Overlay Test");
            try
            {
                CityMapController controller =
                    mapObject.AddComponent<CityMapController>();
                controller.Initialize(
                    layout,
                    default,
                    null,
                    null,
                    plan);
                CityMapBusOverlay overlay = controller.BusOverlay;
                List<Vector3> rawRoute = CreateRawOrderedRoute(plan);

                Assert.That(overlay.IsEmpty, Is.False);
                Assert.That(overlay.RouteId, Is.EqualTo(plan.RouteId));
                Assert.That(
                    overlay.RoutePoints.Count,
                    Is.LessThan(rawRoute.Count));
                Assert.That(
                    PlanarDistance(
                        overlay.RoutePoints[0],
                        overlay.RoutePoints[
                            overlay.RoutePoints.Count - 1]),
                    Is.LessThanOrEqualTo(GeometryTolerance));

                Rect bounds = layout.MapWorldXZBounds;
                for (int index = 0;
                     index < overlay.RoutePoints.Count;
                     index++)
                {
                    Vector3 point = overlay.RoutePoints[index];
                    Assert.That(IsFinite(point), Is.True, index.ToString());
                    Assert.That(
                        point.x,
                        Is.InRange(
                            bounds.xMin - GeometryTolerance,
                            bounds.xMax + GeometryTolerance),
                        index.ToString());
                    Assert.That(
                        point.z,
                        Is.InRange(
                            bounds.yMin - GeometryTolerance,
                            bounds.yMax + GeometryTolerance),
                        index.ToString());
                }

                float maximumError = rawRoute.Max(point =>
                    DistanceToPolyline(
                        point,
                        overlay.RoutePoints));
                Assert.That(
                    maximumError,
                    Is.LessThanOrEqualTo(
                        CityMapBusOverlayBuilder
                            .RouteSimplificationTolerance +
                        GeometryTolerance));

                CityBusStopDescriptor[] orderedStops = plan.Stops
                    .OrderBy(stop => stop.SequenceIndex)
                    .ThenBy(stop => stop.Id, StringComparer.Ordinal)
                    .ToArray();
                Assert.That(
                    overlay.Stops.Count,
                    Is.EqualTo(orderedStops.Length));
                for (int index = 0; index < orderedStops.Length; index++)
                {
                    CityBusStopDescriptor source = orderedStops[index];
                    CityMapBusStopMarker marker = overlay.Stops[index];
                    Assert.That(marker.StableId, Is.EqualTo(source.Id));
                    Assert.That(
                        marker.Ordinal,
                        Is.EqualTo(source.SequenceIndex + 1));
                    Assert.That(
                        marker.WorldPosition,
                        Is.EqualTo(source.Position));
                    Assert.That(
                        marker.LabelLocalizationKey,
                        Is.EqualTo(source.NameLocalizationKey));
                    Assert.That(
                        controller.GetBusStopLabel(index),
                        Is.Not.Empty);
                    Assert.That(
                        controller.GetBusStopLabel(index),
                        Is.Not.EqualTo(marker.LabelLocalizationKey));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void ClosedSimplification_PreservesClosureAndTolerance()
        {
            const float radius = 20f;
            var raw = new List<Vector3>();
            for (int degree = 0; degree <= 360; degree++)
            {
                float radians = degree * Mathf.Deg2Rad;
                raw.Add(new Vector3(
                    Mathf.Cos(radians) * radius,
                    0f,
                    Mathf.Sin(radians) * radius));
            }

            IReadOnlyList<Vector3> simplified =
                CityMapBusOverlayBuilder.SimplifyClosed(
                    raw,
                    CityMapBusOverlayBuilder
                        .RouteSimplificationTolerance);

            Assert.That(simplified.Count, Is.InRange(4, raw.Count - 1));
            Assert.That(
                PlanarDistance(
                    simplified[0],
                    simplified[simplified.Count - 1]),
                Is.LessThanOrEqualTo(GeometryTolerance));
            Assert.That(
                raw.Max(point => DistanceToPolyline(point, simplified)),
                Is.LessThanOrEqualTo(
                    CityMapBusOverlayBuilder
                        .RouteSimplificationTolerance +
                    GeometryTolerance));
        }

        [Test]
        public void MissingPlan_ReturnsSharedEmptyOverlay()
        {
            CityMapBusOverlay overlay =
                CityMapBusOverlayBuilder.Create(null);

            Assert.That(overlay, Is.SameAs(CityMapBusOverlay.Empty));
            Assert.That(overlay.IsEmpty, Is.True);
            Assert.That(overlay.RoutePoints, Is.Empty);
            Assert.That(overlay.Stops, Is.Empty);
        }

        private static void CreateProductionContext(
            out CityLayout layout,
            out CityDecorationPlan decorations)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan fences = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            decorations = CityDecorationPlanner.CreatePlan(
                layout,
                fences,
                night);
        }

        private static List<Vector3> CreateRawOrderedRoute(
            CityBusPlan plan)
        {
            var points = new List<Vector3>();
            for (int orderIndex = 0;
                 orderIndex < plan.OrderedLinkIndices.Count;
                 orderIndex++)
            {
                CityBusRouteLink link =
                    plan.Links[plan.OrderedLinkIndices[orderIndex]];
                for (int sampleIndex = 0;
                     sampleIndex < link.Samples.Count;
                     sampleIndex++)
                {
                    Vector3 point = link.Samples[sampleIndex].Position;
                    if (points.Count == 0 ||
                        PlanarDistance(points[points.Count - 1], point) >
                        GeometryTolerance)
                    {
                        points.Add(point);
                    }
                }
            }

            if (points.Count > 0 &&
                PlanarDistance(points[0], points[points.Count - 1]) >
                GeometryTolerance)
            {
                points.Add(points[0]);
            }

            return points;
        }

        private static float DistanceToPolyline(
            Vector3 point,
            IReadOnlyList<Vector3> polyline)
        {
            float nearestSquared = float.PositiveInfinity;
            for (int index = 1; index < polyline.Count; index++)
            {
                nearestSquared = Mathf.Min(
                    nearestSquared,
                    DistanceToSegmentSquared(
                        point,
                        polyline[index - 1],
                        polyline[index]));
            }

            return Mathf.Sqrt(nearestSquared);
        }

        private static float DistanceToSegmentSquared(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector2 point2D = new Vector2(point.x, point.z);
            Vector2 start2D = new Vector2(start.x, start.z);
            Vector2 delta = new Vector2(
                end.x - start.x,
                end.z - start.z);
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return (point2D - start2D).sqrMagnitude;
            }

            float progress = Mathf.Clamp01(
                Vector2.Dot(point2D - start2D, delta) /
                delta.sqrMagnitude);
            return (point2D - start2D - delta * progress).sqrMagnitude;
        }

        private static float PlanarDistance(
            Vector3 left,
            Vector3 right)
        {
            return Vector2.Distance(
                new Vector2(left.x, left.z),
                new Vector2(right.x, right.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
