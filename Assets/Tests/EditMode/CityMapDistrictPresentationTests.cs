using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapDistrictPresentationTests
    {
        [Test]
        public void DistrictColors_AreDistinctAndOpaque()
        {
            var colors = new HashSet<Color>();
            foreach (CityDistrictKind district in
                     Enum.GetValues(typeof(CityDistrictKind)))
            {
                Color color = InvokePrivate<Color>(
                    "GetDistrictColor",
                    district);
                Assert.That(
                    color.a,
                    Is.EqualTo(1f).Within(0.001f),
                    district.ToString());
                Assert.That(
                    colors.Add(color),
                    Is.True,
                    $"{district} must have a distinct map color.");
            }
        }

        [TestCase(
            CityDistrictKind.OldTown,
            "map.district.old_town")]
        [TestCase(
            CityDistrictKind.Residential,
            "map.district.residential")]
        [TestCase(
            CityDistrictKind.Industrial,
            "map.district.industrial")]
        [TestCase(
            CityDistrictKind.Nightlife,
            "map.district.nightlife")]
        [TestCase(
            CityDistrictKind.CentralPark,
            "map.district.central_park")]
        public void DistrictLocalizationKeys_AreStable(
            CityDistrictKind district,
            string expected)
        {
            Assert.That(
                InvokePrivate<string>(
                    "GetDistrictLocalizationKey",
                    district),
                Is.EqualTo(expected));
        }

        [TestCase(
            CityDistrictPointOfInterestKind.OldTownWaterworksCourt,
            "map.poi.old_town_waterworks_court")]
        [TestCase(
            CityDistrictPointOfInterestKind.ResidentialDryingYard,
            "map.poi.residential_drying_yard")]
        [TestCase(
            CityDistrictPointOfInterestKind.IndustrialWeighbridge,
            "map.poi.industrial_weighbridge")]
        [TestCase(
            CityDistrictPointOfInterestKind.NightlifeLastRouteIsland,
            "map.poi.nightlife_last_route_island")]
        public void PointOfInterestLocalizationKeys_AreStable(
            CityDistrictPointOfInterestKind kind,
            string expected)
        {
            Assert.That(
                CityMapController.TryGetPointOfInterestLocalizationKey(
                    kind,
                    out string actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void UnsupportedPointOfInterestKind_HasNoLocalizationKey()
        {
            Assert.That(
                CityMapController.TryGetPointOfInterestLocalizationKey(
                    (CityDistrictPointOfInterestKind)999,
                    out _),
                Is.False);
        }

        [Test]
        public void DefaultMapMarkers_MirrorCanonicalLayoutPointsOfInterest()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                58021);
            var mapObject = new GameObject("City Map Test");
            var previousRoute = new List<string>(
                GameSessionState.PlannedBarRoute);
            try
            {
                CityMapController controller =
                    mapObject.AddComponent<CityMapController>();
                controller.Initialize(
                    layout,
                    default,
                    null,
                    null);

                Assert.That(controller.PointsOfInterest.Count, Is.EqualTo(4));
                Assert.That(
                    controller.PointsOfInterest.Count,
                    Is.EqualTo(layout.DistrictPointsOfInterest.Count));

                var markersById =
                    new Dictionary<string, CityMapPointOfInterest>();
                for (int index = 0;
                     index < controller.PointsOfInterest.Count;
                     index++)
                {
                    CityMapPointOfInterest marker =
                        controller.PointsOfInterest[index];
                    markersById.Add(marker.StableId, marker);
                }

                for (int index = 0;
                     index < layout.DistrictPointsOfInterest.Count;
                     index++)
                {
                    var descriptor =
                        layout.DistrictPointsOfInterest[index];
                    Assert.That(
                        markersById.TryGetValue(
                            descriptor.Id,
                            out CityMapPointOfInterest marker),
                        Is.True,
                        descriptor.Id);
                    Assert.That(marker.Kind, Is.EqualTo(descriptor.Kind));
                    Assert.That(marker.District, Is.EqualTo(descriptor.District));
                    Assert.That(marker.LotCell, Is.EqualTo(descriptor.Cell));
                    Assert.That(
                        marker.WorldPosition,
                        Is.EqualTo(descriptor.Center));
                }
            }
            finally
            {
                GameSessionState.ClearRoute();
                for (int index = 0; index < previousRoute.Count; index++)
                {
                    GameSessionState.TryAddRouteStop(previousRoute[index]);
                }

                UnityEngine.Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void DefaultMapSupermarket_MirrorsCanonicalLayoutLandmark()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                58021);
            var mapObject = new GameObject("City Map Supermarket Test");
            var previousRoute = new List<string>(
                GameSessionState.PlannedBarRoute);
            try
            {
                CityMapController controller =
                    mapObject.AddComponent<CityMapController>();
                controller.Initialize(
                    layout,
                    default,
                    null,
                    null);

                Assert.That(layout.Supermarket, Is.Not.Null);
                Assert.That(
                    controller.Supermarket,
                    Is.SameAs(layout.Supermarket));
                Assert.That(
                    controller.GetSupermarketLabel(),
                    Is.Not.Empty);
            }
            finally
            {
                GameSessionState.ClearRoute();
                for (int index = 0; index < previousRoute.Count; index++)
                {
                    GameSessionState.TryAddRouteStop(previousRoute[index]);
                }

                UnityEngine.Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void HoverResolution_PrefersNearestMarkerThenPriority()
        {
            var sharedHitbox = new Rect(90f, 90f, 24f, 20f);
            var targets = new[]
            {
                new CityMapView.MapHoverTarget(
                    sharedHitbox,
                    new Vector2(96f, 100f),
                    "far",
                    30),
                new CityMapView.MapHoverTarget(
                    sharedHitbox,
                    new Vector2(104f, 100f),
                    "near-low-priority",
                    10),
                new CityMapView.MapHoverTarget(
                    sharedHitbox,
                    new Vector2(104f, 100f),
                    "near-landmark",
                    30)
            };

            Assert.That(
                CityMapView.ResolveHoveredLabel(
                    targets,
                    new Vector2(103f, 100f)),
                Is.EqualTo("near-landmark"));
            Assert.That(
                CityMapView.ResolveHoveredLabel(
                    targets,
                    new Vector2(130f, 100f)),
                Is.Empty);

            var marginalTargets = new[]
            {
                new CityMapView.MapHoverTarget(
                    sharedHitbox,
                    new Vector2(103.01f, 100f),
                    "marginally-far",
                    30),
                new CityMapView.MapHoverTarget(
                    sharedHitbox,
                    new Vector2(103.005f, 100f),
                    "marginally-near",
                    10)
            };
            Assert.That(
                CityMapView.ResolveHoveredLabel(
                    marginalTargets,
                    new Vector2(103f, 100f)),
                Is.EqualTo("marginally-near"));
        }

        [Test]
        public void HoverTooltip_StaysInsideMapAtEveryCorner()
        {
            var bounds = new Rect(20f, 40f, 420f, 280f);
            var requestedSize = new Vector2(188f, 42f);
            Vector2[] pointers =
            {
                new Vector2(bounds.xMin + 1f, bounds.yMin + 1f),
                new Vector2(bounds.xMax - 1f, bounds.yMin + 1f),
                new Vector2(bounds.xMin + 1f, bounds.yMax - 1f),
                new Vector2(bounds.xMax - 1f, bounds.yMax - 1f)
            };

            for (int index = 0; index < pointers.Length; index++)
            {
                Rect tooltip = CityMapView.CreateTooltipRect(
                    pointers[index],
                    requestedSize,
                    bounds);

                Assert.That(
                    tooltip.xMin,
                    Is.GreaterThanOrEqualTo(bounds.xMin),
                    index.ToString());
                Assert.That(
                    tooltip.xMax,
                    Is.LessThanOrEqualTo(bounds.xMax),
                    index.ToString());
                Assert.That(
                    tooltip.yMin,
                    Is.GreaterThanOrEqualTo(bounds.yMin),
                    index.ToString());
                Assert.That(
                    tooltip.yMax,
                    Is.LessThanOrEqualTo(bounds.yMax),
                    index.ToString());
                Assert.That(
                    tooltip.Contains(pointers[index]),
                    Is.False,
                    index.ToString());
            }
        }

        [Test]
        public void PublicPlaceLot_UsesOpenGroundInsteadOfDistrictBuildingFill()
        {
            Color publicPlace = InvokePrivate<Color>(
                "GetLotColor",
                null,
                true);
            Color oldTownBuilding = InvokePrivate<Color>(
                "GetDistrictColor",
                CityDistrictKind.OldTown);
            Color residentialBuilding = InvokePrivate<Color>(
                "GetDistrictColor",
                CityDistrictKind.Residential);

            Assert.That(publicPlace, Is.Not.EqualTo(oldTownBuilding));
            Assert.That(publicPlace, Is.Not.EqualTo(residentialBuilding));
            Assert.That(publicPlace.a, Is.EqualTo(1f).Within(0.001f));
        }

        [TestCase(0, 1)]
        [TestCase(0, 4)]
        [TestCase(4, 1)]
        [TestCase(4, 4)]
        public void PointOfInterestLegend_FitsBetweenRouteAndFooter(
            int routeCount,
            int pointOfInterestCount)
        {
            var panel = new Rect(461f, 41f, 170f, 311f);
            Rect legend = CityMapView.CreatePointOfInterestLegendRect(
                panel,
                routeCount,
                pointOfInterestCount);
            float routeContentBottom = routeCount == 0
                ? panel.y + 74f
                : panel.y + 29f +
                  (routeCount - 1) * 26f +
                  22f;

            Assert.That(legend.xMin, Is.GreaterThan(panel.xMin));
            Assert.That(legend.xMax, Is.LessThan(panel.xMax));
            Assert.That(
                legend.yMin,
                Is.GreaterThan(routeContentBottom));
            Assert.That(
                legend.yMax,
                Is.LessThanOrEqualTo(panel.yMax - 68f));
        }

        [Test]
        public void ParkPath_UsesNarrowerDistinctMapStyle()
        {
            const float streetWidth = 6f;
            Color streetColor = InvokePrivate<Color>(
                "GetPathColor",
                CityPathKind.Street);
            Color parkColor = InvokePrivate<Color>(
                "GetPathColor",
                CityPathKind.ParkPath);
            float street = InvokePrivate<float>(
                "GetPathWidth",
                CityPathKind.Street,
                streetWidth);
            float park = InvokePrivate<float>(
                "GetPathWidth",
                CityPathKind.ParkPath,
                streetWidth);

            Assert.That(parkColor, Is.Not.EqualTo(streetColor));
            Assert.That(street, Is.EqualTo(streetWidth));
            Assert.That(park, Is.LessThan(street));
            Assert.That(park, Is.GreaterThanOrEqualTo(2f));
        }

        private static T InvokePrivate<T>(
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(CityMapView).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName);
            return (T)method.Invoke(null, arguments);
        }
    }
}
