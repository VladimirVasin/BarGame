using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One authoritative height contract for the continuous parts of the
    /// generated city terrain. Roads already flatten the first half-road at
    /// every grid node; applying the same clamped interpolation to adjoining
    /// ground makes their physical tops meet without a hidden vertical lip
    /// while preserving the deterministic river-valley profile.
    /// </summary>
    public static class CityTerrainSurfacePlan
    {
        private const float SampleNormalOffset = 0.10f;
        private const float BeachTopAboveSeaWater = 0.32f;
        internal const float DistrictPointBlendDistance = 4f;

        public static bool UsesContinuousTop(
            CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround ||
                   surface.Kind == CitySurfaceKind.ParkGround ||
                   surface.Kind == CitySurfaceKind.OpenGround ||
                   surface.Kind == CitySurfaceKind.ChurchGround ||
                   surface.Kind == CitySurfaceKind.Beach;
        }

        public static float SampleDatum(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (surface.Kind == CitySurfaceKind.ChurchGround)
            {
                return CityChurchGroundPlan.SampleDatum(layout, surface, worldXZ);
            }

            float baseDatum = SampleDatum(
                layout.ElevationPlan,
                surface,
                worldXZ);
            return ApplyDistrictPointPad(
                layout,
                surface,
                worldXZ,
                baseDatum);
        }

        internal static float SampleDatum(
            CityElevationPlan elevation,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ)
        {
            if (elevation == null)
            {
                throw new ArgumentNullException(nameof(elevation));
            }

            if (!UsesContinuousTop(surface))
            {
                return surface.DatumY;
            }

            if (surface.Kind == CitySurfaceKind.Beach &&
                elevation.IsElevated)
            {
                float waterlineTop =
                    CitySurfaceDescriptor.WaterTopOffset +
                    BeachTopAboveSeaWater;
                float waterlineDatum = waterlineTop -
                                       CityElevationPlan.GroundTopOffset;
                float cellMinimumZ = elevation.WorldOrigin.z +
                                     surface.Cell.y *
                                     elevation.NodeSpacing.y;
                float landwardZ = cellMinimumZ +
                                  elevation.RoadWidth * 0.5f;
                float waterlineZ = cellMinimumZ +
                                   elevation.NodeSpacing.y;
                float amount = Mathf.InverseLerp(
                    landwardZ,
                    waterlineZ,
                    worldXZ.y);
                float landwardDatum = SampleContinuousDatum(
                    elevation,
                    surface.Cell,
                    new Vector2(
                        worldXZ.x,
                        landwardZ),
                    surface.DatumY);
                return Mathf.Lerp(
                    landwardDatum,
                    waterlineDatum,
                    amount) + CityBeachSandPlan.SampleRelief(elevation, surface, worldXZ);
            }

            return SampleContinuousDatum(
                elevation,
                surface.Cell,
                worldXZ,
                surface.DatumY) + CityBeachSandPlan.SampleRelief(elevation, surface, worldXZ);
        }

        internal static float SampleContinuousDatum(
            CityElevationPlan elevation,
            Vector2Int cell,
            Vector2 worldXZ,
            float fallbackDatum)
        {
            if (elevation == null)
            {
                throw new ArgumentNullException(nameof(elevation));
            }

            ResolveCornerElevations(
                elevation,
                cell,
                fallbackDatum,
                out float southWest,
                out float southEast,
                out float northWest,
                out float northEast);

            float cellMinimumX = elevation.WorldOrigin.x +
                                 cell.x *
                                 elevation.NodeSpacing.x;
            float cellMinimumZ = elevation.WorldOrigin.z +
                                 cell.y *
                                 elevation.NodeSpacing.y;
            float halfRoad = elevation.RoadWidth * 0.5f;
            float xAmount = Mathf.InverseLerp(
                cellMinimumX + halfRoad,
                cellMinimumX + elevation.NodeSpacing.x - halfRoad,
                worldXZ.x);
            float zAmount = Mathf.InverseLerp(
                cellMinimumZ + halfRoad,
                cellMinimumZ + elevation.NodeSpacing.y - halfRoad,
                worldXZ.y);
            float south = Mathf.Lerp(southWest, southEast, xAmount);
            float north = Mathf.Lerp(northWest, northEast, xAmount);
            return Mathf.Lerp(south, north, zAmount);
        }

        public static float SampleTop(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ)
        {
            float offset = surface.Kind == CitySurfaceKind.ParkGround
                ? 0f
                : CityElevationPlan.GroundTopOffset;
            return SampleDatum(layout, surface, worldXZ) + offset;
        }

        public static bool TrySampleGroundTop(
            CityLayout layout,
            Vector2 worldXZ,
            out float topY,
            out CitySurfaceDescriptor surface)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            const float tolerance = 0.001f;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor candidate = layout.Surfaces[index];
                Rect bounds = candidate.WorldBounds;
                if (candidate.IsWater ||
                    worldXZ.x < bounds.xMin - tolerance ||
                    worldXZ.x > bounds.xMax + tolerance ||
                    worldXZ.y < bounds.yMin - tolerance ||
                    worldXZ.y > bounds.yMax + tolerance)
                {
                    continue;
                }

                surface = candidate;
                topY = SampleTop(layout, candidate, worldXZ);
                return true;
            }

            topY = 0f;
            surface = default;
            return false;
        }

        public static Vector3 SampleNormal(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ)
        {
            float west = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.left * SampleNormalOffset);
            float east = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.right * SampleNormalOffset);
            float south = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.down * SampleNormalOffset);
            Vector2 northPoint = worldXZ + Vector2.up * SampleNormalOffset;
            float north = surface.Kind == CitySurfaceKind.Beach &&
                          surface.Feature == CityAreaFeatureKind.NorthWaterfront &&
                          northPoint.y > surface.WorldBounds.yMax
                ? CitySeacoastSeaLayout.SampleSeabedTop(layout, surface, northPoint)
                : SampleTop(layout, surface, northPoint);
            var tangentX = new Vector3(
                SampleNormalOffset * 2f,
                east - west,
                0f);
            var tangentZ = new Vector3(
                0f,
                north - south,
                SampleNormalOffset * 2f);
            return Vector3.Cross(tangentZ, tangentX).normalized;
        }

        private static float ApplyDistrictPointPad(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ,
            float baseDatum)
        {
            if (surface.Kind != CitySurfaceKind.BuildableGround)
            {
                return baseDatum;
            }

            float strongestWeight = 0f;
            float targetDatum = baseDatum;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[index];
                float outsideDistance = DistanceOutside(
                    descriptor.PublicBounds,
                    worldXZ);
                if (outsideDistance > DistrictPointBlendDistance)
                {
                    continue;
                }

                float amount = Mathf.Clamp01(
                    outsideDistance / DistrictPointBlendDistance);
                float weight = 1f - Mathf.SmoothStep(0f, 1f, amount);
                if (weight <= strongestWeight)
                {
                    continue;
                }

                strongestWeight = weight;
                targetDatum = descriptor.Center.y;
            }

            return Mathf.Lerp(baseDatum, targetDatum, strongestWeight);
        }

        private static float DistanceOutside(Rect bounds, Vector2 point)
        {
            float xDistance = point.x < bounds.xMin
                ? bounds.xMin - point.x
                : point.x > bounds.xMax
                    ? point.x - bounds.xMax
                    : 0f;
            float zDistance = point.y < bounds.yMin
                ? bounds.yMin - point.y
                : point.y > bounds.yMax
                    ? point.y - bounds.yMax
                    : 0f;
            return Mathf.Max(xDistance, zDistance);
        }

        private static void ResolveCornerElevations(
            CityElevationPlan elevation,
            Vector2Int cell,
            float fallbackDatum,
            out float southWest,
            out float southEast,
            out float northWest,
            out float northEast)
        {
            southWest = ResolveCornerElevation(
                elevation,
                cell,
                fallbackDatum);
            southEast = ResolveCornerElevation(
                elevation,
                cell + Vector2Int.right,
                fallbackDatum);
            northWest = ResolveCornerElevation(
                elevation,
                cell + Vector2Int.up,
                fallbackDatum);
            northEast = ResolveCornerElevation(
                elevation,
                cell + Vector2Int.one,
                fallbackDatum);
        }

        private static float ResolveCornerElevation(
            CityElevationPlan elevation,
            Vector2Int node,
            float fallbackDatum)
        {
            if (elevation.TryGetNodeElevation(node, out float nodeDatum))
            {
                return nodeDatum;
            }

            float total = 0f;
            int count = 0;
            AddCellDatumIfPresent(
                elevation,
                node + new Vector2Int(-1, -1),
                ref total,
                ref count);
            AddCellDatumIfPresent(
                elevation,
                node + Vector2Int.down,
                ref total,
                ref count);
            AddCellDatumIfPresent(
                elevation,
                node + Vector2Int.left,
                ref total,
                ref count);
            AddCellDatumIfPresent(
                elevation,
                node,
                ref total,
                ref count);

            return count > 0
                ? total / count
                : fallbackDatum;
        }

        private static void AddCellDatumIfPresent(
            CityElevationPlan elevation,
            Vector2Int cell,
            ref float total,
            ref int count)
        {
            if (!elevation.CellElevations.TryGetValue(
                    cell,
                    out float cellDatum))
            {
                return;
            }

            total += cellDatum;
            count++;
        }
    }
}
