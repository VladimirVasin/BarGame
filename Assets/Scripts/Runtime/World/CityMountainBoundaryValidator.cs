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
                    plan.HasSouthWestCornerClosure ||
                    plan.SouthWestCornerClosure != null ||
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
                !plan.HasSouthWestCornerClosure ||
                plan.SouthWestCornerClosure == null ||
                !plan.HasRiverNotch ||
                !plan.HasTunnel)
            {
                throw new InvalidOperationException(
                    "The default coastal mountain contract is incomplete.");
            }

            CityMountainRidgeDescriptor southWestJoin =
                ValidateRidges(layout, plan);
            ValidateSouthWestCornerClosure(
                layout,
                plan.SouthWestCornerClosure,
                southWestJoin);
            ValidateTunnel(layout, plan.Tunnel);
            ValidateRiverNotch(layout, plan.RiverNotch);
            ValidateOpenings(plan);
        }

        private static CityMountainRidgeDescriptor ValidateRidges(
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
                        stationIndex,
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

            return southWestJoin;
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

        private static void ValidateSouthWestCornerClosure(
            CityLayout layout,
            CityMountainCornerClosureDescriptor closure,
            CityMountainRidgeDescriptor join)
        {
            if (!string.Equals(
                    closure.StableId,
                    CityMountainCornerClosureDescriptor.SouthWestStableId,
                    StringComparison.Ordinal) ||
                closure.VoidCell != new Vector2Int(-1, -1) ||
                !string.Equals(
                    closure.JoinStableId,
                    join.StableId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    closure.WestAreaId,
                    CityMountainBoundaryDefinition.WestSouthAreaId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    closure.SouthAreaId,
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    StringComparison.Ordinal) ||
                join.Stations.Count != 3)
            {
                throw new InvalidOperationException(
                    "The south-west mountain corner closure has invalid " +
                    "ownership.");
            }

            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell == closure.VoidCell)
                {
                    throw new InvalidOperationException(
                        "The mountain corner closure must only fill an " +
                        "omitted blueprint cell.");
                }
            }

            Vector3 roadNode = layout.GetNodeWorldPosition(Vector2Int.zero);
            float halfRoad = layout.RoadWidth * 0.5f;
            var westGroundXZ = new Vector2(
                roadNode.x - halfRoad,
                roadNode.z);
            var roadCorner = new Vector3(
                roadNode.x - halfRoad,
                roadNode.y + CityStreetSurfacePlanner.RoadTop,
                roadNode.z - halfRoad);
            var southGroundXZ = new Vector2(
                roadNode.x,
                roadNode.z - halfRoad);
            List<Vector3> expectedWest = CreateExpectedBoundarySamples(
                layout,
                closure.WestAreaId,
                join.Stations[0].WorldXZ,
                westGroundXZ,
                true);
            List<Vector3> expectedSouth = CreateExpectedBoundarySamples(
                layout,
                closure.SouthAreaId,
                southGroundXZ,
                join.Stations[join.Stations.Count - 1].WorldXZ,
                false);
            expectedWest[0] = join.Stations[0].Toe;
            expectedSouth[expectedSouth.Count - 1] =
                join.Stations[join.Stations.Count - 1].Toe;
            var expected = new CityMountainCornerClosureDescriptor(
                CityMountainCornerClosureDescriptor.SouthWestStableId,
                new Vector2Int(-1, -1),
                join.StableId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                expectedWest,
                roadCorner,
                expectedSouth,
                join.Stations[1].Toe);
            if (!closure.Equals(expected) ||
                closure.ProjectedArea <= Tolerance ||
                !IsPositiveRect(closure.XZBounds))
            {
                throw new InvalidOperationException(
                    "The south-west mountain corner closure left its road, " +
                    "terrain or rock-toe boundary.");
            }

            ValidateCornerGround(closure);

            if (closure.SurfaceTriangleIndices.Count == 0 ||
                closure.SupportTriangleIndices.Count != 0 ||
                closure.TriangleIndices.Count !=
                closure.SurfaceTriangleIndices.Count)
            {
                throw new InvalidOperationException(
                    "The mountain corner closure must be one continuous " +
                    "ground surface without stairs or retaining faces.");
            }

            float projectedTriangleArea = 0f;
            for (int index = 0;
                 index < closure.SurfaceTriangleIndices.Count;
                 index += 3)
            {
                if (index + 2 >= closure.TriangleIndices.Count)
                {
                    throw new InvalidOperationException(
                        "The mountain corner closure has incomplete " +
                        "triangles.");
                }

                int first = closure.SurfaceTriangleIndices[index];
                int second = closure.SurfaceTriangleIndices[index + 1];
                int third = closure.SurfaceTriangleIndices[index + 2];
                if (first < 0 || first >= closure.Vertices.Count ||
                    second < 0 || second >= closure.Vertices.Count ||
                    third < 0 || third >= closure.Vertices.Count)
                {
                    throw new InvalidOperationException(
                        "The mountain corner closure has invalid triangle " +
                        "indices.");
                }

                Vector3 normal = Vector3.Cross(
                    closure.Vertices[second] - closure.Vertices[first],
                    closure.Vertices[third] - closure.Vertices[first]);
                if (!IsFinite(normal) || normal.y <= Tolerance)
                {
                    throw new InvalidOperationException(
                        "The mountain corner closure must contain only " +
                        "non-degenerate upward triangles.");
                }

                projectedTriangleArea += normal.y * 0.5f;
            }

            if (Mathf.Abs(
                    projectedTriangleArea - closure.ProjectedArea) >
                Tolerance)
            {
                throw new InvalidOperationException(
                    "The mountain corner closure triangles overlap or " +
                    "leave a ground gap.");
            }
        }

        private static void ValidateCornerGround(
            CityMountainCornerClosureDescriptor closure)
        {
            if (closure.NaturalGroundSlopeDegrees < 0f ||
                closure.NaturalGroundSlopeDegrees >
                CityMountainCornerClosureDescriptor
                    .MaximumNaturalGroundSlopeDegrees + Tolerance)
            {
                throw new InvalidOperationException(
                    "The south-west corner is not one naturally graded " +
                    "ground surface.");
            }
        }

        private static void ValidateStation(
            CityLayout layout,
            CityMountainRidgeDescriptor ridge,
            CityMountainRidgeStation station,
            int stationIndex,
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

            float joinAmount = stationIndex /
                (float)(ridge.Stations.Count - 1);
            Vector3 expectedOutward = ridge.IsSouthWestJoin
                ? Vector3.Lerp(
                    Vector3.left,
                    Vector3.back,
                    joinAmount).normalized
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

            CityMountainRidgeStation westJoin = join.Stations[0];
            CityMountainRidgeStation southJoin =
                join.Stations[join.Stations.Count - 1];
            bool westMatch = false;
            bool southMatch = false;
            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor ridge = plan.Ridges[ridgeIndex];
                if (ReferenceEquals(ridge, join))
                {
                    continue;
                }

                CityMountainRidgeStation first = ridge.Stations[0];
                CityMountainRidgeStation last =
                    ridge.Stations[ridge.Stations.Count - 1];
                if (ridge.Side == CityMountainBoundarySide.West)
                {
                    westMatch |= ProfilesMatch(westJoin, first) ||
                                 ProfilesMatch(westJoin, last);
                }
                else
                {
                    southMatch |= ProfilesMatch(southJoin, first) ||
                                  ProfilesMatch(southJoin, last);
                }
            }

            return westMatch && southMatch;
        }

        private static bool ProfilesMatch(
            CityMountainRidgeStation left,
            CityMountainRidgeStation right)
        {
            return Approximately(left.WorldXZ, right.WorldXZ) &&
                   Mathf.Abs(left.BaseY - right.BaseY) <= Tolerance &&
                   Mathf.Abs(left.PeakY - right.PeakY) <= Tolerance &&
                   Mathf.Abs(left.Depth - right.Depth) <= Tolerance &&
                   Vector3.SqrMagnitude(
                       left.OutwardNormal - right.OutwardNormal) <=
                   Tolerance * Tolerance &&
                   left.Seed == right.Seed;
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

        private static List<Vector3> CreateExpectedBoundarySamples(
            CityLayout layout,
            string areaId,
            Vector2 start,
            Vector2 end,
            bool sampleXAxis)
        {
            CitySurfaceDescriptor owner = default;
            bool foundOwner = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor candidate = layout.Surfaces[index];
                if (candidate.Kind != CitySurfaceKind.OpenGround ||
                    !string.Equals(
                        candidate.AreaId,
                        areaId,
                        StringComparison.Ordinal) ||
                    !ContainsInclusive(candidate.WorldBounds, start) ||
                    !ContainsInclusive(candidate.WorldBounds, end))
                {
                    continue;
                }

                owner = candidate;
                foundOwner = true;
                break;
            }

            if (!foundOwner)
            {
                throw new InvalidOperationException(
                    "A mountain corner edge has no terrain owner.");
            }

            float startCoordinate = sampleXAxis ? start.x : start.y;
            float endCoordinate = sampleXAxis ? end.x : end.y;
            float cellMinimum = sampleXAxis
                ? layout.ElevationPlan.WorldOrigin.x +
                  owner.Cell.x * layout.ElevationPlan.NodeSpacing.x
                : layout.ElevationPlan.WorldOrigin.z +
                  owner.Cell.y * layout.ElevationPlan.NodeSpacing.y;
            float spacing = sampleXAxis
                ? layout.ElevationPlan.NodeSpacing.x
                : layout.ElevationPlan.NodeSpacing.y;
            float halfRoad = layout.ElevationPlan.RoadWidth * 0.5f;
            List<float> coordinates =
                CityTerrainSurfaceWorldBuilder.CreateAxisCoordinates(
                    Mathf.Min(startCoordinate, endCoordinate),
                    Mathf.Max(startCoordinate, endCoordinate),
                    cellMinimum + halfRoad,
                    cellMinimum + spacing - halfRoad,
                    Array.Empty<float>());
            bool reverse = startCoordinate > endCoordinate;
            var result = new List<Vector3>(coordinates.Count);
            for (int index = 0; index < coordinates.Count; index++)
            {
                float coordinate = coordinates[reverse
                    ? coordinates.Count - 1 - index
                    : index];
                var worldXZ = sampleXAxis
                    ? new Vector2(coordinate, start.y)
                    : new Vector2(start.x, coordinate);
                result.Add(new Vector3(
                    worldXZ.x,
                    CityTerrainSurfacePlan.SampleTop(
                        layout,
                        owner,
                        worldXZ),
                    worldXZ.y));
            }

            return result;
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
