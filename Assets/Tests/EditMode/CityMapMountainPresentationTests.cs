using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapMountainPresentationTests
    {
        [Test]
        [Category("CityMountain")]
        public void DefaultCoastal_MapsVisibleRiverCaveWithoutHiddenThroat()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                58021);
            CityMountainBoundaryPlan plan =
                CityMountainBoundaryPlanner.Create(layout);
            var host = new GameObject("City Map Mountain Test");

            try
            {
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                controller.Initialize(
                    layout,
                    default,
                    null,
                    null,
                    null,
                    null,
                    plan);

                Assert.That(
                    controller.MountainBoundaryPlan,
                    Is.SameAs(plan));
                Assert.That(plan.IsEnabled, Is.True);
                Assert.That(plan.HasRiverCave, Is.True);
                Assert.That(plan.HasTunnel, Is.True);
                Assert.That(plan.Tunnel.IsSealed, Is.True);

                Rect layoutBounds = layout.MapWorldXZBounds;
                Rect displayBounds = controller.DisplayWorldXZBounds;
                CityMountainRiverNotchDescriptor cave = plan.RiverCave;
                float expectedMinimumX = layoutBounds.xMin;
                float expectedMinimumZ = layoutBounds.yMin;
                for (int index = 0; index < plan.Ridges.Count; index++)
                {
                    Rect ridgeBounds = plan.Ridges[index].XZBounds;
                    expectedMinimumX = Mathf.Min(
                        expectedMinimumX,
                        ridgeBounds.xMin - 2f);
                    expectedMinimumZ = Mathf.Min(
                        expectedMinimumZ,
                        ridgeBounds.yMin - 2f);
                }
                expectedMinimumX = Mathf.Min(
                    expectedMinimumX,
                    cave.ApproachBounds.xMin - 2f);
                expectedMinimumZ = Mathf.Min(
                    expectedMinimumZ,
                    cave.ApproachBounds.yMin - 2f);
                Rect throat =
                    CityMapView.CreateMountainTunnelThroatBounds(
                        plan.Tunnel);
                expectedMinimumX = Mathf.Min(
                    expectedMinimumX,
                    throat.xMin - 2f);
                expectedMinimumZ = Mathf.Min(
                    expectedMinimumZ,
                    throat.yMin - 2f);

                Assert.That(
                    displayBounds.xMin,
                    Is.EqualTo(expectedMinimumX).Within(0.001f));
                Assert.That(
                    displayBounds.yMin,
                    Is.EqualTo(expectedMinimumZ).Within(0.001f));
                Assert.That(
                    displayBounds.xMin,
                    Is.LessThanOrEqualTo(layoutBounds.xMin));
                Assert.That(
                    displayBounds.yMin,
                    Is.LessThanOrEqualTo(layoutBounds.yMin));
                Assert.That(
                    displayBounds.xMin,
                    Is.LessThanOrEqualTo(cave.ApproachBounds.xMin),
                    "The visible river-cave approach must fit inside the " +
                    "western map extent.");
                Assert.That(
                    displayBounds.yMin,
                    Is.LessThanOrEqualTo(cave.ApproachBounds.yMin),
                    "The visible river-cave approach must fit inside the " +
                    "southern map extent.");
                Assert.That(
                    displayBounds.xMax,
                    Is.GreaterThanOrEqualTo(cave.ApproachBounds.xMax));
                Assert.That(
                    displayBounds.yMax,
                    Is.GreaterThanOrEqualTo(cave.ApproachBounds.yMax));
                Assert.That(
                    displayBounds.yMin,
                    Is.GreaterThan(cave.ThroatWaterBounds.yMin),
                    "The hidden cave throat must not expand the map to its " +
                    "unseen southern end.");
                Assert.That(
                    displayBounds.xMin,
                    Is.LessThanOrEqualTo(throat.xMin),
                    "The tunnel throat must fit inside the western map " +
                    "extent.");
                Assert.That(
                    displayBounds.yMin,
                    Is.LessThanOrEqualTo(throat.yMin),
                    "The tunnel throat must fit inside the southern map " +
                    "extent.");
                Assert.That(
                    displayBounds.xMax,
                    Is.EqualTo(layoutBounds.xMax).Within(0.001f),
                    "Mountain presentation must not expand the east edge.");
                Assert.That(
                    displayBounds.yMax,
                    Is.EqualTo(layoutBounds.yMax).Within(0.001f),
                    "Mountain presentation must not expand the north edge.");

                Vector3 gateCenter = plan.Tunnel.PortalGroundCenter +
                                     plan.Tunnel.Axis *
                                     plan.Tunnel.GateInset;
                Assert.That(
                    throat.width,
                    Is.EqualTo(plan.Tunnel.OpeningWidth).Within(0.001f));
                Assert.That(
                    throat.height,
                    Is.EqualTo(plan.Tunnel.ThroatDepth).Within(0.001f));
                Assert.That(
                    throat.Contains(
                        new Vector2(gateCenter.x, gateCenter.z)),
                    Is.True,
                    "The sealed map crossbar must remain inside the throat.");

                Rect waterApproach = cave.WaterApproachBounds;
                Rect cityChannel = layout.River.Segments[0].WaterBounds;
                Assert.That(
                    waterApproach.width,
                    Is.EqualTo(cityChannel.width).Within(0.001f),
                    "The mapped approach must keep the authored channel " +
                    "width.");
                Assert.That(
                    waterApproach.width,
                    Is.LessThan(cave.ApproachBounds.width),
                    "Only the narrow water channel, not the full approach, " +
                    "is represented as water.");
                Assert.That(
                    waterApproach.xMin,
                    Is.GreaterThanOrEqualTo(cave.ApproachBounds.xMin));
                Assert.That(
                    waterApproach.xMax,
                    Is.LessThanOrEqualTo(cave.ApproachBounds.xMax));
                Assert.That(
                    waterApproach.yMin,
                    Is.GreaterThanOrEqualTo(cave.ApproachBounds.yMin));
                Assert.That(
                    waterApproach.yMax,
                    Is.LessThanOrEqualTo(cave.ApproachBounds.yMax));

                Assert.That(cave.WestBankBounds.width, Is.GreaterThan(0f));
                Assert.That(cave.EastBankBounds.width, Is.GreaterThan(0f));
                Assert.That(
                    cave.WestBankBounds.xMin,
                    Is.GreaterThanOrEqualTo(cave.ApproachBounds.xMin));
                Assert.That(
                    cave.WestBankBounds.xMax,
                    Is.EqualTo(waterApproach.xMin).Within(0.001f));
                Assert.That(
                    cave.EastBankBounds.xMin,
                    Is.EqualTo(waterApproach.xMax).Within(0.001f));
                Assert.That(
                    cave.EastBankBounds.xMax,
                    Is.LessThanOrEqualTo(cave.ApproachBounds.xMax));
                Assert.That(
                    cave.WestBankBounds.Overlaps(waterApproach),
                    Is.False,
                    "The west bank must remain a separate map surface.");
                Assert.That(
                    cave.EastBankBounds.Overlaps(waterApproach),
                    Is.False,
                    "The east bank must remain a separate map surface.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("CityMountain")]
        public void TunnelMarker_RemainsLegibleAndInsideViewport()
        {
            Rect viewport = new Rect(0f, 0f, 120f, 90f);
            Rect clamped = CityMapView.CreateMountainTunnelMarkerRect(
                new Vector2(-100f, 200f),
                viewport);

            Assert.That(clamped.width, Is.EqualTo(19f).Within(0.001f));
            Assert.That(clamped.height, Is.EqualTo(17f).Within(0.001f));
            Assert.That(clamped.xMin, Is.GreaterThanOrEqualTo(viewport.xMin));
            Assert.That(clamped.yMin, Is.GreaterThanOrEqualTo(viewport.yMin));
            Assert.That(clamped.xMax, Is.LessThanOrEqualTo(viewport.xMax));
            Assert.That(clamped.yMax, Is.LessThanOrEqualTo(viewport.yMax));

            var visibleCenter = new Vector2(48f, 36f);
            Rect centered = CityMapView.CreateMountainTunnelMarkerRect(
                visibleCenter,
                viewport);

            Assert.That(centered.width, Is.EqualTo(19f).Within(0.001f));
            Assert.That(centered.height, Is.EqualTo(17f).Within(0.001f));
            Assert.That(centered.center.x, Is.EqualTo(visibleCenter.x)
                .Within(0.001f));
            Assert.That(centered.center.y, Is.EqualTo(visibleCenter.y)
                .Within(0.001f));
        }

        [Test]
        [Category("CityMountain")]
        public void EmptyMountainPlan_KeepsOriginalMapBounds()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                58021);
            var host = new GameObject("City Map Empty Mountain Test");

            try
            {
                CityMapController controller =
                    host.AddComponent<CityMapController>();
                controller.Initialize(layout, default, null, null);

                Assert.That(
                    controller.MountainBoundaryPlan,
                    Is.SameAs(CityMountainBoundaryPlan.Empty));
                Assert.That(
                    controller.DisplayWorldXZBounds,
                    Is.EqualTo(layout.MapWorldXZBounds));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
