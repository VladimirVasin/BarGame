using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The wind-dressing plan's contract: bounded budgets (the art
    /// restraint and the physics budget are the same number), unique
    /// ids, finite geometry, cloth grids the panel factory accepts,
    /// and blocking supports that never seal water or a canonical
    /// street approach behind an invisible wall.
    /// </summary>
    internal static class CityWindDressingValidator
    {
        public const int MaximumSupportCount = 96;
        private const float AccessClearance = 0.45f;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityWindDressingPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.ClothCount > CityWindDressingPlan.MaximumClothCount)
            {
                throw new InvalidOperationException(
                    "Wind dressing exceeds its bounded cloth count.");
            }

            if (plan.Supports.Count > MaximumSupportCount)
            {
                throw new InvalidOperationException(
                    "Wind dressing exceeds its bounded support count.");
            }

            foreach (CityWindDressingZone zone in
                     (CityWindDressingZone[])Enum.GetValues(
                         typeof(CityWindDressingZone)))
            {
                if (plan.GetClothCount(zone) >
                    CityWindDressingRules.MaximumClothCount(zone))
                {
                    throw new InvalidOperationException(
                        $"Wind dressing exceeds the '{zone}' zone's " +
                        "cloth budget.");
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Cloths.Count; index++)
            {
                CityWindDressingClothDescriptor cloth =
                    plan.Cloths[index];
                if (string.IsNullOrWhiteSpace(cloth.StableId) ||
                    !ids.Add(cloth.StableId))
                {
                    throw new InvalidOperationException(
                        "Wind dressing cloth descriptors require " +
                        "unique IDs.");
                }

                if (!IsFinite(cloth.Position) ||
                    !IsFinite(cloth.YawDegrees) ||
                    !IsPositiveFinite(cloth.Width) ||
                    !IsPositiveFinite(cloth.Height))
                {
                    throw new InvalidOperationException(
                        $"Wind dressing cloth '{cloth.StableId}' " +
                        "requires finite positive geometry.");
                }

                // The panel factory's own floor: at least a 1x2 grid.
                if (cloth.Columns < 1 || cloth.Rows < 2 ||
                    cloth.TornVariant < 0)
                {
                    throw new InvalidOperationException(
                        $"Wind dressing cloth '{cloth.StableId}' " +
                        "requires a grid the cloth factory accepts.");
                }
            }

            var water = new List<Rect>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].IsWater)
                {
                    water.Add(layout.Surfaces[index].WorldBounds);
                }
            }

            for (int index = 0; index < plan.Supports.Count; index++)
            {
                CityWindDressingSupportDescriptor support =
                    plan.Supports[index];
                if (string.IsNullOrWhiteSpace(support.StableId) ||
                    !ids.Add(support.StableId))
                {
                    throw new InvalidOperationException(
                        "Wind dressing support descriptors require " +
                        "unique IDs.");
                }

                if (!IsFinite(support.Box.Center) ||
                    !IsPositiveFinite(support.Box.Size.x) ||
                    !IsPositiveFinite(support.Box.Size.y) ||
                    !IsPositiveFinite(support.Box.Size.z))
                {
                    throw new InvalidOperationException(
                        $"Wind dressing support '{support.StableId}' " +
                        "requires finite positive geometry.");
                }

                if (!support.BlocksMovement)
                {
                    continue;
                }

                Rect footprint = FootprintOf(support.Box);
                for (int waterIndex = 0;
                     waterIndex < water.Count;
                     waterIndex++)
                {
                    if (OverlapsStrict(footprint, water[waterIndex]))
                    {
                        throw new InvalidOperationException(
                            $"Wind dressing support " +
                            $"'{support.StableId}' overlaps " +
                            "non-walkable water.");
                    }
                }

                ValidateAccessClearance(layout, support, footprint);
            }
        }

        /// <summary>
        /// A blocking pole never stands on a canonical approach: the
        /// open-area accesses and every district POI access keep the
        /// shared 0.45 doorstep clearance.
        /// </summary>
        private static void ValidateAccessClearance(
            CityLayout layout,
            CityWindDressingSupportDescriptor support,
            Rect footprint)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (OverlapsStrict(
                        footprint,
                        Expand(
                            layout.OpenAreaAccesses[index]
                                .ApproachBounds,
                            AccessClearance)))
                {
                    throw new InvalidOperationException(
                        $"Wind dressing support '{support.StableId}' " +
                        "blocks a canonical open-area approach.");
                }
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    if (OverlapsStrict(
                            footprint,
                            Expand(
                                point.Accesses[accessIndex]
                                    .ApproachBounds,
                                AccessClearance)))
                    {
                        throw new InvalidOperationException(
                            "Wind dressing support " +
                            $"'{support.StableId}' blocks a district " +
                            "point-of-interest approach.");
                    }
                }
            }
        }

        /// <summary>
        /// A conservative axis-aligned XZ envelope of the oriented
        /// box, the fringe parts' footprint convention.
        /// </summary>
        private static Rect FootprintOf(RuntimeOrientedBox box)
        {
            Vector3 axisX =
                box.Rotation * new Vector3(box.Size.x * 0.5f, 0f, 0f);
            Vector3 axisY =
                box.Rotation * new Vector3(0f, box.Size.y * 0.5f, 0f);
            Vector3 axisZ =
                box.Rotation * new Vector3(0f, 0f, box.Size.z * 0.5f);
            float extentX = Mathf.Abs(axisX.x) +
                Mathf.Abs(axisY.x) +
                Mathf.Abs(axisZ.x);
            float extentZ = Mathf.Abs(axisX.z) +
                Mathf.Abs(axisY.z) +
                Mathf.Abs(axisZ.z);
            return Rect.MinMaxRect(
                box.Center.x - extentX,
                box.Center.z - extentZ,
                box.Center.x + extentX,
                box.Center.z + extentZ);
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + (amount * 2f),
                source.height + (amount * 2f));
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
