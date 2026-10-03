using System.Collections.Generic;
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
            string areaId,
            CityDistrictKind district,
            CityLandUseKind landUse,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket,
            string barId,
            BarActivityKind barActivity,
            Vector2Int frontageDirection,
            Vector3 doorPosition,
            Vector3 returnPosition,
            Vector3 sidewalkArrivalPosition,
            int buildingVariant = 0)
        {
            Cell = cell;
            Center = center;
            Size = size;
            Height = height;
            Color = color;
            AreaId = areaId ?? string.Empty;
            District = district;
            LandUse = landUse;
            IsBar = isBar;
            IsPlayerHome = isPlayerHome;
            IsSupermarket = isSupermarket;
            BarId = barId ?? string.Empty;
            BarActivity = barActivity;
            FrontageDirection = frontageDirection;
            DoorPosition = doorPosition;
            ReturnPosition = returnPosition;
            SidewalkArrivalPosition = sidewalkArrivalPosition;
            BuildingVariant = buildingVariant;
        }

        public Vector2Int Cell { get; }

        // Center lies on the ground plane. Builders add Height / 2 on Y for a cube.
        public Vector3 Center { get; }

        // X and Z footprint dimensions.
        public Vector2 Size { get; }
        public Vector2 FootprintSize => Size;
        public float Height { get; }
        public Color Color { get; }
        public int BuildingVariant { get; }
        public string AreaId { get; }
        public CityDistrictKind District { get; }
        public CityLandUseKind LandUse { get; }
        public bool HasBuilding => LandUse == CityLandUseKind.Building;
        public bool IsPark => LandUse == CityLandUseKind.Park;
        public bool IsDistrictPointOfInterest =>
            LandUse == CityLandUseKind.DistrictPointOfInterest;
        public bool IsBar { get; }
        public bool IsPlayerHome { get; }
        public bool IsSupermarket { get; }
        public bool IsOrdinaryBuilding =>
            HasBuilding &&
            !IsBar &&
            !IsPlayerHome &&
            !IsSupermarket;
        public string BarId { get; }
        public BarActivityKind BarActivity { get; }
        public Vector2Int FrontageDirection { get; }
        public bool HasRoadFrontage => FrontageDirection != Vector2Int.zero;
        public Vector3 DoorPosition { get; }

        // Stable graph anchor on the road centerline. Runtime entrances use
        // SidewalkArrivalPosition so scene returns do not place the player in
        // the carriageway.
        public Vector3 ReturnPosition { get; }
        public Vector3 SidewalkArrivalPosition { get; }

        public IReadOnlyList<Rect> CreateCollisionFootprints()
        {
            if (!IsOrdinaryBuilding || BuildingVariant == 0)
                return new[] { new Rect(Center.x - Size.x * .5f,
                    Center.z - Size.y * .5f, Size.x, Size.y) };
            CityBuildingPrototypePose pose =
                CityBuildingPrototypePlacement.ResolveExpectedCityPose(this);
            IReadOnlyList<Bounds> solids = CityBuildingAssetProvider
                .GetExpectedCollisionBounds(District, BuildingVariant);
            var footprints = new Rect[solids.Count];
            for (int index = 0; index < solids.Count; index++)
            {
                Bounds bounds = CityBuildingPrototypePlacement.TransformBounds(solids[index], pose);
                footprints[index] = Rect.MinMaxRect(
                    bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
            }
            return footprints;
        }

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
