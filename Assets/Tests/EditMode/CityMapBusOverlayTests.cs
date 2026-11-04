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
        public void DistrictRings_ShowClosedLinesAndEveryBoardingPlatform()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Resolve(
                GameSessionState.DefaultCityBlueprintId), CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            IReadOnlyList<CityBusPlan> network = CityDistrictBusPlanner.Create(layout);
            var mapObject = new GameObject("City Map Bus Overlay Test");
            try
            {
                CityMapController controller =
                    mapObject.AddComponent<CityMapController>();
                controller.InitializeNetwork(layout, default, null, null, network);
                CityMapBusOverlay overlay = controller.BusOverlay;
                Assert.That(overlay.Lines.Count, Is.EqualTo(3));
                Assert.That(overlay.Stops.Select(stop => stop.StableId), Is.Unique);
                Assert.That(overlay.Stops.Where(stop => stop.TransferNumber > 0)
                    .Select(stop => stop.TransferId).Distinct().Count(), Is.EqualTo(3));
                Rect bounds = layout.MapWorldXZBounds;
                for (int lineIndex = 0; lineIndex < network.Count; lineIndex++)
                {
                    CityBusPlan plan = network[lineIndex];
                    CityMapBusLine line = overlay.Lines[lineIndex];
                    List<Vector3> rawRoute = CreateRawOrderedRoute(plan);
                    Assert.That(overlay.IsEmpty, Is.False);
                    Assert.That(line.RouteId, Is.EqualTo(plan.RouteId));
                    Assert.That(
                        line.Points.Count,
                        Is.LessThan(rawRoute.Count));
                    Assert.That(
                        PlanarDistance(
                            line.Points[0],
                            line.Points[
                                line.Points.Count - 1]),
                        Is.LessThanOrEqualTo(GeometryTolerance));

                    for (int index = 0;
                         index < line.Points.Count;
                         index++)
                    {
                        Vector3 point = line.Points[index];
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
                            line.Points));
                    Assert.That(
                        maximumError,
                        Is.LessThanOrEqualTo(
                            CityMapBusOverlayBuilder
                                .RouteSimplificationTolerance +
                            GeometryTolerance));

                    foreach (CityBusStopDescriptor source in plan.Stops)
                    {
                        CityMapBusStopMarker marker = overlay.Stops.First(stop => stop.StableId == source.Id ||
                            (source.TransferNumber > 0 && stop.TransferId == source.TargetId &&
                             CityBusNetworkFurniture.SamePlatform(stop.WorldPosition, source.ShelterPosition)));
                        Assert.That(CityBusNetworkFurniture.SamePlatform(marker.WorldPosition, source.ShelterPosition), Is.True);
                        Assert.That(marker.LabelLocalizationKey, Is.EqualTo(source.NameLocalizationKey));
                        if (marker.RouteNumber == plan.Number) Assert.That(marker.Ordinal, Is.EqualTo(source.SequenceIndex + 1));
                    }
                }
                var places = controller.GetMapPoints(GameAreaId.City)
                    .Where(p => p.Kind == CityMapPointKind.BusStop).ToDictionary(p => p.StableId);
                Assert.That(places.Count, Is.EqualTo(overlay.Stops.Count));
                for (int i = 0; i < overlay.Stops.Count; i++)
                {
                    var marker = overlay.Stops[i];
                    var plan = network.Single(line => line.Number == marker.RouteNumber);
                    var stop = plan.Stops.Single(s => s.Id == marker.StableId);
                    string code = $"{plan.Number:00}-{stop.SequenceIndex + 1:00}";
                    string markerText = code, prefix = code;
                    if (stop.TransferNumber > 0)
                    {
                        var other = network.Single(line => line.Number == stop.TransferNumber).Stops
                            .Single(s => s.TargetId == stop.TargetId);
                        string partner = $"{stop.TransferNumber:00}-{other.SequenceIndex + 1:00}";
                        bool shared = CityBusNetworkFurniture.SamePlatform(stop.ShelterPosition, other.ShelterPosition);
                        prefix += (shared ? " / " : " → ") + partner;
                        if (shared) markerText += "\n" + partner;
                    }
                    Assert.That(marker.Label.MarkerText, Is.EqualTo(markerText));
                    Assert.That(marker.Label.DescriptionPrefix, Is.EqualTo(prefix));
                    string name = LocalizationService.Get(stop.NameLocalizationKey);
                    Assert.That(name, Is.Not.EqualTo(stop.NameLocalizationKey), "A published stop must name a place.");
                    string caption = controller.GetBusStopLabel(i);
                    Assert.That(caption, Is.EqualTo(prefix + " · " + name));
                    var entry = places["city:bus-stop:" + stop.Id];
                    Assert.That(entry.Label, Is.EqualTo(caption));
                    Assert.That(entry.ScreenHitSize, Is.EqualTo(marker.MarkerSize));
                    Assert.That(marker.MarkerSize.y, Is.GreaterThanOrEqualTo(markerText.Contains("\n") ? 22f : 12f));
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
            Assert.That(overlay.Lines, Is.Empty);
            Assert.That(overlay.Stops, Is.Empty);
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
