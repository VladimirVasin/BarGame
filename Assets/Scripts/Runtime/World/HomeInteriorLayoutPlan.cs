using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeFurnitureKind
    {
        Bed,
        Kitchen,
        Sofa,
        Table,
        Bookcase
    }

    public readonly struct HomeFurnitureFootprint
    {
        internal HomeFurnitureFootprint(
            HomeFurnitureKind kind,
            Rect bounds)
        {
            Kind = kind;
            Bounds = bounds;
        }

        public HomeFurnitureKind Kind { get; }
        public Rect Bounds { get; }
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
            IList<HomeFurnitureFootprint> furniture)
        {
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WalkableBounds = walkableBounds;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            EntryCorridor = entryCorridor;
            Furniture =
                new ReadOnlyCollection<HomeFurnitureFootprint>(
                    new List<HomeFurnitureFootprint>(
                        furniture ??
                        throw new ArgumentNullException(
                            nameof(furniture))));
        }

        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public Rect WalkableBounds { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public Rect EntryCorridor { get; }
        public IReadOnlyList<HomeFurnitureFootprint> Furniture
        {
            get;
        }
    }
}
