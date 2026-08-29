using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityArchShelterPropKind
    {
        BurnBarrel = 0,
        Fire = 1,
        Bedding = 2,
        Clutter = 3
    }

    public enum CityArchShelterNpcStageKind
    {
        StandingWarmer = 0,
        SeatedWarmer = 1,
        Sleeper = 2
    }

    public enum CityArchShelterObstacleKind
    {
        WestAttachment = 0,
        EastAttachment = 1,
        OverheadGallery = 2,
        BurnBarrel = 3,
        Bedding = 4,
        Clutter = 5,
        PlatformNorthGuardRail = 6,
        PlatformSouthGuardRail = 7,
        PlatformWestGuardRail = 8
    }

    public readonly struct CityArchShelterClearLaneDescriptor
    {
        public CityArchShelterClearLaneDescriptor(
            string stableId,
            Rect footprint,
            float surfaceY,
            float minimumHeadroom)
        {
            StableId = stableId ?? string.Empty;
            Footprint = footprint;
            SurfaceY = surfaceY;
            MinimumHeadroom = minimumHeadroom;
        }

        public string StableId { get; }
        public Rect Footprint { get; }
        public float SurfaceY { get; }
        public float MinimumHeadroom { get; }
        public Bounds ClearanceBounds => new Bounds(
            new Vector3(
                Footprint.center.x,
                SurfaceY + MinimumHeadroom * 0.5f,
                Footprint.center.y),
            new Vector3(
                Footprint.width,
                MinimumHeadroom,
                Footprint.height));
    }

    public readonly struct CityArchShelterNpcAnchorDescriptor
    {
        public CityArchShelterNpcAnchorDescriptor(
            string stableId,
            CityArchShelterNpcStageKind stage,
            Vector3 position,
            Vector3 facing)
        {
            StableId = stableId ?? string.Empty;
            Stage = stage;
            Position = position;
            Facing = facing;
        }

        public string StableId { get; }
        public CityArchShelterNpcStageKind Stage { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }
    }

    public readonly struct CityArchShelterStepDescriptor
    {
        public CityArchShelterStepDescriptor(
            string stableId,
            Rect footprint,
            float lowerSurfaceY,
            float upperSurfaceY,
            Vector3 ascentDirection,
            int stepCount)
        {
            StableId = stableId ?? string.Empty;
            Footprint = footprint;
            LowerSurfaceY = lowerSurfaceY;
            UpperSurfaceY = upperSurfaceY;
            AscentDirection = ascentDirection;
            StepCount = stepCount;
        }

        public string StableId { get; }
        public Rect Footprint { get; }
        public float LowerSurfaceY { get; }
        public float UpperSurfaceY { get; }
        public Vector3 AscentDirection { get; }
        public int StepCount { get; }
        public float TotalRise => UpperSurfaceY - LowerSurfaceY;
        public float StepRise => StepCount > 0
            ? TotalRise / StepCount
            : 0f;
        public float TreadDepth => StepCount > 0
            ? Footprint.width / StepCount
            : 0f;
    }

    public readonly struct CityArchShelterLandingDescriptor
    {
        public CityArchShelterLandingDescriptor(
            string stableId,
            Rect footprint,
            float surfaceY)
        {
            StableId = stableId ?? string.Empty;
            Footprint = footprint;
            SurfaceY = surfaceY;
        }

        public string StableId { get; }
        public Rect Footprint { get; }
        public float SurfaceY { get; }
    }

    public readonly struct CityArchShelterPlatformDescriptor
    {
        public CityArchShelterPlatformDescriptor(
            string stableId,
            Rect footprint,
            float supportBottomY,
            float surfaceY)
        {
            StableId = stableId ?? string.Empty;
            Footprint = footprint;
            SupportBottomY = supportBottomY;
            SurfaceY = surfaceY;
        }

        public string StableId { get; }
        public Rect Footprint { get; }
        public float SupportBottomY { get; }
        public float SurfaceY { get; }
        public float SupportHeight => SurfaceY - SupportBottomY;
        public Bounds SupportBounds => new Bounds(
            new Vector3(
                Footprint.center.x,
                SupportBottomY + SupportHeight * 0.5f,
                Footprint.center.y),
            new Vector3(
                Footprint.width,
                SupportHeight,
                Footprint.height));
    }

    public readonly struct CityArchShelterPropDescriptor
    {
        public CityArchShelterPropDescriptor(
            string stableId,
            CityArchShelterPropKind kind,
            int variant,
            Vector3 position,
            Quaternion rotation,
            Bounds bounds,
            bool blocksMovement)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Variant = variant;
            Position = position;
            Rotation = rotation;
            Bounds = bounds;
            BlocksMovement = blocksMovement;
        }

        public string StableId { get; }
        public CityArchShelterPropKind Kind { get; }
        public int Variant { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Bounds Bounds { get; }
        public bool BlocksMovement { get; }
    }

    public readonly struct CityArchShelterObstacleDescriptor
    {
        public CityArchShelterObstacleDescriptor(
            string stableId,
            CityArchShelterObstacleKind kind,
            Bounds bounds)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
        }

        public string StableId { get; }
        public CityArchShelterObstacleKind Kind { get; }
        public Bounds Bounds { get; }
    }

    public readonly struct CityArchShelterRainOccluderDescriptor
    {
        public CityArchShelterRainOccluderDescriptor(
            string stableId,
            Bounds bounds)
        {
            StableId = stableId ?? string.Empty;
            Bounds = bounds;
        }

        public string StableId { get; }
        public Bounds Bounds { get; }

        public bool Contains(Vector3 position)
        {
            return position.x >= Bounds.min.x &&
                   position.x <= Bounds.max.x &&
                   position.z >= Bounds.min.z &&
                   position.z <= Bounds.max.z &&
                   position.y >= Bounds.min.y &&
                   position.y <= Bounds.max.y;
        }
    }

    public readonly struct CityArchShelterPlacement
    {
        public CityArchShelterPlacement(
            Vector2Int westCell,
            Vector2Int eastCell,
            Bounds westBuildingBounds,
            Bounds eastBuildingBounds,
            Rect commonFacadeFootprint,
            Rect passageFootprint,
            Rect shelteredFootprint,
            Rect tableauFootprint,
            Rect railSuppressionFootprint,
            float westSurfaceY,
            float eastSurfaceY,
            float sharedBoundaryX,
            Vector3 structurePosition,
            Quaternion structureRotation,
            Bounds structureBounds)
        {
            WestCell = westCell;
            EastCell = eastCell;
            WestBuildingBounds = westBuildingBounds;
            EastBuildingBounds = eastBuildingBounds;
            CommonFacadeFootprint = commonFacadeFootprint;
            PassageFootprint = passageFootprint;
            ShelteredFootprint = shelteredFootprint;
            TableauFootprint = tableauFootprint;
            RailSuppressionFootprint = railSuppressionFootprint;
            WestSurfaceY = westSurfaceY;
            EastSurfaceY = eastSurfaceY;
            SharedBoundaryX = sharedBoundaryX;
            StructurePosition = structurePosition;
            StructureRotation = structureRotation;
            StructureBounds = structureBounds;
        }

        public Vector2Int WestCell { get; }
        public Vector2Int EastCell { get; }
        public Bounds WestBuildingBounds { get; }
        public Bounds EastBuildingBounds { get; }
        public Rect CommonFacadeFootprint { get; }
        public Rect PassageFootprint { get; }
        public Rect ShelteredFootprint { get; }
        public Rect TableauFootprint { get; }
        public Rect RailSuppressionFootprint { get; }
        public float WestSurfaceY { get; }
        public float EastSurfaceY { get; }
        public float SharedBoundaryX { get; }
        public Vector3 StructurePosition { get; }
        public Quaternion StructureRotation { get; }
        public Bounds StructureBounds { get; }
        public float PassageWidth => PassageFootprint.width;
        public float PassageDepth => PassageFootprint.height;
        public float TerraceRise => EastSurfaceY - WestSurfaceY;
        public bool TopIsWalkable => false;

        public float ResolveSurfaceY(float worldX)
        {
            return worldX < SharedBoundaryX
                ? WestSurfaceY
                : EastSurfaceY;
        }
    }

    public sealed class CityArchShelterPlan
    {
        internal static readonly CityArchShelterPlan Absent =
            new CityArchShelterPlan(
                false,
                default,
                default,
                default,
                default,
                Array.Empty<CityArchShelterClearLaneDescriptor>(),
                Array.Empty<CityArchShelterNpcAnchorDescriptor>(),
                Array.Empty<CityArchShelterPropDescriptor>(),
                Array.Empty<CityArchShelterObstacleDescriptor>(),
                Array.Empty<CityArchShelterRainOccluderDescriptor>());

        private readonly ReadOnlyCollection<
            CityArchShelterClearLaneDescriptor> clearLanes;
        private readonly ReadOnlyCollection<
            CityArchShelterNpcAnchorDescriptor> npcAnchors;
        private readonly ReadOnlyCollection<
            CityArchShelterPropDescriptor> props;
        private readonly ReadOnlyCollection<
            CityArchShelterObstacleDescriptor> obstacles;
        private readonly ReadOnlyCollection<
            CityArchShelterRainOccluderDescriptor> rainOccluders;

        internal CityArchShelterPlan(
            bool isEnabled,
            CityArchShelterPlacement placement,
            CityArchShelterStepDescriptor steps,
            CityArchShelterLandingDescriptor upperLanding,
            CityArchShelterPlatformDescriptor platform,
            IList<CityArchShelterClearLaneDescriptor> sourceClearLanes,
            IList<CityArchShelterNpcAnchorDescriptor> sourceNpcAnchors,
            IList<CityArchShelterPropDescriptor> sourceProps,
            IList<CityArchShelterObstacleDescriptor> sourceObstacles,
            IList<CityArchShelterRainOccluderDescriptor> sourceRainOccluders)
        {
            IsEnabled = isEnabled;
            Placement = placement;
            Steps = steps;
            UpperLanding = upperLanding;
            Platform = platform;
            clearLanes = CopyAndSort(sourceClearLanes, item => item.StableId);
            npcAnchors = CopyAndSort(sourceNpcAnchors, item => item.StableId);
            props = CopyAndSort(sourceProps, item => item.StableId);
            obstacles = CopyAndSort(sourceObstacles, item => item.StableId);
            rainOccluders = CopyAndSort(
                sourceRainOccluders,
                item => item.StableId);
        }

        public bool IsEnabled { get; }
        public CityArchShelterPlacement Placement { get; }
        public CityArchShelterStepDescriptor Steps { get; }
        public CityArchShelterLandingDescriptor UpperLanding { get; }
        public CityArchShelterPlatformDescriptor Platform { get; }
        public IReadOnlyList<CityArchShelterClearLaneDescriptor>
            ClearLanes => clearLanes;
        public IReadOnlyList<CityArchShelterNpcAnchorDescriptor>
            NpcAnchors => npcAnchors;
        public IReadOnlyList<CityArchShelterPropDescriptor> Props => props;
        public IReadOnlyList<CityArchShelterObstacleDescriptor>
            Obstacles => obstacles;
        public IReadOnlyList<CityArchShelterRainOccluderDescriptor>
            RainOccluders => rainOccluders;

        public bool IsRainSheltered(Vector3 position)
        {
            for (int index = 0; index < rainOccluders.Count; index++)
            {
                if (rainOccluders[index].Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        private static ReadOnlyCollection<T> CopyAndSort<T>(
            IList<T> source,
            Func<T, string> getStableId)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var copy = new List<T>(source);
            copy.Sort((left, right) => string.Compare(
                getStableId(left),
                getStableId(right),
                StringComparison.Ordinal));
            return new ReadOnlyCollection<T>(copy);
        }
    }
}
