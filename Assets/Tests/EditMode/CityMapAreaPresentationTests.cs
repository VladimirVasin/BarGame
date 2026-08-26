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
            //  Three since the summit stopped being an MVP pad: the
            //  brink is where the ground stops, which is precisely the
            //  sort of thing a map exists to mark.
            Assert.That(overlay.TerminalLandmarks.Count, Is.EqualTo(3));
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
            Assert.That(
                overlay.TerminalLandmarks[2].Kind,
                Is.EqualTo(MountainRoadTerminalLandmarkKind.Brink));
            Assert.That(
                overlay.TerminalLandmarks[2].LocalizationKey,
                Is.EqualTo("map.mountain_road.brink"));
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
                    _ =>
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
                    request =>
                    {
                        requestedArea = request.DestinationArea;
                        requestedArrival = request.ArrivalToken;
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
            CityMapController controller = null;
            try
            {
                controller = host.AddComponent<CityMapController>();
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
                    _ =>
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
                // This fixture ends with the map deliberately still open,
                // and the map holds the shared modal lock - a plain object
                // in a STATIC field, so it does not go null when its owner
                // is destroyed. Every later fixture that opens anything
                // would be refused with no error anywhere.
                controller?.Close();
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
                    _ => true);

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

                // And the other way round: the inspector no longer belongs
                // to debug mode either. It owns its own teleport, so a
                // toggle in the F9 window has no business closing a mode it
                // does not own - and the button stays.
                Assert.That(
                    controller.SetDebugTeleportEnabled(false),
                    Is.True);
                Assert.That(
                    controller.MapPointInspectionEnabled,
                    Is.True);

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

        /// <summary>
        /// The inspector carries its own teleport, with no second switch.
        ///
        /// It used to be hidden unless the F9 window had armed debug mode -
        /// a switch in another window, and in the mountain-road scene one
        /// that did not exist at all - so the mode read as broken: you pick
        /// a point, you read its coordinates, and there is nothing to press.
        /// Turning the inspector on is the decision; the area is the only
        /// thing still checked.
        /// </summary>
        [Test]
        public void PointInspection_TeleportsWithoutArmingDebugModeFirst()
        {
            var host = new GameObject("Inspector Teleport Test");
            var playerObject = new GameObject("Inspector Teleport Player");
            try
            {
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    58021);
                PlayerInteractor interactor =
                    playerObject.AddComponent<PlayerInteractor>();
                PlayerMotor motor =
                    playerObject.AddComponent<PlayerMotor>();
                var player = new PlayerRuntime(
                    playerObject,
                    motor,
                    interactor,
                    null);
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null);
                controller.ConfigureAreas(
                    GameAreaId.City,
                    CityMapMountainRoadOverlayBuilder.Create(
                        MountainRoadPlanner.Create(58021)),
                    _ => true);

                // A plain object in a static field: a fixture that leaves a
                // map open and destroys it does not release the lock on its
                // own, and everything modal in the game asks this first.
                // Reading it retires a lock whose subject is gone.
                Assert.That(
                    BarMinigameModalLock.IsAnyLocked,
                    Is.False,
                    "Something before this fixture still holds the shared " +
                    "modal lock.");
                Assert.That(controller.Open(), Is.True);
                Assert.That(
                    controller.SetMapPointInspectionEnabled(true),
                    Is.True);
                Assert.That(
                    controller.DebugTeleportEnabled,
                    Is.False,
                    "The whole point: nothing was armed anywhere else.");

                int squareIndex = -1;
                IReadOnlyList<CityMapPointDescriptor> points =
                    controller.ActiveMapPoints;
                for (int index = 0; index < points.Count; index++)
                {
                    if (points[index].Kind ==
                        CityMapPointKind.GroundSquare)
                    {
                        squareIndex = index;
                        break;
                    }
                }

                Assert.That(
                    squareIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "The inspector charts the lattice when it opens.");
                Assert.That(
                    controller.SelectMapPoint(squareIndex),
                    Is.True);
                Assert.That(
                    controller.CanTeleportToSelectedMapPoint,
                    Is.True);

                Assert.That(
                    controller.TryGetSelectedMapPoint(
                        out _,
                        out Vector3 destination),
                    Is.True);
                Assert.That(
                    controller.ConfirmMapPointTeleport(),
                    Is.True);
                Assert.That(
                    new Vector2(
                        playerObject.transform.position.x -
                        destination.x,
                        playerObject.transform.position.z -
                        destination.z).magnitude,
                    Is.LessThanOrEqualTo(0.01f));
                Assert.That(
                    controller.IsOpen,
                    Is.False,
                    "Arriving closes the chart.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        /// <summary>
        /// Picking a place on the other tab starts the trip there and
        /// carries the coordinate.
        ///
        /// "Point is in another area" was a statement of fact standing in
        /// for an answer. It is true that the other tab is a scene which is
        /// not loaded and that reaching it is a transition rather than a
        /// `Motor.Teleport` - and the map is what starts that transition,
        /// so it can say where in it to come out.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void MapPointOnTheOtherTab_TravelsCarryingTheCoordinate()
        {
            var host = new GameObject("Cross Area Point Test");
            var playerObject = new GameObject("Cross Area Point Player");
            CityMapController controller = null;
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
                controller = host.AddComponent<CityMapController>();
                controller.Initialize(layout, player, null, null);

                MountainRoadPlan plan = MountainRoadPlanner.Create(58021);
                AreaTravelRequest captured = default;
                int requestCount = 0;
                controller.ConfigureAreas(
                    GameAreaId.City,
                    CityMapMountainRoadOverlayBuilder.Create(plan),
                    request =>
                    {
                        captured = request;
                        requestCount++;
                        return true;
                    });

                Assert.That(
                    BarMinigameModalLock.IsAnyLocked,
                    Is.False,
                    "Something before this fixture still holds the shared " +
                    "modal lock.");
                Assert.That(controller.Open(), Is.True);
                Assert.That(
                    controller.SetMapPointInspectionEnabled(true),
                    Is.True);
                Assert.That(
                    controller.SelectArea(GameAreaId.MountainRoad),
                    Is.True);

                IReadOnlyList<CityMapPointDescriptor> mountainPoints =
                    controller.GetMapPoints(GameAreaId.MountainRoad);
                int plateauIndex = -1;
                for (int index = 0; index < mountainPoints.Count; index++)
                {
                    if (mountainPoints[index].Kind ==
                        CityMapPointKind.Plateau)
                    {
                        plateauIndex = index;
                        break;
                    }
                }

                Assert.That(plateauIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    controller.SelectMapPoint(plateauIndex),
                    Is.True);
                Assert.That(
                    controller.CanTeleportToSelectedMapPoint,
                    Is.False,
                    "A different scene is never a Motor.Teleport.");
                Assert.That(
                    controller.CanTravelToSelectedMapPoint,
                    Is.True);
                Assert.That(controller.ConfirmMapPointTravel(), Is.True);

                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(
                    captured.DestinationArea,
                    Is.EqualTo(GameAreaId.MountainRoad));
                Assert.That(
                    captured.ArrivalToken,
                    Is.EqualTo(AreaArrivalToken.MapPoint));
                Assert.That(captured.HasArrivalPosition, Is.True);
                Assert.That(captured.IsValid, Is.True);
                Assert.That(
                    captured.ArrivalPosition,
                    Is.EqualTo(
                        mountainPoints[plateauIndex].WorldPosition));

                // And the destination holds it to its own ground rather
                // than spawning wherever the chart happened to draw.
                var ground = new CityMapMountainRoadTeleportGround(plan);
                Assert.That(
                    ground.TryClampArrival(
                        captured.ArrivalPosition,
                        out Vector3 spawn),
                    Is.True);
                Assert.That(
                    new MountainRoadWalkableArea(plan).Contains(
                        spawn,
                        CityGroundTraversalPlanner.MaximumAgentRadius),
                    Is.True);
                Assert.That(
                    controller.IsOpen,
                    Is.False,
                    "Leaving closes the chart.");
            }
            finally
            {
                controller?.Close();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        /// <summary>
        /// The mountain road is charted into even squares, and every square
        /// the lattice keeps is somewhere the hero can actually be put down:
        /// the whole serpentine and the plateau at the top of it, not only
        /// the handful of places a landmark happens to name.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void MountainTeleportLattice_CoversTheWholeRoadAndPlateau()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(58021);
            var walkable = new MountainRoadWalkableArea(plan);
            var ground = new CityMapMountainRoadTeleportGround(walkable);
            CityMapMountainRoadOverlay overlay =
                CityMapMountainRoadOverlayBuilder.Create(plan);

            CityMapTeleportLattice lattice =
                CityMapTeleportLatticeBuilder.Create(
                    overlay.DisplayWorldXZBounds,
                    Vector2.zero,
                    CityMapController.MountainRoadTeleportCellSize,
                    ground);

            Assert.That(lattice.Area, Is.EqualTo(GameAreaId.MountainRoad));
            Assert.That(lattice.Squares, Is.Not.Empty);
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            for (int index = 0; index < lattice.Squares.Count; index++)
            {
                CityMapTeleportSquare square = lattice.Squares[index];
                Assert.That(
                    square.WorldBounds.Contains(new Vector2(
                        square.StandingPosition.x,
                        square.StandingPosition.z)),
                    Is.True,
                    $"Square {square.Cell} lands outside itself, so the " +
                    "chart would send the player to its neighbour.");
                Assert.That(
                    walkable.Contains(square.StandingPosition, radius),
                    Is.True,
                    $"Square {square.Cell} is not walkable ground.");
            }

            // Every twenty metres of the real route has a square of its own,
            // which is what "anywhere along the road" has to mean.
            for (float distance = 0f;
                 distance <= plan.Route.Length;
                 distance += 20f)
            {
                Vector3 position = plan.Route.Sample(distance).Position;
                Assert.That(
                    lattice.TryGetSquareIndexAt(
                        new Vector2(position.x, position.z),
                        out int routeSquare),
                    Is.True,
                    $"The road at {distance:0} m has no square.");
                Assert.That(
                    Mathf.Abs(
                        lattice.Squares[routeSquare].StandingPosition.y -
                        position.y),
                    Is.LessThanOrEqualTo(2.5f),
                    "A road square must stand on the road, not on the " +
                    "terrain the road is cut into.");
            }

            Vector2 plateauCenter = new Vector2(
                plan.Plateau.Center.x,
                plan.Plateau.Center.z);
            Assert.That(
                lattice.TryGetSquareIndexAt(
                    plateauCenter,
                    out int plateauSquare),
                Is.True,
                "The plateau at the top is the one place the road leads.");
            Assert.That(
                lattice.Squares[plateauSquare].StandingPosition.y,
                Is.EqualTo(
                    plan.Plateau.Center.y +
                    PlayerFactory.GroundedRootOffset).Within(0.01f));
        }

        /// <summary>
        /// The two areas share a coordinate system - the mountain route
        /// starts at the world origin, on top of the city - so a mountain
        /// arrival measured against the city's mask is not refused, it is
        /// quietly answered with a street that is not in that scene. The map
        /// clamps against the ground of the area the player is standing in.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void MountainMap_ClampsAgainstMountainGroundNotTheCity()
        {
            var host = new GameObject("Mountain Lattice Map Test");
            var playerObject = new GameObject("Mountain Lattice Player");
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

                MountainRoadPlan plan = MountainRoadPlanner.Create(58021);
                controller.ConfigureAreas(
                    GameAreaId.MountainRoad,
                    CityMapMountainRoadOverlayBuilder.Create(plan),
                    _ => true,
                    new CityMapMountainRoadTeleportGround(plan));

                Assert.That(
                    controller.ActiveTeleportLattice.IsEmpty,
                    Is.True,
                    "Charting the lattice costs thousands of mask probes, " +
                    "so nobody pays for it until the inspector asks.");

                Assert.That(
                    controller.SetDebugTeleportEnabled(true),
                    Is.True);
                Assert.That(
                    controller.SetMapPointInspectionEnabled(true),
                    Is.True);

                CityMapTeleportLattice lattice =
                    controller.ActiveTeleportLattice;
                Assert.That(lattice.Squares, Is.Not.Empty);
                Assert.That(
                    lattice.CellSize,
                    Is.EqualTo(
                        CityMapController.MountainRoadTeleportCellSize)
                        .Within(0.001f));

                IReadOnlyList<CityMapPointDescriptor> mountainPoints =
                    controller.GetMapPoints(GameAreaId.MountainRoad);
                Assert.That(
                    mountainPoints.Count(
                        point => point.Kind ==
                                 CityMapPointKind.GroundSquare),
                    Is.EqualTo(lattice.Squares.Count));
                AssertUniqueFinitePoints(
                    mountainPoints,
                    GameAreaId.MountainRoad);
                Assert.That(
                    controller.GetMapPoints(GameAreaId.City).Any(
                        point => point.Kind ==
                                 CityMapPointKind.GroundSquare),
                    Is.False,
                    "The other tab charts a scene that is not loaded; " +
                    "reaching it is a transition, not a teleport.");

                // The pointer lands on ground, and the ground answers.
                var plateauCenter = new Vector2(
                    plan.Plateau.Center.x,
                    plan.Plateau.Center.z);
                Assert.That(
                    controller.TryGetTeleportSquarePointIndex(
                        plateauCenter,
                        out int pointIndex),
                    Is.True);
                Assert.That(
                    controller.SelectMapPoint(pointIndex),
                    Is.True);
                Assert.That(
                    controller.TryGetSelectedMapPoint(
                        out CityMapPointDescriptor selected,
                        out Vector3 worldPosition),
                    Is.True);
                Assert.That(
                    selected.Kind,
                    Is.EqualTo(CityMapPointKind.GroundSquare));
                Assert.That(
                    worldPosition.y,
                    Is.EqualTo(
                        plan.Plateau.Center.y +
                        PlayerFactory.GroundedRootOffset).Within(0.01f),
                    "Clamped against the city mask this would have been " +
                    "sampled off a street lying under the mountain.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        /// <summary>
        /// In the city the lattice is the city's own cell grid, so a square
        /// never cuts a block in half - and because the carriageway runs
        /// along the SEAM between two squares rather than through the middle
        /// of one, a square is answered from its edges as well as its
        /// middle. Without that every street would be off the chart and
        /// every block square would arrive inside its own building.
        /// </summary>
        [Test]
        public void CityTeleportLattice_TurnsTheStreetsThemselvesIntoPlaces()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                58021);
            var ground = new CityMapCityTeleportGround(layout);
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            Assert.That(church, Is.Not.Null);
            Assert.That(
                ground.TryResolveStandingPosition(
                    church.ModelFootprint.center,
                    out _),
                Is.False,
                "The church occupies special open ground, so its model " +
                "footprint must be excluded explicitly from arrivals.");

            CityMapTeleportLattice lattice =
                CityMapTeleportLatticeBuilder.Create(
                    layout.MapWorldXZBounds,
                    new Vector2(layout.WorldOrigin.x, layout.WorldOrigin.z),
                    Mathf.Min(
                        layout.NodeSpacing.x,
                        layout.NodeSpacing.y),
                    ground);

            Assert.That(lattice.Area, Is.EqualTo(GameAreaId.City));
            Assert.That(lattice.Squares.Count, Is.GreaterThan(20));
            for (int index = 0; index < lattice.Squares.Count; index++)
            {
                CityMapTeleportSquare square = lattice.Squares[index];
                var landing = new Vector2(
                    square.StandingPosition.x,
                    square.StandingPosition.z);
                Assert.That(
                    square.WorldBounds.Contains(landing),
                    Is.True,
                    $"Square {square.Cell} lands outside itself.");
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (!lot.HasBuilding)
                    {
                        continue;
                    }

                    Assert.That(
                        Mathf.Abs(landing.x - lot.Center.x) <
                        lot.Size.x * 0.5f &&
                        Mathf.Abs(landing.y - lot.Center.z) <
                        lot.Size.y * 0.5f,
                        Is.False,
                        $"Square {square.Cell} arrives inside the building " +
                        $"on {lot.Cell}. The walkable mask allows it - the " +
                        "ground under a block is walkable and the building " +
                        "is a collider standing on it - and it is still " +
                        "not a place to be put down.");
                }
            }

            // A square exists for the middle of a real street, which is the
            // part of the city no marker ever covered.
            RoadEdge edge = layout.RoadEdges[layout.RoadEdges.Count / 2];
            Vector3 midpoint = (layout.GetNodeWorldPosition(edge.A) +
                                layout.GetNodeWorldPosition(edge.B)) * 0.5f;
            Assert.That(
                lattice.TryGetSquareIndexAt(
                    new Vector2(midpoint.x, midpoint.z),
                    out _),
                Is.True,
                "The street between two junctions has no square.");
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
