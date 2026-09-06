using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeInteriorZoneKind
    {
        MainRoom = 0,
        Bathroom = 1
    }

    public enum HomeInteriorPathKind
    {
        Entry = 0,
        Main = 1,
        BathroomAccess = 2
    }

    public enum HomeFurnitureKind
    {
        Bed = 0,
        Kitchen = 1,
        Sofa = 2,
        Table = 3,
        Bookcase = 4,
        Toilet = 5,
        Shower = 6,
        Sink = 7,
        CameraCornerJunk = 8
    }

    public readonly struct HomeInteriorZone
    {
        public HomeInteriorZone(
            string id,
            HomeInteriorZoneKind kind,
            Rect bounds)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
        }

        public string Id { get; }
        public HomeInteriorZoneKind Kind { get; }
        public Rect Bounds { get; }
    }

    public readonly struct HomeInteriorPath
    {
        public HomeInteriorPath(
            string id,
            HomeInteriorPathKind kind,
            Rect bounds,
            float clearance)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            Clearance = clearance;
        }

        public string Id { get; }
        public HomeInteriorPathKind Kind { get; }
        public Rect Bounds { get; }
        public float Clearance { get; }
        public bool IsMain => Kind == HomeInteriorPathKind.Main;
    }

    public readonly struct HomeFurnitureFootprint
    {
        internal HomeFurnitureFootprint(
            HomeFurnitureKind kind,
            Rect bounds)
            : this(
                kind.ToString(),
                kind,
                bounds,
                1f,
                true)
        {
        }

        public HomeFurnitureFootprint(
            string id,
            HomeFurnitureKind kind,
            Rect bounds,
            float height,
            bool blocksMovement)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            Height = height;
            BlocksMovement = blocksMovement;
        }

        public string Id { get; }
        public HomeFurnitureKind Kind { get; }
        public Rect Bounds { get; }
        public float Height { get; }
        public bool BlocksMovement { get; }
        public bool IsFixture =>
            Kind == HomeFurnitureKind.Toilet ||
            Kind == HomeFurnitureKind.Shower ||
            Kind == HomeFurnitureKind.Sink;
        public Vector3 Center =>
            new Vector3(
                Bounds.center.x,
                Height * 0.5f,
                Bounds.center.y);
        public Vector3 Size =>
            new Vector3(
                Bounds.width,
                Height,
                Bounds.height);
    }

    public sealed class HomeInteriorLayoutPlan
    {
        internal HomeInteriorLayoutPlan(
            Vector2 roomSize,
            float roomHeight,
            Rect walkableBounds,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitTriggerSize,
            Rect entryCorridor,
            Rect bathroomBounds,
            Rect bathroomDoorway,
            IList<HomeInteriorZone> zones,
            IList<HomeInteriorPath> paths,
            IList<HomeFurnitureFootprint> furniture)
        {
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            RoomBounds = Rect.MinMaxRect(
                roomSize.x * -0.5f,
                roomSize.y * -0.5f,
                roomSize.x * 0.5f,
                roomSize.y * 0.5f);
            float wallInset = PlayerHomeBalconyGeometry.WallThickness * 0.5f;
            InnerRoomBounds = Rect.MinMaxRect(
                RoomBounds.xMin + wallInset, RoomBounds.yMin + wallInset,
                RoomBounds.xMax - wallInset, RoomBounds.yMax - wallInset);
            WalkableBounds = walkableBounds;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            EntryCorridor = entryCorridor;
            BathroomBounds = bathroomBounds;
            BathroomDoorway = bathroomDoorway;
            Zones = Copy(zones, nameof(zones));
            Paths = Copy(paths, nameof(paths));
            Furniture = Copy(furniture, nameof(furniture));
        }

        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public Rect RoomBounds { get; }
        // Furniture reaches the wall face; the player's walking margin is
        // deliberately farther inside and must not push furniture away.
        public Rect InnerRoomBounds { get; }
        public Rect WalkableBounds { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public Rect EntryCorridor { get; }
        public Rect BathroomBounds { get; }
        public Rect BathroomDoorway { get; }
        public IReadOnlyList<HomeInteriorZone> Zones { get; }
        public IReadOnlyList<HomeInteriorPath> Paths { get; }
        public IReadOnlyList<HomeFurnitureFootprint> Furniture { get; }

        public bool TryGetZone(
            HomeInteriorZoneKind kind,
            out HomeInteriorZone zone)
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
            HomeInteriorPathKind kind,
            out HomeInteriorPath path)
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

        public bool TryGetFurniture(
            HomeFurnitureKind kind,
            out HomeFurnitureFootprint furniture)
        {
            for (int index = 0; index < Furniture.Count; index++)
            {
                if (Furniture[index].Kind == kind)
                {
                    furniture = Furniture[index];
                    return true;
                }
            }

            furniture = default;
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

            return new ReadOnlyCollection<T>(
                new List<T>(source));
        }
    }
}
