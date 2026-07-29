using System;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorLayoutValidator
    {
        public static void ValidateOrThrow(
            HomeInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!IsPositiveFinite(plan.RoomSize.x) ||
                !IsPositiveFinite(plan.RoomSize.y) ||
                !IsPositiveFinite(plan.RoomHeight))
            {
                throw new InvalidOperationException(
                    "The home room dimensions must be positive and finite.");
            }

            if (!Contains(
                    plan.WalkableBounds,
                    plan.PlayerSpawn,
                    0.35f) ||
                !Contains(
                    plan.WalkableBounds,
                    plan.ExitPosition,
                    0f))
            {
                throw new InvalidOperationException(
                    "The home spawn and exit must lie inside " +
                    "the walkable room.");
            }

            if (!Contains(
                    plan.WalkableBounds,
                    plan.EntryCorridor))
            {
                throw new InvalidOperationException(
                    "The home entry corridor must stay inside " +
                    "the walkable room.");
            }

            for (int index = 0;
                 index < plan.Furniture.Count;
                 index++)
            {
                Rect footprint = plan.Furniture[index].Bounds;
                if (footprint.width <= 0f ||
                    footprint.height <= 0f ||
                    !Contains(
                        plan.WalkableBounds,
                        footprint) ||
                    footprint.Overlaps(
                        plan.EntryCorridor,
                        true))
                {
                    throw new InvalidOperationException(
                        $"Home furniture '{plan.Furniture[index].Kind}' " +
                        "must be bounded and leave the entry clear.");
                }

                for (int other = index + 1;
                     other < plan.Furniture.Count;
                     other++)
                {
                    if (footprint.Overlaps(
                            plan.Furniture[other].Bounds,
                            true))
                    {
                        throw new InvalidOperationException(
                            "Home furniture footprints cannot overlap.");
                    }
                }
            }
        }

        private static bool Contains(
            Rect outer,
            Rect inner)
        {
            return inner.xMin >= outer.xMin &&
                   inner.xMax <= outer.xMax &&
                   inner.yMin >= outer.yMin &&
                   inner.yMax <= outer.yMax;
        }

        private static bool Contains(
            Rect bounds,
            Vector3 position,
            float radius)
        {
            return position.x >= bounds.xMin + radius &&
                   position.x <= bounds.xMax - radius &&
                   position.z >= bounds.yMin + radius &&
                   position.z <= bounds.yMax - radius;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
