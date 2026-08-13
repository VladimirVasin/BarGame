using System;
using UnityEngine;

namespace BarPromenade
{
    public static class PlayerHomeBalconyGeometry
    {
        public const float PreferredBuildingHeight = 8.8f;
        public const float ApartmentFloorElevation = 4.7f;
        public const float HomeFacadeX = 5f;
        public const float WallThickness = 0.24f;

        public const float DoorCenterZ = -0.50f;
        public const float DoorWidth = 1.36f;
        public const float DoorHeight = 2.34f;

        public const float WindowCenterZ = -2.35f;
        public const float WindowCenterY = 2.08f;
        public const float WindowWidth = 1.48f;
        public const float WindowHeight = 1.10f;

        public const float BalconyCenterZ = -1.45f;
        public const float BalconyWidth = 3.90f;
        public const float BalconyDepth = 2.30f;
        public const float BalconySlabThickness = 0.18f;
        public const float RailingHeight = 1.05f;
        public const float RailingThickness = 0.10f;
        public const float RailingCapHeight = 0.07f;

        public static float ResolveBuildingHeight(
            CityGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return Mathf.Min(
                PreferredBuildingHeight,
                settings.MaximumBuildingHeight);
        }

        public static bool SupportsThirdFloor(float buildingHeight)
        {
            return IsFinite(buildingHeight) &&
                   buildingHeight >=
                   ApartmentFloorElevation +
                   DoorHeight +
                   0.35f;
        }

        public static Vector3 GetFrontageDirection(BuildingLot home)
        {
            ValidateHome(home);
            return new Vector3(
                home.FrontageDirection.x,
                0f,
                home.FrontageDirection.y);
        }

        public static Vector3 GetTangent(BuildingLot home)
        {
            Vector3 direction = GetFrontageDirection(home);
            return new Vector3(
                -direction.z,
                0f,
                direction.x);
        }

        public static Vector3 ToHomeLocal(
            BuildingLot home,
            Vector3 cityWorldPosition)
        {
            ValidateHome(home);
            Vector3 direction = GetFrontageDirection(home);
            Vector3 tangent = GetTangent(home);
            Vector3 delta =
                cityWorldPosition - home.DoorPosition;
            return new Vector3(
                HomeFacadeX + Vector3.Dot(delta, direction),
                delta.y - ApartmentFloorElevation,
                Vector3.Dot(delta, tangent));
        }

        public static Vector3 ToCityWorld(
            BuildingLot home,
            Vector3 homeLocalPosition)
        {
            ValidateHome(home);
            Vector3 direction = GetFrontageDirection(home);
            Vector3 tangent = GetTangent(home);
            return home.DoorPosition +
                   direction *
                   (homeLocalPosition.x - HomeFacadeX) +
                   tangent * homeLocalPosition.z +
                   Vector3.up *
                   (homeLocalPosition.y +
                    ApartmentFloorElevation);
        }

        public static Vector3 ToHomeLocalDirection(
            BuildingLot home,
            Vector3 cityWorldDirection)
        {
            ValidateHome(home);
            Vector3 direction = GetFrontageDirection(home);
            Vector3 tangent = GetTangent(home);
            return new Vector3(
                Vector3.Dot(cityWorldDirection, direction),
                cityWorldDirection.y,
                Vector3.Dot(cityWorldDirection, tangent));
        }

        public static Vector3 ToHomeLocalSize(
            BuildingLot home,
            Vector3 cityWorldSize)
        {
            ValidateHome(home);
            Vector3 direction = GetFrontageDirection(home);
            Vector3 tangent = GetTangent(home);
            return new Vector3(
                Mathf.Abs(direction.x) * cityWorldSize.x +
                Mathf.Abs(direction.z) * cityWorldSize.z,
                cityWorldSize.y,
                Mathf.Abs(tangent.x) * cityWorldSize.x +
                Mathf.Abs(tangent.z) * cityWorldSize.z);
        }

        public static Rect ToHomeLocalRect(
            BuildingLot home,
            Rect cityWorldRect)
        {
            Vector3 first = ToHomeLocal(
                home,
                new Vector3(
                    cityWorldRect.xMin,
                    0f,
                    cityWorldRect.yMin));
            Vector3 second = ToHomeLocal(
                home,
                new Vector3(
                    cityWorldRect.xMax,
                    0f,
                    cityWorldRect.yMax));
            return Rect.MinMaxRect(
                Mathf.Min(first.x, second.x),
                Mathf.Min(first.z, second.z),
                Mathf.Max(first.x, second.x),
                Mathf.Max(first.z, second.z));
        }

        public static Vector3 GetCityBalconyCenter(
            BuildingLot home)
        {
            return ToCityWorld(
                home,
                new Vector3(
                    HomeFacadeX + BalconyDepth * 0.5f,
                    -BalconySlabThickness * 0.5f,
                    BalconyCenterZ));
        }

        private static void ValidateHome(BuildingLot home)
        {
            if (home == null)
            {
                throw new ArgumentNullException(nameof(home));
            }

            if (!home.IsPlayerHome ||
                !home.HasRoadFrontage)
            {
                throw new ArgumentException(
                    "Balcony geometry requires the player-home lot.",
                    nameof(home));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
