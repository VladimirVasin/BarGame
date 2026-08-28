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
        [TestCase(
            CityDistrictKind.NorthWaterfront,
            "map.district.north_waterfront")]
        [TestCase(
            CityDistrictKind.Cemetery,
            "map.district.cemetery")]
        [TestCase(
            CityDistrictKind.Yard,
            "map.district.yard")]
        [TestCase(
            CityDistrictKind.Church,
            "map.district.church")]
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

                Assert.That(controller.Bars, Has.Count.EqualTo(1));
                Assert.That(
                    controller.GetBarLabel(0),
                    Is.EqualTo(
                        LocalizationService.Get(
                            BarDistrictIdentityCatalog.Get(
                                    controller.Bars[0].District)
                                .DisplayNameKey)));
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
        public void AreaOverlay_DrawsAndNamesEveryCanonicalPrecinct()
        {
            // The precincts under test only exist on the shipped coastal
            // blueprint; the bare settings overload builds the legacy city.
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                58021);
            IReadOnlyList<CityMapAreaRegion> regions =
                CityMapAreaOverlayBuilder.Create(layout);

            Assert.That(
                regions,
                Has.Count.EqualTo(layout.Blueprint.Areas.Count));

            var byFeature =
                new Dictionary<CityAreaFeatureKind, CityMapAreaRegion>();
            for (int index = 0; index < regions.Count; index++)
            {
                CityMapAreaRegion region = regions[index];
                Assert.That(
                    region.LandBounds.Count + region.WaterBounds.Count,
                    Is.GreaterThan(0),
                    region.AreaId);
                Assert.That(
                    region.Outline,
                    Is.Not.Empty,
                    region.AreaId);
                Assert.That(
                    region.LocalizationKey,
                    Is.Not.Empty,
                    region.AreaId);
                byFeature[region.Feature] = region;
            }

            Assert.That(
                byFeature.Keys,
                Is.EquivalentTo(new[]
                {
                    CityAreaFeatureKind.UrbanDistrict,
                    CityAreaFeatureKind.CentralPark,
                    CityAreaFeatureKind.NorthWaterfront,
                    CityAreaFeatureKind.Cemetery,
                    CityAreaFeatureKind.Yard,
                    CityAreaFeatureKind.Church
                }));

            // The waterfront is the one precinct that carries open
            // water now that the lake is drained, and an open precinct
            // is only enterable through its gates.
            Assert.That(
                byFeature[CityAreaFeatureKind.NorthWaterfront]
                    .WaterBounds,
                Is.Not.Empty);
            Assert.That(
                byFeature[CityAreaFeatureKind.NorthWaterfront].Gates,
                Is.Not.Empty);
            Assert.That(
                byFeature[CityAreaFeatureKind.Cemetery].Gates,
                Has.Count.EqualTo(1));
            Assert.That(
                byFeature[CityAreaFeatureKind.Church].Gates,
                Has.Count.EqualTo(1));
            Assert.That(
                byFeature[CityAreaFeatureKind.Cemetery]
                    .InternalPassages,
                Has.Count.EqualTo(1));
            Assert.That(
                byFeature[CityAreaFeatureKind.Church]
                    .InternalPassages,
                Is.EqualTo(
                    byFeature[CityAreaFeatureKind.Cemetery]
                        .InternalPassages));
            Assert.That(
                byFeature[CityAreaFeatureKind.UrbanDistrict].IsUrban,
                Is.True);
            Assert.That(
                byFeature[CityAreaFeatureKind.Yard].IsUrban,
                Is.False);
        }

        [Test]
        public void AreaTeleportTargets_LandOnWalkableGroundInEveryOpenPrecinct()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                58021);
            var mapObject = new GameObject("City Map Area Target Test");
            var previousRoute = new List<string>(
                GameSessionState.PlannedBarRoute);
            try
            {
                CityMapController controller =
                    mapObject.AddComponent<CityMapController>();
                controller.Initialize(layout, default, null, null);

                IReadOnlyList<CityMapAreaTarget> targets =
                    controller.MapAreaTargets;
                // Not merely equal to the access count: both being zero
                // would let every assertion below pass on an empty list.
                Assert.That(
                    targets,
                    Is.Not.Empty,
                    "The coastal city has open precincts to reach.");
                Assert.That(
                    targets,
                    Has.Count.EqualTo(layout.OpenAreaAccesses.Count),
                    "Every open precinct must be reachable, and only those.");
                TestContext.Out.WriteLine(
                    $"open precinct targets: {targets.Count}");

                // One mask for the whole fixture: building it runs a full
                // layout validation and every stair placement.
                RoadWalkableArea walkable =
                    RoadWalkableArea.FromLayout(layout);
                var accessesByArea =
                    new Dictionary<string, CityOpenAreaAccessDescriptor>();
                for (int index = 0;
                     index < layout.OpenAreaAccesses.Count;
                     index++)
                {
                    CityOpenAreaAccessDescriptor access =
                        layout.OpenAreaAccesses[index];
                    accessesByArea.Add(access.AreaId, access);
                }

                for (int index = 0; index < targets.Count; index++)
                {
                    CityMapAreaTarget target = targets[index];
                    string what = target.Region.AreaId;
                    Assert.That(
                        target.SelectionIndex,
                        Is.EqualTo(controller.MapObjects.Count + index),
                        what);
                    Assert.That(
                        target.Region.IsUrban,
                        Is.False,
                        what);
                    Assert.That(
                        accessesByArea.TryGetValue(
                            what,
                            out CityOpenAreaAccessDescriptor access),
                        Is.True,
                        what);

                    // The mask is the real boundary of the city, and it
                    // tests one rectangle at a time - so this is the whole
                    // safety contract of the feature.
                    Assert.That(
                        walkable.Contains(
                            target.ArrivalPosition,
                            CityGroundTraversalPlanner.MaximumAgentRadius),
                        Is.True,
                        $"{what} arrives outside the walkable mask.");
                    Assert.That(
                        CityTerrainSurfacePlan.TrySampleGroundTop(
                            layout,
                            new Vector2(
                                target.ArrivalPosition.x,
                                target.ArrivalPosition.z),
                            out float groundTop,
                            out CitySurfaceDescriptor surface),
                        Is.True,
                        what);
                    Assert.That(surface.IsWater, Is.False, what);
                    Assert.That(
                        target.ArrivalPosition.y,
                        Is.EqualTo(
                            groundTop + PlayerFactory.GroundedRootOffset)
                            .Within(0.001f),
                        $"{what} must stand on the drawn ground.");

                    // OutwardNormal points from the street into the area;
                    // a future negation would face the player at the kerb.
                    Assert.That(
                        target.ArrivalFacing,
                        Is.EqualTo(access.OutwardNormal),
                        what);
                    Assert.That(target.Cell, Is.EqualTo(access.Cell), what);
                    Assert.That(
                        controller.GetMapObjectLabel(target.SelectionIndex),
                        Is.Not.Empty,
                        what);
                }

                // The five yards share one name, so the labels must still
                // come out distinct or the teleport panel is ambiguous.
                var labels = new HashSet<string>();
                for (int index = 0; index < targets.Count; index++)
                {
                    Assert.That(
                        labels.Add(
                            controller.GetMapObjectLabel(
                                targets[index].SelectionIndex)),
                        Is.True,
                        targets[index].Region.AreaId);
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
        public void HoverResolution_PrefersAnyMarkerOverTheAreaBeneathIt()
        {
            var cell = new Rect(80f, 80f, 40f, 40f);
            var targets = new[]
            {
                new CityMapView.MapHoverTarget(
                    cell,
                    cell.center,
                    "area",
                    CityMapView.AreaHoverPriority),
                new CityMapView.MapHoverTarget(
                    new Rect(84f, 84f, 12f, 12f),
                    new Vector2(90f, 90f),
                    "marker",
                    10)
            };

            // The pointer sits nearer the precinct's own centre, which
            // must still not outbid the marker it is standing under.
            Assert.That(
                CityMapView.ResolveHoveredLabel(
                    targets,
                    new Vector2(92f, 92f)),
                Is.EqualTo("marker"));
            Assert.That(
                CityMapView.ResolveHoveredLabel(
                    targets,
                    new Vector2(110f, 110f)),
                Is.EqualTo("area"));
            Assert.That(
                CityMapView.AreaHoverPriority,
                Is.LessThan(0));
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

        [Test]
        [Category("CityRiver")]
        public void RiverBridges_KeepThreeDistinctMapStyles()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                58021);
            var colors = new HashSet<Color>();
            int roadBridgeCount = 0;
            int footbridgeCount = 0;

            for (int index = 0;
                 index < layout.River.Bridges.Count;
                 index++)
            {
                CityBridgeDefinition bridge =
                    layout.River.Bridges[index].Definition;
                Color color = InvokePrivate<Color>(
                    "GetRiverBridgeMapColor",
                    bridge.Style);
                float width = InvokePrivate<float>(
                    "GetRiverBridgeMapWidth",
                    bridge,
                    6f);

                Assert.That(colors.Add(color), Is.True, bridge.Id);
                Assert.That(color.a, Is.EqualTo(1f).Within(0.001f));
                if (bridge.Role == CityBridgeRole.Road)
                {
                    roadBridgeCount++;
                    Assert.That(width, Is.EqualTo(6f));
                }
                else
                {
                    footbridgeCount++;
                    Assert.That(width, Is.LessThan(6f));
                    Assert.That(width, Is.GreaterThanOrEqualTo(2f));
                }
            }

            Assert.That(roadBridgeCount, Is.EqualTo(2));
            Assert.That(footbridgeCount, Is.EqualTo(1));
            Assert.That(colors.Count, Is.EqualTo(3));
        }

        [Test]
        public void BusRoute_UsesExactOpaqueBlueDistinctFromOtherLines()
        {
            Color bus = CityMapView.BusRouteColor;
            Color street = InvokePrivate<Color>(
                "GetPathColor",
                CityPathKind.Street);

            Assert.That(bus.r, Is.EqualTo(91f / 255f).Within(0.0001f));
            Assert.That(bus.g, Is.EqualTo(143f / 255f).Within(0.0001f));
            Assert.That(bus.b, Is.EqualTo(209f / 255f).Within(0.0001f));
            Assert.That(bus.a, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(bus, Is.Not.EqualTo(street));
            Assert.That(bus, Is.Not.EqualTo(RetroUiTheme.Accent));
        }

        [TestCase(false, 18f)]
        [TestCase(true, 33f)]
        public void BusLegend_IsFixedAndContainedInVisibleMap(
            bool includeStop,
            float expectedHeight)
        {
            var map = new Rect(0f, 0f, 452f, 311f);
            Rect legend = CityMapView.CreateBusLegendRect(
                map,
                includeStop);

            Assert.That(legend.x, Is.EqualTo(map.x + 5f));
            Assert.That(legend.y, Is.EqualTo(map.y + 5f));
            Assert.That(legend.width, Is.EqualTo(132f));
            Assert.That(legend.height, Is.EqualTo(expectedHeight));
            Assert.That(legend.xMax, Is.LessThan(map.xMax));
            Assert.That(legend.yMax, Is.LessThan(map.yMax));
        }

        [Test]
        public void BusStopHoverPriority_SitsBetweenPoiAndBar()
        {
            var hitbox = new Rect(90f, 90f, 20f, 20f);
            var anchor = new Vector2(100f, 100f);
            var targets = new[]
            {
                new CityMapView.MapHoverTarget(
                    hitbox,
                    anchor,
                    "poi",
                    10),
                new CityMapView.MapHoverTarget(
                    hitbox,
                    anchor,
                    "stop",
                    CityMapView.BusStopHoverPriority)
            };

            Assert.That(CityMapView.BusStopHoverPriority, Is.EqualTo(15));
            Assert.That(
                CityMapView.ResolveHoveredLabel(targets, anchor),
                Is.EqualTo("stop"));
            Assert.That(
                CityMapView.BusStopHoverPriority,
                Is.LessThan(20));
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
