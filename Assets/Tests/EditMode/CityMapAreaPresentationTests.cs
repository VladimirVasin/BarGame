using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapAreaPresentationTests
    {
        [Test]
        public void MountainRoadOverlay_OwnsLongRouteAndTwoHairpins()
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
        public void MountainRoadOverlay_FromPlanOwnsTerminalLandmarks()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(58021);

            CityMapMountainRoadOverlay overlay =
                CityMapMountainRoadOverlayBuilder.Create(plan);

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
    }
}
