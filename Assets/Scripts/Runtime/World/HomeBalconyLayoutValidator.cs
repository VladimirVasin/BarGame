using System;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeBalconyLayoutValidator
    {
        private const float Tolerance = 0.001f;

        public static void ValidateOrThrow(
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan balcony)
        {
            if (interior == null)
            {
                throw new ArgumentNullException(nameof(interior));
            }

            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            ValidateRect(
                balcony.InteriorAccessPath,
                "The balcony access path");
            ValidateRect(
                balcony.DoorwayBridge,
                "The balcony doorway bridge");
            ValidateRect(
                balcony.BalconyBounds,
                "The balcony bounds");

            if (!Contains(
                    interior.WalkableBounds,
                    balcony.InteriorAccessPath) ||
                !balcony.InteriorAccessPath.Overlaps(
                    balcony.DoorwayBridge,
                    true) ||
                !balcony.DoorwayBridge.Overlaps(
                    balcony.BalconyBounds,
                    true))
            {
                throw new InvalidOperationException(
                    "The room, doorway and balcony must form one connected route.");
            }

            if (balcony.BalconyBounds.xMin >
                    interior.RoomBounds.xMax +
                    Tolerance ||
                balcony.BalconyBounds.xMax <=
                    interior.RoomBounds.xMax + 1f)
            {
                throw new InvalidOperationException(
                    "The balcony must bridge the right facade and extend outdoors.");
            }

            if (!IsPositiveFinite(balcony.DoorSize) ||
                !IsPositiveFinite(balcony.WindowSize) ||
                !IsFinite(balcony.DoorCenter) ||
                !IsFinite(balcony.WindowCenter) ||
                balcony.DoorSize.z <
                    HomeInteriorLayoutValidator
                        .MinimumPathClearance ||
                balcony.DoorSize.y >
                    interior.RoomHeight + Tolerance)
            {
                throw new InvalidOperationException(
                    "The balcony door and window dimensions must be safe and finite.");
            }

            for (int index = 0;
                 index < interior.Furniture.Count;
                 index++)
            {
                HomeFurnitureFootprint furniture =
                    interior.Furniture[index];
                if (furniture.BlocksMovement &&
                    furniture.Bounds.Overlaps(
                        balcony.InteriorAccessPath,
                        true))
                {
                    throw new InvalidOperationException(
                        $"Blocking furniture '{furniture.Id}' intersects the balcony route.");
                }
            }

            if (!interior.TryGetFurniture(
                    HomeFurnitureKind.Sofa,
                    out HomeFurnitureFootprint sofa) ||
                balcony.WindowCenter.y -
                balcony.WindowSize.y * 0.5f <
                sofa.Height - 0.12f)
            {
                throw new InvalidOperationException(
                    "The balcony window must clear the sofa.");
            }

            var walkable =
                new RoadWalkableArea(
                    balcony.WalkableRectangles);
            float radius =
                HomeInteriorLayoutValidator
                    .PlayerClearanceRadius;
            Vector3 roomSample = new Vector3(
                interior.WalkableBounds.xMax - radius,
                0f,
                PlayerHomeBalconyGeometry.DoorCenterZ);
            Vector3 thresholdSample = new Vector3(
                PlayerHomeBalconyGeometry.HomeFacadeX,
                0f,
                PlayerHomeBalconyGeometry.DoorCenterZ);
            Vector3 balconySample = new Vector3(
                balcony.BalconyBounds.xMax - radius,
                0f,
                PlayerHomeBalconyGeometry.BalconyCenterZ);
            if (!walkable.Contains(roomSample, radius) ||
                !walkable.Contains(thresholdSample, radius) ||
                !walkable.Contains(balconySample, radius))
            {
                throw new InvalidOperationException(
                    "The composed balcony walkable area must support the player capsule.");
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static void ValidateRect(
            Rect value,
            string label)
        {
            if (!IsFinite(value.xMin) ||
                !IsFinite(value.xMax) ||
                !IsFinite(value.yMin) ||
                !IsFinite(value.yMax) ||
                value.width <= 0f ||
                value.height <= 0f)
            {
                throw new InvalidOperationException(
                    $"{label} must be finite and positive.");
            }
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y) &&
                   IsPositiveFinite(value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
