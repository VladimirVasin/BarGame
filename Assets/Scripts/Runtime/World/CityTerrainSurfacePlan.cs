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
        private static readonly Vector2Int[] RoadGradeDirections =
        { Vector2Int.down, Vector2Int.right, Vector2Int.up, Vector2Int.left };

        public static bool UsesContinuousTop(
            CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround ||
                   surface.Kind == CitySurfaceKind.ParkGround ||
                   surface.Kind == CitySurfaceKind.OpenGround ||
                   surface.Kind == CitySurfaceKind.ChurchGround ||
                   surface.Kind == CitySurfaceKind.Beach;
        }

        /// <summary>
        /// Everything a sample needs that depends on the surface alone: the
        /// four corner elevations of its cell and, for the beach, the port
        /// access plan. A terrain mesh samples one surface tens of
        /// thousands of times, so these are resolved once per surface and
        /// carried in rather than looked up under every vertex.
        /// </summary>
        internal readonly struct SurfaceContext
        {
            internal SurfaceContext(
                float southWest,
                float southEast,
                float northWest,
                float northEast,
                CityPortAccessPlan access)
            {
                SouthWest = southWest;
                SouthEast = southEast;
                NorthWest = northWest;
                NorthEast = northEast;
                Access = access;
            }

            internal float SouthWest { get; }
            internal float SouthEast { get; }
            internal float NorthWest { get; }
            internal float NorthEast { get; }
            internal CityPortAccessPlan Access { get; }
        }

        internal static SurfaceContext ResolveSurfaceContext(
            CityLayout layout,
            CitySurfaceDescriptor surface)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            ResolveCornerElevations(
                layout.ElevationPlan,
                surface.Cell,
                surface.DatumY,
                out float southWest,
                out float southEast,
                out float northWest,
                out float northEast);
            // Only the beach is graded by the port; the other kinds never
            // needed the plan, and looking it up per sample was the cost.
            // Skipping the lookup here cannot change which port creates the
            // per-layout plan first: RoadFencePlanner.CreatePlan builds it
            // among the world plans before any ground is sampled, and the
            // only plan ahead of it (the arch shelter) never samples terrain.
            CityPortAccessPlan access = surface.Kind == CitySurfaceKind.Beach
                ? CityPortAccessPlan.ForLayout(layout)
                : null;
            return new SurfaceContext(
                southWest,
                southEast,
                northWest,
                northEast,
                access);
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

            return SampleDatum(
                layout,
                surface,
                worldXZ,
                ResolveSurfaceContext(layout, surface));
        }

        internal static float SampleDatum(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ,
            in SurfaceContext context)
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
                worldXZ,
                in context);
            float datum = ApplyDistrictPointPad(
                layout,
                surface,
                worldXZ,
                baseDatum);
            datum = ApplyCurvedRoadGrade(layout, surface, worldXZ, datum);
            if (surface.AreaId == "yard-east" || surface.AreaId == "yard-north-east")
                datum = CityEastExitPlanner.Create(layout).ApplyGroundTop(worldXZ,
                    datum + CityElevationPlan.GroundTopOffset) - CityElevationPlan.GroundTopOffset;
            if (surface.Kind == CitySurfaceKind.Beach && context.Access != null)
                datum = context.Access.ApplyGroundTop(worldXZ, datum + CityElevationPlan.GroundTopOffset) -
                    CityElevationPlan.GroundTopOffset;
            return datum;
        }

        internal static float SampleDatum(
            CityElevationPlan elevation,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ,
            in SurfaceContext context)
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
                Rect cellBounds = elevation.SpatialPlan.GetCellBounds(surface.Cell);
                float cellMinimumZ = elevation.WorldOrigin.z + cellBounds.yMin;
                float landwardZ = cellMinimumZ +
                                  elevation.RoadWidth * 0.5f;
                float waterlineZ = elevation.WorldOrigin.z + cellBounds.yMax;
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
                    in context);
                return Mathf.Lerp(
                    landwardDatum,
                    waterlineDatum,
                    amount) + CityBeachSandPlan.SampleRelief(elevation, surface, worldXZ);
            }

            return SampleContinuousDatum(
                elevation,
                surface.Cell,
                worldXZ,
                in context) + CityBeachSandPlan.SampleRelief(elevation, surface, worldXZ);
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
            return SampleContinuousDatum(
                elevation,
                cell,
                worldXZ,
                new SurfaceContext(
                    southWest,
                    southEast,
                    northWest,
                    northEast,
                    null));
        }

        /// <summary>
        /// The clamped bilinear blend of four corner elevations already
        /// resolved for <paramref name="cell"/>. The arithmetic is the whole
        /// contract: a caller that resolves the corners once and blends here
        /// per vertex gets the same bits as the per-sample overload.
        /// </summary>
        internal static float SampleContinuousDatum(
            CityElevationPlan elevation,
            Vector2Int cell,
            Vector2 worldXZ,
            in SurfaceContext context)
        {
            if (elevation == null)
            {
                throw new ArgumentNullException(nameof(elevation));
            }

            float southWest = context.SouthWest;
            float southEast = context.SouthEast;
            float northWest = context.NorthWest;
            float northEast = context.NorthEast;

            Rect cellBounds = elevation.SpatialPlan.GetCellBounds(cell);
            float cellMinimumX = elevation.WorldOrigin.x + cellBounds.xMin;
            float cellMinimumZ = elevation.WorldOrigin.z + cellBounds.yMin;
            float halfRoad = elevation.RoadWidth * 0.5f;
            float xAmount = Mathf.InverseLerp(
                cellMinimumX + halfRoad,
                elevation.WorldOrigin.x + cellBounds.xMax - halfRoad,
                worldXZ.x);
            float zAmount = Mathf.InverseLerp(
                cellMinimumZ + halfRoad,
                elevation.WorldOrigin.z + cellBounds.yMax - halfRoad,
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

        internal static float SampleTop(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ,
            in SurfaceContext context)
        {
            float offset = surface.Kind == CitySurfaceKind.ParkGround
                ? 0f
                : CityElevationPlan.GroundTopOffset;
            return SampleDatum(layout, surface, worldXZ, in context) + offset;
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

                if (layout.RoadGeometry.IsReplannedCell(candidate.Cell))
                {
                    bool inside = false;
                    foreach (Vector2[] polygon in layout.RoadGeometry.GetGroundPolygons(candidate.Cell))
                        if (CityRoadPolygon.Contains(polygon, worldXZ)) { inside = true; break; }
                    if (!inside) continue;
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
            return SampleNormal(
                layout,
                surface,
                worldXZ,
                ResolveSurfaceContext(layout, surface));
        }

        internal static Vector3 SampleNormal(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2 worldXZ,
            in SurfaceContext context)
        {
            float west = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.left * SampleNormalOffset,
                in context);
            float east = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.right * SampleNormalOffset,
                in context);
            float south = SampleTop(
                layout,
                surface,
                worldXZ + Vector2.down * SampleNormalOffset,
                in context);
            Vector2 northPoint = worldXZ + Vector2.up * SampleNormalOffset;
            float north = surface.Kind == CitySurfaceKind.Beach &&
                          surface.Feature == CityAreaFeatureKind.NorthWaterfront &&
                          northPoint.y > surface.WorldBounds.yMax
                ? CitySeacoastSeaLayout.SampleSeabedTop(layout, surface, northPoint)
                : SampleTop(layout, surface, northPoint, in context);
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
                targetDatum = descriptor.Kind == CityDistrictPointOfInterestKind.IndustrialCannery
                    ? CityCanneryPlan.Create(layout)?.Origin.y ?? descriptor.Center.y
                    : descriptor.Center.y;
            }

            return Mathf.Lerp(baseDatum, targetDatum, strongestWeight);
        }

        private static float ApplyCurvedRoadGrade(CityLayout layout, CitySurfaceDescriptor surface,
            Vector2 point, float datum)
        {
            if (!layout.RoadGeometry.IsReplannedCell(surface.Cell)) return datum;
            float nearest = float.PositiveInfinity;
            float target = datum;
            foreach (Vector2Int direction in RoadGradeDirections)
            {
                RoadEdge edge = RoadEdge.ForCellFrontage(surface.Cell, direction);
                if (!layout.RoadGeometry.IsCurved(edge)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadProjection projection = path.Project(point);
                if (projection.DistanceSquared >= nearest) continue;
                nearest = projection.DistanceSquared;
                target = layout.ElevationPlan.SampleRoadDatum(edge, projection.DistanceAlong / path.Length);
            }
            float outside = Mathf.Sqrt(nearest) - layout.RoadWidth * .5f;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / 4f));
            return Mathf.Lerp(datum, target, weight);
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
