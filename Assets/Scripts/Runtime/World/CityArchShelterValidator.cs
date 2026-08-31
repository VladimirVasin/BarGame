using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityArchShelterValidator
    {
        private const float GeometryTolerance = 0.01f;
        private const float DirectionTolerance = 0.01f;
        private const int ExpectedClearLaneCount = 1;
        private const int ExpectedNpcAnchorCount = 3;
        private const int ExpectedPropCount = 4;
        private const int ExpectedObstacleCount = 10;
        private const int ExpectedRainOccluderCount = 1;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityArchShelterPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsEnabled)
            {
                ValidateAbsent(plan);
                return;
            }

            if (!CityArchShelterPlacementResolver.TryCreate(
                    layout,
                    out CityArchShelterPlacement expected))
            {
                throw new InvalidOperationException(
                    "An enabled arch shelter requires its measured default " +
                    "Nightlife gap.");
            }

            ValidatePlacement(plan.Placement, expected);
            ValidateSteps(plan);
            ValidateUpperLanding(plan);
            ValidatePlatform(plan);
            ValidateCounts(plan);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            RequireUniqueId(ids, plan.Steps.StableId);
            RequireUniqueId(ids, plan.UpperLanding.StableId);
            RequireUniqueId(ids, plan.Platform.StableId);
            ValidateClearLanes(plan, ids);
            ValidateProps(plan, ids);
            ValidateNpcAnchors(plan, ids);
            ValidateObstacles(plan, ids);
            ValidateRainOccluders(plan, ids);
        }

        private static void ValidateAbsent(CityArchShelterPlan plan)
        {
            if (plan.ClearLanes.Count != 0 ||
                plan.NpcAnchors.Count != 0 ||
                plan.Props.Count != 0 ||
                plan.Obstacles.Count != 0 ||
                plan.RainOccluders.Count != 0 ||
                 !string.IsNullOrEmpty(plan.Steps.StableId) ||
                 !string.IsNullOrEmpty(plan.UpperLanding.StableId) ||
                 !string.IsNullOrEmpty(plan.Platform.StableId))
            {
                throw new InvalidOperationException(
                    "A disabled arch-shelter plan must be empty.");
            }
        }

        private static void ValidatePlacement(
            CityArchShelterPlacement actual,
            CityArchShelterPlacement expected)
        {
            Rect measuredCommonFacade = Rect.MinMaxRect(
                actual.WestBuildingBounds.max.x,
                Mathf.Max(
                    actual.WestBuildingBounds.min.z,
                    actual.EastBuildingBounds.min.z),
                actual.EastBuildingBounds.min.x,
                Mathf.Min(
                    actual.WestBuildingBounds.max.z,
                    actual.EastBuildingBounds.max.z));
            if (actual.WestCell != expected.WestCell ||
                actual.EastCell != expected.EastCell ||
                !Approximately(
                    actual.WestBuildingBounds,
                    expected.WestBuildingBounds) ||
                !Approximately(
                    actual.EastBuildingBounds,
                    expected.EastBuildingBounds) ||
                !Approximately(
                    actual.CommonFacadeFootprint,
                    expected.CommonFacadeFootprint) ||
                !Approximately(
                    actual.CommonFacadeFootprint,
                    measuredCommonFacade) ||
                !Approximately(
                    actual.PassageFootprint,
                    expected.PassageFootprint) ||
                !Approximately(
                    actual.ShelteredFootprint,
                    expected.ShelteredFootprint) ||
                !Approximately(
                    actual.TableauFootprint,
                    expected.TableauFootprint) ||
                !Approximately(
                    actual.RailSuppressionFootprint,
                    expected.RailSuppressionFootprint) ||
                !Approximately(actual.WestSurfaceY, expected.WestSurfaceY) ||
                !Approximately(actual.EastSurfaceY, expected.EastSurfaceY) ||
                !Approximately(
                    actual.SharedBoundaryX,
                    expected.SharedBoundaryX) ||
                !Approximately(
                    actual.StructurePosition,
                    expected.StructurePosition) ||
                !Approximately(
                    actual.StructureBounds,
                    expected.StructureBounds) ||
                Quaternion.Angle(
                    actual.StructureRotation,
                    expected.StructureRotation) > DirectionTolerance)
            {
                throw new InvalidOperationException(
                    "The arch shelter no longer matches the measured " +
                    "building presentation gap.");
            }

            if (!IsPositiveFinite(actual.CommonFacadeFootprint) ||
                !IsPositiveFinite(actual.PassageFootprint) ||
                !IsPositiveFinite(actual.ShelteredFootprint) ||
                !IsPositiveFinite(actual.TableauFootprint) ||
                !IsPositiveFinite(actual.StructureBounds) ||
                actual.PassageWidth < 10f ||
                actual.PassageDepth < 8f ||
                !Contains(
                    actual.CommonFacadeFootprint,
                    actual.PassageFootprint) ||
                !Approximately(
                    actual.ShelteredFootprint,
                    actual.CommonFacadeFootprint) ||
                !Contains(
                    actual.PassageFootprint,
                    actual.TableauFootprint) ||
                !Approximately(
                    actual.PassageFootprint.yMin -
                    actual.CommonFacadeFootprint.yMin,
                    CityArchShelterPlacementResolver.PortalInset) ||
                !Approximately(
                    actual.CommonFacadeFootprint.yMax -
                    actual.PassageFootprint.yMax,
                    CityArchShelterPlacementResolver.PortalInset) ||
                !Approximately(
                    actual.StructureBounds.min.z,
                    actual.CommonFacadeFootprint.yMin) ||
                !Approximately(
                    actual.StructureBounds.max.z,
                    actual.CommonFacadeFootprint.yMax) ||
                actual.TopIsWalkable)
            {
                throw new InvalidOperationException(
                    "The arch shelter requires a bounded ground passage " +
                    "and a non-walkable overhead gallery.");
            }
        }

        private static void ValidateSteps(CityArchShelterPlan plan)
        {
            CityArchShelterPlacement placement = plan.Placement;
            CityArchShelterStepDescriptor steps = plan.Steps;
            float lower = Mathf.Min(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            float upper = Mathf.Max(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            Vector3 expectedAscent = placement.WestSurfaceY <=
                                     placement.EastSurfaceY
                ? Vector3.right
                : Vector3.left;
            if (string.IsNullOrWhiteSpace(steps.StableId) ||
                !Approximately(
                    steps.Footprint,
                    placement.RailSuppressionFootprint) ||
                !Contains(
                    placement.PassageFootprint,
                    steps.Footprint) ||
                !Approximately(steps.LowerSurfaceY, lower) ||
                !Approximately(steps.UpperSurfaceY, upper) ||
                Vector3.Distance(
                    steps.AscentDirection,
                    expectedAscent) > DirectionTolerance ||
                steps.StepCount < 1 ||
                steps.StepRise <= 0f ||
                steps.StepRise >
                    CityRoadGroundBoundaryPlanner.MaximumSafeStep +
                    GeometryTolerance ||
                steps.TreadDepth <= 0f)
            {
                throw new InvalidOperationException(
                    "The authored shelter steps must occupy the complete " +
                    "rail opening and safely join both terrace datums.");
            }

            if (OverlapsStrict(
                    steps.Footprint,
                    placement.TableauFootprint))
            {
                throw new InvalidOperationException(
                    "The local stair and fire-tableau bands must remain " +
                    "independent inside the full-depth arch.");
            }
        }

        private static void ValidateUpperLanding(CityArchShelterPlan plan)
        {
            CityArchShelterLandingDescriptor landing = plan.UpperLanding;
            CityArchShelterStepDescriptor steps = plan.Steps;
            if (string.IsNullOrWhiteSpace(landing.StableId) ||
                !IsPositiveFinite(landing.Footprint) ||
                !Contains(
                    plan.Placement.PassageFootprint,
                    landing.Footprint) ||
                !Approximately(
                    landing.Footprint.yMin,
                    steps.Footprint.yMin) ||
                !Approximately(
                    landing.Footprint.yMax,
                    steps.Footprint.yMax) ||
                landing.Footprint.width <
                    CityArchShelterPlacementResolver.UpperLandingLength -
                    GeometryTolerance ||
                !Approximately(landing.SurfaceY, steps.UpperSurfaceY) ||
                OverlapsStrict(landing.Footprint, steps.Footprint))
            {
                throw new InvalidOperationException(
                    "The upper landing must be a full safe continuation of " +
                    "the last stair tread on the upper datum.");
            }

            bool ascendsEast = steps.AscentDirection.x > 0f;
            float seamGap = ascendsEast
                ? landing.Footprint.xMin - steps.Footprint.xMax
                : steps.Footprint.xMin - landing.Footprint.xMax;
            float exitX = ascendsEast
                ? landing.Footprint.xMax
                : landing.Footprint.xMin;
            if (!Approximately(seamGap, 0f) ||
                !Approximately(
                    plan.Placement.ResolveSurfaceY(exitX),
                    landing.SurfaceY))
            {
                throw new InvalidOperationException(
                    "The upper landing must abut the last tread without a " +
                    "horizontal or vertical seam.");
            }

            for (int index = 0; index < plan.ClearLanes.Count; index++)
            {
                if (OverlapsStrict(
                        landing.Footprint,
                        plan.ClearLanes[index].Footprint))
                {
                    throw new InvalidOperationException(
                        "The upper landing cannot close a longitudinal " +
                        "side lane.");
                }
            }

            for (int index = 0; index < plan.Props.Count; index++)
            {
                if (OverlapsStrict(
                        landing.Footprint,
                        ToXZRect(plan.Props[index].Bounds)))
                {
                    throw new InvalidOperationException(
                        "The upper landing exit must remain clear of the " +
                        "shelter tableau.");
                }
            }

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
            for (int index = 0; index < plan.Obstacles.Count; index++)
            {
                if (OverlapsStrict(
                        landingClearance,
                        plan.Obstacles[index].Bounds))
                {
                    throw new InvalidOperationException(
                        "The upper landing and its exit require full " +
                        "headroom without rail-return blockers.");
                }
            }
        }

        private static void ValidatePlatform(CityArchShelterPlan plan)
        {
            CityArchShelterPlatformDescriptor platform = plan.Platform;
            CityArchShelterStepDescriptor steps = plan.Steps;
            CityArchShelterLandingDescriptor landing = plan.UpperLanding;
            Rect expectedFootprint;
            if (steps.AscentDirection.x > 0f)
            {
                expectedFootprint = Rect.MinMaxRect(
                    steps.Footprint.xMax,
                    plan.Placement.CommonFacadeFootprint.yMin,
                    plan.Placement.PassageFootprint.xMax -
                    CityArchShelterPlacementResolver.PlatformWallInset,
                    steps.Footprint.yMax);
            }
            else
            {
                expectedFootprint = Rect.MinMaxRect(
                    plan.Placement.PassageFootprint.xMin +
                    CityArchShelterPlacementResolver.PlatformWallInset,
                    plan.Placement.CommonFacadeFootprint.yMin,
                    steps.Footprint.xMin,
                    steps.Footprint.yMax);
            }

            if (!IsPositiveFinite(platform.Footprint) ||
                !IsPositiveFinite(platform.SupportBounds) ||
                !Approximately(platform.Footprint, expectedFootprint) ||
                !Contains(
                    plan.Placement.CommonFacadeFootprint,
                    platform.Footprint) ||
                !Contains(platform.Footprint, landing.Footprint) ||
                !Approximately(
                    platform.SupportBottomY,
                    steps.LowerSurfaceY) ||
                !Approximately(platform.SurfaceY, steps.UpperSurfaceY) ||
                !Approximately(platform.SupportHeight, steps.TotalRise))
            {
                throw new InvalidOperationException(
                    "The shelter platform must be one wall-attached " +
                    "upper-datum footprint containing the stair landing, " +
                    "reaching the south wall end and supporting the local " +
                    "tableau.");
            }

            bool ascendsEast = steps.AscentDirection.x > 0f;
            float stairSeam = ascendsEast
                ? platform.Footprint.xMin - steps.Footprint.xMax
                : steps.Footprint.xMin - platform.Footprint.xMax;
            float wallSeam = ascendsEast
                ? plan.Placement.PassageFootprint.xMax -
                  platform.Footprint.xMax -
                  CityArchShelterPlacementResolver.PlatformWallInset
                : platform.Footprint.xMin -
                  plan.Placement.PassageFootprint.xMin -
                  CityArchShelterPlacementResolver.PlatformWallInset;
            float southWallSeam = platform.Footprint.yMin -
                                  plan.Placement
                                      .CommonFacadeFootprint.yMin;
            if (!Approximately(stairSeam, 0f) ||
                !Approximately(wallSeam, 0f) ||
                !Approximately(southWallSeam, 0f))
            {
                throw new InvalidOperationException(
                    "The supported service terrace must join the highest stair " +
                    "tread, the upper facade wall and the raw south wall " +
                    "end without a seam.");
            }

            for (int index = 0; index < plan.ClearLanes.Count; index++)
            {
                CityArchShelterClearLaneDescriptor lane =
                    plan.ClearLanes[index];
                if (OverlapsStrict(platform.Footprint, lane.Footprint))
                {
                    throw new InvalidOperationException(
                        "The wall-attached service terrace cannot close the lower " +
                        "longitudinal lane.");
                }
            }

            ValidatePlatformSupport(plan);
        }

        private static void ValidatePlatformSupport(
            CityArchShelterPlan plan)
        {
            Rect platform = plan.Platform.Footprint;
            for (int index = 0; index < plan.Props.Count; index++)
            {
                CityArchShelterPropDescriptor prop = plan.Props[index];
                if ((prop.Kind == CityArchShelterPropKind.BurnBarrel ||
                     prop.Kind == CityArchShelterPropKind.Bedding) &&
                    (!Contains(platform, ToXZRect(prop.Bounds)) ||
                     !Approximately(
                         prop.Position.y,
                         plan.Platform.SurfaceY)))
                {
                    throw new InvalidOperationException(
                        $"Shelter prop '{prop.StableId}' must be visibly " +
                        "supported by the platform.");
                }
            }

            CityArchShelterPropDescriptor bedding = FindProp(
                plan,
                CityArchShelterPropKind.Bedding);
            for (int index = 0; index < plan.NpcAnchors.Count; index++)
            {
                CityArchShelterNpcAnchorDescriptor anchor =
                    plan.NpcAnchors[index];
                Rect support = anchor.Stage ==
                               CityArchShelterNpcStageKind.Sleeper
                    ? ToXZRect(bedding.Bounds)
                    : Rect.MinMaxRect(
                        anchor.Position.x - 0.32f,
                        anchor.Position.z - 0.32f,
                        anchor.Position.x + 0.32f,
                        anchor.Position.z + 0.32f);
                float expectedY = anchor.Stage ==
                                  CityArchShelterNpcStageKind.Sleeper
                    ? bedding.Bounds.max.y
                    : plan.Platform.SurfaceY;
                if (!Contains(platform, support) ||
                    !Approximately(anchor.Position.y, expectedY))
                {
                    throw new InvalidOperationException(
                        $"Staged resident '{anchor.StableId}' must be " +
                        "visibly supported by the platform or bedding.");
                }
            }
        }

        private static void ValidateCounts(CityArchShelterPlan plan)
        {
            if (plan.ClearLanes.Count != ExpectedClearLaneCount ||
                plan.NpcAnchors.Count != ExpectedNpcAnchorCount ||
                plan.Props.Count != ExpectedPropCount ||
                plan.Obstacles.Count != ExpectedObstacleCount ||
                plan.RainOccluders.Count != ExpectedRainOccluderCount)
            {
                throw new InvalidOperationException(
                    "The arch shelter requires one clear lower lane, three " +
                    "staged residents, four authored prop assemblies, seven " +
                    "tableau/structure blockers, three platform guards and " +
                    "one rain volume.");
            }
        }

        private static void ValidateClearLanes(
            CityArchShelterPlan plan,
            ISet<string> ids)
        {
            Rect passage = plan.Placement.PassageFootprint;
            bool foundWest = false;
            for (int index = 0; index < plan.ClearLanes.Count; index++)
            {
                CityArchShelterClearLaneDescriptor lane =
                    plan.ClearLanes[index];
                RequireUniqueId(ids, lane.StableId);
                if (!IsPositiveFinite(lane.Footprint) ||
                    !IsFinite(lane.SurfaceY) ||
                    !IsFinite(lane.MinimumHeadroom) ||
                    lane.MinimumHeadroom <
                    CityArchShelterPlacementResolver.MinimumClearHeight -
                    GeometryTolerance ||
                    !Contains(passage, lane.Footprint) ||
                    lane.Footprint.yMin > passage.yMin + GeometryTolerance ||
                    lane.Footprint.yMax < passage.yMax - GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Clear lane '{lane.StableId}' is not a continuous " +
                        "full-depth route.");
                }

                if (lane.Footprint.center.x <
                    plan.Placement.SharedBoundaryX)
                {
                    foundWest = true;
                    if (!Approximately(
                            lane.SurfaceY,
                            plan.Placement.WestSurfaceY))
                    {
                        throw new InvalidOperationException(
                            "The west clear lane must retain the west datum.");
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        "The raised east service terrace is a guarded " +
                        "dead-end, not a through-lane.");
                }
            }

            if (!foundWest)
            {
                throw new InvalidOperationException(
                    "The shelter requires one full-depth west ground lane.");
            }
        }

        private static void ValidateProps(
            CityArchShelterPlan plan,
            ISet<string> ids)
        {
            var kinds = new HashSet<CityArchShelterPropKind>();
            int blockingCount = 0;
            for (int index = 0; index < plan.Props.Count; index++)
            {
                CityArchShelterPropDescriptor prop = plan.Props[index];
                RequireUniqueId(ids, prop.StableId);
                if (!Enum.IsDefined(typeof(CityArchShelterPropKind), prop.Kind) ||
                    !kinds.Add(prop.Kind) ||
                    prop.Variant < 0 ||
                    !IsFinite(prop.Position) ||
                    !IsFinite(prop.Rotation) ||
                    !IsPositiveFinite(prop.Bounds) ||
                    !Contains(
                        plan.Placement.TableauFootprint,
                        ToXZRect(prop.Bounds)))
                {
                    throw new InvalidOperationException(
                        $"Authored shelter prop '{prop.StableId}' is invalid.");
                }

                if (prop.BlocksMovement)
                {
                    blockingCount++;
                }

                if (prop.Kind == CityArchShelterPropKind.BurnBarrel &&
                    Mathf.Abs(
                        prop.Position.x -
                        plan.Placement.PassageFootprint.center.x) > 1.35f)
                {
                    throw new InvalidOperationException(
                        "The burn barrel must remain the central focus.");
                }

                if (OverlapsStrict(
                        ToXZRect(prop.Bounds),
                        plan.Steps.Footprint))
                {
                    throw new InvalidOperationException(
                        $"Authored shelter prop '{prop.StableId}' overlaps " +
                        "the terrace steps.");
                }
            }

            if (kinds.Count != ExpectedPropCount || blockingCount != 3)
            {
                throw new InvalidOperationException(
                    "The shelter prop recipe must contain barrel, flame, " +
                    "bedding and clutter with only the flame non-blocking.");
            }

            CityArchShelterPropDescriptor barrel = FindProp(
                plan,
                CityArchShelterPropKind.BurnBarrel);
            float ascentSign = plan.Steps.AscentDirection.x;
            float horizontalClearance = ascentSign > 0f
                ? barrel.Bounds.min.x - plan.Steps.Footprint.xMax
                : plan.Steps.Footprint.xMin - barrel.Bounds.max.x;
            float upperY = Mathf.Max(
                plan.Placement.WestSurfaceY,
                plan.Placement.EastSurfaceY);
            if (horizontalClearance < 0.40f ||
                !Approximately(barrel.Position.y, upperY))
            {
                throw new InvalidOperationException(
                    "The burn barrel must stand clear of the last step on " +
                    "the native upper terrace.");
            }
        }

        private static void ValidateNpcAnchors(
            CityArchShelterPlan plan,
            ISet<string> ids)
        {
            var stages = new HashSet<CityArchShelterNpcStageKind>();
            CityArchShelterPropDescriptor barrel = FindProp(
                plan,
                CityArchShelterPropKind.BurnBarrel);
            CityArchShelterPropDescriptor bedding = FindProp(
                plan,
                CityArchShelterPropKind.Bedding);
            for (int index = 0; index < plan.NpcAnchors.Count; index++)
            {
                CityArchShelterNpcAnchorDescriptor anchor =
                    plan.NpcAnchors[index];
                RequireUniqueId(ids, anchor.StableId);
                Vector2 position = new Vector2(
                    anchor.Position.x,
                    anchor.Position.z);
                if (!Enum.IsDefined(
                        typeof(CityArchShelterNpcStageKind),
                        anchor.Stage) ||
                    !stages.Add(anchor.Stage) ||
                    !IsFinite(anchor.Position) ||
                    !IsFinite(anchor.Facing) ||
                    Mathf.Abs(anchor.Facing.y) > DirectionTolerance ||
                    Mathf.Abs(anchor.Facing.sqrMagnitude - 1f) >
                    DirectionTolerance ||
                    !Contains(
                        plan.Placement.TableauFootprint,
                        position))
                {
                    throw new InvalidOperationException(
                        $"Staged resident '{anchor.StableId}' is invalid.");
                }

                Rect residentFootprint = anchor.Stage ==
                                         CityArchShelterNpcStageKind.Sleeper
                    ? ToXZRect(bedding.Bounds)
                    : Rect.MinMaxRect(
                        anchor.Position.x - 0.32f,
                        anchor.Position.z - 0.32f,
                        anchor.Position.x + 0.32f,
                        anchor.Position.z + 0.32f);
                float expectedY = anchor.Stage ==
                                  CityArchShelterNpcStageKind.Sleeper
                    ? bedding.Bounds.max.y
                    : barrel.Position.y;
                if (!Approximately(anchor.Position.y, expectedY) ||
                    OverlapsStrict(
                        residentFootprint,
                        plan.Steps.Footprint))
                {
                    throw new InvalidOperationException(
                        $"Staged resident '{anchor.StableId}' is not on its " +
                        "declared physical surface or overlaps the steps.");
                }

                for (int laneIndex = 0;
                     laneIndex < plan.ClearLanes.Count;
                     laneIndex++)
                {
                    if (OverlapsStrict(
                            plan.ClearLanes[laneIndex].Footprint,
                            residentFootprint))
                    {
                        throw new InvalidOperationException(
                            $"Staged resident '{anchor.StableId}' blocks a " +
                        "clear side lane.");
                    }
                }

                for (int propIndex = 0;
                     propIndex < plan.Props.Count;
                     propIndex++)
                {
                    CityArchShelterPropDescriptor prop =
                        plan.Props[propIndex];
                    if (anchor.Stage ==
                            CityArchShelterNpcStageKind.Sleeper &&
                        prop.Kind == CityArchShelterPropKind.Bedding)
                    {
                        continue;
                    }

                    if (OverlapsStrict(
                            residentFootprint,
                            ToXZRect(prop.Bounds)))
                    {
                        throw new InvalidOperationException(
                            $"Staged resident '{anchor.StableId}' overlaps " +
                            $"prop '{prop.StableId}'.");
                    }
                }
            }

            if (stages.Count != ExpectedNpcAnchorCount)
            {
                throw new InvalidOperationException(
                    "The shelter requires standing, seated and sleeping " +
                    "resident stages.");
            }
        }

        private static void ValidateObstacles(
            CityArchShelterPlan plan,
            ISet<string> ids)
        {
            var kinds = new HashSet<CityArchShelterObstacleKind>();
            for (int index = 0; index < plan.Obstacles.Count; index++)
            {
                CityArchShelterObstacleDescriptor obstacle =
                    plan.Obstacles[index];
                RequireUniqueId(ids, obstacle.StableId);
                if (!Enum.IsDefined(
                        typeof(CityArchShelterObstacleKind),
                        obstacle.Kind) ||
                    !kinds.Add(obstacle.Kind) ||
                    !IsPositiveFinite(obstacle.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Shelter blocker '{obstacle.StableId}' is invalid.");
                }

                if (obstacle.Kind ==
                    CityArchShelterObstacleKind.OverheadGallery)
                {
                    Rect roof = ToXZRect(obstacle.Bounds);
                    Rect common = plan.Placement.CommonFacadeFootprint;
                    if (!Contains(roof, common) ||
                        !Approximately(roof.yMin, common.yMin) ||
                        !Approximately(roof.yMax, common.yMax))
                    {
                        throw new InvalidOperationException(
                            "The non-walkable arch roof must span the " +
                            "complete common side-facade depth.");
                    }
                }
                else if (obstacle.Kind ==
                         CityArchShelterObstacleKind.VaultCrown)
                {
                    ValidateVaultCrown(plan, obstacle);
                }

                for (int laneIndex = 0;
                     laneIndex < plan.ClearLanes.Count;
                     laneIndex++)
                {
                    if (OverlapsStrict(
                            obstacle.Bounds,
                            plan.ClearLanes[laneIndex].ClearanceBounds))
                    {
                        throw new InvalidOperationException(
                            $"Shelter blocker '{obstacle.StableId}' intrudes " +
                            "into a clear side lane.");
                    }
                }
            }

            if (kinds.Count != ExpectedObstacleCount)
            {
                throw new InvalidOperationException(
                    "Each shelter blocker kind must appear exactly once.");
            }

            ValidatePlatformWallAttachment(plan);
            ValidatePlatformGuardRails(plan);
        }

        private static void ValidateVaultCrown(
            CityArchShelterPlan plan,
            CityArchShelterObstacleDescriptor vault)
        {
            CityArchShelterPlacement placement = plan.Placement;
            Bounds overhead = FindObstacle(
                plan,
                CityArchShelterObstacleKind.OverheadGallery).Bounds;
            float lowerSurface = Mathf.Min(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            Rect footprint = ToXZRect(vault.Bounds);
            Rect common = placement.CommonFacadeFootprint;
            float inset = CityArchShelterPlacementResolver.VaultDepthInset;
            if (!Approximately(
                    vault.Bounds.min.y,
                    lowerSurface +
                    CityArchShelterPlacementResolver
                        .VaultCrownClearanceAboveLowerSurface) ||
                !Approximately(vault.Bounds.max.y, overhead.min.y) ||
                !Approximately(
                    vault.Bounds.center.x,
                    placement.StructurePosition.x) ||
                !Approximately(
                    vault.Bounds.size.x,
                    CityArchShelterPlacementResolver.VaultCrownHalfWidth *
                    2f) ||
                !Approximately(footprint.yMin, common.yMin + inset) ||
                !Approximately(footprint.yMax, common.yMax - inset))
            {
                throw new InvalidOperationException(
                    "The vault crown blocker must fill the measured " +
                    "masonry below the overhead gallery.");
            }
        }

        private static void ValidatePlatformWallAttachment(
            CityArchShelterPlan plan)
        {
            bool ascendsEast = plan.Steps.AscentDirection.x > 0f;
            Bounds wall = FindObstacle(
                plan,
                ascendsEast
                    ? CityArchShelterObstacleKind.EastAttachment
                    : CityArchShelterObstacleKind.WestAttachment).Bounds;
            float physicalSeam = ascendsEast
                ? wall.min.x - plan.Platform.Footprint.xMax
                : plan.Platform.Footprint.xMin - wall.max.x;
            if (!Approximately(physicalSeam, 0f))
            {
                throw new InvalidOperationException(
                    "The platform must meet the physical upper-facade " +
                    "support without a fall-through seam.");
            }
        }

        private static void ValidatePlatformGuardRails(
            CityArchShelterPlan plan)
        {
            CityArchShelterObstacleDescriptor north = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformNorthGuardRail);
            CityArchShelterObstacleDescriptor south = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformSouthGuardRail);
            CityArchShelterObstacleDescriptor west = FindObstacle(
                plan,
                CityArchShelterObstacleKind.PlatformWestGuardRail);
            Bounds expectedNorth = CreateGuardRailBounds(
                plan.Platform.Footprint.xMin,
                plan.Platform.Footprint.xMax,
                plan.Platform.Footprint.yMax +
                CityArchShelterPlacementResolver
                    .PlatformGuardRailThickness * 0.5f,
                plan.Platform.SurfaceY);
            Bounds expectedSouth = CreateGuardRailBounds(
                plan.Platform.Footprint.xMin,
                plan.Platform.Footprint.xMax,
                plan.Platform.Footprint.yMin -
                CityArchShelterPlacementResolver
                    .PlatformGuardRailThickness * 0.5f,
                plan.Platform.SurfaceY);
            Bounds expectedWest = CreateWestGuardRailBounds(plan);
            if (!Approximately(north.Bounds, expectedNorth) ||
                !Approximately(south.Bounds, expectedSouth) ||
                !Approximately(west.Bounds, expectedWest))
            {
                throw new InvalidOperationException(
                    "The raised service terrace requires complete north, " +
                    "south and west edge guards while its stair opening " +
                    "remains clear.");
            }
        }

        private static Bounds CreateGuardRailBounds(
            float xMin,
            float xMax,
            float centerZ,
            float surfaceY)
        {
            float height = CityArchShelterPlacementResolver
                .PlatformGuardRailHeight;
            return new Bounds(
                new Vector3(
                    (xMin + xMax) * 0.5f,
                    surfaceY + height * 0.5f,
                    centerZ),
                new Vector3(
                    xMax - xMin,
                    height,
                    CityArchShelterPlacementResolver
                        .PlatformGuardRailThickness));
        }

        private static Bounds CreateWestGuardRailBounds(
            CityArchShelterPlan plan)
        {
            float height = CityArchShelterPlacementResolver
                .PlatformGuardRailHeight;
            float thickness = CityArchShelterPlacementResolver
                .PlatformGuardRailThickness;
            return new Bounds(
                new Vector3(
                    plan.Platform.Footprint.xMin - thickness * 0.5f,
                    plan.Platform.SurfaceY + height * 0.5f,
                    (plan.Platform.Footprint.yMin +
                     plan.Steps.Footprint.yMin) * 0.5f),
                new Vector3(
                    thickness,
                    height,
                    plan.Steps.Footprint.yMin -
                    plan.Platform.Footprint.yMin));
        }

        private static void ValidateRainOccluders(
            CityArchShelterPlan plan,
            ISet<string> ids)
        {
            CityArchShelterRainOccluderDescriptor occluder =
                plan.RainOccluders[0];
            RequireUniqueId(ids, occluder.StableId);
            Rect footprint = ToXZRect(occluder.Bounds);
            float low = Mathf.Min(
                plan.Placement.WestSurfaceY,
                plan.Placement.EastSurfaceY);
            float requiredTop = Mathf.Max(
                                    plan.Placement.WestSurfaceY,
                                    plan.Placement.EastSurfaceY) +
                                CityArchShelterPlacementResolver
                                    .MinimumClearHeight;
            if (!IsPositiveFinite(occluder.Bounds) ||
                !Approximately(
                    footprint,
                    plan.Placement.CommonFacadeFootprint) ||
                occluder.Bounds.min.y > low + GeometryTolerance ||
                occluder.Bounds.max.y < requiredTop - GeometryTolerance)
            {
                throw new InvalidOperationException(
                    "The rain shelter must cover the complete gallery " +
                    "volume from the lower terrace to the arch ceiling.");
            }
        }

        private static void RequireUniqueId(
            ISet<string> ids,
            string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId) || !ids.Add(stableId))
            {
                throw new InvalidOperationException(
                    "Arch-shelter descriptors require unique stable IDs.");
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - GeometryTolerance &&
                   inner.xMax <= outer.xMax + GeometryTolerance &&
                   inner.yMin >= outer.yMin - GeometryTolerance &&
                   inner.yMax <= outer.yMax + GeometryTolerance;
        }

        private static bool Contains(Rect rect, Vector2 point)
        {
            return point.x >= rect.xMin - GeometryTolerance &&
                   point.x <= rect.xMax + GeometryTolerance &&
                   point.y >= rect.yMin - GeometryTolerance &&
                   point.y <= rect.yMax + GeometryTolerance;
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            return left.xMin < right.xMax - GeometryTolerance &&
                   left.xMax > right.xMin + GeometryTolerance &&
                   left.yMin < right.yMax - GeometryTolerance &&
                   left.yMax > right.yMin + GeometryTolerance;
        }

        private static bool OverlapsStrict(Bounds left, Bounds right)
        {
            return left.min.x < right.max.x - GeometryTolerance &&
                   left.max.x > right.min.x + GeometryTolerance &&
                   left.min.y < right.max.y - GeometryTolerance &&
                   left.max.y > right.min.y + GeometryTolerance &&
                   left.min.z < right.max.z - GeometryTolerance &&
                   left.max.z > right.min.z + GeometryTolerance;
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static CityArchShelterPropDescriptor FindProp(
            CityArchShelterPlan plan,
            CityArchShelterPropKind kind)
        {
            for (int index = 0; index < plan.Props.Count; index++)
            {
                if (plan.Props[index].Kind == kind)
                {
                    return plan.Props[index];
                }
            }

            throw new InvalidOperationException(
                $"The arch shelter has no {kind} prop.");
        }

        private static CityArchShelterObstacleDescriptor FindObstacle(
            CityArchShelterPlan plan,
            CityArchShelterObstacleKind kind)
        {
            for (int index = 0; index < plan.Obstacles.Count; index++)
            {
                if (plan.Obstacles[index].Kind == kind)
                {
                    return plan.Obstacles[index];
                }
            }

            throw new InvalidOperationException(
                $"The arch shelter has no {kind} obstacle.");
        }

        private static bool IsPositiveFinite(Rect rect)
        {
            return IsFinite(rect.xMin) &&
                   IsFinite(rect.xMax) &&
                   IsFinite(rect.yMin) &&
                   IsFinite(rect.yMax) &&
                   rect.width > 0f &&
                   rect.height > 0f;
        }

        private static bool IsPositiveFinite(Bounds bounds)
        {
            return IsFinite(bounds.center) &&
                   IsFinite(bounds.size) &&
                   bounds.size.x > 0f &&
                   bounds.size.y > 0f &&
                   bounds.size.z > 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= GeometryTolerance;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Vector3.Distance(left, right) <= GeometryTolerance;
        }

        private static bool Approximately(Rect left, Rect right)
        {
            return Approximately(left.xMin, right.xMin) &&
                   Approximately(left.xMax, right.xMax) &&
                   Approximately(left.yMin, right.yMin) &&
                   Approximately(left.yMax, right.yMax);
        }

        private static bool Approximately(Bounds left, Bounds right)
        {
            return Approximately(left.center, right.center) &&
                   Approximately(left.size, right.size);
        }
    }
}
