using UnityEngine;

namespace BarPromenade
{
    public sealed class BuildingLot
    {
        internal BuildingLot(
            Vector2Int cell,
            Vector3 center,
            Vector2 size,
            float height,
            Color color,
            bool isBar,
            string barId,
            Vector2Int frontageDirection,
            Vector3 doorPosition,
            Vector3 returnPosition)
        {
            Cell = cell;
            Center = center;
            Size = size;
            Height = height;
            Color = color;
            IsBar = isBar;
            BarId = barId ?? string.Empty;
            FrontageDirection = frontageDirection;
            DoorPosition = doorPosition;
            ReturnPosition = returnPosition;
        }

        public Vector2Int Cell { get; }

        // Center lies on the ground plane. Builders add Height / 2 on Y for a cube.
        public Vector3 Center { get; }

        // X and Z footprint dimensions.
        public Vector2 Size { get; }
        public Vector2 FootprintSize => Size;
        public float Height { get; }
        public Color Color { get; }
        public bool IsBar { get; }
        public string BarId { get; }
        public Vector2Int FrontageDirection { get; }
        public bool HasRoadFrontage => FrontageDirection != Vector2Int.zero;
        public Vector3 DoorPosition { get; }
        public Vector3 ReturnPosition { get; }

        public Bounds WorldBounds
        {
            get
            {
                Vector3 boundsCenter = Center + (Vector3.up * (Height * 0.5f));
                return new Bounds(
                    boundsCenter,
                    new Vector3(Size.x, Height, Size.y));
            }
        }
    }
}
