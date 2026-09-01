using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MothersHouseInteriorPathKind
    {
        EntryApproach = 0,
        MainPassage = 1,
        TableApproach = 2
    }

    public enum MothersHouseInteriorFixtureKind
    {
        LowTable = 0,
        RockingChair = 1,
        Sofa = 2,
        Fireplace = 3,
        Cupboard = 4,
        YarnBasket = 5,
        FloorLamp = 6
    }

    public readonly struct MothersHouseInteriorPathPlan
    {
        internal MothersHouseInteriorPathPlan(
            string id,
            MothersHouseInteriorPathKind kind,
            Rect bounds,
            float minimumClearance)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            MinimumClearance = minimumClearance;
        }

        public string Id { get; }
        public MothersHouseInteriorPathKind Kind { get; }
        public Rect Bounds { get; }
        public float MinimumClearance { get; }
    }

    public readonly struct MothersHouseInteriorFixturePlan
    {
        internal MothersHouseInteriorFixturePlan(
            string id,
            MothersHouseInteriorFixtureKind kind,
            Rect bounds,
            float baseHeight,
            float height,
            bool blocksMovement)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            BaseHeight = baseHeight;
            Height = height;
            BlocksMovement = blocksMovement;
        }

        public string Id { get; }
        public MothersHouseInteriorFixtureKind Kind { get; }
        public Rect Bounds { get; }
        public float BaseHeight { get; }
        public float Height { get; }
        public bool BlocksMovement { get; }
        public Vector3 Center => new Vector3(
            Bounds.center.x,
            BaseHeight + Height * 0.5f,
            Bounds.center.y);
        public Vector3 Size => new Vector3(
            Bounds.width,
            Height,
            Bounds.height);
    }

    public sealed class MothersHouseInteriorLayoutPlan
    {
        internal MothersHouseInteriorLayoutPlan(
            Vector2 roomSize,
            float roomHeight,
            float wallThickness,
            float doorOpeningWidth,
            Rect roomBounds,
            Bounds modelLocalBounds,
            Rect walkableBounds,
            Vector3 entryPosition,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitTriggerSize,
            Vector3 cameraTarget,
            HomeCameraShot cameraShot,
            Vector3 westWindowPosition,
            Vector3 eastWindowPosition,
            Vector3 fireplacePosition,
            Vector3 fireLightPosition,
            Vector3 floorLampLightPosition,
            Vector3 tabletopPosition,
            Vector3 teapotDockPosition,
            string modelResourcePath,
            IList<MothersHouseInteriorPathPlan> paths,
            IList<MothersHouseInteriorFixturePlan> fixtures)
        {
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WallThickness = wallThickness;
            DoorOpeningWidth = doorOpeningWidth;
            RoomBounds = roomBounds;
            ModelLocalBounds = modelLocalBounds;
            WalkableBounds = walkableBounds;
            EntryPosition = entryPosition;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            CameraTarget = cameraTarget;
            CameraShot = cameraShot;
            WestWindowPosition = westWindowPosition;
            EastWindowPosition = eastWindowPosition;
            FireplacePosition = fireplacePosition;
            FireLightPosition = fireLightPosition;
            FloorLampLightPosition = floorLampLightPosition;
            TabletopPosition = tabletopPosition;
            TeapotDockPosition = teapotDockPosition;
            ModelResourcePath = modelResourcePath ?? string.Empty;
            Paths = Copy(paths, nameof(paths));
            Fixtures = Copy(fixtures, nameof(fixtures));
        }

        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float WallThickness { get; }
        public float DoorOpeningWidth { get; }
        public Rect RoomBounds { get; }
        public Bounds ModelLocalBounds { get; }
        public Rect WalkableBounds { get; }
        public Vector3 EntryPosition { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public Vector3 CameraTarget { get; }
        public HomeCameraShot CameraShot { get; }
        public Vector3 WestWindowPosition { get; }
        public Vector3 EastWindowPosition { get; }
        public Vector3 FireplacePosition { get; }
        public Vector3 FireLightPosition { get; }
        public Vector3 FloorLampLightPosition { get; }
        public Vector3 TabletopPosition { get; }
        public Vector3 TeapotDockPosition { get; }
        public string ModelResourcePath { get; }
        public IReadOnlyList<MothersHouseInteriorPathPlan> Paths { get; }
        public IReadOnlyList<MothersHouseInteriorFixturePlan> Fixtures { get; }

        public bool TryGetPath(
            MothersHouseInteriorPathKind kind,
            out MothersHouseInteriorPathPlan path)
        {
            for (int index = 0; index < Paths.Count; index++)
            {
                if (Paths[index].Kind == kind)
                {
                    path = Paths[index];
                    return true;
                }
            }

            path = default;
            return false;
        }

        public bool TryGetFixture(
            MothersHouseInteriorFixtureKind kind,
            out MothersHouseInteriorFixturePlan fixture)
        {
            for (int index = 0; index < Fixtures.Count; index++)
            {
                if (Fixtures[index].Kind == kind)
                {
                    fixture = Fixtures[index];
                    return true;
                }
            }

            fixture = default;
            return false;
        }

        private static IReadOnlyList<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
