using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityArchShelterPlanner
    {
        public const string StableId = "city-arch-shelter-10-05-11-05";
        public const float ClearLaneWidth = 2.2f;
        public const float ClearLaneEdgeInset = 0.65f;
        public const float MinimumHeadroom =
            CityArchShelterPlacementResolver.MinimumClearHeight;

        private static readonly Vector3 BarrelSize =
            new Vector3(0.82f, 1.05f, 0.82f);
        private static readonly Vector3 FireVisualSize =
            new Vector3(0.68f, 1.45f, 0.68f);
        public const float BeddingMattressLength = 1.89618f;
        public const float BeddingMattressWidth = 0.83633f;
        public const float BeddingMattressTop = 0.2725f;

        // Conservative world-axis envelope of the imported 2.079462 x
        // 0.951008 m cardboard/mattress assembly after its five-degree yaw.
        // The old values described the footprint with its axes swapped.
        private static readonly Vector3 BeddingSize =
            new Vector3(2.193f, BeddingMattressTop, 1.153f);
        private static readonly Vector3 ClutterSize =
            new Vector3(1.35f, 0.90f, 1.10f);

        public static CityArchShelterPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!CityArchShelterPlacementResolver.TryCreate(
                    layout,
                    out CityArchShelterPlacement placement))
            {
                return CityArchShelterPlan.Absent;
            }

            List<CityArchShelterClearLaneDescriptor> clearLanes =
                CreateClearLanes(placement);
            CityArchShelterStepDescriptor steps = CreateSteps(placement);
            CityArchShelterLandingDescriptor upperLanding =
                CreateUpperLanding(placement, steps);
            CityArchShelterPlatformDescriptor platform =
                CreatePlatform(placement, steps);
            List<CityArchShelterPropDescriptor> props =
                CreateProps(placement);
            List<CityArchShelterNpcAnchorDescriptor> npcAnchors =
                CreateNpcAnchors(placement, props);
            List<CityArchShelterObstacleDescriptor> obstacles =
                CreateObstacles(
                    placement,
                    steps,
                    platform,
                    props);
            List<CityArchShelterRainOccluderDescriptor> rainOccluders =
                CreateRainOccluders(placement);
            var plan = new CityArchShelterPlan(
                true,
                placement,
                steps,
                upperLanding,
                platform,
                clearLanes,
                npcAnchors,
                props,
                obstacles,
                rainOccluders);
            CityArchShelterValidator.ValidateOrThrow(layout, plan);
            return plan;
        }

        private static CityArchShelterPlatformDescriptor CreatePlatform(
            CityArchShelterPlacement placement,
            CityArchShelterStepDescriptor steps)
        {
            bool ascendsEast = steps.AscentDirection.x > 0f;
            float xMin;
            float xMax;
            if (ascendsEast)
            {
                xMin = steps.Footprint.xMax;
                xMax = placement.PassageFootprint.xMax -
                       CityArchShelterPlacementResolver.PlatformWallInset;
            }
            else
            {
                xMax = steps.Footprint.xMin;
                xMin = placement.PassageFootprint.xMin +
                       CityArchShelterPlacementResolver.PlatformWallInset;
            }

            return new CityArchShelterPlatformDescriptor(
                $"{StableId}-platform",
                Rect.MinMaxRect(
                    xMin,
                    placement.CommonFacadeFootprint.yMin,
                    xMax,
                    steps.Footprint.yMax),
                steps.LowerSurfaceY,
                steps.UpperSurfaceY);
        }

        private static CityArchShelterLandingDescriptor CreateUpperLanding(
            CityArchShelterPlacement placement,
            CityArchShelterStepDescriptor steps)
        {
            float xMin;
            float xMax;
            if (steps.AscentDirection.x > 0f)
            {
                xMin = steps.Footprint.xMax;
                xMax = xMin +
                       CityArchShelterPlacementResolver.UpperLandingLength;
            }
            else
            {
                xMax = steps.Footprint.xMin;
                xMin = xMax -
                       CityArchShelterPlacementResolver.UpperLandingLength;
            }

            return new CityArchShelterLandingDescriptor(
                $"{StableId}-upper-landing",
                Rect.MinMaxRect(
                    xMin,
                    steps.Footprint.yMin,
                    xMax,
                    steps.Footprint.yMax),
                steps.UpperSurfaceY);
        }

        private static CityArchShelterStepDescriptor CreateSteps(
            CityArchShelterPlacement placement)
        {
            float lower = Mathf.Min(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            float upper = Mathf.Max(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            Vector3 ascent = placement.WestSurfaceY <=
                             placement.EastSurfaceY
                ? Vector3.right
                : Vector3.left;
            int stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt((upper - lower) / 0.17f));
            return new CityArchShelterStepDescriptor(
                $"{StableId}-steps",
                placement.RailSuppressionFootprint,
                lower,
                upper,
                ascent,
                stepCount);
        }

        private static List<CityArchShelterClearLaneDescriptor>
            CreateClearLanes(CityArchShelterPlacement placement)
        {
            Rect passage = placement.PassageFootprint;
            var west = Rect.MinMaxRect(
                passage.xMin + ClearLaneEdgeInset,
                passage.yMin,
                passage.xMin + ClearLaneEdgeInset + ClearLaneWidth,
                passage.yMax);
            return new List<CityArchShelterClearLaneDescriptor>(1)
            {
                new CityArchShelterClearLaneDescriptor(
                    $"{StableId}-clear-lane-west",
                    west,
                    placement.WestSurfaceY,
                    MinimumHeadroom)
            };
        }

        private static List<CityArchShelterPropDescriptor> CreateProps(
            CityArchShelterPlacement placement)
        {
            Rect sheltered = placement.TableauFootprint;
            float upperDirection = placement.EastSurfaceY >=
                                   placement.WestSurfaceY
                ? 1f
                : -1f;
            Vector3 barrelBase = OnSurface(
                placement,
                new Vector2(
                    placement.SharedBoundaryX + upperDirection * 1.15f,
                    sheltered.center.y));
            Vector3 beddingBase = OnSurface(
                placement,
                new Vector2(
                    ClampToUpperSide(
                        placement,
                        barrelBase.x + upperDirection * 1.80f,
                        3.45f),
                    sheltered.center.y + 0.25f));
            Vector3 clutterBase = OnSurface(
                placement,
                new Vector2(
                    ClampToLowerSide(
                        placement,
                        placement.SharedBoundaryX -
                        upperDirection * 1.65f,
                        3.35f),
                    sheltered.center.y + 0.55f));

            return new List<CityArchShelterPropDescriptor>(4)
            {
                CreateProp(
                    "burn-barrel",
                    CityArchShelterPropKind.BurnBarrel,
                    0,
                    barrelBase,
                    Quaternion.identity,
                    BarrelSize,
                    true),
                CreateProp(
                    "fire",
                    CityArchShelterPropKind.Fire,
                    0,
                    barrelBase,
                    Quaternion.identity,
                    FireVisualSize,
                    false),
                CreateProp(
                    "bedding",
                    CityArchShelterPropKind.Bedding,
                    0,
                    beddingBase,
                    Quaternion.Euler(0f, -5f, 0f),
                    BeddingSize,
                    true),
                CreateProp(
                    "clutter",
                    CityArchShelterPropKind.Clutter,
                    0,
                    clutterBase,
                    Quaternion.Euler(0f, 9f, 0f),
                    ClutterSize,
                    true)
            };
        }

        private static List<CityArchShelterNpcAnchorDescriptor>
            CreateNpcAnchors(
                CityArchShelterPlacement placement,
                IReadOnlyList<CityArchShelterPropDescriptor> props)
        {
            float upperDirection = placement.EastSurfaceY >=
                                   placement.WestSurfaceY
                ? 1f
                : -1f;
            CityArchShelterPropDescriptor barrel = FindProp(
                props,
                CityArchShelterPropKind.BurnBarrel);
            CityArchShelterPropDescriptor bedding = FindProp(
                props,
                CityArchShelterPropKind.Bedding);
            Vector3 standing = OnSurface(
                placement,
                new Vector2(
                    barrel.Position.x + upperDirection * 0.25f,
                    barrel.Position.z - 0.92f));
            Vector3 seated = OnSurface(
                placement,
                new Vector2(
                    barrel.Position.x - upperDirection * 0.55f,
                    barrel.Position.z + 0.82f));
            Vector3 sleeping = new Vector3(
                bedding.Position.x,
                bedding.Position.y + BeddingMattressTop,
                bedding.Position.z);
            return new List<CityArchShelterNpcAnchorDescriptor>(3)
            {
                CreateNpcAnchor(
                    "npc-standing-warmer",
                    CityArchShelterNpcStageKind.StandingWarmer,
                    standing,
                    barrel.Position - standing),
                CreateNpcAnchor(
                    "npc-seated-warmer",
                    CityArchShelterNpcStageKind.SeatedWarmer,
                    seated,
                    barrel.Position - seated),
                CreateNpcAnchor(
                    "npc-sleeper",
                    CityArchShelterNpcStageKind.Sleeper,
                    sleeping,
                    bedding.Rotation * Vector3.forward)
            };
        }

        private static List<CityArchShelterObstacleDescriptor>
            CreateObstacles(
                CityArchShelterPlacement placement,
                CityArchShelterStepDescriptor steps,
                CityArchShelterPlatformDescriptor platform,
                IReadOnlyList<CityArchShelterPropDescriptor> props)
        {
            Rect sheltered = placement.ShelteredFootprint;
            Bounds structure = placement.StructureBounds;
            float supportWidth =
                CityArchShelterPlacementResolver.PlatformWallInset +
                CityArchShelterPlacementResolver.FacadeAttachmentOverlap;
            float roofBottom = Mathf.Max(
                                   placement.WestSurfaceY,
                                   placement.EastSurfaceY) +
                               MinimumHeadroom;
            Bounds westAttachment = CreateBoundsFromBase(
                new Vector3(
                    structure.min.x,
                    Mathf.Min(
                        placement.WestSurfaceY,
                        placement.EastSurfaceY),
                    sheltered.center.y),
                new Vector3(
                    supportWidth,
                    structure.size.y,
                    sheltered.height));
            Bounds eastAttachment = CreateBoundsFromBase(
                new Vector3(
                    structure.max.x - supportWidth,
                    Mathf.Min(
                        placement.WestSurfaceY,
                        placement.EastSurfaceY),
                    sheltered.center.y),
                new Vector3(
                    supportWidth,
                    structure.size.y,
                    sheltered.height));
            var overhead = new Bounds(
                new Vector3(
                    structure.center.x,
                    (roofBottom + structure.max.y) * 0.5f,
                    sheltered.center.y),
                new Vector3(
                    structure.size.x,
                    structure.max.y - roofBottom,
                    sheltered.height));
            var result = new List<CityArchShelterObstacleDescriptor>(9)
            {
                new CityArchShelterObstacleDescriptor(
                    $"{StableId}-obstacle-west-attachment",
                    CityArchShelterObstacleKind.WestAttachment,
                    westAttachment),
                new CityArchShelterObstacleDescriptor(
                    $"{StableId}-obstacle-east-attachment",
                    CityArchShelterObstacleKind.EastAttachment,
                    eastAttachment),
                new CityArchShelterObstacleDescriptor(
                    $"{StableId}-obstacle-overhead-gallery",
                    CityArchShelterObstacleKind.OverheadGallery,
                    overhead)
            };

            result.Add(new CityArchShelterObstacleDescriptor(
                $"{StableId}-obstacle-platform-north-guard-rail",
                CityArchShelterObstacleKind.PlatformNorthGuardRail,
                CreateGuardRailBounds(
                    platform.Footprint.xMin,
                    platform.Footprint.xMax,
                    platform.Footprint.yMax +
                    CityArchShelterPlacementResolver
                        .PlatformGuardRailThickness * 0.5f,
                    platform.SurfaceY)));
            result.Add(new CityArchShelterObstacleDescriptor(
                $"{StableId}-obstacle-platform-south-guard-rail",
                CityArchShelterObstacleKind.PlatformSouthGuardRail,
                CreateGuardRailBounds(
                    platform.Footprint.xMin,
                    platform.Footprint.xMax,
                    platform.Footprint.yMin -
                    CityArchShelterPlacementResolver
                        .PlatformGuardRailThickness * 0.5f,
                    platform.SurfaceY)));
            result.Add(new CityArchShelterObstacleDescriptor(
                $"{StableId}-obstacle-platform-west-guard-rail",
                CityArchShelterObstacleKind.PlatformWestGuardRail,
                CreateWestGuardRailBounds(platform, steps)));

            AppendPropObstacle(
                result,
                props,
                CityArchShelterPropKind.BurnBarrel,
                CityArchShelterObstacleKind.BurnBarrel);
            AppendPropObstacle(
                result,
                props,
                CityArchShelterPropKind.Bedding,
                CityArchShelterObstacleKind.Bedding);
            AppendPropObstacle(
                result,
                props,
                CityArchShelterPropKind.Clutter,
                CityArchShelterObstacleKind.Clutter);
            return result;
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
            CityArchShelterPlatformDescriptor platform,
            CityArchShelterStepDescriptor steps)
        {
            float height = CityArchShelterPlacementResolver
                .PlatformGuardRailHeight;
            float thickness = CityArchShelterPlacementResolver
                .PlatformGuardRailThickness;
            return new Bounds(
                new Vector3(
                    platform.Footprint.xMin - thickness * 0.5f,
                    platform.SurfaceY + height * 0.5f,
                    (platform.Footprint.yMin +
                     steps.Footprint.yMin) * 0.5f),
                new Vector3(
                    thickness,
                    height,
                    steps.Footprint.yMin -
                    platform.Footprint.yMin));
        }

        private static List<CityArchShelterRainOccluderDescriptor>
            CreateRainOccluders(CityArchShelterPlacement placement)
        {
            Rect sheltered = placement.ShelteredFootprint;
            float floor = Mathf.Min(
                placement.WestSurfaceY,
                placement.EastSurfaceY);
            float roof = Mathf.Max(
                             placement.WestSurfaceY,
                             placement.EastSurfaceY) +
                         MinimumHeadroom;
            return new List<CityArchShelterRainOccluderDescriptor>(1)
            {
                new CityArchShelterRainOccluderDescriptor(
                    $"{StableId}-rain-shelter",
                    new Bounds(
                        new Vector3(
                            sheltered.center.x,
                            (floor + roof) * 0.5f,
                            sheltered.center.y),
                        new Vector3(
                            sheltered.width,
                            roof - floor,
                            sheltered.height)))
            };
        }

        private static CityArchShelterPropDescriptor CreateProp(
            string suffix,
            CityArchShelterPropKind kind,
            int variant,
            Vector3 basePosition,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement)
        {
            var bounds = new Bounds(
                basePosition + Vector3.up * (size.y * 0.5f),
                size);
            return new CityArchShelterPropDescriptor(
                $"{StableId}-{suffix}",
                kind,
                variant,
                basePosition,
                rotation,
                bounds,
                blocksMovement);
        }

        private static CityArchShelterNpcAnchorDescriptor CreateNpcAnchor(
            string suffix,
            CityArchShelterNpcStageKind stage,
            Vector3 position,
            Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
            {
                facing = Vector3.forward;
            }

            return new CityArchShelterNpcAnchorDescriptor(
                $"{StableId}-{suffix}",
                stage,
                position,
                facing.normalized);
        }

        private static CityArchShelterPropDescriptor FindProp(
            IReadOnlyList<CityArchShelterPropDescriptor> props,
            CityArchShelterPropKind kind)
        {
            for (int index = 0; index < props.Count; index++)
            {
                if (props[index].Kind == kind)
                {
                    return props[index];
                }
            }

            throw new InvalidOperationException(
                $"The arch shelter has no {kind} prop.");
        }

        private static void AppendPropObstacle(
            ICollection<CityArchShelterObstacleDescriptor> destination,
            IReadOnlyList<CityArchShelterPropDescriptor> props,
            CityArchShelterPropKind propKind,
            CityArchShelterObstacleKind obstacleKind)
        {
            CityArchShelterPropDescriptor prop = FindProp(props, propKind);
            destination.Add(new CityArchShelterObstacleDescriptor(
                $"{StableId}-obstacle-{propKind.ToString().ToLowerInvariant()}",
                obstacleKind,
                prop.Bounds));
        }

        private static Vector3 OnSurface(
            CityArchShelterPlacement placement,
            Vector2 xz)
        {
            return new Vector3(
                xz.x,
                placement.ResolveSurfaceY(xz.x),
                xz.y);
        }

        private static float ClampToUpperSide(
            CityArchShelterPlacement placement,
            float desired,
            float edgeInset)
        {
            if (placement.EastSurfaceY >= placement.WestSurfaceY)
            {
                return Mathf.Clamp(
                    desired,
                    placement.SharedBoundaryX + 0.30f,
                    placement.PassageFootprint.xMax - edgeInset);
            }

            return Mathf.Clamp(
                desired,
                placement.PassageFootprint.xMin + edgeInset,
                placement.SharedBoundaryX - 0.30f);
        }

        private static float ClampToLowerSide(
            CityArchShelterPlacement placement,
            float desired,
            float edgeInset)
        {
            if (placement.WestSurfaceY <= placement.EastSurfaceY)
            {
                return Mathf.Clamp(
                    desired,
                    placement.PassageFootprint.xMin + edgeInset,
                    placement.SharedBoundaryX - 0.30f);
            }

            return Mathf.Clamp(
                desired,
                placement.SharedBoundaryX + 0.30f,
                placement.PassageFootprint.xMax - edgeInset);
        }

        private static Bounds CreateBoundsFromBase(
            Vector3 minimumCenter,
            Vector3 size)
        {
            return new Bounds(
                new Vector3(
                    minimumCenter.x + size.x * 0.5f,
                    minimumCenter.y + size.y * 0.5f,
                    minimumCenter.z),
                size);
        }
    }
}
