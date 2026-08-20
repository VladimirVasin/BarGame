using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityMountainBoundaryValidator
    {
        private const float Tolerance = 0.02f;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityMountainBoundaryPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            bool shouldBeEnabled =
                CityMountainBoundaryDefinition.TryResolve(
                    layout.BlueprintId,
                    out CityMountainBoundaryDefinition definition);
            if (!shouldBeEnabled)
            {
                if (plan.IsEnabled ||
                    plan.RidgeCount != 0 ||
                    plan.HasRiverNotch ||
                    plan.HasTunnel)
                {
                    throw new InvalidOperationException(
                        "A non-coastal blueprint acquired a mountain rim.");
                }

                return;
            }

            if (!plan.IsEnabled ||
                !ReferenceEquals(plan.Definition, definition) ||
                plan.RidgeCount < 6 ||
                plan.GetRidgeCount(CityMountainBoundarySide.West) < 3 ||
                plan.GetRidgeCount(CityMountainBoundarySide.South) < 3 ||
                !plan.HasRiverNotch ||
                !plan.HasTunnel)
            {
                throw new InvalidOperationException(
                    "The default coastal mountain contract is incomplete.");
            }

            ValidateRidges(layout, plan);
            ValidateTunnel(layout, plan.Tunnel);
            ValidateRiverNotch(layout, plan.RiverNotch);
            ValidateOpenings(plan);
        }

        private static void ValidateRidges(
            CityLayout layout,
            CityMountainBoundaryPlan plan)
        {
            var ridgeIds = new HashSet<string>(StringComparer.Ordinal);
            var stationIds = new HashSet<string>(StringComparer.Ordinal);
            CityMountainRidgeDescriptor southWestJoin = null;
            int joinCount = 0;
            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor ridge = plan.Ridges[ridgeIndex];
                if (ridge == null ||
                    string.IsNullOrWhiteSpace(ridge.StableId) ||
                    !ridgeIds.Add(ridge.StableId) ||
                    !Enum.IsDefined(
                        typeof(CityMountainBoundarySide),
                        ridge.Side) ||
                    ridge.Stations.Count < 2)
                {
                    throw new InvalidOperationException(
                        "A mountain ridge descriptor is invalid.");
                }

                if (ridge.IsSouthWestJoin)
                {
                    joinCount++;
                    southWestJoin = ridge;
                    if (ridge.Side != CityMountainBoundarySide.West ||
                        !string.IsNullOrEmpty(ridge.SourceAreaId))
                    {
                        throw new InvalidOperationException(
                            "The south-west join has invalid ownership.");
                    }
                }
                else
                {
                    ValidateSourceArea(ridge);
                }

                for (int stationIndex = 0;
                     stationIndex < ridge.Stations.Count;
                     stationIndex++)
                {
                    CityMountainRidgeStation station =
                        ridge.Stations[stationIndex];
                    ValidateStation(
                        layout,
                        ridge,
                        station,
                        stationIds);
                    if (stationIndex == 0)
                    {
                        continue;
                    }

                    float spacing = Vector2.Distance(
                        ridge.Stations[stationIndex - 1].WorldXZ,
                        station.WorldXZ);
                    if (!IsFinite(spacing) ||
                        spacing <= Tolerance ||
                        spacing >
                        CityMountainBoundaryDefinition
                            .RidgeStationSpacing + Tolerance)
                    {
                        throw new InvalidOperationException(
                            $"Ridge '{ridge.StableId}' has a broken " +
                            "station sequence.");
                    }
                }
            }

            if (joinCount != 1 ||
                !JoinConnectsBothSides(plan, southWestJoin))
            {
                throw new InvalidOperationException(
                    "The mountain rim requires one continuous " +
                    "south-west join.");
            }
        }

        private static void ValidateSourceArea(
            CityMountainRidgeDescriptor ridge)
        {
            bool valid = ridge.Side == CityMountainBoundarySide.West
                ? string.Equals(
                      ridge.SourceAreaId,
                      CityMountainBoundaryDefinition.WestSouthAreaId,
                      StringComparison.Ordinal) ||
                  string.Equals(
                      ridge.SourceAreaId,
                      CityMountainBoundaryDefinition.WestNorthAreaId,
                      StringComparison.Ordinal)
                : string.Equals(
                      ridge.SourceAreaId,
                      CityMountainBoundaryDefinition.SouthWestAreaId,
                      StringComparison.Ordinal) ||
                  string.Equals(
                      ridge.SourceAreaId,
                      CityMountainBoundaryDefinition.SouthEastAreaId,
                      StringComparison.Ordinal);
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Ridge '{ridge.StableId}' has an invalid source area.");
            }
        }

        private static void ValidateStation(
            CityLayout layout,
            CityMountainRidgeDescriptor ridge,
            CityMountainRidgeStation station,
            ISet<string> stationIds)
        {
            if (string.IsNullOrWhiteSpace(station.StableId) ||
                !stationIds.Add(station.StableId) ||
                !IsFinite(station.WorldXZ) ||
                !IsFinite(station.BaseY) ||
                !IsFinite(station.PeakY) ||
                !IsFinite(station.OutwardNormal) ||
                !IsFinite(station.Depth) ||
                station.PeakY - station.BaseY <
                CityMountainBoundaryDefinition.NorthTaperHeight -
                Tolerance ||
                station.Depth <
                CityMountainBoundaryDefinition.RidgeMinimumDepth -
                Tolerance ||
                station.Depth >
                CityMountainBoundaryDefinition.RidgeMaximumDepth +
                Tolerance ||
                Mathf.Abs(station.OutwardNormal.y) > Tolerance ||
                Mathf.Abs(station.OutwardNormal.magnitude - 1f) > Tolerance)
            {
                throw new InvalidOperationException(
                    $"Mountain station '{station.StableId}' is invalid.");
            }

            Vector3 expectedOutward = ridge.IsSouthWestJoin
                ? new Vector3(-1f, 0f, -1f).normalized
                : ridge.Side == CityMountainBoundarySide.West
                    ? Vector3.left
                    : Vector3.back;
            if (Vector3.Dot(
                    station.OutwardNormal,
                    expectedOutward) < 0.995f)
            {
                throw new InvalidOperationException(
                    $"Mountain station '{station.StableId}' points inward.");
            }

            if (!ridge.IsSouthWestJoin &&
                (!TrySampleAreaTop(
                    layout,
                    ridge.SourceAreaId,
                    station.WorldXZ,
                    out float sampledTop) ||
                 Mathf.Abs(sampledTop - station.BaseY) > Tolerance))
            {
                throw new InvalidOperationException(
                    $"Mountain station '{station.StableId}' left its " +
                    "authoritative terrain top.");
            }

            Vector2 outer = new Vector2(
                station.OuterFoot.x,
                station.OuterFoot.z);
            if (!ContainsInclusive(ridge.XZBounds, station.WorldXZ) ||
                !ContainsInclusive(ridge.XZBounds, outer))
            {
                throw new InvalidOperationException(
                    $"Ridge '{ridge.StableId}' has incorrect bounds.");
            }
        }

        private static void ValidateTunnel(
            CityLayout layout,
            CityMountainTunnelDescriptor tunnel)
        {
            if (!string.Equals(
                    tunnel.StableId,
                    "mountain-south-tunnel-stub",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    tunnel.TargetAccessId,
                    CityMountainBoundaryDefinition.TunnelAccessId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    tunnel.AreaId,
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    StringComparison.Ordinal) ||
                !tunnel.IsSealed ||
                !IsFinite(tunnel.PortalGroundCenter) ||
                !IsFinite(tunnel.Axis) ||
                Vector3.Dot(tunnel.Axis, Vector3.back) < 0.995f ||
                Mathf.Abs(tunnel.Axis.magnitude - 1f) > Tolerance ||
                !IsPositiveRect(tunnel.PortalBounds) ||
                !IsPositiveRect(tunnel.ApproachBounds) ||
                Mathf.Abs(
                    tunnel.OpeningWidth - tunnel.PortalBounds.width) >
                Tolerance ||
                Mathf.Abs(
                    tunnel.OpeningWidth -
                    CityMountainBoundaryDefinition.TunnelOpeningWidth) >
                Tolerance ||
                Mathf.Abs(
                    tunnel.OpeningHeight -
                    CityMountainBoundaryDefinition.TunnelOpeningHeight) >
                Tolerance ||
                tunnel.GateInset <= 0f ||
                tunnel.GateInset >= tunnel.ThroatDepth)
            {
                throw new InvalidOperationException(
                    "The sealed south tunnel descriptor is invalid.");
            }

            CityOpenAreaAccessDescriptor access = default;
            bool foundAccess = false;
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (string.Equals(
                        layout.OpenAreaAccesses[index].Id,
                        tunnel.TargetAccessId,
                        StringComparison.Ordinal))
                {
                    access = layout.OpenAreaAccesses[index];
                    foundAccess = true;
                    break;
                }
            }

            if (!foundAccess ||
                Mathf.Abs(access.Center.x -
                          tunnel.PortalGroundCenter.x) > Tolerance ||
                Vector3.Dot(access.OutwardNormal, tunnel.Axis) < 0.995f ||
                !ContainsInclusive(
                    tunnel.ApproachBounds,
                    new Vector2(access.Center.x, access.Center.z)) ||
                !ContainsInclusive(
                    tunnel.ApproachBounds,
                    new Vector2(
                        tunnel.PortalGroundCenter.x,
                        tunnel.PortalGroundCenter.z)) ||
                !TrySampleAreaTop(
                    layout,
                    tunnel.AreaId,
                    new Vector2(
                        tunnel.PortalGroundCenter.x,
                        tunnel.PortalGroundCenter.z),
                    out float groundY) ||
                Mathf.Abs(groundY - tunnel.PortalGroundCenter.y) >
                Tolerance)
            {
                throw new InvalidOperationException(
                    "The south tunnel no longer follows its yard access.");
            }
        }

        private static void ValidateRiverNotch(
            CityLayout layout,
            CityMountainRiverNotchDescriptor notch)
        {
            if (!layout.River.IsEnabled ||
                layout.River.Segments.Count == 0 ||
                !string.Equals(
                    notch.StableId,
                    "mountain-south-river-notch",
                    StringComparison.Ordinal) ||
                notch.Side != CityMountainBoundarySide.South ||
                !IsPositiveRect(notch.OpeningBounds) ||
                !IsFinite(notch.ChannelAxis) ||
                Vector3.Dot(notch.ChannelAxis, Vector3.forward) < 0.995f ||
                notch.ClearWidth <=
                layout.River.Definition.ChannelWidth + Tolerance ||
                !IsFinite(notch.BaseY) ||
                !IsFinite(notch.WestPeakY) ||
                !IsFinite(notch.EastPeakY) ||
                Mathf.Abs(
                    notch.BaseY -
                    layout.River.Segments[0].SouthWaterY) > Tolerance)
            {
                throw new InvalidOperationException(
                    "The south river gorge descriptor is invalid.");
            }
        }

        private static void ValidateOpenings(CityMountainBoundaryPlan plan)
        {
            Rect portal = plan.Tunnel.PortalBounds;
            Rect river = plan.RiverNotch.OpeningBounds;
            if (OverlapsStrict(portal, river))
            {
                throw new InvalidOperationException(
                    "The tunnel and river gorge overlap.");
            }

            float southZ = plan.Tunnel.PortalGroundCenter.z;
            if (!HasSouthEndpoint(plan, portal.xMin, southZ) ||
                !HasSouthEndpoint(plan, portal.xMax, southZ) ||
                !HasSouthEndpoint(plan, river.xMin, southZ) ||
                !HasSouthEndpoint(plan, river.xMax, southZ))
            {
                throw new InvalidOperationException(
                    "A south mountain opening has no ridge shoulder.");
            }

            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor ridge = plan.Ridges[ridgeIndex];
                for (int stationIndex = 1;
                     stationIndex < ridge.Stations.Count;
                     stationIndex++)
                {
                    Vector2 first =
                        ridge.Stations[stationIndex - 1].WorldXZ;
                    Vector2 second = ridge.Stations[stationIndex].WorldXZ;
                    if (SegmentCrossesRect(first, second, portal) ||
                        SegmentCrossesRect(first, second, river))
                    {
                        throw new InvalidOperationException(
                            $"Ridge '{ridge.StableId}' closes a planned " +
                            "south opening.");
                    }
                }
            }
        }

        private static bool JoinConnectsBothSides(
            CityMountainBoundaryPlan plan,
            CityMountainRidgeDescriptor join)
        {
            if (join == null)
            {
                return false;
            }

            bool westMatch = false;
            bool southMatch = false;
            Vector2[] joinEnds = { join.StartXZ, join.EndXZ };
            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor ridge = plan.Ridges[ridgeIndex];
                if (ReferenceEquals(ridge, join))
                {
                    continue;
                }

                for (int endIndex = 0; endIndex < joinEnds.Length; endIndex++)
                {
                    if (Approximately(joinEnds[endIndex], ridge.StartXZ) ||
                        Approximately(joinEnds[endIndex], ridge.EndXZ))
                    {
                        westMatch |=
                            ridge.Side == CityMountainBoundarySide.West;
                        southMatch |=
                            ridge.Side == CityMountainBoundarySide.South;
                    }
                }
            }

            return westMatch && southMatch;
        }

        private static bool HasSouthEndpoint(
            CityMountainBoundaryPlan plan,
            float x,
            float z)
        {
            var target = new Vector2(x, z);
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                CityMountainRidgeDescriptor ridge = plan.Ridges[index];
                if (ridge.Side != CityMountainBoundarySide.South)
                {
                    continue;
                }

                if (Approximately(target, ridge.StartXZ) ||
                    Approximately(target, ridge.EndXZ))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySampleAreaTop(
            CityLayout layout,
            string areaId,
            Vector2 worldXZ,
            out float topY)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!string.Equals(
                        surface.AreaId,
                        areaId,
                        StringComparison.Ordinal) ||
                    !ContainsInclusive(surface.WorldBounds, worldXZ))
                {
                    continue;
                }

                topY = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    surface,
                    worldXZ);
                return true;
            }

            topY = 0f;
            return false;
        }

        private static bool SegmentCrossesRect(
            Vector2 first,
            Vector2 second,
            Rect bounds)
        {
            float xMin = Mathf.Min(first.x, second.x);
            float xMax = Mathf.Max(first.x, second.x);
            float zMin = Mathf.Min(first.y, second.y);
            float zMax = Mathf.Max(first.y, second.y);
            return xMin < bounds.xMax - Tolerance &&
                   xMax > bounds.xMin + Tolerance &&
                   zMin < bounds.yMax - Tolerance &&
                   zMax > bounds.yMin + Tolerance;
        }

        private static bool ContainsInclusive(Rect bounds, Vector2 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.y >= bounds.yMin - Tolerance &&
                   point.y <= bounds.yMax + Tolerance;
        }

        private static bool IsPositiveRect(Rect bounds)
        {
            return IsFinite(bounds.xMin) &&
                   IsFinite(bounds.yMin) &&
                   IsFinite(bounds.xMax) &&
                   IsFinite(bounds.yMax) &&
                   bounds.width > Tolerance &&
                   bounds.height > Tolerance;
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            return left.xMin < right.xMax - Tolerance &&
                   left.xMax > right.xMin + Tolerance &&
                   left.yMin < right.yMax - Tolerance &&
                   left.yMax > right.yMin + Tolerance;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Vector2.SqrMagnitude(left - right) <=
                   Tolerance * Tolerance;
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
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
