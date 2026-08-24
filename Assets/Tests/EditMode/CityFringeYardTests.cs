using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFringeYardTests
    {
        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_PlansFiveDeterministicFringesAndOpenForecourt()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan first =
                CityFringeYardPlanner.Create(layout, mountains);
            CityFringeYardPlan second =
                CityFringeYardPlanner.Create(layout, mountains);

            Assert.That(first.IsEnabled, Is.True);
            Assert.That(
                first.Yards,
                Has.Count.EqualTo(CityFringeYardPlanner.ExpectedYardCount));
            Assert.That(
                first.PartCount,
                Is.InRange(300, CityFringeYardPlanner.MaximumPartCount));
            Assert.That(first.HasTunnelForecourt, Is.True);
            Assert.That(
                first.Practicals,
                Has.Count.EqualTo(
                    CityFringeYardPracticalValidator.ExpectedPracticalCount));
            Assert.That(first.Practicals, Is.EqualTo(second.Practicals));
            Assert.That(
                first.Practicals.Select(item => item.AreaId),
                Is.EquivalentTo(new[]
                {
                    CityMountainBoundaryDefinition.WestNorthAreaId,
                    CityMountainBoundaryDefinition.WestSouthAreaId,
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    CityMountainBoundaryDefinition.SouthEastAreaId
                }));
            Assert.That(
                first.Practicals.Any(item =>
                    item.YardKind == CityFringeYardKind.EastUtilityEdge),
                Is.False);
            Assert.That(
                first.TunnelForecourt,
                Is.EqualTo(second.TunnelForecourt));
            Assert.That(
                first.TunnelForecourt.TunnelStableId,
                Is.EqualTo(mountains.Tunnel.StableId));
            Assert.That(first.TunnelForecourt.HasPhysicalGate, Is.False);
            Assert.That(mountains.Tunnel.HasPhysicalGate, Is.False);
            Assert.That(mountains.Tunnel.TravelAvailable, Is.False);
            Assert.That(
                first.TunnelForecourt.ApproachWidth,
                Is.EqualTo(6.9f).Within(0.01f));
            Assert.That(
                first.TunnelForecourt.DriveClearWidth,
                Is.GreaterThanOrEqualTo(6f));

            string[] expectedAreas =
            {
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                CityMountainBoundaryDefinition.SouthEastAreaId,
                "yard-east"
            };
            CityFringeYardKind[] expectedKinds =
            {
                CityFringeYardKind.WestStoneTerraces,
                CityFringeYardKind.WestIndustrialBelt,
                CityFringeYardKind.SouthTunnelForecourt,
                CityFringeYardKind.SouthFloodWorks,
                CityFringeYardKind.EastUtilityEdge
            };
            Assert.That(
                first.Yards.Select(item => item.AreaId),
                Is.EqualTo(expectedAreas));
            Assert.That(
                first.Yards.Select(item => item.Kind),
                Is.EqualTo(expectedKinds));
            Assert.That(
                first.Yards.Select(item => item.Access.Id).Distinct().Count(),
                Is.EqualTo(expectedAreas.Length));
            Assert.That(
                mountains.Ridges.Any(item => item.SourceAreaId == "yard-east"),
                Is.False,
                "The eastern utility edge must not acquire a mountain.");

            for (int yardIndex = 0;
                 yardIndex < first.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor left = first.Yards[yardIndex];
                CityFringeYardDescriptor right = second.Yards[yardIndex];
                Assert.That(right.StableId, Is.EqualTo(left.StableId));
                Assert.That(right.AreaBounds, Is.EqualTo(left.AreaBounds));
                Assert.That(right.Access, Is.EqualTo(left.Access));
                Assert.That(
                    right.TraversalBounds,
                    Is.EqualTo(left.TraversalBounds));
                Assert.That(
                    right.Parts,
                    Is.EqualTo(left.Parts));
                AssertForefieldCoverage(left);
                AssertPoleSpacing(left);
                foreach (CityFringeYardPartDescriptor part in left.Parts)
                {
                    Assert.That(
                        Contains(left.AreaBounds, part.Footprint),
                        Is.True,
                        part.StableId);
                    if (part.BlocksMovement)
                    {
                        Assert.That(
                            part.Footprint.Overlaps(
                                left.Access.ApproachBounds),
                            Is.False,
                            part.StableId);
                        Assert.That(
                            part.Footprint.Overlaps(left.TraversalBounds),
                            Is.False,
                            part.StableId);
                    }

                    if (mountains.HasRiverNotch)
                    {
                        Assert.That(
                            part.Footprint.Overlaps(
                                mountains.RiverNotch.OpeningBounds),
                            Is.False,
                            part.StableId);
                    }
                }
            }

            AssertVocabulary(first);
            CityFringeYardPracticalDescriptor tunnelPractical =
                first.Practicals.Single(item =>
                    item.Kind ==
                    CityFringeYardPracticalKind.TunnelReturnLamp);
            Vector3 expectedTunnelLightPosition =
                CityTunnelPortalLightGeometry.ResolvePosition(
                    first.TunnelForecourt);
            Vector3 expectedTunnelLightForward =
                CityTunnelPortalLightGeometry.ResolveForward(
                    first.TunnelForecourt);
            Assert.That(
                Vector3.Distance(
                    tunnelPractical.Position,
                    expectedTunnelLightPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    tunnelPractical.Forward,
                    expectedTunnelLightForward),
                Is.LessThan(0.01f));
            Assert.That(
                Vector3.Dot(
                    tunnelPractical.Forward,
                    first.TunnelForecourt.Axis),
                Is.GreaterThan(0.60f));
            Assert.DoesNotThrow(() =>
                CityFringeYardValidator.ValidateOrThrow(
                    layout,
                    mountains,
                    first));

            var host = new GameObject("Fringe Yard Test Host");
            try
            {
                CityFringeYardWorldResult result =
                    CityFringeYardWorldBuilder.Build(
                    host.transform,
                    first);
                GameObject root = result.Root;
                Assert.That(root, Is.Not.Null);
                Assert.That(
                    result.PracticalAnchors,
                    Has.Count.EqualTo(
                        CityFringeYardPracticalValidator
                            .ExpectedPracticalCount));
                MeshRenderer[] renderers =
                    root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(
                    renderers.Length,
                    Is.InRange(12, 120),
                    "The 48-metre style/collision batches must stay bounded.");
                Assert.That(
                    renderers.Count(item =>
                        item.name == "Practical Emissive Lens"),
                    Is.EqualTo(4));
                foreach (MeshRenderer lens in renderers.Where(item =>
                             item.name == "Practical Emissive Lens"))
                {
                    Assert.That(
                        lens.transform.localPosition,
                        Is.EqualTo(
                            Vector3.forward *
                            CityFringeYardWorldBuilder
                                .PracticalLensForwardOffset));
                }
                Assert.That(
                    root.GetComponentsInChildren<MeshCollider>(true).Length,
                    Is.GreaterThan(0));
                Assert.That(
                    root.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    root.GetComponentsInChildren<MonoBehaviour>(true)
                        .Any(item =>
                            item is IInteractable ||
                            item is SceneTransitionService),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_CellFiveMinusOneUsesNarrowTunnelTraces()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringe =
                CityFringeYardPlanner.Create(layout, mountains);
            CitySurfaceDescriptor cell = layout.Surfaces.Single(surface =>
                surface.Cell == new Vector2Int(5, -1) &&
                surface.AreaId ==
                    CityMountainBoundaryDefinition.SouthWestAreaId);
            CityFringeYardDescriptor yard = fringe.Yards.Single(item =>
                item.AreaId ==
                    CityMountainBoundaryDefinition.SouthWestAreaId);

            Assert.That(
                yard.Kind,
                Is.EqualTo(CityFringeYardKind.SouthTunnelForecourt));
            Assert.That(
                fringe.TunnelForecourt.ApproachWidth,
                Is.EqualTo(6.9f).Within(0.01f));
            Assert.That(
                fringe.TunnelForecourt.DriveClearWidth,
                Is.GreaterThanOrEqualTo(
                    CityFringeYardPlanner.MinimumTunnelDriveClearWidth));
            Assert.That(
                yard.TraversalBounds,
                Is.EqualTo(fringe.TunnelForecourt.DriveClearBounds));
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.RepairPad ||
                    part.Kind == CityFringeYardPartKind.RepairStack),
                Is.False,
                "The tunnel forecourt must not inherit a repair pocket.");

            CityFringeYardPartDescriptor[] surfaceTraces = yard.Parts.Where(
                    part =>
                        !part.BlocksMovement &&
                        (part.Kind == CityFringeYardPartKind.ServiceTrack ||
                         part.Kind == CityFringeYardPartKind.AccessApron ||
                         part.Kind == CityFringeYardPartKind.ServiceSpur))
                .ToArray();
            Assert.That(surfaceTraces, Is.Not.Empty);
            foreach (CityFringeYardPartKind kind in new[]
                     {
                         CityFringeYardPartKind.ServiceTrack,
                         CityFringeYardPartKind.AccessApron,
                         CityFringeYardPartKind.ServiceSpur
                     })
            {
                Assert.That(
                    surfaceTraces.Any(part => part.Kind == kind),
                    Is.True,
                    $"The tunnel forecourt lacks its {kind} trace.");
            }

            foreach (CityFringeYardPartDescriptor trace in surfaceTraces)
            {
                Assert.That(
                    trace.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumTunnelTraceWidth),
                    $"{trace.StableId} reads as a floating slab.");
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    trace,
                    0.02f,
                    0.11f);
            }
            Assert.That(
                surfaceTraces.Any(part => part.StableId.Contains(
                    "service-track-roadward")),
                Is.True,
                "The roadward maintenance trace disappeared.");
            Assert.That(
                surfaceTraces.Any(part => part.StableId.Contains(
                    "service-track-toeward")),
                Is.True,
                "The toe-side maintenance trace disappeared.");

            CityFringeYardPartDescriptor[] wheelRuts = yard.Parts.Where(
                    part =>
                        part.Kind == CityFringeYardPartKind.WheelRut &&
                        part.Footprint.Overlaps(cell.WorldBounds))
                .ToArray();
            Assert.That(wheelRuts, Is.Not.Empty);
            foreach (CityFringeYardPartDescriptor wheelRut in wheelRuts)
            {
                Assert.That(wheelRut.BlocksMovement, Is.False);
                Assert.That(
                    wheelRut.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumTunnelTraceWidth));
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    wheelRut,
                    0.02f,
                    0.11f);
            }
            CityFringeYardPartDescriptor[] approachWheelRuts = wheelRuts
                .Where(part => part.StableId.Contains(
                    $"{yard.AreaId}-tunnel-wheel-rut-"))
                .ToArray();
            Assert.That(
                approachWheelRuts.All(part =>
                    part.Footprint.Overlaps(yard.TraversalBounds)),
                Is.True,
                "Approach wheel ruts must remain inside the logical route.");
            Assert.That(
                approachWheelRuts.Any(part => part.StableId.Contains(
                    "tunnel-wheel-rut--1-")),
                Is.True,
                "The left tunnel wheel rut disappeared.");
            Assert.That(
                approachWheelRuts.Any(part => part.StableId.Contains(
                    "tunnel-wheel-rut-1-")),
                Is.True,
                "The right tunnel wheel rut disappeared.");
            foreach (string stableStem in new[]
                     {
                         "tunnel-approach-",
                         "tunnel-wheel-rut--1-",
                         "tunnel-wheel-rut-1-"
                     })
            {
                AssertSegmentsMeetWithoutOverlap(
                    yard.Parts.Where(part => part.StableId.Contains(
                        $"{yard.AreaId}-{stableStem}")),
                    fringe.TunnelForecourt.Axis);
            }

            CityFringeYardPartDescriptor[] competingSurfaceTraces =
                yard.Parts.Where(part =>
                        part.Footprint.Overlaps(
                            fringe.TunnelForecourt.DriveClearBounds) &&
                        (part.Kind ==
                             CityFringeYardPartKind.RoadShoulder ||
                         part.Kind ==
                             CityFringeYardPartKind.ServiceTrack ||
                         part.Kind ==
                             CityFringeYardPartKind.ServiceSpur ||
                         part.Kind ==
                             CityFringeYardPartKind.ForefieldAnchor ||
                         (part.Kind == CityFringeYardPartKind.WheelRut &&
                          !part.StableId.Contains(
                              "-tunnel-wheel-rut-"))))
                    .ToArray();
            Assert.That(
                competingSurfaceTraces,
                Is.Empty,
                "Generic forefield traces overlap the authored tunnel lane.");

            CityFringeYardPartDescriptor[] freightAnchors = yard.Parts
                .Where(part =>
                    part.Kind == CityFringeYardPartKind.ForefieldAnchor)
                .ToArray();
            Assert.That(
                freightAnchors.Length,
                Is.InRange(
                    CityFringeYardPlanner.MinimumForefieldAnchorCount,
                    CityFringeYardPlanner.MaximumForefieldAnchorCount));
            foreach (CityFringeYardPartDescriptor anchor in freightAnchors)
            {
                Assert.That(anchor.BlocksMovement, Is.False, anchor.StableId);
                Assert.That(
                    anchor.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumTunnelTraceWidth),
                    $"{anchor.StableId} reads as a freight slab.");
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    anchor,
                    0.02f,
                    0.11f);
            }

            float[] expectedReturnWidths = { 1.10f, 1.20f, 1.35f };
            float[] expectedReturnHeights = { 0.80f, 1.55f, 2.35f };
            var tunnelReturns = new List<CityFringeYardPartDescriptor>(6);
            foreach (int side in new[] { -1, 1 })
            {
                float previousEnd = float.NegativeInfinity;
                float previousHeight = 0f;
                for (int index = 0; index < 3; index++)
                {
                    CityFringeYardPartDescriptor section =
                        yard.Parts.Single(part => part.StableId ==
                            $"{yard.AreaId}-tunnel-return-{side}-{index:00}");
                    tunnelReturns.Add(section);
                    Assert.That(
                        section.Kind,
                        Is.EqualTo(CityFringeYardPartKind.TunnelCheek));
                    Assert.That(
                        section.Style,
                        Is.EqualTo(CityFringeYardStyle.Concrete));
                    Assert.That(section.BlocksMovement, Is.True);
                    Assert.That(
                        section.Size.x,
                        Is.EqualTo(expectedReturnWidths[index])
                            .Within(0.001f));
                    Assert.That(
                        section.Size.y,
                        Is.EqualTo(expectedReturnHeights[index])
                            .Within(0.001f));
                    Assert.That(
                        section.Size.x,
                        Is.GreaterThanOrEqualTo(1.10f));
                    Assert.That(
                        section.Size.y,
                        Is.GreaterThan(previousHeight),
                        "Tunnel returns must rise toward the portal.");
                    previousHeight = section.Size.y;
                    Assert.That(
                        Expanded(section.Footprint, 0.20f).Overlaps(
                            fringe.TunnelForecourt.DriveClearBounds),
                        Is.False,
                        $"{section.StableId} pinches the exact drive clear.");
                    AssertBottomCornersSeated(
                        layout,
                        yard,
                        section,
                        0.001f,
                        0.85f);

                    float startAlong = Vector3.Dot(
                        section.Center,
                        fringe.TunnelForecourt.Axis) -
                        section.Size.z * 0.5f;
                    float endAlong = startAlong + section.Size.z;
                    if (index > 0)
                    {
                        Assert.That(
                            startAlong,
                            Is.EqualTo(previousEnd).Within(0.001f),
                            "Portal returns must meet without coplanar " +
                            "overlap or a visible gap.");
                    }

                    previousEnd = endAlong;
                    CityFringeYardPartDescriptor cap = yard.Parts.Single(
                        part => part.StableId ==
                            $"{yard.AreaId}-tunnel-return-cap-" +
                            $"{side}-{index:00}");
                    Assert.That(
                        cap.Kind,
                        Is.EqualTo(CityFringeYardPartKind.RepairFrame));
                    Assert.That(
                        cap.Style,
                        Is.EqualTo(CityFringeYardStyle.Iron));
                    Assert.That(cap.BlocksMovement, Is.False);
                    Assert.That(
                        Contains(section.Footprint, cap.Footprint),
                        Is.True,
                        $"{cap.StableId} leaves its concrete support.");
                    Assert.That(
                        cap.Center.y - cap.Size.y * 0.5f,
                        Is.EqualTo(
                                section.Center.y + section.Size.y * 0.5f)
                            .Within(0.001f),
                        $"{cap.StableId} floats above its return.");
                }
            }

            Assert.That(tunnelReturns, Has.Count.EqualTo(6));
            Assert.That(
                yard.Parts.Count(part =>
                    part.Kind == CityFringeYardPartKind.TunnelCheek),
                Is.EqualTo(6),
                "The portal must have exactly three returns per side.");
            Assert.That(
                yard.Parts.Count(part => part.StableId.StartsWith(
                    $"{yard.AreaId}-tunnel-return-cap-")),
                Is.EqualTo(6));
            float returnStart = tunnelReturns.Min(part =>
                Vector3.Dot(part.Center, fringe.TunnelForecourt.Axis) -
                part.Size.z * 0.5f);
            float returnEnd = tunnelReturns.Max(part =>
                Vector3.Dot(part.Center, fringe.TunnelForecourt.Axis) +
                part.Size.z * 0.5f);
            Assert.That(
                returnEnd - returnStart,
                Is.GreaterThanOrEqualTo(8f),
                "The portal works lost their legible longitudinal mass.");
            Assert.That(
                yard.Parts.Any(part =>
                    part.StableId.Contains("landmark-side-stock")),
                Is.False);
            Assert.That(
                yard.Parts.Any(part =>
                    part.StableId.Contains("landmark-return-light-mast")),
                Is.False);
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.PipeStock),
                Is.False,
                "SouthTunnel must not inherit unsupported pipe stock.");

            CityFringeYardPartDescriptor[] framePosts = yard.Parts.Where(
                    part => part.StableId.StartsWith(
                        $"{yard.AreaId}-landmark-return-service-post-"))
                .OrderBy(part => Vector3.Dot(
                    part.Center,
                    fringe.TunnelForecourt.Axis))
                .ToArray();
            Assert.That(framePosts, Has.Length.EqualTo(2));
            foreach (CityFringeYardPartDescriptor post in framePosts)
            {
                Assert.That(
                    post.Kind,
                    Is.EqualTo(CityFringeYardPartKind.RepairFrame));
                Assert.That(post.Style, Is.EqualTo(CityFringeYardStyle.Iron));
                Assert.That(post.BlocksMovement, Is.True);
                Assert.That(post.Size.y, Is.GreaterThanOrEqualTo(4.2f));
                Assert.That(
                    Expanded(post.Footprint, 0.20f).Overlaps(
                        fringe.TunnelForecourt.DriveClearBounds),
                    Is.False,
                    $"{post.StableId} enters the traversal margin.");
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    post,
                    0.001f,
                    0.18f);
            }

            CityFringeYardPartDescriptor serviceBeam = yard.Parts.Single(
                part => part.StableId ==
                    $"{yard.AreaId}-landmark-return-service-beam");
            CityFringeYardPartDescriptor serviceBrace = yard.Parts.Single(
                part => part.StableId ==
                    $"{yard.AreaId}-landmark-return-service-brace");
            CityFringeYardPartDescriptor housing = yard.Parts.Single(
                part => part.StableId ==
                    $"{yard.AreaId}-landmark-practical-housing");
            foreach (CityFringeYardPartDescriptor member in new[]
                     {
                         serviceBeam,
                         serviceBrace
                     })
            {
                Assert.That(
                    member.Kind,
                    Is.EqualTo(CityFringeYardPartKind.RepairFrame));
                Assert.That(
                    member.Style,
                    Is.EqualTo(CityFringeYardStyle.Iron));
                Assert.That(member.BlocksMovement, Is.False);
                Assert.That(
                    member.Footprint.Overlaps(
                        fringe.TunnelForecourt.DriveClearBounds),
                    Is.False,
                    $"{member.StableId} enters the traversal.");
            }

            AssertMemberEndpointsSupported(serviceBeam, framePosts, true);
            AssertMemberEndpointsSupported(serviceBrace, framePosts, false);
            CityFringeYardPracticalDescriptor tunnelPractical =
                fringe.Practicals.Single(practical =>
                    practical.AreaId == yard.AreaId);
            Assert.That(
                housing.Center,
                Is.EqualTo(
                    CityTunnelPortalLightGeometry.ResolvePosition(
                        fringe.TunnelForecourt)));
            Assert.That(
                housing.Center,
                Is.EqualTo(tunnelPractical.Position),
                "The emissive lens left its portal housing.");
            Assert.That(
                housing.Size,
                Is.EqualTo(CityTunnelPortalLightGeometry.HousingSize));
            float housingFront = housing.Size.z * 0.5f;
            float lensBack =
                CityFringeYardWorldBuilder.PracticalLensForwardOffset -
                tunnelPractical.LensSize.z * 0.5f;
            Assert.That(
                lensBack - housingFront,
                Is.GreaterThanOrEqualTo(0.015f),
                "The emissive lens is buried inside the portal housing.");
            Assert.That(
                housing.Footprint.Overlaps(
                    fringe.TunnelForecourt.DriveClearBounds),
                Is.True,
                "The floodlight is no longer centred over the portal lane.");
            float housingGround = SampleOwnerTop(
                layout,
                yard,
                housing.Center);
            Assert.That(
                housing.Center.y - housingGround,
                Is.GreaterThan(5.5f),
                "The portal floodlight dropped into traversal clearance.");
            Assert.That(
                housing.Kind,
                Is.EqualTo(CityFringeYardPartKind.PracticalHousing));
            Assert.That(housing.BlocksMovement, Is.False);
            Assert.That(
                Vector3.Dot(
                    tunnelPractical.Forward,
                    fringe.TunnelForecourt.Axis),
                Is.GreaterThan(0.60f),
                "The portal floodlight no longer aims into the throat.");
            Assert.That(
                tunnelPractical.Forward.y,
                Is.LessThan(-0.70f),
                "The portal floodlight no longer washes the tunnel floor.");
            float floorRayDistance =
                (fringe.TunnelForecourt.PortalAnchor.y -
                 tunnelPractical.Position.y) /
                tunnelPractical.Forward.y;
            float floorAxisDistance = floorRayDistance * Vector3.Dot(
                tunnelPractical.Forward,
                fringe.TunnelForecourt.Axis);
            Assert.That(
                floorAxisDistance,
                Is.InRange(2f, mountains.Tunnel.WalkableDepth),
                "The floodlight misses the open entrance floor.");

            CityFringeYardPartDescriptor[] drainCovers = yard.Parts.Where(
                    part => part.StableId.StartsWith(
                        $"{yard.AreaId}-drain-cover-"))
                .ToArray();
            Assert.That(drainCovers, Has.Length.EqualTo(5));
            foreach (CityFringeYardPartDescriptor cover in drainCovers)
            {
                float ground = SampleOwnerTop(layout, yard, cover.Center);
                float bottomOffset = cover.Center.y -
                                     cover.Size.y * 0.5f - ground;
                Assert.That(
                    bottomOffset,
                    Is.InRange(-0.02f, 0.001f),
                    $"{cover.StableId} floats above its drain seat.");
            }
        }

        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_CellSevenMinusOneSeatsForefieldAndBlocksMasses()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringe =
                CityFringeYardPlanner.Create(layout, mountains);
            CitySurfaceDescriptor cell = layout.Surfaces.Single(surface =>
                surface.Cell == new Vector2Int(7, -1) &&
                surface.AreaId ==
                    CityMountainBoundaryDefinition.SouthEastAreaId);
            CityFringeYardDescriptor yard = fringe.Yards.Single(item =>
                item.AreaId ==
                    CityMountainBoundaryDefinition.SouthEastAreaId);

            Assert.That(
                yard.Kind,
                Is.EqualTo(CityFringeYardKind.SouthFloodWorks));
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.ServiceTrack),
                Is.False,
                "The south-east toe must not rebuild the broad earth slab.");
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.RepairPad ||
                    part.Kind == CityFringeYardPartKind.SiltFan),
                Is.False,
                "Floodworks must not rebuild a broad decorative platform.");
            CityFringeYardPartDescriptor[] floodSurfaceTraces = yard.Parts
                .Where(part =>
                    !part.BlocksMovement &&
                    (part.Kind == CityFringeYardPartKind.AccessApron ||
                     part.Kind == CityFringeYardPartKind.RoadShoulder ||
                     part.Kind == CityFringeYardPartKind.ServiceSpur ||
                     part.Kind == CityFringeYardPartKind.ForefieldAnchor ||
                     part.Kind == CityFringeYardPartKind.DrainChannel))
                .ToArray();
            Assert.That(floodSurfaceTraces, Is.Not.Empty);
            foreach (CityFringeYardPartDescriptor trace in floodSurfaceTraces)
            {
                Assert.That(
                    trace.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumRoadShoulderWidth),
                    $"{trace.StableId} reads as a broad surface platform.");
            }

            CityFringeYardPartDescriptor[] floodAccess = yard.Parts.Where(
                    part => part.Kind ==
                            CityFringeYardPartKind.AccessApron)
                .ToArray();
            Assert.That(floodAccess, Is.Not.Empty);
            foreach (CityFringeYardPartDescriptor trace in floodAccess)
            {
                Assert.That(
                    trace.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumRoadShoulderWidth),
                    trace.StableId);
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    trace,
                    0.04f,
                    0.16f);
            }
            CityFringeYardPartDescriptor[] roadShoulders = yard.Parts
                .Where(part =>
                    part.Kind == CityFringeYardPartKind.RoadShoulder &&
                    part.Footprint.Overlaps(cell.WorldBounds))
                .ToArray();
            Assert.That(roadShoulders, Is.Not.Empty);
            foreach (CityFringeYardPartDescriptor shoulder in roadShoulders)
            {
                Assert.That(
                    shoulder.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumRoadShoulderWidth),
                    shoulder.StableId);
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    shoulder,
                    0.04f,
                    0.07f);
            }

            CityFringeYardPartDescriptor anchor = yard.Parts.Single(part =>
                part.Kind == CityFringeYardPartKind.ForefieldAnchor &&
                part.StableId.Contains("forefield-anchor-00-") &&
                part.Footprint.Overlaps(cell.WorldBounds));
            Assert.That(anchor.BlocksMovement, Is.False);
            Assert.That(
                anchor.Size.x,
                Is.LessThanOrEqualTo(
                    CityFringeYardPlanner.MaximumForefieldAnchorWidth));
            Vector3 anchorForward = anchor.Rotation * Vector3.forward;
            anchorForward.y = 0f;
            Assert.That(
                Vector3.Dot(
                    anchorForward.normalized,
                    yard.Access.OutwardNormal),
                Is.GreaterThan(0.99f));
            AssertBottomCornersSeated(
                layout,
                yard,
                anchor,
                0.04f,
                0.07f);

            CityFringeYardPartDescriptor[] lowReturns = yard.Parts.Where(
                    part => part.StableId.Contains(
                        "forefield-low-return-00-"))
                .ToArray();
            Assert.That(lowReturns, Has.Length.EqualTo(3));
            foreach (CityFringeYardPartDescriptor lowReturn in lowReturns)
            {
                Assert.That(lowReturn.BlocksMovement, Is.True);
                Assert.That(
                    lowReturn.Size.x,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumLowReturnDepth));
                AssertBottomCornersSeated(
                    layout,
                    yard,
                    lowReturn,
                    0.04f,
                    0.16f);
            }

            foreach (CityFringeYardPartDescriptor mass in fringe.Yards
                         .SelectMany(item => item.Parts)
                         .Where(part =>
                             part.Kind ==
                                 CityFringeYardPartKind.RockfallMass ||
                             (part.Kind ==
                                  CityFringeYardPartKind.TerraceShelf &&
                              part.Size.y >= 0.20f)))
            {
                Assert.That(
                    mass.BlocksMovement,
                    Is.True,
                    $"{mass.StableId} is a pass-through solid mass.");
            }
        }

        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_OpensRingFrontagesAndKeepsRockRoutesClear()
        {
            const float playerRadius = 0.32f;
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringe =
                CityFringeYardPlanner.Create(layout, mountains);
            CityRoadGroundBoundaryPlan boundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            string[] mountainAreas =
            {
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                CityMountainBoundaryDefinition.SouthEastAreaId
            };

            foreach (string areaId in mountainAreas)
            {
                Assert.That(
                    fringe.TryGetYard(
                        areaId,
                        out CityFringeYardDescriptor yard),
                    Is.True,
                    areaId);
                Assert.That(
                    TryFindOpenSampleBeyondAuthoredGate(
                        boundaries,
                        yard,
                        playerRadius,
                        out CityRoadGroundBoundarySpan openSpan,
                        out float openAlong),
                    Is.True,
                    $"{areaId} kept its invisible single-gate boundary.");
                AssertWalkableAcrossRoadSeam(
                    walkable,
                    openSpan,
                    openAlong,
                    playerRadius,
                    areaId);
                AssertAllStepSafeRoadSeamsCapsuleClear(
                    walkable,
                    boundaries,
                    yard,
                    playerRadius);

                Vector3 target = CreateToeApproach(yard, playerRadius);
                Assert.That(
                    TraversalReaches(yard, target),
                    Is.True,
                    $"{areaId} traversal stops before the mountain toe.");
                AssertCapsuleClearRoute(
                    walkable,
                    yard,
                    target,
                    playerRadius);

                if (yard.Kind == CityFringeYardKind.SouthTunnelForecourt)
                {
                    Assert.That(
                        mountains.Tunnel.HasPhysicalGate,
                        Is.False);
                    Assert.That(
                        mountains.Tunnel.TravelAvailable,
                        Is.False);
                    Assert.That(
                        HorizontalDistance(
                            target,
                            mountains.Tunnel.PortalGroundCenter),
                        Is.LessThanOrEqualTo(playerRadius + 0.05f));
                }
                else
                {
                    Assert.That(
                        DistanceToRidgeToe(mountains, areaId, target),
                        Is.LessThanOrEqualTo(playerRadius + 0.05f),
                        $"{areaId} route does not reach its rock face.");
                    Assert.That(
                        yard.Parts.Any(part =>
                            part.Kind ==
                                CityFringeYardPartKind.AccessApron &&
                            Expanded(part.Footprint, 0.08f).Contains(
                                new Vector2(target.x, target.z))),
                        Is.True,
                        $"{areaId} route needs a visible gravel spur.");
                }
            }

            Assert.That(
                fringe.TryGetYard("yard-east", out _),
                Is.True);
            Assert.That(
                CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    "yard-east"),
                Is.False);
        }

        private static void AssertVocabulary(CityFringeYardPlan plan)
        {
            var required = new Dictionary<CityFringeYardKind,
                CityFringeYardPartKind[]>
            {
                [CityFringeYardKind.WestStoneTerraces] = new[]
                {
                    CityFringeYardPartKind.RetainingSection,
                    CityFringeYardPartKind.DrainChannel,
                    CityFringeYardPartKind.TerraceShelf,
                    CityFringeYardPartKind.CulvertHeadwall
                },
                [CityFringeYardKind.WestIndustrialBelt] = new[]
                {
                    CityFringeYardPartKind.RepairStack,
                    CityFringeYardPartKind.UtilityPole,
                    CityFringeYardPartKind.RepairFrame,
                    CityFringeYardPartKind.PipeStock
                },
                [CityFringeYardKind.SouthTunnelForecourt] = new[]
                {
                    CityFringeYardPartKind.WheelRut,
                    CityFringeYardPartKind.DrainCover,
                    CityFringeYardPartKind.TunnelCheek,
                    CityFringeYardPartKind.PracticalHousing
                },
                [CityFringeYardKind.SouthFloodWorks] = new[]
                {
                    CityFringeYardPartKind.Gabion,
                    CityFringeYardPartKind.DrainChannel,
                    CityFringeYardPartKind.GabionCage,
                    CityFringeYardPartKind.FloodGauge
                },
                [CityFringeYardKind.EastUtilityEdge] = new[]
                {
                    CityFringeYardPartKind.UtilityShed,
                    CityFringeYardPartKind.EarthBerm
                }
            };

            foreach (CityFringeYardDescriptor yard in plan.Yards)
            {
                foreach (CityFringeYardPartKind partKind in required[yard.Kind])
                {
                    Assert.That(
                        yard.Parts.Any(item => item.Kind == partKind),
                        Is.True,
                        $"{yard.AreaId} lacks {partKind}.");
                }
            }
        }

        private static void AssertForefieldCoverage(
            CityFringeYardDescriptor yard)
        {
            CityFringeYardPartDescriptor[] forefield = yard.Parts.Where(
                    part => part.Kind ==
                            CityFringeYardPartKind.ForefieldAnchor)
                .ToArray();
            bool mountain =
                CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    yard.AreaId);
            if (!mountain)
            {
                Assert.That(forefield, Is.Empty);
                Assert.That(
                    yard.Parts.Any(part =>
                        part.Kind == CityFringeYardPartKind.RoadShoulder ||
                        part.Kind == CityFringeYardPartKind.ServiceSpur),
                    Is.False);
                return;
            }

            Assert.That(
                forefield.Length,
                Is.InRange(
                    CityFringeYardPlanner.MinimumForefieldAnchorCount,
                    CityFringeYardPlanner.MaximumForefieldAnchorCount),
                yard.AreaId);
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.RoadShoulder &&
                    !part.BlocksMovement),
                Is.True,
                $"{yard.AreaId} lacks its collider-free road band.");
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.ServiceSpur &&
                    !part.BlocksMovement),
                Is.True,
                $"{yard.AreaId} lacks road-to-track service traces.");
            bool usesServiceTrack =
                yard.Kind != CityFringeYardKind.SouthFloodWorks;
            Assert.That(
                yard.Parts.Any(part =>
                    part.Kind == CityFringeYardPartKind.ServiceTrack &&
                    DepthFromRoad(yard, part.Center) >=
                        CityFringeYardPlanner
                            .ForefieldMiddleBandMaximumDepth),
                Is.EqualTo(usesServiceTrack),
                $"{yard.AreaId} has the wrong mountain-toe track contract.");
            if (!usesServiceTrack)
            {
                Assert.That(
                    yard.Parts.Any(part =>
                        part.Kind == CityFringeYardPartKind.DrainChannel &&
                        DepthFromRoad(yard, part.Center) >=
                            CityFringeYardPlanner
                                .ForefieldMiddleBandMaximumDepth),
                    Is.True,
                    $"{yard.AreaId} lacks its floodworks toe drain.");
            }

            float[] coordinates = forefield
                .Select(part => LongCoordinate(yard, part.Center))
                .OrderBy(value => value)
                .ToArray();
            float minimum = Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? yard.AreaBounds.yMin
                : yard.AreaBounds.xMin;
            float maximum = Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? yard.AreaBounds.yMax
                : yard.AreaBounds.xMax;
            float previous = minimum;
            foreach (float coordinate in coordinates)
            {
                Assert.That(
                    coordinate - previous,
                    Is.LessThanOrEqualTo(
                        CityFringeYardPlanner.MaximumForefieldAnchorGap +
                        0.04f),
                    $"{yard.AreaId} has an empty forefield interval.");
                previous = coordinate;
            }

            Assert.That(
                maximum - previous,
                Is.LessThanOrEqualTo(
                    CityFringeYardPlanner.MaximumForefieldAnchorGap + 0.04f),
                $"{yard.AreaId} has an empty terminal forefield interval.");
            foreach (CityFringeYardPartDescriptor anchor in forefield)
            {
                Assert.That(anchor.BlocksMovement, Is.False, anchor.StableId);
                Assert.That(
                    DepthFromRoad(yard, anchor.Center),
                    Is.InRange(
                        CityFringeYardPlanner.ForefieldRoadBandMaximumDepth,
                        CityFringeYardPlanner
                            .ForefieldMiddleBandMaximumDepth),
                    anchor.StableId);
            }
        }

        private static void AssertPoleSpacing(
            CityFringeYardDescriptor yard)
        {
            if (!CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    yard.AreaId))
            {
                return;
            }

            float maximum =
                yard.Kind == CityFringeYardKind.WestStoneTerraces
                    ? 40f
                    : 34f;
            float[] coordinates = yard.Parts.Where(part =>
                    part.Kind == CityFringeYardPartKind.UtilityPole)
                .Select(part => LongCoordinate(yard, part.Center))
                .OrderBy(value => value)
                .ToArray();
            Assert.That(coordinates.Length, Is.GreaterThanOrEqualTo(2));
            for (int index = 1; index < coordinates.Length; index++)
            {
                Assert.That(
                    coordinates[index] - coordinates[index - 1],
                    Is.LessThanOrEqualTo(maximum + 0.04f),
                    $"{yard.AreaId} pole spacing {index - 1}->{index}");
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            const float tolerance = 0.04f;
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

        private static bool TryFindOpenSampleBeyondAuthoredGate(
            CityRoadGroundBoundaryPlan boundaries,
            CityFringeYardDescriptor yard,
            float radius,
            out CityRoadGroundBoundarySpan selected,
            out float along)
        {
            float gateCenter = yard.Access.FrontageEdge.IsHorizontal
                ? yard.Access.Center.x
                : yard.Access.Center.z;
            float gateMinimum = gateCenter - yard.Access.Width * 0.5f;
            float gateMaximum = gateCenter + yard.Access.Width * 0.5f;
            for (int index = 0;
                 index < boundaries.SafeConnections.Count;
                 index++)
            {
                CityRoadGroundBoundarySpan span =
                    boundaries.SafeConnections[index];
                if (span.Surface.AreaId != yard.AreaId ||
                    span.Edge != yard.Access.FrontageEdge)
                {
                    continue;
                }

                float usableMinimum = span.MinimumCoordinate + radius + 0.02f;
                float usableMaximum = span.MaximumCoordinate - radius - 0.02f;
                if (usableMinimum < gateMinimum - 0.05f)
                {
                    selected = span;
                    along = Mathf.Min(
                        gateMinimum - 0.08f,
                        usableMaximum);
                    return along >= usableMinimum;
                }

                if (usableMaximum > gateMaximum + 0.05f)
                {
                    selected = span;
                    along = Mathf.Max(
                        gateMaximum + 0.08f,
                        usableMinimum);
                    return along <= usableMaximum;
                }
            }

            selected = default;
            along = 0f;
            return false;
        }

        private static void AssertWalkableAcrossRoadSeam(
            RoadWalkableArea walkable,
            CityRoadGroundBoundarySpan span,
            float along,
            float radius,
            string label)
        {
            Rect surface = span.Surface.WorldBounds;
            Vector3 inward = span.IsHorizontal
                ? new Vector3(
                    0f,
                    0f,
                    Mathf.Sign(surface.center.y - span.FixedCoordinate))
                : new Vector3(
                    Mathf.Sign(surface.center.x - span.FixedCoordinate),
                    0f,
                    0f);
            Vector3 seam = span.IsHorizontal
                ? new Vector3(along, 0f, span.FixedCoordinate)
                : new Vector3(span.FixedCoordinate, 0f, along);
            for (int index = -4; index <= 4; index++)
            {
                Vector3 sample = seam + inward * (index * 0.10f);
                Assert.That(
                    walkable.Contains(sample, radius),
                    Is.True,
                    $"{label} road seam sample {index}");
            }
        }

        private static void AssertAllStepSafeRoadSeamsCapsuleClear(
            RoadWalkableArea walkable,
            CityRoadGroundBoundaryPlan boundaries,
            CityFringeYardDescriptor yard,
            float radius)
        {
            CityRoadGroundBoundarySpan[] spans = boundaries.SafeConnections
                .Where(span => span.Surface.AreaId == yard.AreaId)
                .ToArray();
            Assert.That(
                spans,
                Is.Not.Empty,
                $"{yard.AreaId} has no step-safe road frontage.");
            foreach (CityRoadGroundBoundarySpan span in spans)
            {
                float usableMinimum = span.MinimumCoordinate + radius + 0.02f;
                float usableMaximum = span.MaximumCoordinate - radius - 0.02f;
                Assert.That(
                    usableMaximum,
                    Is.GreaterThanOrEqualTo(usableMinimum),
                    $"{yard.AreaId} safe span is narrower than the capsule.");
                float[] samples =
                {
                    usableMinimum,
                    (usableMinimum + usableMaximum) * 0.5f,
                    usableMaximum
                };
                foreach (float along in samples)
                {
                    AssertWalkableAcrossRoadSeam(
                        walkable,
                        span,
                        along,
                        radius,
                        yard.AreaId);
                }

                Rect seam = span.CreateConnector(radius + 0.10f);
                foreach (CityFringeYardPartDescriptor part in yard.Parts)
                {
                    if (!part.BlocksMovement)
                    {
                        continue;
                    }

                    Assert.That(
                        Expanded(part.Footprint, radius + 0.05f)
                            .Overlaps(seam),
                        Is.False,
                        $"{part.StableId} blocks a step-safe road seam.");
                }
            }
        }

        private static Vector3 CreateToeApproach(
            CityFringeYardDescriptor yard,
            float inset)
        {
            Vector3 result = yard.Access.Center;
            Vector3 outward = yard.Access.OutwardNormal;
            if (outward.x < -0.5f)
            {
                result.x = yard.AreaBounds.xMin + inset;
            }
            else if (outward.x > 0.5f)
            {
                result.x = yard.AreaBounds.xMax - inset;
            }
            else if (outward.z < -0.5f)
            {
                result.z = yard.AreaBounds.yMin + inset;
            }
            else
            {
                result.z = yard.AreaBounds.yMax - inset;
            }

            return result;
        }

        private static bool TraversalReaches(
            CityFringeYardDescriptor yard,
            Vector3 target)
        {
            const float tolerance = 0.04f;
            return target.x >= yard.TraversalBounds.xMin - tolerance &&
                   target.x <= yard.TraversalBounds.xMax + tolerance &&
                   target.z >= yard.TraversalBounds.yMin - tolerance &&
                   target.z <= yard.TraversalBounds.yMax + tolerance;
        }

        private static void AssertCapsuleClearRoute(
            RoadWalkableArea walkable,
            CityFringeYardDescriptor yard,
            Vector3 target,
            float radius)
        {
            Vector3 start = yard.Access.Center;
            float distance = HorizontalDistance(start, target);
            int sampleCount = Mathf.CeilToInt(distance / 0.10f);
            foreach (CityFringeYardPartDescriptor part in yard.Parts)
            {
                if (part.BlocksMovement)
                {
                    Assert.That(
                        part.Footprint.Overlaps(yard.TraversalBounds),
                        Is.False,
                        $"{part.StableId} narrows the declared 6 m route.");
                }
            }

            for (int sampleIndex = 0;
                 sampleIndex <= sampleCount;
                 sampleIndex++)
            {
                float amount = sampleIndex / (float)sampleCount;
                Vector3 sample = Vector3.Lerp(start, target, amount);
                Assert.That(
                    walkable.Contains(sample, radius),
                    Is.True,
                    $"{yard.AreaId} route sample {sampleIndex}");
                foreach (CityFringeYardPartDescriptor part in yard.Parts)
                {
                    if (!part.BlocksMovement)
                    {
                        continue;
                    }

                    Assert.That(
                        Expanded(part.Footprint, radius + 0.05f).Contains(
                            new Vector2(sample.x, sample.z)),
                        Is.False,
                        $"{part.StableId} blocks {yard.AreaId} route.");
                }
            }
        }

        private static float DistanceToRidgeToe(
            CityMountainBoundaryPlan mountains,
            string areaId,
            Vector3 target)
        {
            Vector2 point = new Vector2(target.x, target.z);
            float best = float.PositiveInfinity;
            foreach (CityMountainRidgeDescriptor ridge in mountains.Ridges)
            {
                if (ridge.SourceAreaId != areaId)
                {
                    continue;
                }

                for (int index = 1; index < ridge.Stations.Count; index++)
                {
                    best = Mathf.Min(
                        best,
                        DistanceToSegment(
                            point,
                            ridge.Stations[index - 1].WorldXZ,
                            ridge.Stations[index].WorldXZ));
                }
            }

            return best;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                return Vector2.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) / lengthSquared);
            return Vector2.Distance(point, start + delta * amount);
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static void AssertSegmentsMeetWithoutOverlap(
            IEnumerable<CityFringeYardPartDescriptor> source,
            Vector3 axis)
        {
            Vector3 normalizedAxis = axis.normalized;
            CityFringeYardPartDescriptor[] segments = source
                .OrderBy(part => Vector3.Dot(
                    part.Center,
                    normalizedAxis))
                .ToArray();
            Assert.That(segments.Length, Is.GreaterThan(1));
            float previousEnd = float.NegativeInfinity;
            for (int index = 0; index < segments.Length; index++)
            {
                CityFringeYardPartDescriptor segment = segments[index];
                float halfSpan = segment.Size.z * 0.5f * Mathf.Abs(
                    Vector3.Dot(
                        segment.Rotation * Vector3.forward,
                        normalizedAxis));
                float start = Vector3.Dot(
                    segment.Center,
                    normalizedAxis) - halfSpan;
                float end = start + halfSpan * 2f;
                if (index > 0)
                {
                    Assert.That(
                        start,
                        Is.EqualTo(previousEnd).Within(0.001f),
                        $"{segment.StableId} overlaps its previous segment.");
                }

                previousEnd = end;
            }
        }

        private static void AssertMemberEndpointsSupported(
            CityFringeYardPartDescriptor member,
            IReadOnlyList<CityFringeYardPartDescriptor> posts,
            bool seatsOnPostTops)
        {
            foreach (Vector3 endpoint in PartEndpoints(member))
            {
                CityFringeYardPartDescriptor post = posts.OrderBy(candidate =>
                        HorizontalDistance(endpoint, candidate.Center))
                    .First();
                Assert.That(
                    HorizontalDistance(endpoint, post.Center),
                    Is.LessThanOrEqualTo(0.03f),
                    $"{member.StableId} misses its frame post.");
                float postBottom = post.Center.y - post.Size.y * 0.5f;
                float postTop = post.Center.y + post.Size.y * 0.5f;
                if (seatsOnPostTops)
                {
                    Vector3 memberBottom = endpoint -
                        (member.Rotation * Vector3.up) *
                        (member.Size.y * 0.5f);
                    Assert.That(
                        memberBottom.y,
                        Is.EqualTo(postTop).Within(0.035f),
                        $"{member.StableId} is not seated on {post.StableId}.");
                }
                else
                {
                    Assert.That(
                        endpoint.y,
                        Is.InRange(postBottom - 0.01f, postTop + 0.01f),
                        $"{member.StableId} leaves {post.StableId}.");
                }
            }
        }

        private static Vector3[] PartEndpoints(
            CityFringeYardPartDescriptor part)
        {
            Vector3 halfRun = (part.Rotation * Vector3.forward) *
                              (part.Size.z * 0.5f);
            return new[]
            {
                part.Center - halfRun,
                part.Center + halfRun
            };
        }

        private static float SampleOwnerTop(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            Vector2 sample = new Vector2(point.x, point.z);
            CitySurfaceDescriptor surface = layout.Surfaces.First(candidate =>
                candidate.AreaId == yard.AreaId &&
                sample.x >= candidate.WorldBounds.xMin - 0.04f &&
                sample.x <= candidate.WorldBounds.xMax + 0.04f &&
                sample.y >= candidate.WorldBounds.yMin - 0.04f &&
                sample.y <= candidate.WorldBounds.yMax + 0.04f);
            return CityTerrainSurfacePlan.SampleTop(layout, surface, sample);
        }

        private static void AssertBottomCornersSeated(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            float maximumGap,
            float maximumEmbed)
        {
            Vector3 right =
                (part.Rotation * Vector3.right) * (part.Size.x * 0.5f);
            Vector3 forward =
                (part.Rotation * Vector3.forward) * (part.Size.z * 0.5f);
            Vector3 down =
                (part.Rotation * Vector3.up) * (part.Size.y * 0.5f);
            for (int rightSign = -1; rightSign <= 1; rightSign += 2)
            {
                for (int forwardSign = -1;
                     forwardSign <= 1;
                     forwardSign += 2)
                {
                    Vector3 corner = part.Center - down +
                        right * rightSign +
                        forward * forwardSign;
                    Vector2 sample = new Vector2(corner.x, corner.z);
                    CitySurfaceDescriptor surface = layout.Surfaces.First(
                        candidate =>
                            candidate.AreaId == yard.AreaId &&
                            sample.x >= candidate.WorldBounds.xMin - 0.04f &&
                            sample.x <= candidate.WorldBounds.xMax + 0.04f &&
                            sample.y >= candidate.WorldBounds.yMin - 0.04f &&
                            sample.y <= candidate.WorldBounds.yMax + 0.04f);
                    float ground = CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        sample);
                    Assert.That(
                        corner.y - ground,
                        Is.InRange(-maximumEmbed, maximumGap),
                        $"{part.StableId} has a floating/buried corner.");
                }
            }
        }

        private static float DepthFromRoad(
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            Vector3 outward = yard.Access.OutwardNormal;
            if (outward.x < -0.5f)
            {
                return yard.AreaBounds.xMax - point.x;
            }

            if (outward.x > 0.5f)
            {
                return point.x - yard.AreaBounds.xMin;
            }

            if (outward.z < -0.5f)
            {
                return yard.AreaBounds.yMax - point.z;
            }

            return point.z - yard.AreaBounds.yMin;
        }

        private static float LongCoordinate(
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            return Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? point.z
                : point.x;
        }

        private static Rect Expanded(Rect source, float amount)
        {
            return Rect.MinMaxRect(
                source.xMin - amount,
                source.yMin - amount,
                source.xMax + amount,
                source.yMax + amount);
        }
    }
}
