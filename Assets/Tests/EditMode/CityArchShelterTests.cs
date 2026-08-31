using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityArchShelter")]
    public sealed class CityArchShelterTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void DefaultCity_TargetsCurrentNightlifeCellsDeterministically()
        {
            CityLayout layout = CreateDefaultLayout();
            BuildingLot west = FindLot(
                layout,
                CityArchShelterPlacementResolver.WestCell);
            BuildingLot east = FindLot(
                layout,
                CityArchShelterPlacementResolver.EastCell);

            Assert.That(
                CityArchShelterPlacementResolver.WestCell,
                Is.EqualTo(new Vector2Int(10, 5)));
            Assert.That(
                CityArchShelterPlacementResolver.EastCell,
                Is.EqualTo(new Vector2Int(11, 5)));
            Assert.That(west.IsOrdinaryBuilding, Is.True);
            Assert.That(east.IsOrdinaryBuilding, Is.True);
            Assert.That(west.District, Is.EqualTo(CityDistrictKind.Nightlife));
            Assert.That(east.District, Is.EqualTo(CityDistrictKind.Nightlife));
            Assert.That(
                layout.HasRoad(
                    RoadEdge.ForCellFrontage(
                        west.Cell,
                        Vector2Int.right)),
                Is.False,
                "The authored connector belongs in the roadless seam.");

            CityArchShelterPlan first =
                CityArchShelterPlanner.Create(layout);
            CityArchShelterPlan second =
                CityArchShelterPlanner.Create(layout);

            Assert.That(first.IsEnabled, Is.True);
            Assert.That(first.Placement.WestCell, Is.EqualTo(west.Cell));
            Assert.That(first.Placement.EastCell, Is.EqualTo(east.Cell));
            AssertBoundsEqual(
                CityArchShelterPlacementResolver
                    .ResolveExpectedBuildingBounds(west),
                first.Placement.WestBuildingBounds);
            AssertBoundsEqual(
                CityArchShelterPlacementResolver
                    .ResolveExpectedBuildingBounds(east),
                first.Placement.EastBuildingBounds);
            Assert.That(
                first.Placement.PassageFootprint.xMin,
                Is.EqualTo(first.Placement.WestBuildingBounds.max.x)
                    .Within(Tolerance));
            Assert.That(
                first.Placement.PassageFootprint.xMax,
                Is.EqualTo(first.Placement.EastBuildingBounds.min.x)
                    .Within(Tolerance));
            Assert.DoesNotThrow(
                () => CityArchShelterValidator.ValidateOrThrow(
                    layout,
                    first));
            AssertPlansEqual(first, second);
        }

        [Test]
        public void NonCanonicalCity_ReturnsAValidatedEmptyPlan()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed + 1);

            CityArchShelterPlan plan =
                CityArchShelterPlanner.Create(layout);

            Assert.That(plan.IsEnabled, Is.False);
            Assert.That(plan.ClearLanes, Is.Empty);
            Assert.That(plan.NpcAnchors, Is.Empty);
            Assert.That(plan.Props, Is.Empty);
            Assert.That(plan.Obstacles, Is.Empty);
            Assert.That(plan.RainOccluders, Is.Empty);
            Assert.That(
                string.IsNullOrEmpty(plan.Steps.StableId),
                Is.True);
            Assert.That(
                string.IsNullOrEmpty(plan.UpperLanding.StableId),
                Is.True);
            Assert.That(
                string.IsNullOrEmpty(plan.Platform.StableId),
                Is.True);
            Assert.DoesNotThrow(
                () => CityArchShelterValidator.ValidateOrThrow(
                    layout,
                    plan));
        }

        [Test]
        public void DefaultCity_ArchMassCoversTheFullCommonFacadeDepth()
        {
            CityArchShelterPlan plan = CreateDefaultPlan();
            CityArchShelterPlacement placement = plan.Placement;
            Rect common = placement.CommonFacadeFootprint;
            Rect passage = placement.PassageFootprint;
            float expectedCommonMinZ = Mathf.Max(
                placement.WestBuildingBounds.min.z,
                placement.EastBuildingBounds.min.z);
            float expectedCommonMaxZ = Mathf.Min(
                placement.WestBuildingBounds.max.z,
                placement.EastBuildingBounds.max.z);

            Assert.That(
                common.yMin,
                Is.EqualTo(expectedCommonMinZ).Within(Tolerance));
            Assert.That(
                common.yMax,
                Is.EqualTo(expectedCommonMaxZ).Within(Tolerance));
            Assert.That(
                passage.yMin - common.yMin,
                Is.EqualTo(
                        CityArchShelterPlacementResolver.PortalInset)
                    .Within(Tolerance));
            Assert.That(
                common.yMax - passage.yMax,
                Is.EqualTo(
                        CityArchShelterPlacementResolver.PortalInset)
                    .Within(Tolerance));
            Assert.That(
                common.height,
                Is.GreaterThan(10f),
                "The bridge must read as the whole shared side-facade " +
                "mass, not as the former six-metre local canopy.");

            AssertDepthEqual(
                common,
                ToXZRect(placement.StructureBounds),
                "structure bounds");
            AssertDepthEqual(
                common,
                placement.ShelteredFootprint,
                "sheltered footprint");
            AssertDepthEqual(
                common,
                ToXZRect(
                    FindObstacle(
                            plan,
                            CityArchShelterObstacleKind.OverheadGallery)
                        .Bounds),
                "overhead roof");
            AssertDepthEqual(
                common,
                ToXZRect(plan.RainOccluders.Single().Bounds),
                "rain volume");

            Assert.That(
                Contains(passage, placement.TableauFootprint),
                Is.True,
                "The local tableau must stay inside the full-depth arch.");
            Assert.That(
                Contains(passage, plan.Steps.Footprint),
                Is.True,
                "The local stair must stay inside the full-depth arch.");
            Assert.That(
                OverlapsStrict(
                    placement.TableauFootprint,
                    plan.Steps.Footprint),
                Is.False);
            foreach (CityArchShelterPropDescriptor prop in plan.Props)
            {
                Assert.That(
                    Contains(
                        placement.TableauFootprint,
                        ToXZRect(prop.Bounds)),
                    Is.True,
                    prop.StableId);
            }

            Assert.That(plan.ClearLanes, Has.Count.EqualTo(1));
            foreach (CityArchShelterClearLaneDescriptor lane in
                     plan.ClearLanes)
            {
                Assert.That(
                    lane.Footprint.width,
                    Is.GreaterThanOrEqualTo(
                        CityArchShelterPlanner.ClearLaneWidth -
                        Tolerance));
                AssertDepthEqual(
                    passage,
                    lane.Footprint,
                    lane.StableId);
            }
        }

        [Test]
        public void DefaultCity_StepsJoinOneGuardedWallTerraceAndClearLane()
        {
            CityArchShelterPlan plan = CreateDefaultPlan();
            CityArchShelterPlacement placement = plan.Placement;
            CityArchShelterStepDescriptor steps = plan.Steps;
            CityArchShelterLandingDescriptor landing = plan.UpperLanding;
            CityArchShelterPlatformDescriptor platform = plan.Platform;
            float terraceRise = Mathf.Abs(placement.TerraceRise);

            Assert.That(placement.WestSurfaceY, Is.LessThan(
                placement.EastSurfaceY));
            Assert.That(placement.StructureRotation,
                Is.EqualTo(Quaternion.identity));
            Assert.That(steps.AscentDirection, Is.EqualTo(Vector3.right));
            Assert.That(steps.TotalRise,
                Is.EqualTo(terraceRise).Within(Tolerance));
            Assert.That(steps.StepCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(steps.StepRise, Is.InRange(
                float.Epsilon,
                CityRoadGroundBoundaryPlanner.MaximumSafeStep + Tolerance));
            AssertRectEqual(placement.RailSuppressionFootprint,
                steps.Footprint);
            Assert.That(Contains(placement.PassageFootprint,
                steps.Footprint), Is.True);
            Assert.That(OverlapsStrict(steps.Footprint,
                placement.TableauFootprint), Is.False);
            foreach (CityArchShelterPropDescriptor prop in plan.Props)
            {
                Assert.That(OverlapsStrict(steps.Footprint,
                    ToXZRect(prop.Bounds)), Is.False, prop.StableId);
            }

            Assert.That(Contains(placement.PassageFootprint,
                landing.Footprint), Is.True);
            AssertDepthEqual(steps.Footprint, landing.Footprint,
                "upper landing");
            Assert.That(landing.Footprint.xMin,
                Is.EqualTo(steps.Footprint.xMax).Within(Tolerance));
            Assert.That(landing.Footprint.width, Is.EqualTo(
                CityArchShelterPlacementResolver.UpperLandingLength)
                .Within(Tolerance));
            Assert.That(landing.SurfaceY,
                Is.EqualTo(steps.UpperSurfaceY).Within(Tolerance));
            foreach (CityArchShelterPropDescriptor prop in plan.Props)
            {
                Assert.That(OverlapsStrict(landing.Footprint,
                    ToXZRect(prop.Bounds)), Is.False, prop.StableId);
            }

            Rect expectedPlatform = Rect.MinMaxRect(
                steps.Footprint.xMax,
                placement.CommonFacadeFootprint.yMin,
                placement.PassageFootprint.xMax -
                CityArchShelterPlacementResolver.PlatformWallInset,
                steps.Footprint.yMax);
            AssertRectEqual(expectedPlatform, platform.Footprint);
            Assert.That(Contains(platform.Footprint, landing.Footprint),
                Is.True);
            Assert.That(platform.SupportBottomY,
                Is.EqualTo(steps.LowerSurfaceY).Within(Tolerance));
            Assert.That(platform.SurfaceY,
                Is.EqualTo(steps.UpperSurfaceY).Within(Tolerance));
            Assert.That(platform.SupportHeight,
                Is.EqualTo(steps.TotalRise).Within(Tolerance));
            Assert.That(platform.Footprint.xMin -
                placement.StructurePosition.x,
                Is.EqualTo(-0.36614f).Within(Tolerance));
            Assert.That(platform.Footprint.xMax -
                placement.StructurePosition.x,
                Is.EqualTo(6.94f).Within(Tolerance));
            Assert.That(platform.Footprint.yMin -
                placement.StructurePosition.z,
                Is.EqualTo(-5.801f).Within(Tolerance));
            Assert.That(platform.Footprint.yMax -
                placement.StructurePosition.z,
                Is.EqualTo(3.05f).Within(Tolerance));

            Assert.That(plan.ClearLanes, Has.Count.EqualTo(1));
            CityArchShelterClearLaneDescriptor lane = plan.ClearLanes[0];
            Assert.That(lane.Footprint.center.x,
                Is.LessThan(placement.SharedBoundaryX));
            Assert.That(lane.Footprint.width,
                Is.EqualTo(CityArchShelterPlanner.ClearLaneWidth)
                    .Within(Tolerance));
            AssertDepthEqual(placement.PassageFootprint, lane.Footprint,
                lane.StableId);
            Assert.That(lane.SurfaceY,
                Is.EqualTo(placement.WestSurfaceY).Within(Tolerance));
            Assert.That(OverlapsStrict(platform.Footprint, lane.Footprint),
                Is.False);
            Assert.That(OverlapsStrict(landing.Footprint, lane.Footprint),
                Is.False);

            var landingClearance = new Bounds(
                new Vector3(
                    landing.Footprint.center.x,
                    landing.SurfaceY +
                    CityArchShelterPlacementResolver
                        .MinimumUpperLandingHeadroom * 0.5f,
                    landing.Footprint.center.y),
                new Vector3(
                    landing.Footprint.width,
                    CityArchShelterPlacementResolver
                        .MinimumUpperLandingHeadroom,
                    landing.Footprint.height));
            foreach (CityArchShelterObstacleDescriptor obstacle in plan.Obstacles)
            {
                Assert.That(OverlapsStrict(landingClearance,
                    obstacle.Bounds), Is.False, obstacle.StableId);
            }
        }

        [Test]
        public void DefaultCity_TableauIsGroundedOnOnePhysicalPlatform()
        {
            CityArchShelterPlan plan = CreateDefaultPlan();
            CityArchShelterPlatformDescriptor platform = plan.Platform;
            CityArchShelterPropDescriptor barrel = FindProp(
                plan,
                CityArchShelterPropKind.BurnBarrel);
            CityArchShelterPropDescriptor bedding = FindProp(
                plan,
                CityArchShelterPropKind.Bedding);
            CityArchShelterNpcAnchorDescriptor standing = FindAnchor(
                plan,
                CityArchShelterNpcStageKind.StandingWarmer);
            CityArchShelterNpcAnchorDescriptor seated = FindAnchor(
                plan,
                CityArchShelterNpcStageKind.SeatedWarmer);
            CityArchShelterNpcAnchorDescriptor sleeper = FindAnchor(
                plan,
                CityArchShelterNpcStageKind.Sleeper);

            Assert.That(plan.NpcAnchors, Has.Count.EqualTo(3));
            Assert.That(
                plan.NpcAnchors.Select(anchor => anchor.Stage),
                Is.EquivalentTo(new[]
                {
                    CityArchShelterNpcStageKind.StandingWarmer,
                    CityArchShelterNpcStageKind.SeatedWarmer,
                    CityArchShelterNpcStageKind.Sleeper
                }));

            int tableauSide = SideOfSteps(
                plan.Steps.Footprint,
                barrel.Position);
            Assert.That(
                tableauSide,
                Is.Not.EqualTo(0),
                "The fire cannot stand on the terrace steps.");

            foreach (CityArchShelterNpcAnchorDescriptor warmer in
                     new[] { standing, seated })
            {
                Assert.That(
                    SideOfSteps(plan.Steps.Footprint, warmer.Position),
                    Is.EqualTo(tableauSide),
                    $"{warmer.Stage} must warm up beside the barrel on " +
                    "the same flat terrace, not on or beyond the steps.");
                Assert.That(
                    warmer.Position.y,
                    Is.EqualTo(barrel.Position.y).Within(Tolerance),
                    warmer.Stage.ToString());
                Vector3 toBarrel = barrel.Position - warmer.Position;
                toBarrel.y = 0f;
                Assert.That(
                    Vector3.Dot(
                        warmer.Facing,
                        toBarrel.normalized),
                    Is.GreaterThan(0.99f),
                    $"{warmer.Stage} must face the visible heat source.");
            }

            Assert.That(
                barrel.Position.y,
                Is.EqualTo(platform.SurfaceY).Within(Tolerance));
            Assert.That(
                bedding.Position.y,
                Is.EqualTo(platform.SurfaceY).Within(Tolerance));
            Assert.That(
                Contains(
                    platform.Footprint,
                    ToXZRect(barrel.Bounds)),
                Is.True,
                "The barrel footprint must sit on the visible platform.");
            Assert.That(
                Contains(
                    platform.Footprint,
                    ToXZRect(bedding.Bounds)),
                Is.True,
                "The mattress footprint must sit on the visible platform.");

            foreach (CityArchShelterNpcAnchorDescriptor resident in
                     plan.NpcAnchors)
            {
                Rect supportFootprint = resident.Stage ==
                                        CityArchShelterNpcStageKind.Sleeper
                    ? ToXZRect(bedding.Bounds)
                    : Rect.MinMaxRect(
                        resident.Position.x - 0.32f,
                        resident.Position.z - 0.32f,
                        resident.Position.x + 0.32f,
                        resident.Position.z + 0.32f);
                Assert.That(
                    Contains(platform.Footprint, supportFootprint),
                    Is.True,
                    $"{resident.Stage} must be over the platform rather " +
                    "than suspended past its edge.");
                float expectedY = resident.Stage ==
                                  CityArchShelterNpcStageKind.Sleeper
                    ? bedding.Bounds.max.y
                    : platform.SurfaceY;
                Assert.That(
                    resident.Position.y,
                    Is.EqualTo(expectedY).Within(Tolerance),
                    $"{resident.Stage} has no supporting top surface.");
            }

            Assert.That(
                ContainsXZ(bedding.Bounds, sleeper.Position),
                Is.True,
                "The sleeping resident must remain over the bedding.");
            Assert.That(
                sleeper.Position.y,
                Is.InRange(
                    bedding.Bounds.min.y - Tolerance,
                    bedding.Bounds.max.y + Tolerance));
        }

        [Test]
        public void DefaultCity_ConnectorAndObstacleFootprintsStayPhysical()
        {
            CityArchShelterPlan plan = CreateDefaultPlan();
            CityArchShelterPlacement placement = plan.Placement;
            float upperSurface = Mathf.Max(
                placement.WestSurfaceY,
                placement.EastSurfaceY);

            Assert.That(placement.TopIsWalkable, Is.False);
            CityArchShelterObstacleDescriptor overhead = FindObstacle(
                plan,
                CityArchShelterObstacleKind.OverheadGallery);
            Assert.That(
                overhead.Bounds.min.y,
                Is.GreaterThanOrEqualTo(
                    upperSurface +
                    CityArchShelterPlacementResolver.MinimumClearHeight -
                    Tolerance));
            Assert.That(
                Contains(
                    ToXZRect(overhead.Bounds),
                    placement.ShelteredFootprint),
                Is.True,
                "The non-walkable connector must roof the authored seam.");

            Assert.That(plan.Obstacles, Has.Count.EqualTo(10));
            Assert.That(
                plan.Obstacles.Select(obstacle => obstacle.Kind),
                Is.EquivalentTo(Enum.GetValues(
                    typeof(CityArchShelterObstacleKind))));
            Assert.That(
                OverlapsStrict(
                    FindObstacle(
                            plan,
                            CityArchShelterObstacleKind.WestAttachment)
                        .Bounds,
                    placement.WestBuildingBounds),
                Is.True,
                "The west support must actually enter its facade.");
            Assert.That(
                OverlapsStrict(
                    FindObstacle(
                            plan,
                            CityArchShelterObstacleKind.EastAttachment)
                        .Bounds,
                    placement.EastBuildingBounds),
                Is.True,
                "The east support must actually enter its facade.");
            Assert.That(
                FindObstacle(
                        plan,
                        CityArchShelterObstacleKind.EastAttachment)
                    .Bounds.min.x,
                Is.EqualTo(plan.Platform.Footprint.xMax)
                    .Within(Tolerance),
                "The service terrace cannot leave a physical seam before the " +
                "east wall support.");

            AssertPropObstacleMatches(
                plan,
                CityArchShelterPropKind.BurnBarrel,
                CityArchShelterObstacleKind.BurnBarrel);
            AssertPropObstacleMatches(
                plan,
                CityArchShelterPropKind.Bedding,
                CityArchShelterObstacleKind.Bedding);
            AssertPropObstacleMatches(
                plan,
                CityArchShelterPropKind.Clutter,
                CityArchShelterObstacleKind.Clutter);

            CityArchShelterObstacleDescriptor northGuard = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformNorthGuardRail);
            CityArchShelterObstacleDescriptor southGuard = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformSouthGuardRail);
            CityArchShelterObstacleDescriptor westGuard = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformWestGuardRail);
            AssertGuardRailBounds(plan, northGuard, true);
            AssertGuardRailBounds(plan, southGuard, false);
            AssertWestGuardRailBounds(plan, westGuard);

            foreach (CityArchShelterObstacleDescriptor obstacle in
                     plan.Obstacles)
            {
                foreach (CityArchShelterClearLaneDescriptor lane in
                         plan.ClearLanes)
                {
                    Assert.That(
                        OverlapsStrict(
                            obstacle.Bounds,
                            lane.ClearanceBounds),
                        Is.False,
                        $"{obstacle.StableId} blocks {lane.StableId}.");
                }
            }

            Assert.That(plan.RainOccluders, Has.Count.EqualTo(1));
            CityArchShelterRainOccluderDescriptor rain =
                plan.RainOccluders[0];
            Assert.That(
                Contains(
                    ToXZRect(rain.Bounds),
                    placement.ShelteredFootprint),
                Is.True);
            Assert.That(
                rain.Bounds.min.y,
                Is.LessThanOrEqualTo(
                    Mathf.Min(
                        placement.WestSurfaceY,
                        placement.EastSurfaceY) +
                    Tolerance));
            Assert.That(
                rain.Bounds.max.y,
                Is.GreaterThanOrEqualTo(
                    upperSurface +
                    CityArchShelterPlacementResolver.MinimumClearHeight -
                    Tolerance));
            var shelteredPoint = new Vector3(
                placement.ShelteredFootprint.center.x,
                upperSurface + 1f,
                placement.ShelteredFootprint.center.y);
            Assert.That(plan.IsRainSheltered(shelteredPoint), Is.True);
            shelteredPoint.x = rain.Bounds.max.x + 0.1f;
            Assert.That(plan.IsRainSheltered(shelteredPoint), Is.False);
            shelteredPoint.x = rain.Bounds.center.x;
            shelteredPoint.y = rain.Bounds.max.y + 0.1f;
            Assert.That(plan.IsRainSheltered(shelteredPoint), Is.False);
        }

        [Test]
        public void WorldBuilder_MaterializesThePassiveShelterAndPresentation()
        {
            CityLayout layout = CreateDefaultLayout();
            CityArchShelterPlan plan =
                CityArchShelterPlanner.Create(layout);
            var parent = new GameObject("Arch Shelter World Test");
            try
            {
                CityArchShelterWorldResult result =
                    CityArchShelterWorldBuilder.Build(
                        parent.transform,
                        layout,
                        plan);

                Assert.That(result.Root, Is.Not.Null);
                Assert.That(
                    result.Root.name,
                    Is.EqualTo(CityArchShelterWorldBuilder.RootName));
                Assert.That(result.StructureRoot, Is.Not.Null);
                Assert.That(
                    result.StructureRoot.name,
                    Is.EqualTo(
                        CityArchShelterWorldBuilder.StructureRootName));
                Assert.That(result.PropRoots, Has.Count.EqualTo(4));
                Assert.That(result.ResidentRoots, Has.Count.EqualTo(3));
                Assert.That(
                    result.PropRoots.Select(root => root.name),
                    Is.EqualTo(plan.Props.Select(prop => prop.StableId)));
                Assert.That(
                    result.ResidentRoots.Select(root => root.name),
                    Is.EqualTo(
                        plan.NpcAnchors.Select(anchor => anchor.StableId)));

                AssertRendererComponents(
                    result.StructureRoot,
                    CityMiscKind.NightlifeArchBridgeShell,
                    0);
                Renderer[] structureRenderers = result.StructureRoot
                    .GetComponentsInChildren<Renderer>(true);
                AssertDepthCoversWithBoundedOverlap(
                    plan.Placement.CommonFacadeFootprint,
                    ToXZRect(
                        EncapsulateRendererBounds(structureRenderers)),
                    CityArchShelterPlacementResolver
                        .FacadeAttachmentOverlap,
                    "materialized arch structure");
                Renderer roofRenderer = structureRenderers.Single(
                    renderer => renderer.name == "Roof_Street");
                AssertDepthCoversWithBoundedOverlap(
                    plan.Placement.CommonFacadeFootprint,
                    ToXZRect(roofRenderer.bounds),
                    CityArchShelterPlacementResolver
                        .FacadeAttachmentOverlap,
                    "materialized arch roof");
                Renderer platformSupportRenderer =
                    structureRenderers.Single(
                        renderer =>
                            renderer.name ==
                            "PlatformSupport_Masonry");
                Renderer platformSlabRenderer =
                    structureRenderers.Single(
                        renderer =>
                            renderer.name == "PlatformSlab_Street");
                AssertRectEqual(
                    plan.Platform.Footprint,
                    ToXZRect(platformSupportRenderer.bounds));
                AssertRectEqual(
                    plan.Platform.Footprint,
                    ToXZRect(platformSlabRenderer.bounds));
                Assert.That(
                    platformSupportRenderer.bounds.min.y,
                    Is.EqualTo(plan.Platform.SupportBottomY)
                        .Within(Tolerance));
                Assert.That(
                    platformSupportRenderer.bounds.max.y,
                    Is.EqualTo(platformSlabRenderer.bounds.min.y)
                        .Within(Tolerance),
                    "The visible support and top slab cannot have a gap.");
                Assert.That(
                    platformSlabRenderer.bounds.max.y,
                    Is.EqualTo(plan.Platform.SurfaceY)
                        .Within(Tolerance));
                Bounds renderedPlatformBounds =
                    platformSupportRenderer.bounds;
                renderedPlatformBounds.Encapsulate(
                    platformSlabRenderer.bounds);
                AssertBoundsEqual(
                    plan.Platform.SupportBounds,
                    renderedPlatformBounds);
                for (int index = 0; index < plan.Props.Count; index++)
                {
                    AssertRendererComponents(
                        result.PropRoots[index],
                        ResolveMiscKind(plan.Props[index].Kind),
                        plan.Props[index].Variant);
                }

                Assert.That(result.SurfaceAppearance.IsComplete, Is.True);
                Assert.That(
                    result.SurfaceAppearance.TexturedRendererCount,
                    Is.EqualTo(
                        CityArchShelterSurfaceAppearance
                            .ExpectedComponentCount));
                Assert.That(
                    result.SurfaceAppearance.AppliedRendererCount,
                    Is.EqualTo(
                        CityArchShelterSurfaceAppearance
                            .ExpectedComponentCount));
                Assert.That(
                    result.SurfaceAppearance.MissingComponentCount,
                    Is.Zero);
                Assert.That(
                    result.SurfaceAppearance.DuplicateComponentCount,
                    Is.Zero);
                foreach (string componentName in
                         CityArchShelterSurfaceAppearance
                             .SupportedComponentNames)
                {
                    Renderer renderer = result.Root
                        .GetComponentsInChildren<Renderer>(true)
                        .Single(candidate =>
                            candidate.name == componentName);
                    var surfaceProperties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(surfaceProperties);
                    Assert.That(
                        surfaceProperties.GetTexture("_BaseMap"),
                        Is.Not.Null,
                        componentName);
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            RuntimePrimitiveFactory.DefaultMaterial));
                    Assert.That(
                        renderer.shadowCastingMode,
                        Is.EqualTo(
                            UnityEngine.Rendering.ShadowCastingMode.On));
                    Assert.That(renderer.receiveShadows, Is.True);
                    Assert.That(
                        CityArchShelterSurfaceAppearance
                            .TryGetTextureResourcePath(
                                componentName,
                                out string resourcePath),
                        Is.True);
                    Assert.That(resourcePath, Is.Not.Empty);
                }

                CityArchShelterSurfaceApplyResult repeatedAppearance =
                    CityArchShelterSurfaceAppearance.Apply(
                        result.Root.transform);
                Assert.That(repeatedAppearance.IsComplete, Is.True);
                Assert.That(repeatedAppearance.AppliedRendererCount, Is.Zero);
                Assert.That(
                    repeatedAppearance.AlreadyAppliedRendererCount,
                    Is.EqualTo(
                        CityArchShelterSurfaceAppearance
                            .ExpectedComponentCount),
                    "The surface pass must be idempotent.");

                for (int index = 0;
                     index < plan.NpcAnchors.Count;
                     index++)
                {
                    Transform residentRoot = result.ResidentRoots[index];
                    CityArchShelterNpcAnchorDescriptor anchor =
                        plan.NpcAnchors[index];
                    CityArchShelterResidentRole expectedRole =
                        ResolveResidentRole(anchor.Stage);
                    CityArchShelterResidentAssetRegistry registry =
                        residentRoot.GetComponentInChildren<
                            CityArchShelterResidentAssetRegistry>(true);
                    Assert.That(registry, Is.Not.Null, anchor.StableId);
                    Assert.That(registry.Role, Is.EqualTo(expectedRole));

                    CityArchShelterResidentPresentation residentPresentation =
                        residentRoot.GetComponentInChildren<
                            CityArchShelterResidentPresentation>(true);
                    Assert.That(residentPresentation, Is.Not.Null);
                    Assert.That(residentPresentation.IsInitialized, Is.True);
                    Assert.That(
                        residentPresentation.Role,
                        Is.EqualTo(expectedRole));
                    Assert.That(
                        residentPresentation.ActiveClip,
                        Is.SameAs(registry.IdleClip));
                    Assert.That(
                        residentRoot.GetComponentsInChildren<
                            PlayerAttentionMagnet>(true),
                        Is.Empty,
                        "Shelter residents never react to or look at the " +
                        "hero.");
                }

                int beddingIndex = Enumerable.Range(0, plan.Props.Count)
                    .Single(index =>
                        plan.Props[index].Kind ==
                        CityArchShelterPropKind.Bedding);
                Renderer legacyBlanket = result.PropRoots[beddingIndex]
                    .GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.name ==
                        CityArchShelterSurfaceAppearance.BlanketComponentName);
                Assert.That(legacyBlanket.enabled, Is.False,
                    "The rigged sleeper replaces the static blanket.");
                Assert.That(
                    Contains(
                        plan.Platform.Footprint,
                        ToXZRect(EncapsulateRendererBounds(
                            result.PropRoots[beddingIndex]
                                .GetComponentsInChildren<Renderer>(true)))),
                    Is.True,
                    "The imported bedding geometry must not overhang the " +
                    "platform it rests on.");
                BoxCollider[] stepColliders = result.Root
                    .GetComponentsInChildren<BoxCollider>(true)
                    .Where(collider => collider.name.StartsWith(
                        plan.Steps.StableId + "-tread-",
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.That(
                    stepColliders,
                    Has.Length.EqualTo(plan.Steps.StepCount));
                Assert.That(
                    stepColliders.All(collider => !collider.isTrigger),
                    Is.True);

                Transform collisionRoot = result.Root.transform.Find(
                    CityArchShelterWorldBuilder.CollisionRootName);
                Assert.That(collisionRoot, Is.Not.Null);
                foreach (CityArchShelterObstacleDescriptor obstacle in
                         plan.Obstacles)
                {
                    Transform proxy = collisionRoot.Find(obstacle.StableId);
                    Assert.That(proxy, Is.Not.Null, obstacle.StableId);
                    BoxCollider collider = proxy.GetComponent<BoxCollider>();
                    Assert.That(collider, Is.Not.Null, obstacle.StableId);
                    Assert.That(collider.isTrigger, Is.False);
                    AssertBoundsEqual(obstacle.Bounds, collider.bounds);
                }

                Collider platformCollider = result.PlatformCollider;
                Assert.That(platformCollider, Is.Not.Null);
                Assert.That(platformCollider, Is.TypeOf<BoxCollider>());
                Assert.That(
                    platformCollider.name,
                    Is.EqualTo(plan.Platform.StableId));
                Assert.That(platformCollider.isTrigger, Is.False);
                AssertBoundsEqual(
                    plan.Platform.SupportBounds,
                    platformCollider.bounds);
                Assert.That(
                    result.UpperLandingCollider,
                    Is.SameAs(platformCollider),
                    "The landing must alias the one platform collider, " +
                    "not add a coplanar floating collider.");
                Assert.That(
                    result.Root.GetComponentsInChildren<BoxCollider>(true)
                        .Count(collider =>
                            collider.name == plan.Platform.StableId),
                    Is.EqualTo(1));
                Assert.That(
                    result.Root.GetComponentsInChildren<BoxCollider>(true)
                        .Any(collider =>
                            collider.name == plan.UpperLanding.StableId),
                    Is.False,
                    "The landing is a logical sub-footprint of the single " +
                    "physical platform.");
                Assert.That(
                    platformCollider.bounds.min.y,
                    Is.EqualTo(plan.Steps.LowerSurfaceY)
                        .Within(Tolerance));
                Assert.That(
                    platformCollider.bounds.max.y,
                    Is.EqualTo(plan.Platform.SurfaceY)
                        .Within(Tolerance));
                BoxCollider highestTread = stepColliders
                    .OrderByDescending(
                        collider => collider.bounds.max.y)
                    .First();
                Assert.That(
                    highestTread.bounds.max.y,
                    Is.EqualTo(platformCollider.bounds.max.y)
                        .Within(Tolerance),
                    "The last tread cannot drop below the platform.");
                if (plan.Steps.AscentDirection.x > 0f)
                {
                    Assert.That(
                        highestTread.bounds.max.x,
                        Is.EqualTo(platformCollider.bounds.min.x)
                            .Within(Tolerance));
                }
                else
                {
                    Assert.That(
                        highestTread.bounds.min.x,
                        Is.EqualTo(platformCollider.bounds.max.x)
                            .Within(Tolerance));
                }

                Assert.That(
                    result.RainShelterColliders,
                    Has.Count.EqualTo(1));
                Collider rainTrigger = result.RainShelterColliders[0];
                Assert.That(rainTrigger, Is.TypeOf<BoxCollider>());
                Assert.That(rainTrigger.isTrigger, Is.True);
                AssertBoundsEqual(
                    plan.RainOccluders.Single().Bounds,
                    rainTrigger.bounds);
                AssertDepthEqual(
                    plan.Placement.CommonFacadeFootprint,
                    ToXZRect(rainTrigger.bounds),
                    "materialized rain shelter");
                Assert.That(
                    rainTrigger.gameObject.layer,
                    Is.EqualTo(
                        CityArchShelterWorldBuilder.IgnoreRaycastLayer));
                Assert.That(
                    result.Root.GetComponentsInChildren<Collider>(true)
                        .Count(collider => collider.isTrigger),
                    Is.EqualTo(1));
                Assert.That(
                    result.Root.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);

                var presentation = result.Root
                    .AddComponent<CityArchShelterPresentation>();
                presentation.Initialize(layout.Seed);

                Assert.That(presentation.IsInitialized, Is.True);
                Assert.That(presentation.FlameRenderer, Is.Not.Null);
                Assert.That(
                    presentation.FlameRenderer.name,
                    Is.EqualTo(
                        CityArchShelterPresentation.FlameComponentName));
                Assert.That(
                    presentation.FlameRenderers.Count,
                    Is.EqualTo(5));
                Assert.That(
                    presentation.FlameRenderers
                        .Select(renderer => renderer.name),
                    Is.EqualTo(new[]
                    {
                        CityArchShelterPresentation.FlameComponentName,
                        CityArchShelterPresentation
                            .FlameOuterComponentName,
                        CityArchShelterPresentation
                            .FlameLeftComponentName,
                        CityArchShelterPresentation
                            .FlameRightComponentName,
                        CityArchShelterPresentation.EmberComponentName
                    }));
                Assert.That(presentation.SpillRenderer, Is.Not.Null);
                Assert.That(
                    presentation.SpillRenderer.name,
                    Is.EqualTo(
                        CityArchShelterPresentation.SpillComponentName));
                AudioSource[] audioSources = result.Root
                    .GetComponentsInChildren<AudioSource>(true);
                Assert.That(audioSources, Has.Length.EqualTo(1));
                Assert.That(
                    audioSources[0],
                    Is.SameAs(presentation.CrackleSource));
                Assert.That(presentation.FireHalo, Is.Not.Null);
                Assert.That(presentation.FireSparks, Is.Not.Null);
                Assert.That(
                    presentation.FireSparks.useAutoRandomSeed,
                    Is.False);
                Assert.That(
                    presentation.FireSparks.lights.enabled,
                    Is.False);
                Light[] lights = result.Root
                    .GetComponentsInChildren<Light>(true);
                Assert.That(
                    lights,
                    Has.Length.EqualTo(1),
                    "Only the causally local barrel fire may emit dynamic " +
                    "light.");
                Light fireLight = lights[0];
                Assert.That(
                    fireLight,
                    Is.SameAs(presentation.FireLight));
                Assert.That(
                    fireLight.name,
                    Is.EqualTo(
                        CityArchShelterPresentation
                            .FireLightObjectName));
                Assert.That(fireLight.type, Is.EqualTo(LightType.Point));
                Assert.That(
                    fireLight.lightmapBakeType,
                    Is.EqualTo(LightmapBakeType.Realtime));
                Assert.That(
                    fireLight.renderMode,
                    Is.EqualTo(LightRenderMode.ForcePixel));
                Assert.That(
                    fireLight.shadows,
                    Is.EqualTo(LightShadows.Soft));
                Assert.That(
                    CityArchShelterPresentation.FireLightBaseIntensity,
                    Is.InRange(85f, 110f),
                    "The barrel must stay warm and readable without " +
                    "washing the shelter.");
                Assert.That(
                    presentation.AppliedFireFactor,
                    Is.GreaterThanOrEqualTo(
                        CityArchShelterPresentation
                            .FireLightMinimumFactor));
                Assert.That(
                    fireLight.intensity,
                    Is.EqualTo(
                            CityArchShelterPresentation
                                .FireLightBaseIntensity *
                            presentation.AppliedFireFactor)
                        .Within(Tolerance));
                float expectedRange =
                    CityArchShelterPresentation.FireLightRange *
                    Mathf.Lerp(
                        0.98f,
                        1.02f,
                        Mathf.InverseLerp(
                            CityArchShelterPresentation
                                .FireLightMinimumFactor,
                            1.25f,
                            presentation.AppliedFireFactor));
                Assert.That(
                    fireLight.range,
                    Is.EqualTo(expectedRange).Within(Tolerance));
                AssertWarmFireColor(fireLight.color);
                Assert.That(
                    fireLight.transform.IsChildOf(
                        result.Root.transform),
                    Is.True);
                Bounds flameBounds = EncapsulateRendererBounds(
                    presentation.FlameRenderers.ToArray());
                Vector3 lightPosition = fireLight.transform.position;
                Assert.That(
                    lightPosition.x,
                    Is.InRange(
                        flameBounds.min.x - 0.1f,
                        flameBounds.max.x + 0.1f));
                Assert.That(
                    lightPosition.y,
                    Is.InRange(
                        flameBounds.min.y - 0.1f,
                        flameBounds.max.y + 0.1f));
                Assert.That(
                    lightPosition.z,
                    Is.InRange(
                        flameBounds.min.z - 0.1f,
                        flameBounds.max.z + 0.1f));
                Assert.That(
                    result.Root.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void RainField_LocalShelterUsesAKillTriggerNotTheGlobalDonut()
        {
            CityArchShelterPlan plan = CreateDefaultPlan();
            CityArchShelterRainOccluderDescriptor descriptor =
                plan.RainOccluders.Single();
            var host = new GameObject("Local Rain Shelter Test");
            var target = new GameObject("Local Rain Shelter Target");
            var shelterObject = new GameObject("Arch Rain Occluder");
            try
            {
                var collider = shelterObject.AddComponent<BoxCollider>();
                collider.center = descriptor.Bounds.center;
                collider.size = descriptor.Bounds.size;
                var field = host.AddComponent<CityRainField>();
                field.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    GameSessionState.DefaultCitySeed,
                    1f);

                field.SetLocalShelters(
                    new Collider[] { collider, null, collider });

                Assert.That(field.LocalShelters.Count, Is.EqualTo(1));
                Assert.That(field.LocalShelters[0], Is.SameAs(collider));
                ParticleSystem.TriggerModule trigger =
                    field.Particles.trigger;
                Assert.That(trigger.enabled, Is.True);
                Assert.That(trigger.GetCollider(0), Is.SameAs(collider));
                Assert.That(
                    trigger.enter,
                    Is.EqualTo(ParticleSystemOverlapAction.Kill));
                Assert.That(
                    trigger.inside,
                    Is.EqualTo(ParticleSystemOverlapAction.Kill));
                Assert.That(
                    trigger.exit,
                    Is.EqualTo(ParticleSystemOverlapAction.Ignore));
                Assert.That(
                    trigger.outside,
                    Is.EqualTo(ParticleSystemOverlapAction.Ignore));
                Assert.That(trigger.radiusScale, Is.EqualTo(0.5f));
                Assert.That(field.IsSheltered, Is.False);
                Assert.That(
                    field.Particles.shape.shapeType,
                    Is.EqualTo(ParticleSystemShapeType.Box),
                    "A local roof must not turn the whole follow field " +
                    "into the bus/tunnel donut.");

                field.SetLocalShelters(Array.Empty<Collider>());
                trigger = field.Particles.trigger;
                Assert.That(field.LocalShelters, Is.Empty);
                Assert.That(trigger.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shelterObject);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CityArchShelterPlan CreateDefaultPlan()
        {
            CityArchShelterPlan plan =
                CityArchShelterPlanner.Create(CreateDefaultLayout());
            Assert.That(plan.IsEnabled, Is.True);
            return plan;
        }

        private static BuildingLot FindLot(
            CityLayout layout,
            Vector2Int cell)
        {
            return layout.BuildingLots.Single(lot => lot.Cell == cell);
        }

        private static CityArchShelterPropDescriptor FindProp(
            CityArchShelterPlan plan,
            CityArchShelterPropKind kind)
        {
            return plan.Props.Single(prop => prop.Kind == kind);
        }

        private static CityArchShelterNpcAnchorDescriptor FindAnchor(
            CityArchShelterPlan plan,
            CityArchShelterNpcStageKind stage)
        {
            return plan.NpcAnchors.Single(anchor => anchor.Stage == stage);
        }

        private static CityArchShelterObstacleDescriptor FindObstacle(
            CityArchShelterPlan plan,
            CityArchShelterObstacleKind kind)
        {
            return plan.Obstacles.Single(obstacle => obstacle.Kind == kind);
        }

        private static CityMiscKind ResolveMiscKind(
            CityArchShelterPropKind kind)
        {
            switch (kind)
            {
                case CityArchShelterPropKind.BurnBarrel:
                    return CityMiscKind.NightlifeBurnBarrel;
                case CityArchShelterPropKind.Fire:
                    return CityMiscKind.NightlifeShelterFire;
                case CityArchShelterPropKind.Bedding:
                    return CityMiscKind.NightlifeShelterBedding;
                case CityArchShelterPropKind.Clutter:
                    return CityMiscKind.NightlifeShelterClutter;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unsupported shelter prop kind.");
            }
        }

        private static CityArchShelterResidentRole ResolveResidentRole(
            CityArchShelterNpcStageKind stage)
        {
            switch (stage)
            {
                case CityArchShelterNpcStageKind.StandingWarmer:
                    return CityArchShelterResidentRole.StandingWarmer;
                case CityArchShelterNpcStageKind.SeatedWarmer:
                    return CityArchShelterResidentRole.SeatedWarmer;
                case CityArchShelterNpcStageKind.Sleeper:
                    return CityArchShelterResidentRole.Sleeper;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        private static void AssertRendererComponents(
            Transform root,
            CityMiscKind kind,
            int variant)
        {
            string[] expected = Enumerable.Range(
                    0,
                    CityMiscAssetProvider.GetPartCount(kind))
                .Select(index =>
                    CityMiscAssetProvider.GetExpectedComponent(
                        kind,
                        variant,
                        index))
                .ToArray();
            string[] actual = root
                .GetComponentsInChildren<Renderer>(true)
                .Select(renderer => renderer.name)
                .ToArray();
            Assert.That(
                actual,
                Is.EquivalentTo(expected),
                $"Imported component names drifted for {kind}/{variant}.");
        }

        private static void AssertPropObstacleMatches(
            CityArchShelterPlan plan,
            CityArchShelterPropKind propKind,
            CityArchShelterObstacleKind obstacleKind)
        {
            CityArchShelterPropDescriptor prop = FindProp(plan, propKind);
            CityArchShelterObstacleDescriptor obstacle =
                FindObstacle(plan, obstacleKind);
            Assert.That(prop.BlocksMovement, Is.True);
            AssertBoundsEqual(prop.Bounds, obstacle.Bounds);
        }

        private static void AssertGuardRailBounds(
            CityArchShelterPlan plan,
            CityArchShelterObstacleDescriptor guard,
            bool north)
        {
            Rect platform = plan.Platform.Footprint;
            float thickness = CityArchShelterPlacementResolver
                .PlatformGuardRailThickness;
            float height = CityArchShelterPlacementResolver
                .PlatformGuardRailHeight;
            float edgeZ = north ? platform.yMax : platform.yMin;
            float centerZ = edgeZ + (north ? 1f : -1f) *
                thickness * 0.5f;
            var expected = new Bounds(
                new Vector3(
                    platform.center.x,
                    plan.Platform.SurfaceY + height * 0.5f,
                    centerZ),
                new Vector3(platform.width, height, thickness));

            AssertBoundsEqual(expected, guard.Bounds);
            Assert.That(
                north ? guard.Bounds.min.z : guard.Bounds.max.z,
                Is.EqualTo(edgeZ).Within(Tolerance),
                "The guard inner face must touch, but not cover, the " +
                "platform edge.");
        }

        private static void AssertWestGuardRailBounds(
            CityArchShelterPlan plan,
            CityArchShelterObstacleDescriptor guard)
        {
            Rect platform = plan.Platform.Footprint;
            float thickness = CityArchShelterPlacementResolver
                .PlatformGuardRailThickness;
            float height = CityArchShelterPlacementResolver
                .PlatformGuardRailHeight;
            var expected = new Bounds(
                new Vector3(
                    platform.xMin - thickness * 0.5f,
                    plan.Platform.SurfaceY + height * 0.5f,
                    (platform.yMin + plan.Steps.Footprint.yMin) * 0.5f),
                new Vector3(
                    thickness,
                    height,
                    plan.Steps.Footprint.yMin - platform.yMin));

            AssertBoundsEqual(expected, guard.Bounds);
            Assert.That(guard.Bounds.max.x,
                Is.EqualTo(platform.xMin).Within(Tolerance));
            Assert.That(guard.Bounds.max.z,
                Is.EqualTo(plan.Steps.Footprint.yMin).Within(Tolerance),
                "The west guard must end exactly where the stair opening " +
                "begins.");
        }

        private static void AssertPlansEqual(
            CityArchShelterPlan expected,
            CityArchShelterPlan actual)
        {
            Assert.That(actual.IsEnabled, Is.EqualTo(expected.IsEnabled));
            Assert.That(
                actual.Placement.WestCell,
                Is.EqualTo(expected.Placement.WestCell));
            Assert.That(
                actual.Placement.EastCell,
                Is.EqualTo(expected.Placement.EastCell));
            AssertRectEqual(
                expected.Placement.CommonFacadeFootprint,
                actual.Placement.CommonFacadeFootprint);
            AssertRectEqual(
                expected.Placement.PassageFootprint,
                actual.Placement.PassageFootprint);
            AssertRectEqual(
                expected.Placement.ShelteredFootprint,
                actual.Placement.ShelteredFootprint);
            AssertRectEqual(
                expected.Placement.TableauFootprint,
                actual.Placement.TableauFootprint);
            AssertRectEqual(
                expected.Placement.RailSuppressionFootprint,
                actual.Placement.RailSuppressionFootprint);
            Assert.That(
                actual.Placement.StructurePosition,
                Is.EqualTo(expected.Placement.StructurePosition));
            Assert.That(
                actual.Placement.StructureRotation,
                Is.EqualTo(expected.Placement.StructureRotation));
            AssertBoundsEqual(
                expected.Placement.StructureBounds,
                actual.Placement.StructureBounds);
            Assert.That(actual.Steps, Is.EqualTo(expected.Steps));
            Assert.That(
                actual.UpperLanding,
                Is.EqualTo(expected.UpperLanding));
            Assert.That(
                actual.Platform,
                Is.EqualTo(expected.Platform));
            CollectionAssert.AreEqual(
                expected.ClearLanes,
                actual.ClearLanes);
            CollectionAssert.AreEqual(
                expected.NpcAnchors,
                actual.NpcAnchors);
            CollectionAssert.AreEqual(expected.Props, actual.Props);
            CollectionAssert.AreEqual(
                expected.Obstacles,
                actual.Obstacles);
            CollectionAssert.AreEqual(
                expected.RainOccluders,
                actual.RainOccluders);
        }

        private static int SideOfSteps(Rect steps, Vector3 position)
        {
            if (position.x < steps.xMin - Tolerance)
            {
                return -1;
            }

            if (position.x > steps.xMax + Tolerance)
            {
                return 1;
            }

            return 0;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x - Tolerance &&
                   point.x <= bounds.max.x + Tolerance &&
                   point.z >= bounds.min.z - Tolerance &&
                   point.z <= bounds.max.z + Tolerance;
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            return left.xMin < right.xMax - Tolerance &&
                   left.xMax > right.xMin + Tolerance &&
                   left.yMin < right.yMax - Tolerance &&
                   left.yMax > right.yMin + Tolerance;
        }

        private static bool OverlapsStrict(Bounds left, Bounds right)
        {
            return left.min.x < right.max.x - Tolerance &&
                   left.max.x > right.min.x + Tolerance &&
                   left.min.y < right.max.y - Tolerance &&
                   left.max.y > right.min.y + Tolerance &&
                   left.min.z < right.max.z - Tolerance &&
                   left.max.z > right.min.z + Tolerance;
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static Bounds EncapsulateRendererBounds(
            Renderer[] renderers)
        {
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void AssertWarmFireColor(Color actual)
        {
            Color steady =
                CityArchShelterPresentation.FireLightColor;
            Color gutter =
                CityArchShelterPresentation.FireGutterColor;
            Assert.That(
                actual.r,
                Is.InRange(
                    Mathf.Min(steady.r, gutter.r) - Tolerance,
                    Mathf.Max(steady.r, gutter.r) + Tolerance));
            Assert.That(
                actual.g,
                Is.InRange(
                    Mathf.Min(steady.g, gutter.g) - Tolerance,
                    Mathf.Max(steady.g, gutter.g) + Tolerance));
            Assert.That(
                actual.b,
                Is.InRange(
                    Mathf.Min(steady.b, gutter.b) - Tolerance,
                    Mathf.Max(steady.b, gutter.b) + Tolerance));
            Assert.That(actual.r, Is.GreaterThan(actual.g * 2f));
            Assert.That(actual.g, Is.GreaterThan(actual.b * 3f));
        }

        private static void AssertRectEqual(Rect expected, Rect actual)
        {
            Assert.That(
                actual.xMin,
                Is.EqualTo(expected.xMin).Within(Tolerance));
            Assert.That(
                actual.xMax,
                Is.EqualTo(expected.xMax).Within(Tolerance));
            Assert.That(
                actual.yMin,
                Is.EqualTo(expected.yMin).Within(Tolerance));
            Assert.That(
                actual.yMax,
                Is.EqualTo(expected.yMax).Within(Tolerance));
        }

        private static void AssertDepthEqual(
            Rect expected,
            Rect actual,
            string label)
        {
            Assert.That(
                actual.yMin,
                Is.EqualTo(expected.yMin).Within(Tolerance),
                $"{label} lost the south end of the shared facade.");
            Assert.That(
                actual.yMax,
                Is.EqualTo(expected.yMax).Within(Tolerance),
                $"{label} lost the north end of the shared facade.");
            Assert.That(
                actual.height,
                Is.EqualTo(expected.height).Within(Tolerance),
                $"{label} must occupy the complete shared Z depth.");
        }

        private static void AssertDepthCoversWithBoundedOverlap(
            Rect expected,
            Rect actual,
            float maximumEndOverlap,
            string label)
        {
            Assert.That(
                actual.yMin,
                Is.LessThanOrEqualTo(expected.yMin + Tolerance),
                $"{label} lost the south end of the shared facade.");
            Assert.That(
                actual.yMax,
                Is.GreaterThanOrEqualTo(expected.yMax - Tolerance),
                $"{label} lost the north end of the shared facade.");
            Assert.That(
                expected.yMin - actual.yMin,
                Is.InRange(-Tolerance, maximumEndOverlap + Tolerance),
                $"{label} has excessive south attachment overlap.");
            Assert.That(
                actual.yMax - expected.yMax,
                Is.InRange(-Tolerance, maximumEndOverlap + Tolerance),
                $"{label} has excessive north attachment overlap.");
        }

        private static void AssertBoundsEqual(
            Bounds expected,
            Bounds actual)
        {
            Assert.That(
                actual.center.x,
                Is.EqualTo(expected.center.x).Within(Tolerance));
            Assert.That(
                actual.center.y,
                Is.EqualTo(expected.center.y).Within(Tolerance));
            Assert.That(
                actual.center.z,
                Is.EqualTo(expected.center.z).Within(Tolerance));
            Assert.That(
                actual.size.x,
                Is.EqualTo(expected.size.x).Within(Tolerance));
            Assert.That(
                actual.size.y,
                Is.EqualTo(expected.size.y).Within(Tolerance));
            Assert.That(
                actual.size.z,
                Is.EqualTo(expected.size.z).Within(Tolerance));
        }
    }
}
