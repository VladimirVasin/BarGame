using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Produces the west/south mountain rim from the default coastal
    /// blueprint's stable perimeter yards. The plan owns every opening;
    /// materialisation only follows its already-split ridge strips.
    /// </summary>
    public static class CityMountainBoundaryPlanner
    {
        private const float BoundsTolerance = 0.01f;
        private const uint HeightSalt = 0x4D4F554Eu;
        private const uint DepthSalt = 0x52494447u;

        public static CityMountainBoundaryPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!CityMountainBoundaryDefinition.TryResolve(
                    layout.BlueprintId,
                    out CityMountainBoundaryDefinition definition))
            {
                return CityMountainBoundaryPlan.Empty;
            }

            List<CitySurfaceDescriptor> westSouthSurfaces = GetAreaSurfaces(
                layout,
                CityMountainBoundaryDefinition.WestSouthAreaId);
            List<CitySurfaceDescriptor> westNorthSurfaces = GetAreaSurfaces(
                layout,
                CityMountainBoundaryDefinition.WestNorthAreaId);
            List<CitySurfaceDescriptor> southWestSurfaces = GetAreaSurfaces(
                layout,
                CityMountainBoundaryDefinition.SouthWestAreaId);
            List<CitySurfaceDescriptor> southEastSurfaces = GetAreaSurfaces(
                layout,
                CityMountainBoundaryDefinition.SouthEastAreaId);

            Rect westSouthBounds = Encapsulate(westSouthSurfaces);
            Rect westNorthBounds = Encapsulate(westNorthSurfaces);
            Rect southWestBounds = Encapsulate(southWestSurfaces);
            Rect southEastBounds = Encapsulate(southEastSurfaces);
            CityOpenAreaAccessDescriptor tunnelAccess = GetAccess(
                layout,
                CityMountainBoundaryDefinition.TunnelAccessId);

            CityMountainTunnelDescriptor tunnel = CreateTunnel(
                layout,
                southWestSurfaces,
                southWestBounds,
                tunnelAccess);

            var ridges = new List<CityMountainRidgeDescriptor>();
            CityMountainRidgeDescriptor westSouth = CreateLinearRidge(
                layout,
                "mountain-west-south",
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundarySide.West,
                new Vector2(westSouthBounds.xMin, westSouthBounds.yMin),
                new Vector2(westSouthBounds.xMin, westSouthBounds.yMax),
                Vector3.left,
                westSouthSurfaces,
                false);
            ridges.Add(westSouth);
            ridges.Add(CreateLinearRidge(
                layout,
                "mountain-west-north",
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityMountainBoundarySide.West,
                new Vector2(westNorthBounds.xMin, westNorthBounds.yMin),
                new Vector2(westNorthBounds.xMin, westNorthBounds.yMax),
                Vector3.left,
                westNorthSurfaces,
                true));

            Vector2 southWestStart = new Vector2(
                southWestBounds.xMin,
                southWestBounds.yMin);
            CityMountainRidgeDescriptor southWestBeforeTunnel =
                CreateLinearRidge(
                    layout,
                    "mountain-south-west-before-tunnel",
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    CityMountainBoundarySide.South,
                    southWestStart,
                    new Vector2(
                        tunnel.PortalBounds.xMin,
                        southWestBounds.yMin),
                    Vector3.back,
                    southWestSurfaces,
                    false);
            ridges.Add(southWestBeforeTunnel);

            CityMountainRidgeDescriptor southWestAfterTunnel =
                CreateLinearRidge(
                    layout,
                    "mountain-south-west-after-tunnel",
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    CityMountainBoundarySide.South,
                    new Vector2(
                        tunnel.PortalBounds.xMax,
                        southWestBounds.yMin),
                    new Vector2(
                        southWestBounds.xMax,
                        southWestBounds.yMin),
                    Vector3.back,
                    southWestSurfaces,
                    false);
            ridges.Add(southWestAfterTunnel);

            CityMountainRidgeDescriptor southEast = CreateLinearRidge(
                layout,
                "mountain-south-east",
                CityMountainBoundaryDefinition.SouthEastAreaId,
                CityMountainBoundarySide.South,
                new Vector2(southEastBounds.xMin, southEastBounds.yMin),
                new Vector2(southEastBounds.xMax, southEastBounds.yMin),
                Vector3.back,
                southEastSurfaces,
                false);
            ridges.Add(southEast);

            CityMountainRidgeDescriptor southWestJoin = CreateSouthWestJoin(
                layout,
                westSouth,
                southWestBeforeTunnel);
            ridges.Add(southWestJoin);

            CityMountainCornerClosureDescriptor southWestCornerClosure =
                CreateSouthWestCornerClosure(
                    layout,
                    westSouthSurfaces,
                    westSouthBounds,
                    southWestSurfaces,
                    southWestBounds,
                    southWestJoin);

            CityMountainRiverNotchDescriptor notch = CreateRiverCave(
                layout,
                southWestBounds,
                southEastBounds,
                southWestAfterTunnel,
                southEast);
            var plan = new CityMountainBoundaryPlan(
                definition,
                ridges,
                true,
                southWestCornerClosure,
                true,
                notch,
                true,
                tunnel);
            CityMountainBoundaryValidator.ValidateOrThrow(layout, plan);
            return plan;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityMountainBoundaryPlan plan)
        {
            CityMountainBoundaryValidator.ValidateOrThrow(layout, plan);
        }

        private static CityMountainTunnelDescriptor CreateTunnel(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect areaBounds,
            CityOpenAreaAccessDescriptor access)
        {
            float halfWidth =
                CityMountainBoundaryDefinition.TunnelOpeningWidth * 0.5f;
            float centerX = Mathf.Clamp(
                access.Center.x,
                areaBounds.xMin + halfWidth,
                areaBounds.xMax - halfWidth);
            var portalXZ = new Vector2(centerX, areaBounds.yMin);
            float groundY = SampleAreaTop(layout, surfaces, portalXZ);
            float halfDepth =
                CityMountainBoundaryDefinition.TunnelPortalDepth * 0.5f;
            Rect portalBounds = Rect.MinMaxRect(
                centerX - halfWidth,
                portalXZ.y - halfDepth,
                centerX + halfWidth,
                portalXZ.y + halfDepth);
            Rect approachBounds = Rect.MinMaxRect(
                centerX - halfWidth,
                Mathf.Min(portalXZ.y, access.ApproachBounds.yMin),
                centerX + halfWidth,
                Mathf.Max(portalXZ.y, access.ApproachBounds.yMax));
            return new CityMountainTunnelDescriptor(
                "mountain-south-tunnel-stub",
                access.Id,
                access.AreaId,
                new Vector3(centerX, groundY, portalXZ.y),
                access.OutwardNormal.normalized,
                portalBounds,
                approachBounds,
                CityMountainBoundaryDefinition.TunnelOpeningWidth,
                CityMountainBoundaryDefinition.TunnelOpeningHeight,
                CityMountainBoundaryDefinition.TunnelThroatDepth,
                CityMountainBoundaryDefinition.TunnelGateInset,
                true);
        }

        private static CityMountainRidgeDescriptor CreateLinearRidge(
            CityLayout layout,
            string stableId,
            string sourceAreaId,
            CityMountainBoundarySide side,
            Vector2 start,
            Vector2 end,
            Vector3 outwardNormal,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            bool taperNorthEnd)
        {
            float length = Vector2.Distance(start, end);
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    length /
                    CityMountainBoundaryDefinition.RidgeStationSpacing));
            var stations = new List<CityMountainRidgeStation>(
                segmentCount + 1);
            for (int index = 0; index <= segmentCount; index++)
            {
                float amount = index / (float)segmentCount;
                Vector2 worldXZ = Vector2.Lerp(start, end, amount);
                float baseY = SampleAreaTop(layout, surfaces, worldXZ);
                float height = ResolveHeight(layout.Seed, worldXZ);
                if (taperNorthEnd && amount > 0.70f)
                {
                    float taper = Mathf.InverseLerp(0.70f, 1f, amount);
                    height = Mathf.Lerp(
                        height,
                        CityMountainBoundaryDefinition.NorthTaperHeight,
                        Mathf.SmoothStep(0f, 1f, taper));
                }

                stations.Add(CreateStation(
                    layout.Seed,
                    $"{stableId}-station-{index:00}",
                    worldXZ,
                    baseY,
                    height,
                    outwardNormal));
            }

            return new CityMountainRidgeDescriptor(
                stableId,
                sourceAreaId,
                side,
                stations,
                false);
        }

        private static CityMountainRidgeDescriptor CreateSouthWestJoin(
            CityLayout layout,
            CityMountainRidgeDescriptor west,
            CityMountainRidgeDescriptor south)
        {
            CityMountainRidgeStation westEndpoint = west.Stations[0];
            CityMountainRidgeStation southEndpoint = south.Stations[0];
            Vector2 start = westEndpoint.WorldXZ;
            Vector2 end = southEndpoint.WorldXZ;
            int segmentCount = Mathf.Max(
                2,
                Mathf.CeilToInt(
                    Vector2.Distance(start, end) /
                    CityMountainBoundaryDefinition.RidgeStationSpacing));
            var stations = new List<CityMountainRidgeStation>(
                segmentCount + 1);
            for (int index = 0; index <= segmentCount; index++)
            {
                float amount = index / (float)segmentCount;
                Vector2 worldXZ = Vector2.Lerp(start, end, amount);
                float baseY = Mathf.Lerp(
                    westEndpoint.BaseY,
                    southEndpoint.BaseY,
                    amount);
                Vector3 outward = Vector3.Lerp(
                    westEndpoint.OutwardNormal,
                    southEndpoint.OutwardNormal,
                    amount).normalized;
                stations.Add(CreateStation(
                    layout.Seed,
                    $"mountain-south-west-join-station-{index:00}",
                    worldXZ,
                    baseY,
                    ResolveHeight(layout.Seed, worldXZ),
                    outward));
            }

            return new CityMountainRidgeDescriptor(
                "mountain-south-west-join",
                string.Empty,
                CityMountainBoundarySide.West,
                stations,
                true);
        }

        private static CityMountainCornerClosureDescriptor
            CreateSouthWestCornerClosure(
                CityLayout layout,
                IReadOnlyList<CitySurfaceDescriptor> westSurfaces,
                Rect westBounds,
                IReadOnlyList<CitySurfaceDescriptor> southSurfaces,
                Rect southBounds,
                CityMountainRidgeDescriptor join)
        {
            if (join.Stations.Count != 3)
            {
                throw new InvalidOperationException(
                    "The south-west corner apron requires one middle " +
                    "join station.");
            }

            Vector3 roadNode = layout.GetNodeWorldPosition(Vector2Int.zero);
            float halfRoad = layout.RoadWidth * 0.5f;
            var roadCornerXZ = new Vector2(
                roadNode.x - halfRoad,
                roadNode.z - halfRoad);
            List<Vector3> westBoundarySamples = CreateBoundarySamples(
                layout,
                westSurfaces,
                join.Stations[0].WorldXZ,
                new Vector2(westBounds.xMax, westBounds.yMin),
                true);
            List<Vector3> southBoundarySamples = CreateBoundarySamples(
                layout,
                southSurfaces,
                new Vector2(southBounds.xMin, southBounds.yMax),
                join.Stations[join.Stations.Count - 1].WorldXZ,
                false);
            westBoundarySamples[0] = join.Stations[0].Toe;
            southBoundarySamples[southBoundarySamples.Count - 1] =
                join.Stations[join.Stations.Count - 1].Toe;
            return new CityMountainCornerClosureDescriptor(
                CityMountainCornerClosureDescriptor.SouthWestStableId,
                new Vector2Int(-1, -1),
                join.StableId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                westBoundarySamples,
                new Vector3(
                    roadCornerXZ.x,
                    roadNode.y + CityStreetSurfacePlanner.RoadTop,
                    roadCornerXZ.y),
                southBoundarySamples,
                join.Stations[1].Toe);
        }

        private static List<Vector3> CreateBoundarySamples(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Vector2 start,
            Vector2 end,
            bool sampleXAxis)
        {
            CitySurfaceDescriptor surface = FindSurfaceContainingEdge(
                surfaces,
                start,
                end);
            float startCoordinate = sampleXAxis ? start.x : start.y;
            float endCoordinate = sampleXAxis ? end.x : end.y;
            float minimum = Mathf.Min(startCoordinate, endCoordinate);
            float maximum = Mathf.Max(startCoordinate, endCoordinate);
            float cellMinimum = sampleXAxis
                ? layout.ElevationPlan.WorldOrigin.x +
                  surface.Cell.x * layout.ElevationPlan.NodeSpacing.x
                : layout.ElevationPlan.WorldOrigin.z +
                  surface.Cell.y * layout.ElevationPlan.NodeSpacing.y;
            float spacing = sampleXAxis
                ? layout.ElevationPlan.NodeSpacing.x
                : layout.ElevationPlan.NodeSpacing.y;
            float halfRoad = layout.ElevationPlan.RoadWidth * 0.5f;
            List<float> coordinates =
                CityTerrainSurfaceWorldBuilder.CreateAxisCoordinates(
                    minimum,
                    maximum,
                    cellMinimum + halfRoad,
                    cellMinimum + spacing - halfRoad,
                    Array.Empty<float>());
            var result = new List<Vector3>(coordinates.Count);
            bool reverse = startCoordinate > endCoordinate;
            for (int index = 0; index < coordinates.Count; index++)
            {
                int coordinateIndex = reverse
                    ? coordinates.Count - 1 - index
                    : index;
                float coordinate = coordinates[coordinateIndex];
                var worldXZ = sampleXAxis
                    ? new Vector2(coordinate, start.y)
                    : new Vector2(start.x, coordinate);
                result.Add(new Vector3(
                    worldXZ.x,
                    CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        worldXZ),
                    worldXZ.y));
            }

            return result;
        }

        private static CitySurfaceDescriptor FindSurfaceContainingEdge(
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Vector2 start,
            Vector2 end)
        {
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (ContainsInclusive(bounds, start) &&
                    ContainsInclusive(bounds, end))
                {
                    return surface;
                }
            }

            throw new InvalidOperationException(
                "A mountain corner boundary left its source terrain.");
        }

        private static bool ContainsInclusive(Rect bounds, Vector2 point)
        {
            return point.x >= bounds.xMin - BoundsTolerance &&
                   point.x <= bounds.xMax + BoundsTolerance &&
                   point.y >= bounds.yMin - BoundsTolerance &&
                   point.y <= bounds.yMax + BoundsTolerance;
        }

        private static CityMountainRiverNotchDescriptor CreateRiverCave(
            CityLayout layout,
            Rect southWestBounds,
            Rect southEastBounds,
            CityMountainRidgeDescriptor westRidge,
            CityMountainRidgeDescriptor eastRidge)
        {
            if (!layout.River.IsEnabled || layout.River.Segments.Count == 0)
            {
                throw new InvalidOperationException(
                    "The default mountain boundary requires its river.");
            }

            CityRiverSegmentDescriptor southRiver =
                layout.River.Segments[0];
            CityRiverPromenadeDescriptor westPromenade = default;
            CityRiverPromenadeDescriptor eastPromenade = default;
            bool hasWestPromenade = false;
            bool hasEastPromenade = false;
            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[index];
                if (promenade.WestBank)
                {
                    westPromenade = promenade;
                    hasWestPromenade = true;
                }
                else
                {
                    eastPromenade = promenade;
                    hasEastPromenade = true;
                }
            }

            if (!hasWestPromenade || !hasEastPromenade)
            {
                throw new InvalidOperationException(
                    "The river cave requires both promenade banks.");
            }

            float mountainLineZ = Mathf.Min(
                southWestBounds.yMin,
                southEastBounds.yMin);
            Rect approach = Rect.MinMaxRect(
                southWestBounds.xMax,
                mountainLineZ,
                southEastBounds.xMin,
                southRiver.WaterBounds.yMin);
            Rect waterApproach = Rect.MinMaxRect(
                southRiver.WaterBounds.xMin,
                mountainLineZ,
                southRiver.WaterBounds.xMax,
                southRiver.WaterBounds.yMin);
            float portalHalfDepth =
                CityMountainBoundaryDefinition.RiverCavePortalDepth * 0.5f;
            Rect mouth = Rect.MinMaxRect(
                southRiver.WaterBounds.xMin,
                mountainLineZ - portalHalfDepth,
                southRiver.WaterBounds.xMax,
                mountainLineZ + portalHalfDepth);
            Rect throatWater = Rect.MinMaxRect(
                southRiver.WaterBounds.xMin,
                mountainLineZ -
                CityMountainBoundaryDefinition.RiverCaveThroatDepth,
                southRiver.WaterBounds.xMax,
                mountainLineZ);
            Rect westBank = Rect.MinMaxRect(
                westPromenade.Bounds.xMin,
                mountainLineZ,
                southRiver.WaterBounds.xMin,
                southRiver.WaterBounds.yMin);
            Rect eastBank = Rect.MinMaxRect(
                southRiver.WaterBounds.xMax,
                mountainLineZ,
                eastPromenade.Bounds.xMax,
                southRiver.WaterBounds.yMin);
            Rect westPromenadeApproach = Rect.MinMaxRect(
                westPromenade.Bounds.xMin,
                mountainLineZ,
                westPromenade.Bounds.xMax,
                southRiver.WaterBounds.yMin);
            Rect eastPromenadeApproach = Rect.MinMaxRect(
                eastPromenade.Bounds.xMin,
                mountainLineZ,
                eastPromenade.Bounds.xMax,
                southRiver.WaterBounds.yMin);
            CityMountainRidgeStation westStation =
                westRidge.Stations[westRidge.Stations.Count - 1];
            CityMountainRidgeStation eastStation = eastRidge.Stations[0];
            return new CityMountainRiverNotchDescriptor(
                "mountain-south-river-cave",
                approach,
                waterApproach,
                mouth,
                throatWater,
                westBank,
                eastBank,
                westPromenadeApproach,
                eastPromenadeApproach,
                Vector3.back,
                southRiver.SouthWaterY,
                westPromenade.SouthY,
                westStation.BaseY,
                eastPromenade.SouthY,
                eastStation.BaseY,
                CityMountainBoundaryDefinition.RiverCaveOpeningHeight,
                CityMountainBoundaryDefinition.RiverCaveThroatDepth,
                westStation.PeakY,
                eastStation.PeakY);
        }

        private static CityMountainRidgeStation CreateStation(
            int citySeed,
            string stableId,
            Vector2 worldXZ,
            float baseY,
            float height,
            Vector3 outwardNormal)
        {
            uint hash = StableHash(citySeed, worldXZ, DepthSalt);
            float depthAmount = (hash & 0xFFFFu) / 65535f;
            float depth = Mathf.Lerp(
                CityMountainBoundaryDefinition.RidgeMinimumDepth,
                CityMountainBoundaryDefinition.RidgeMaximumDepth,
                depthAmount);
            return new CityMountainRidgeStation(
                stableId,
                worldXZ,
                baseY,
                baseY + height,
                outwardNormal.normalized,
                depth,
                unchecked((int)hash));
        }

        private static float ResolveHeight(int citySeed, Vector2 worldXZ)
        {
            uint hash = StableHash(citySeed, worldXZ, HeightSalt);
            float amount = (hash & 0xFFFFu) / 65535f;
            return Mathf.Lerp(
                CityMountainBoundaryDefinition.RidgeMinimumHeight,
                CityMountainBoundaryDefinition.RidgeMaximumHeight,
                amount);
        }

        private static List<CitySurfaceDescriptor> GetAreaSurfaces(
            CityLayout layout,
            string areaId)
        {
            var result = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (string.Equals(
                        surface.AreaId,
                        areaId,
                        StringComparison.Ordinal) &&
                    surface.Kind == CitySurfaceKind.OpenGround)
                {
                    result.Add(surface);
                }
            }

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Mountain boundary area '{areaId}' has no ground.");
            }

            return result;
        }

        private static CityOpenAreaAccessDescriptor GetAccess(
            CityLayout layout,
            string accessId)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (string.Equals(
                        access.Id,
                        accessId,
                        StringComparison.Ordinal))
                {
                    return access;
                }
            }

            throw new InvalidOperationException(
                $"Mountain tunnel access '{accessId}' is missing.");
        }

        private static Rect Encapsulate(
            IReadOnlyList<CitySurfaceDescriptor> surfaces)
        {
            Rect bounds = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                Rect next = surfaces[index].WorldBounds;
                bounds = Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, next.xMin),
                    Mathf.Min(bounds.yMin, next.yMin),
                    Mathf.Max(bounds.xMax, next.xMax),
                    Mathf.Max(bounds.yMax, next.yMax));
            }

            return bounds;
        }

        private static float SampleAreaTop(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Vector2 worldXZ)
        {
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (worldXZ.x < bounds.xMin - BoundsTolerance ||
                    worldXZ.x > bounds.xMax + BoundsTolerance ||
                    worldXZ.y < bounds.yMin - BoundsTolerance ||
                    worldXZ.y > bounds.yMax + BoundsTolerance)
                {
                    continue;
                }

                return CityTerrainSurfacePlan.SampleTop(
                    layout,
                    surface,
                    worldXZ);
            }

            throw new InvalidOperationException(
                $"Mountain station {worldXZ} has no source ground.");
        }

        private static uint StableHash(
            int seed,
            Vector2 worldXZ,
            uint salt)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(worldXZ.x * 10f);
                int z = Mathf.RoundToInt(worldXZ.y * 10f);
                uint value = (uint)seed ^ salt;
                value = (value ^ (uint)x) * 16777619u;
                value = (value ^ (uint)z) * 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }
}
