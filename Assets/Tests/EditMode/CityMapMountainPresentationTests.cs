using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapMountainPresentationTests
    {
        [Test]
        [Category("CityMountain")]
        public void DefaultCoastal_ExpandsOnlyWestAndSouthForMountainMap()
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
                Assert.That(plan.HasRiverNotch, Is.True);
                Assert.That(plan.HasTunnel, Is.True);
                Assert.That(plan.Tunnel.IsSealed, Is.True);

                Rect layoutBounds = layout.MapWorldXZBounds;
                Rect displayBounds = controller.DisplayWorldXZBounds;
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
                    plan.RiverNotch.OpeningBounds.xMin - 2f);
                expectedMinimumZ = Mathf.Min(
                    expectedMinimumZ,
                    plan.RiverNotch.OpeningBounds.yMin - 2f);
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
                bool openingsAreSeparate =
                    plan.RiverNotch.OpeningBounds.xMax <=
                    plan.Tunnel.PortalBounds.xMin ||
                    plan.RiverNotch.OpeningBounds.xMin >=
                    plan.Tunnel.PortalBounds.xMax;
                Assert.That(
                    openingsAreSeparate,
                    Is.True,
                    "The river continuation and sealed tunnel must stay " +
                    "separate map openings.");
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
