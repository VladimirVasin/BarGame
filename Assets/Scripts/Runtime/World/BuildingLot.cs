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
            int buildingVariant = 0,
            Vector3? facadeForward = null)
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
            Vector3 cardinal = new Vector3(frontageDirection.x, 0f, frontageDirection.y);
            if (cardinal.sqrMagnitude < .5f) cardinal = Vector3.back;
            Vector3 facing = facadeForward ?? cardinal;
            facing.y = 0f;
            FacadeForward = facing.sqrMagnitude > .5f ? facing.normalized : cardinal;
            FacadeRotation = Quaternion.LookRotation(FacadeForward, Vector3.up);
            HasFacadeRotation = Vector3.Angle(cardinal, FacadeForward) > .01f;
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
        // Graph frontage remains cardinal. All authored geometry and attached
        // anchors use this one rigid world pose instead.
        public Vector3 FacadeForward { get; }
        public Quaternion FacadeRotation { get; }
        public bool HasFacadeRotation { get; }
        public bool HasRoadFrontage => FrontageDirection != Vector2Int.zero;
        public Vector3 DoorPosition { get; }

        // Stable graph anchor on the road centerline. Runtime entrances use
        // SidewalkArrivalPosition so scene returns do not place the player in
        // the carriageway.
        public Vector3 ReturnPosition { get; }
        public Vector3 SidewalkArrivalPosition { get; }

        public IReadOnlyList<Rect> CreateCollisionFootprints()
        {
            if (HasFacadeRotation)
            {
                IReadOnlyList<Vector2[]> polygons = CreateCollisionPolygons();
                var bounds = new Rect[polygons.Count];
                for (int index = 0; index < polygons.Count; index++)
                    bounds[index] = CityRoadPolygon.Bounds(polygons[index]);
                return bounds;
            }
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

        public IReadOnlyList<Vector2[]> CreateCollisionPolygons()
        {
            if (!IsOrdinaryBuilding || BuildingVariant == 0)
            {
                float width = FrontageDirection.x != 0 ? Size.y : Size.x;
                float depth = FrontageDirection.x != 0 ? Size.x : Size.y;
                return new[] { TransformFootprint(new Bounds(Vector3.zero,
                    new Vector3(width, Height, depth)), Center, FacadeRotation) };
            }
            CityBuildingPrototypePose pose =
                CityBuildingPrototypePlacement.ResolveExpectedCityPose(this);
            IReadOnlyList<Bounds> solids = CityBuildingAssetProvider
                .GetExpectedCollisionBounds(District, BuildingVariant);
            var polygons = new Vector2[solids.Count][];
            for (int index = 0; index < solids.Count; index++)
                polygons[index] = TransformFootprint(solids[index], pose.Position, pose.Rotation);
            return polygons;
        }

        private static Vector2[] TransformFootprint(Bounds bounds,
            Vector3 position, Quaternion rotation)
        {
            Vector3 min = bounds.min, max = bounds.max;
            var points = new[] { new Vector3(min.x, 0f, min.z),
                new Vector3(max.x, 0f, min.z), new Vector3(max.x, 0f, max.z),
                new Vector3(min.x, 0f, max.z) };
            var polygon = new Vector2[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 world = position + rotation * points[index];
                polygon[index] = new Vector2(world.x, world.z);
            }
            return CityRoadPolygon.CounterClockwise(polygon);
        }

        public Bounds WorldBounds
        {
            get
            {
                if (HasFacadeRotation)
                {
                    IReadOnlyList<Vector2[]> polygons = CreateCollisionPolygons();
                    Rect footprint = CityRoadPolygon.Bounds(polygons[0]);
                    for (int index = 1; index < polygons.Count; index++)
                    {
                        Rect part = CityRoadPolygon.Bounds(polygons[index]);
                        footprint = Rect.MinMaxRect(Mathf.Min(footprint.xMin, part.xMin),
                            Mathf.Min(footprint.yMin, part.yMin), Mathf.Max(footprint.xMax, part.xMax),
                            Mathf.Max(footprint.yMax, part.yMax));
                    }
                    return new Bounds(new Vector3(footprint.center.x, Center.y + Height * .5f,
                        footprint.center.y), new Vector3(footprint.width, Height, footprint.height));
                }
                Vector3 boundsCenter = Center + (Vector3.up * (Height * 0.5f));
                return new Bounds(
                    boundsCenter,
                    new Vector3(Size.x, Height, Size.y));
            }
        }
    }
}
