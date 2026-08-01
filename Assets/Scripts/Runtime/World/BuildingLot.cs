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
            CityDistrictKind district,
            CityLandUseKind landUse,
            bool isBar,
            bool isPlayerHome,
            string barId,
            BarActivityKind barActivity,
            Vector2Int frontageDirection,
            Vector3 doorPosition,
            Vector3 returnPosition)
        {
            Cell = cell;
            Center = center;
            Size = size;
            Height = height;
            Color = color;
            District = district;
            LandUse = landUse;
            IsBar = isBar;
            IsPlayerHome = isPlayerHome;
            BarId = barId ?? string.Empty;
            BarActivity = barActivity;
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
        public CityDistrictKind District { get; }
        public CityLandUseKind LandUse { get; }
        public bool HasBuilding => LandUse == CityLandUseKind.Building;
        public bool IsPark => LandUse == CityLandUseKind.Park;
        public bool IsDistrictPointOfInterest =>
            LandUse == CityLandUseKind.DistrictPointOfInterest;
        public bool IsBar { get; }
        public bool IsPlayerHome { get; }
        public string BarId { get; }
        public BarActivityKind BarActivity { get; }
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
