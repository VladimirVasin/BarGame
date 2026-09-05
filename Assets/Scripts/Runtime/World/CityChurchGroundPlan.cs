using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The church keeps a level inhabited garden and uses its northern
    /// reserve as a planted earth slope into the adjoining yard. No height
    /// is changed outside the church's own land.
    /// </summary>
    public static class CityChurchGroundPlan
    {
        public const float FlatGardenDepth = 38f;
        public const float FenceHeight = 1.18f;
        public const float FenceThickness = 0.18f;
        public const float MaximumFenceSpan = 2.6f;
        private const float Tolerance = 0.001f;

        public static Rect GetGrounds(CityLayout layout, string areaId)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Rect result = default;
            bool found = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor candidate = layout.Surfaces[index];
                if (candidate.Kind != CitySurfaceKind.ChurchGround ||
                    !string.Equals(candidate.AreaId, areaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Rect bounds = candidate.WorldBounds;
                result = found
                    ? Rect.MinMaxRect(
                        Mathf.Min(result.xMin, bounds.xMin),
                        Mathf.Min(result.yMin, bounds.yMin),
                        Mathf.Max(result.xMax, bounds.xMax),
                        Mathf.Max(result.yMax, bounds.yMax))
                    : bounds;
                found = true;
            }

            return result;
        }

        internal static float SampleDatum(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ)
        {
            Rect grounds = GetGrounds(layout, surface.AreaId);
            float blendStart = grounds.yMin + FlatGardenDepth;
            if (grounds.height <= FlatGardenDepth ||
                worldXZ.y <= blendStart)
            {
                return surface.DatumY;
            }

            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor neighbour = layout.Surfaces[index];
                Rect bounds = neighbour.WorldBounds;
                if (neighbour.Kind == CitySurfaceKind.ChurchGround ||
                    neighbour.IsWater ||
                    Mathf.Abs(bounds.yMin - grounds.yMax) > Tolerance ||
                    worldXZ.x < bounds.xMin - Tolerance ||
                    worldXZ.x > bounds.xMax + Tolerance)
                {
                    continue;
                }

                var edge = new Vector2(worldXZ.x, grounds.yMax);
                float target = CityTerrainSurfacePlan.SampleTop(
                    layout, neighbour, edge) -
                    CityElevationPlan.GroundTopOffset;
                // Linear grading keeps a predictable walking slope. The
                // mesh owns an explicit row at the beginning of the grade.
                return Mathf.Lerp(surface.DatumY, target,
                    Mathf.InverseLerp(blendStart, grounds.yMax, worldXZ.y));
            }

            return surface.DatumY;
        }

        /// <summary>
        /// Transparent iron closes only the existing street frontage outside
        /// its one aperture and true unsupported map edges. The north seam
        /// has adjoining ground, and the cemetery owns its own south fence.
        /// </summary>
        public static IReadOnlyList<CityChurchGroundFenceSpan> CreateFenceSpans(
            CityLayout layout,
            CityChurchPlan church)
        {
            var spans = new List<CityChurchGroundFenceSpan>();
            if (church == null)
            {
                return spans;
            }

            Rect grounds = church.Grounds;
            float halfAccess = church.Access.Width * 0.5f;
            // Set the end posts outside the measured clear aperture.
            float postInset = FenceThickness * 0.5f;
            AppendFence(layout, church, spans,
                new Vector2(grounds.xMin + postInset, grounds.yMin),
                new Vector2(grounds.xMin + postInset,
                    church.Access.Center.z - halfAccess - postInset));
            AppendFence(layout, church, spans,
                new Vector2(grounds.xMin + postInset,
                    church.Access.Center.z + halfAccess + postInset),
                new Vector2(grounds.xMin + postInset, grounds.yMax));

            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.ChurchGround ||
                    !string.Equals(surface.AreaId, church.AreaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Rect bounds = surface.WorldBounds;
                if (!HasNeighbour(layout, surface.Cell + Vector2Int.right) &&
                    !layout.HasRoad(RoadEdge.ForCellFrontage(
                        surface.Cell, Vector2Int.right)))
                {
                    AppendFence(layout, church, spans,
                        new Vector2(bounds.xMax - postInset, bounds.yMin),
                        new Vector2(bounds.xMax - postInset, bounds.yMax));
                }

                if (!HasNeighbour(layout, surface.Cell + Vector2Int.down) &&
                    !layout.HasRoad(RoadEdge.ForCellFrontage(
                        surface.Cell, Vector2Int.down)))
                {
                    AppendFence(layout, church, spans,
                        new Vector2(bounds.xMin, bounds.yMin + postInset),
                        new Vector2(bounds.xMax, bounds.yMin + postInset));
                }
            }

            return spans;
        }

        private static bool HasNeighbour(CityLayout layout, Vector2Int cell)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell == cell)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendFence(
            CityLayout layout,
            CityChurchPlan church,
            ICollection<CityChurchGroundFenceSpan> destination,
            Vector2 first,
            Vector2 second)
        {
            float length = Vector2.Distance(first, second);
            if (length <= Tolerance)
            {
                return;
            }

            int count = Mathf.CeilToInt(length / MaximumFenceSpan);
            for (int index = 0; index < count; index++)
            {
                Vector2 start = Vector2.Lerp(first, second, index / (float)count);
                Vector2 end = Vector2.Lerp(first, second, (index + 1f) / count);
                Vector3 firstGround = GroundPoint(layout, church, start);
                Vector3 secondGround = GroundPoint(layout, church, end);
                destination.Add(new CityChurchGroundFenceSpan(
                    firstGround,
                    secondGround,
                    FenceTop(layout, church, firstGround),
                    FenceTop(layout, church, secondGround)));
            }
        }

        private static float FenceTop(
            CityLayout layout,
            CityChurchPlan church,
            Vector3 ground)
        {
            float baseY = ground.y;
            if (ground.x - church.Grounds.xMin <= FenceThickness &&
                layout.ElevationPlan.TrySampleSurface(
                    new Vector2(church.Grounds.xMin, ground.z),
                    CitySurfaceRole.SidewalkTop,
                    out float streetTop,
                    out _))
            {
                baseY = Mathf.Max(baseY, streetTop);
            }

            return baseY + FenceHeight;
        }

        private static Vector3 GroundPoint(
            CityLayout layout,
            CityChurchPlan church,
            Vector2 point)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (surface.Kind == CitySurfaceKind.ChurchGround &&
                    point.x >= bounds.xMin - Tolerance &&
                    point.x <= bounds.xMax + Tolerance &&
                    point.y >= bounds.yMin - Tolerance &&
                    point.y <= bounds.yMax + Tolerance)
                {
                    return new Vector3(point.x,
                        CityTerrainSurfacePlan.SampleTop(layout, surface, point),
                        point.y);
                }
            }

            return new Vector3(point.x, church.GroundTopY, point.y);
        }
    }

    public readonly struct CityChurchGroundFenceSpan
    {
        internal CityChurchGroundFenceSpan(
            Vector3 first,
            Vector3 second,
            float firstTopY,
            float secondTopY)
        {
            First = first;
            Second = second;
            FirstTopY = firstTopY;
            SecondTopY = secondTopY;
        }

        public Vector3 First { get; }
        public Vector3 Second { get; }
        public float FirstTopY { get; }
        public float SecondTopY { get; }
    }
}
