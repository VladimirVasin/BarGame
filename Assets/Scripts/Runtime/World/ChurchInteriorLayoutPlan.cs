using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum ChurchInteriorZoneKind
    {
        Narthex = 0,
        Nave = 1,
        CrossingAndChoir = 2,
        Sanctuary = 3
    }

    public enum ChurchInteriorPathKind
    {
        MainNave = 0,
        NorthSideAisle = 1,
        SouthSideAisle = 2,
        NarthexCrossing = 3,
        TranseptChoirCrossing = 4
    }

    public enum ChurchInteriorFixtureKind
    {
        Pier = 0,
        AltarRail = 1,
        AltarTable = 2,
        HighAltar = 3,
        Crucifix = 4,
        Pew = 5,
        Confessional = 6,
        VotiveCandleStand = 7,
        BaptismalFont = 8,
        ChoirLoft = 9,
        Organ = 10,
        ChoirLoftSupport = 11
    }

    public readonly struct ChurchInteriorZonePlan
    {
        internal ChurchInteriorZonePlan(
            string id,
            ChurchInteriorZoneKind kind,
            Rect bounds,
            float ceilingHeight,
            bool isAccessible)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            CeilingHeight = ceilingHeight;
            IsAccessible = isAccessible;
        }

        public string Id { get; }
        public ChurchInteriorZoneKind Kind { get; }
        public Rect Bounds { get; }
        public float CeilingHeight { get; }
        public bool IsAccessible { get; }
    }

    public readonly struct ChurchInteriorPathPlan
    {
        internal ChurchInteriorPathPlan(
            string id,
            ChurchInteriorPathKind kind,
            Rect bounds,
            float minimumClearance)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            MinimumClearance = minimumClearance;
        }

        public string Id { get; }
        public ChurchInteriorPathKind Kind { get; }
        public Rect Bounds { get; }
        public float MinimumClearance { get; }
    }

    public readonly struct ChurchInteriorFixturePlan
    {
        internal ChurchInteriorFixturePlan(
            string id,
            ChurchInteriorFixtureKind kind,
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
        public ChurchInteriorFixtureKind Kind { get; }
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

    public sealed class ChurchInteriorLayoutPlan
    {
        internal ChurchInteriorLayoutPlan(
            int citySeed,
            uint stableSeed,
            Vector2 roomSize,
            float roomHeight,
            float wallThickness,
            Rect roomBounds,
            Bounds modelLocalBounds,
            Rect walkableBounds,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitTriggerSize,
            string modelResourcePath,
            IList<ChurchInteriorZonePlan> zones,
            IList<ChurchInteriorPathPlan> paths,
            IList<ChurchInteriorFixturePlan> fixtures)
        {
            CitySeed = citySeed;
            StableSeed = stableSeed;
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WallThickness = wallThickness;
            RoomBounds = roomBounds;
            ModelLocalBounds = modelLocalBounds;
            WalkableBounds = walkableBounds;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            ModelResourcePath = modelResourcePath ?? string.Empty;
            Zones = Copy(zones, nameof(zones));
            Paths = Copy(paths, nameof(paths));
            Fixtures = Copy(fixtures, nameof(fixtures));
        }

        public int CitySeed { get; }
        public uint StableSeed { get; }
        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float WallThickness { get; }
        public Rect RoomBounds { get; }
        public Bounds ModelLocalBounds { get; }
        public Rect WalkableBounds { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public string ModelResourcePath { get; }
        public IReadOnlyList<ChurchInteriorZonePlan> Zones { get; }
        public IReadOnlyList<ChurchInteriorPathPlan> Paths { get; }
        public IReadOnlyList<ChurchInteriorFixturePlan> Fixtures { get; }

        public bool TryGetZone(
            ChurchInteriorZoneKind kind,
            out ChurchInteriorZonePlan zone)
        {
            for (int index = 0; index < Zones.Count; index++)
            {
                if (Zones[index].Kind == kind)
                {
                    zone = Zones[index];
                    return true;
                }
            }

            zone = default;
            return false;
        }

        public bool TryGetPath(
            ChurchInteriorPathKind kind,
            out ChurchInteriorPathPlan path)
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
