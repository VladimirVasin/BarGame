using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapAreaPresentationTests
    {
        [Test]
        public void MountainRoadOverlay_SampleOnlyInputInfersTwoHairpins()
        {
            var samples = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 14f),
                new Vector3(8f, 2f, 21f),
                new Vector3(18f, 3f, 14f),
                new Vector3(18f, 4f, 30f),
                new Vector3(10f, 5f, 38f),
                new Vector3(0f, 6f, 30f),
                new Vector3(0f, 7f, 50f),
                new Vector3(10f, 8.7f, 65f)
            };
            var plateau = new Rect(4f, 60f, 12f, 10f);

            CityMapMountainRoadOverlay overlay =
                CityMapMountainRoadOverlayBuilder.Create(
                    samples,
                    plateau);

            Assert.That(overlay.IsEmpty, Is.False);
            Assert.That(overlay.RoutePoints.Count, Is.EqualTo(samples.Count));
            Assert.That(overlay.TunnelPosition, Is.EqualTo(samples[0]));
            Assert.That(
                overlay.EndpointPosition,
                Is.EqualTo(samples[samples.Count - 1]));
            Assert.That(overlay.HairpinPositions.Count, Is.EqualTo(2));
            Assert.That(overlay.HasBridge, Is.False);
            Assert.That(overlay.BridgePosition, Is.EqualTo(Vector3.zero));
            Assert.That(overlay.MountainHatches.Count, Is.EqualTo(18));
            Assert.That(overlay.TerminalLandmarks, Is.Empty);
            Assert.That(overlay.PlateauBounds, Is.EqualTo(plateau));
            for (int index = 0; index < samples.Count; index++)
            {
                Assert.That(
                    overlay.DisplayWorldXZBounds.Contains(
                        new Vector2(samples[index].x, samples[index].z)),
                    Is.True,
                    $"Route sample {index} must fit the chart.");
            }

            samples[0] = new Vector3(999f, 999f, 999f);
            Assert.That(
                overlay.TunnelPosition,
                Is.EqualTo(new Vector3(0f, 0f, 0f)),
                "The chart must not borrow a mutable scene list.");
        }

        [Test]
        [Category("MountainRoad")]
        public void MountainRoadOverlay_FromPlanOwnsHairpinsBridgeAndLandmarks()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(58021);

            CityMapMountainRoadOverlay overlay =
                CityMapMountainRoadOverlayBuilder.Create(plan);

            Assert.That(plan.Route.Hairpins.Count, Is.GreaterThan(2));
            Assert.That(
                overlay.HairpinPositions.Count,
                Is.EqualTo(plan.Route.Hairpins.Count));
            Assert.That(
                overlay.RoutePoints,
                Has.Count.InRange(150, 190),
                "The plan chart must retain the serpentine without issuing " +
                "one IMGUI draw pair per metre.");
            for (int index = 0; index < plan.Route.Hairpins.Count; index++)
            {
                Assert.That(
                    overlay.HairpinPositions[index],
                    Is.EqualTo(plan.Route.Hairpins[index].ApexPosition));
                Assert.That(
                    overlay.RoutePoints.Any(point =>
                        Vector3.Distance(
                            point,
                            plan.Route.Hairpins[index].ApexPosition) <
                        0.001f),
                    Is.True,
                    $"Chart route omitted hairpin apex {index}.");
            }

            Assert.That(overlay.HasBridge, Is.True);
            Assert.That(overlay.BridgePosition, Is.EqualTo(plan.Bridge.Center));
            Assert.That(
                overlay.DisplayWorldXZBounds.Contains(
                    new Vector2(
                        overlay.BridgePosition.x,
                        overlay.BridgePosition.z)),
                Is.True,
                "The authored bridge must fit the chart.");
            Assert.That(overlay.TerminalLandmarks.Count, Is.EqualTo(2));
            Assert.That(
                overlay.TerminalLandmarks,
                Is.Not.SameAs(plan.Terminal.Landmarks));
            Assert.That(
                overlay.TerminalLandmarks[0].Kind,
                Is.EqualTo(MountainRoadTerminalLandmarkKind.Cafe));
            Assert.That(
                overlay.TerminalLandmarks[0].LocalizationKey,
                Is.EqualTo("map.mountain_road.cafe"));
            Assert.That(
                overlay.TerminalLandmarks[1].Kind,
                Is.EqualTo(MountainRoadTerminalLandmarkKind.Cableway));
            Assert.That(
                overlay.TerminalLandmarks[1].LocalizationKey,
                Is.EqualTo("map.mountain_road.cableway"));
            for (int index = 0;
                 index < overlay.TerminalLandmarks.Count;
                 index++)
            {
                Vector3 position = overlay.TerminalLandmarks[index].Position;
                Assert.That(
                    overlay.DisplayWorldXZBounds.Contains(
                        new Vector2(position.x, position.z)),
                    Is.True,
                    $"Terminal landmark {index} must fit the chart.");
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void MapPointInspection_CoversBothTabsWithoutRequestingTravel()
        {
            var host = new GameObject("Two Area Map Point Test");
            var playerObject = new GameObject("Map Point Test Player");
            try
            {
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    58021);
                PlayerInteractor interactor =
                    playerObject.AddComponent<PlayerInteractor>();
                var player = new PlayerRuntime(
                    playerObject,
                    null,
                    interactor,
                    null);
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null);

                MountainRoadPlan mountainPlan =
                    MountainRoadPlanner.Create(58021);
                CityMapMountainRoadOverlay mountainOverlay =
                    CityMapMountainRoadOverlayBuilder.Create(mountainPlan);
                int travelRequestCount = 0;
                controller.ConfigureAreas(
                    GameAreaId.City,
                    mountainOverlay,
                    (_, _) =>
                    {
                        travelRequestCount++;
                        return true;
                    });

                IReadOnlyList<CityMapPointDescriptor> cityPoints =
                    controller.GetMapPoints(GameAreaId.City);
                IReadOnlyList<CityMapPointDescriptor> mountainPoints =
                    controller.GetMapPoints(GameAreaId.MountainRoad);
                Assert.That(
                    cityPoints,
                    Has.Count.EqualTo(
                        controller.MapObjects.Count +
                        controller.MapAreaTargets.Count +
                        1),
                    "City should contain one point per legacy map object, " +
                    "one per open-area target and the current player.");
                AssertUniqueFinitePoints(cityPoints, GameAreaId.City);
                AssertUniqueFinitePoints(
                    mountainPoints,
                    GameAreaId.MountainRoad);

                for (int index = 0;
                     index < controller.MapObjects.Count;
                     index++)
                {
                    BuildingLot lot = controller.MapObjects[index];
                    string stableId;
                    CityMapPointKind kind;
                    Vector3 position;
                    if (lot.IsBar)
                    {
                        stableId = "city:bar:" + lot.BarId;
                        kind = CityMapPointKind.Bar;
                        position = lot.ReturnPosition;
                    }
                    else if (lot.IsPlayerHome)
                    {
                        stableId = "city:home";
                        kind = CityMapPointKind.Home;
                        position = lot.Center;
                    }
                    else if (lot.IsSupermarket)
                    {
                        stableId = "city:supermarket";
                        kind = CityMapPointKind.Supermarket;
                        position = lot.Center;
                    }
                    else
                    {
                        CityMapPointOfInterest pointOfInterest =
                            controller.PointsOfInterest.FirstOrDefault(
                                point => point.LotCell == lot.Cell);
                        if (!string.IsNullOrEmpty(pointOfInterest.StableId))
                        {
                            stableId =
                                "city:poi:" + pointOfInterest.StableId;
                            kind = CityMapPointKind.PointOfInterest;
                            position = pointOfInterest.WorldPosition;
                        }
                        else
                        {
                            stableId =
                                $"city:lot:{lot.Cell.x}:{lot.Cell.y}";
                            kind = CityMapPointKind.MapObject;
                            position = lot.Center;
                        }
                    }

                    AssertPoint(
                        cityPoints,
                        stableId,
                        kind,
                        position);
                }

                for (int index = 0;
                     index < controller.MapAreaTargets.Count;
                     index++)
                {
                    CityMapAreaTarget target =
                        controller.MapAreaTargets[index];
                    AssertPoint(
                        cityPoints,
                        "city:open-area:" + target.Region.AreaId,
                        CityMapPointKind.OpenArea,
                        target.ArrivalPosition);
                }

                Assert.That(
                    mountainPoints,
                    Has.Count.EqualTo(
                        mountainPlan.Route.Hairpins.Count +
                        mountainPlan.Terminal.Landmarks.Count +
                        3),
                    "Mountain points should be tunnel, every hairpin, " +
                    "bridge, plateau and terminal landmarks.");
                AssertPoint(
                    mountainPoints,
                    "mountain-road:tunnel-exit",
                    CityMapPointKind.Tunnel,
                    mountainOverlay.TunnelPosition);
                for (int index = 0;
                     index < mountainPlan.Route.Hairpins.Count;
                     index++)
                {
                    AssertPoint(
                        mountainPoints,
                        $"mountain-road:hairpin:{index + 1:00}",
                        CityMapPointKind.Hairpin,
                        mountainPlan.Route.Hairpins[index].ApexPosition);
                }

                AssertPoint(
                    mountainPoints,
                    "mountain-road:bridge",
                    CityMapPointKind.Bridge,
                    mountainPlan.Bridge.Center);
                AssertPoint(
                    mountainPoints,
                    "mountain-road:plateau",
                    CityMapPointKind.Plateau,
                    mountainOverlay.EndpointPosition);
                for (int index = 0;
                     index < mountainPlan.Terminal.Landmarks.Count;
                     index++)
                {
                    MountainRoadTerminalLandmark landmark =
                        mountainPlan.Terminal.Landmarks[index];
                    AssertPoint(
                        mountainPoints,
                        "mountain-road:terminal:" + index,
                        landmark.Kind ==
                        MountainRoadTerminalLandmarkKind.Cafe
                            ? CityMapPointKind.Cafe
                            : CityMapPointKind.Cableway,
                        landmark.Position);
                }

                Assert.That(
                    controller.SetMapPointInspectionEnabled(true),
                    Is.True);
                Assert.That(controller.SelectMapPoint(0), Is.True);
                Assert.That(controller.SelectedMapPointIndex, Is.EqualTo(0));
                Assert.That(travelRequestCount, Is.Zero);

                Assert.That(
                    controller.SelectArea(GameAreaId.MountainRoad),
                    Is.True);
                Assert.That(controller.SelectedMapPointIndex, Is.EqualTo(-1));
                Assert.That(
                    controller.SelectMapPoint(mountainPoints.Count - 1),
                    Is.True);
                Assert.That(
                    controller.SelectedMapPointIndex,
                    Is.EqualTo(mountainPoints.Count - 1));
                Assert.That(
                    travelRequestCount,
                    Is.Zero,
                    "Selecting a point on the other tab must not request " +
                    "cross-area travel.");

                var overlappingTargets = new[]
                {
                    new CityMapView.MapHoverTarget(
                        new Rect(20f, 20f, 60f, 60f),
                        new Vector2(50f, 50f),
                        "plateau",
                        CityMapView.AreaHoverPriority,
                        3),
                    new CityMapView.MapHoverTarget(
                        new Rect(42f, 42f, 16f, 16f),
                        new Vector2(50f, 50f),
                        "cafe",
                        30,
                        9)
                };
                Assert.That(
                    CityMapView.ResolveMapPointIndex(
                        overlappingTargets,
                        new Vector2(50f, 50f)),
                    Is.EqualTo(9),
                    "The same foreground-first resolver must drive map " +
                    "point clicks and hover priority.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void MountainRoadOverlay_RejectsNonFiniteRouteSamples()
        {
            var samples = new[]
            {
                Vector3.zero,
                new Vector3(float.NaN, 1f, 4f)
            };

            Assert.That(
                () => CityMapMountainRoadOverlayBuilder.Create(
                    samples,
                    new Rect(-6f, 76f, 12f, 10f)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AreaTabs_HaveSeparatePointerTargetsInsideHeader()
        {
            var panel = new Rect(8f, 8f, 624f, 344f);
            Rect city = CityMapView.CreateAreaTabRect(panel, 0, 2);
            Rect mountain = CityMapView.CreateAreaTabRect(panel, 1, 2);

            Assert.That(city.Overlaps(mountain), Is.False);
            Assert.That(city.xMin, Is.GreaterThanOrEqualTo(panel.xMin));
            Assert.That(city.yMin, Is.GreaterThanOrEqualTo(panel.yMin));
            Assert.That(city.xMax, Is.LessThanOrEqualTo(panel.xMax));
            Assert.That(city.yMax, Is.LessThanOrEqualTo(panel.yMax));
            Assert.That(mountain.xMin, Is.GreaterThanOrEqualTo(panel.xMin));
            Assert.That(mountain.yMin, Is.GreaterThanOrEqualTo(panel.yMin));
            Assert.That(mountain.xMax, Is.LessThanOrEqualTo(panel.xMax));
            Assert.That(mountain.yMax, Is.LessThanOrEqualTo(panel.yMax));
            Assert.That(city.height, Is.EqualTo(18f));
            Assert.That(mountain.height, Is.EqualTo(18f));
        }

        [Test]
        public void CrossAreaTravel_UsesCallbackAndMapTeleportArrival()
        {
            var host = new GameObject("Two Area Map Test");
            try
            {
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                CityMapMountainRoadOverlay overlay =
                    CityMapMountainRoadOverlayBuilder.Create(
                        new[]
                        {
                            Vector3.zero,
                            new Vector3(0f, 2f, 20f),
                            new Vector3(12f, 4f, 30f),
                            new Vector3(2f, 6f, 40f),
                            new Vector3(8f, 8f, 64f)
                        },
                        new Rect(2f, 59f, 12f, 10f));
                GameAreaId requestedArea = GameAreaId.City;
                AreaArrivalToken requestedArrival =
                    AreaArrivalToken.Default;
                int requestCount = 0;

                controller.ConfigureAreas(
                    GameAreaId.City,
                    overlay,
                    (area, arrival) =>
                    {
                        requestedArea = area;
                        requestedArrival = arrival;
                        requestCount++;
                        return true;
                    });

                Assert.That(controller.AreaTabs.Count, Is.EqualTo(2));
                Assert.That(controller.CurrentArea, Is.EqualTo(GameAreaId.City));
                Assert.That(controller.SelectedArea, Is.EqualTo(GameAreaId.City));
                Assert.That(
                    controller.RequestSelectedAreaTravel(),
                    Is.False,
                    "The current area must never reload itself.");

                Assert.That(
                    controller.SelectArea(GameAreaId.MountainRoad),
                    Is.True);
                Assert.That(controller.IsSelectedAreaCurrent, Is.False);
                Assert.That(controller.RequestSelectedAreaTravel(), Is.True);
                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(
                    requestedArea,
                    Is.EqualTo(GameAreaId.MountainRoad));
                Assert.That(
                    requestedArrival,
                    Is.EqualTo(AreaArrivalToken.MapTeleport));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CrossAreaTravel_RejectionKeepsSelectionAndReportsFalse()
        {
            var host = new GameObject("Rejected Two Area Map Test");
            var playerObject = new GameObject("Rejected Map Test Player");
            try
            {
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                PlayerInteractor interactor =
                    playerObject.AddComponent<PlayerInteractor>();
                var player = new PlayerRuntime(
                    playerObject,
                    null,
                    interactor,
                    null);
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    58021);
                controller.Initialize(layout, player, null, null);
                CityMapMountainRoadOverlay overlay =
                    CityMapMountainRoadOverlayBuilder.Create(
                        new[]
                        {
                            Vector3.zero,
                            new Vector3(0f, 2f, 20f),
                            new Vector3(12f, 4f, 30f),
                            new Vector3(2f, 6f, 40f),
                            new Vector3(8f, 8f, 64f)
                        },
                        new Rect(2f, 59f, 12f, 10f));
                int requestCount = 0;
                controller.ConfigureAreas(
                    GameAreaId.City,
                    overlay,
                    (_, _) =>
                    {
                        requestCount++;
                        return false;
                    });
                controller.SelectArea(GameAreaId.MountainRoad);
                Assert.That(controller.Open(), Is.True);

                Assert.That(
                    controller.RequestSelectedAreaTravel(),
                    Is.False);
                Assert.That(
                    controller.IsOpen,
                    Is.True,
                    "A rejected request must leave the map modal open.");
                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(
                    controller.SelectedArea,
                    Is.EqualTo(GameAreaId.MountainRoad),
                    "A rejected request must leave the destination selected " +
                    "for a retry.");
                Assert.That(
                    controller.CurrentArea,
                    Is.EqualTo(GameAreaId.City));
                Assert.That(
                    controller.CanRequestSelectedAreaTravel,
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        private static void AssertUniqueFinitePoints(
            IReadOnlyList<CityMapPointDescriptor> points,
            GameAreaId expectedArea)
        {
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < points.Count; index++)
            {
                CityMapPointDescriptor point = points[index];
                Assert.That(point.Area, Is.EqualTo(expectedArea));
                Assert.That(
                    point.StableId,
                    Is.Not.Null.And.Not.Empty,
                    $"Map point {index} in {expectedArea} has no stable ID.");
                Assert.That(
                    stableIds.Add(point.StableId),
                    Is.True,
                    $"Duplicate {expectedArea} map point ID " +
                    $"'{point.StableId}'.");
                Assert.That(
                    IsFinite(point.WorldPosition.x) &&
                    IsFinite(point.WorldPosition.y) &&
                    IsFinite(point.WorldPosition.z),
                    Is.True,
                    $"Map point '{point.StableId}' has non-finite XYZ " +
                    $"{point.WorldPosition}.");
            }
        }

        /// <summary>
        /// The point inspector and the debug teleport are meant to be used
        /// TOGETHER, and for a while they cancelled each other.
        ///
        /// The reasoning behind that was half right: a single map click
        /// cannot mean both "select this lot" and "select this point". But
        /// the fix was applied to the MODES rather than to the click, so
        /// opening the inspector silently switched debug mode off - and the
        /// inspector is exactly when a precise teleport is most useful,
        /// because until then the only destinations were whole precincts
        /// like the cemetery or the yards. Selecting one of those could only
        /// ever mean "somewhere in there".
        /// </summary>
        [Test]
        public void PointInspection_KeepsDebugTeleportAndOffersThePointItself()
        {
            var host = new GameObject("Map Point Teleport Test");
            var playerObject = new GameObject("Map Point Teleport Player");
            try
            {
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    58021);
                PlayerInteractor interactor =
                    playerObject.AddComponent<PlayerInteractor>();
                var player = new PlayerRuntime(
                    playerObject,
                    null,
                    interactor,
                    null);
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null);
                CityMapMountainRoadOverlay mountainOverlay =
                    CityMapMountainRoadOverlayBuilder.Create(
                        MountainRoadPlanner.Create(58021));
                controller.ConfigureAreas(
                    GameAreaId.City,
                    mountainOverlay,
                    (_, _) => true);

                Assert.That(
                    controller.SetDebugTeleportEnabled(true),
                    Is.True);
                Assert.That(
                    controller.SetMapPointInspectionEnabled(true),
                    Is.True);
                Assert.That(
                    controller.DebugTeleportEnabled,
                    Is.True,
                    "Opening the coordinate inspector must not take the " +
                    "teleport away; picking the exact point is what it is " +
                    "for.");
                Assert.That(
                    controller.SelectedMapObjectIndex,
                    Is.EqualTo(-1),
                    "The whole-lot selection does go, so one map click " +
                    "still means exactly one thing.");

                // And the other way round: leaving debug mode takes the
                // debug-only inspector with it.
                Assert.That(
                    controller.SetDebugTeleportEnabled(false),
                    Is.True);
                Assert.That(
                    controller.MapPointInspectionEnabled,
                    Is.False);

                // Every open precinct is reachable as a POINT, which is the
                // half the whole-lot teleport never had.
                IReadOnlyList<CityMapPointDescriptor> cityPoints =
                    controller.GetMapPoints(GameAreaId.City);
                foreach (CityMapAreaTarget target in
                         controller.MapAreaTargets)
                {
                    string stableId =
                        "city:open-area:" + target.Region.AreaId;
                    CityMapPointDescriptor point = cityPoints.SingleOrDefault(
                        candidate => string.Equals(
                            candidate.StableId,
                            stableId,
                            StringComparison.Ordinal));
                    Assert.That(
                        point.StableId,
                        Is.EqualTo(stableId),
                        $"The precinct '{target.Region.AreaId}' has no " +
                        "point of its own, so it can only be teleported to " +
                        "as a whole region.");
                    Assert.That(
                        point.Kind,
                        Is.EqualTo(CityMapPointKind.OpenArea));
                }

                // A point on the other tab is a chart of somewhere else, and
                // getting there is a scene transition rather than a
                // teleport.
                controller.SetDebugTeleportEnabled(true);
                controller.SetMapPointInspectionEnabled(true);
                Assert.That(
                    controller.SelectArea(GameAreaId.MountainRoad),
                    Is.True);
                IReadOnlyList<CityMapPointDescriptor> mountainPoints =
                    controller.GetMapPoints(GameAreaId.MountainRoad);
                Assert.That(
                    controller.SelectMapPoint(mountainPoints.Count - 1),
                    Is.True);
                Assert.That(
                    controller.CanTeleportToSelectedMapPoint,
                    Is.False,
                    "The mountain road is a different scene; the area " +
                    "travel button owns that trip.");
                Assert.That(
                    controller.ConfirmMapPointTeleport(),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        /// <summary>
        /// A plan view has two coordinates. The readout printed three for a
        /// while, which is a debug dump rather than a chart: height is the
        /// one number the projection cannot show and nobody navigates by it.
        /// </summary>
        [Test]
        public void PointCoordinates_ReadOutXAndZOnly()
        {
            string text = CityMapView.FormatMapPointCoordinates(
                new Vector3(12.25f, 7.5f, -34.5f));
            Assert.That(text, Does.Contain("12.2").Or.Contain("12.3"));
            Assert.That(text, Does.Contain("-34.5"));
            Assert.That(
                text,
                Does.Not.Contain("7.5"),
                "Height is not a map coordinate.");
            Assert.That(
                text.Split('\n').Last().Count(character => character == 'Z'),
                Is.EqualTo(1));
        }

        private static void AssertPoint(
            IReadOnlyList<CityMapPointDescriptor> points,
            string stableId,
            CityMapPointKind expectedKind,
            Vector3 expectedPosition)
        {
            CityMapPointDescriptor point = points.SingleOrDefault(
                candidate => string.Equals(
                    candidate.StableId,
                    stableId,
                    StringComparison.Ordinal));
            Assert.That(
                point.StableId,
                Is.EqualTo(stableId),
                $"Missing selectable map point '{stableId}'.");
            Assert.That(point.Kind, Is.EqualTo(expectedKind), stableId);
            Assert.That(
                Vector3.Distance(point.WorldPosition, expectedPosition),
                Is.LessThanOrEqualTo(0.001f),
                stableId);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
