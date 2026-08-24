using System;
using System.Collections.Generic;
using UnityEngine;
namespace BarPromenade
{
    public static partial class CityFringeYardPlanner
    {
        public const int ExpectedYardCount = 5;
        // The narrow-trace plan has a formal 638-part ceiling. Replacing its
        // tunnel slivers/light stock with six supported caps and a two-post
        // service frame adds at most seven descriptors. Runtime still batches
        // the fine strokes by 48 m chunk/style/collision.
        public const int MaximumPartCount = 650;
        public const float MinimumTunnelDriveClearWidth = 6f;

        private const float SurfaceLift = 0.035f;
        private const float SurfaceThickness = 0.055f;
        private const float SurfaceSegmentOverlap = 0.06f;
        private const float BeltRunSegmentLength = 17.5f;
        private const float NarrowSurfaceRunSegmentLength = 8f;
        private const float TunnelRunSegmentLength = 3f;
        private const float TunnelReturnInnerClearance = 0.24f;
        private const float TunnelReturnEmbed = 0.08f;
        private const float LongEdgeMargin = 1.2f;
        private const float MountainTrackInset = 6.1f;
        private const float MountainDrainInset = 3.15f;
        private const float MountainWallInset = 1.15f;
        private const float RetainingOpeningMargin = 0.15f;
        private const float MinimumRetainingFragmentLength = 0.75f;
        public static CityFringeYardPlan Create(
            CityLayout layout,
            CityMountainBoundaryPlan mountains)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }
            if (mountains == null)
            {
                throw new ArgumentNullException(nameof(mountains));
            }
            if (!string.Equals(
                    layout.BlueprintId,
                    CityBlueprintCatalog.DefaultBlueprintId,
                    StringComparison.Ordinal))
            {
                return CityFringeYardPlan.Empty;
            }
            if (!mountains.IsEnabled ||
                !mountains.HasTunnel ||
                mountains.Tunnel.HasPhysicalGate ||
                mountains.Tunnel.TravelAvailable)
            {
                throw new InvalidOperationException(
                    "The default fringe yards require the validated open, " +
                    "unavailable mountain tunnel plan.");
            }
            CityTunnelForecourtDescriptor forecourt =
                CreateTunnelForecourt(layout, mountains.Tunnel);
            var yards = new List<CityFringeYardDescriptor>(
                ExpectedYardCount)
            {
                CreateMountainYard(
                    layout,
                    CityMountainBoundaryDefinition.WestNorthAreaId,
                    CityFringeYardKind.WestStoneTerraces,
                    default,
                    false),
                CreateMountainYard(
                    layout,
                    CityMountainBoundaryDefinition.WestSouthAreaId,
                    CityFringeYardKind.WestIndustrialBelt,
                    default,
                    false),
                CreateMountainYard(
                    layout,
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    CityFringeYardKind.SouthTunnelForecourt,
                    forecourt,
                    true),
                CreateMountainYard(
                    layout,
                    CityMountainBoundaryDefinition.SouthEastAreaId,
                    CityFringeYardKind.SouthFloodWorks,
                    default,
                    false),
                CreateEastUtilityYard(layout)
            };
            CityFringeYardPlan plan = CityFringeYardLandmarkPlanner.CreatePlan(
                layout,
                yards,
                forecourt);
            CityFringeYardValidator.ValidateOrThrow(layout, mountains, plan);
            return plan;
        }
        private static CityTunnelForecourtDescriptor CreateTunnelForecourt(
            CityLayout layout,
            CityMountainTunnelDescriptor tunnel)
        {
            CityOpenAreaAccessDescriptor access = GetAccess(
                layout,
                tunnel.TargetAccessId,
                tunnel.AreaId);
            Vector3 axis = FlattenCardinal(tunnel.Axis);
            float approachWidth = Mathf.Min(
                access.Width - 0.8f,
                tunnel.OpeningWidth - 1.1f);
            float driveWidth = Mathf.Min(
                approachWidth - 0.6f,
                tunnel.OpeningWidth - 1.8f);
            driveWidth = Mathf.Max(
                MinimumTunnelDriveClearWidth,
                driveWidth);
            Rect approach = Union(
                tunnel.ApproachBounds,
                CreateCorridorBounds(
                    access.Center,
                    tunnel.PortalGroundCenter,
                    approachWidth));
            Rect driveClear = CreateCorridorBounds(
                access.Center,
                tunnel.PortalGroundCenter,
                driveWidth);
            return new CityTunnelForecourtDescriptor(
                "fringe-south-west-tunnel-forecourt",
                tunnel.AreaId,
                access.Id,
                tunnel.StableId,
                access.Center,
                tunnel.PortalGroundCenter,
                axis,
                approach,
                driveClear,
                approachWidth,
                driveWidth,
                tunnel.HasPhysicalGate);
        }
        private static CityFringeYardDescriptor CreateMountainYard(
            CityLayout layout,
            string areaId,
            CityFringeYardKind kind,
            CityTunnelForecourtDescriptor forecourt,
            bool ownsTunnel)
        {
            List<CitySurfaceDescriptor> surfaces =
                GetAreaSurfaces(layout, areaId);
            Rect bounds = CalculateBounds(surfaces);
            CityOpenAreaAccessDescriptor access =
                GetAccess(layout, null, areaId);
            Vector3 outward = FlattenCardinal(access.OutwardNormal);
            Vector3 tangent = ResolveLongAxis(outward);
            float trackInset = kind == CityFringeYardKind.WestStoneTerraces
                ? 7.2f
                : MountainTrackInset;
            Vector3 trackLine = PointAtOuterInset(
                bounds,
                access.Center,
                outward,
                trackInset);
            Vector3 drainLine = PointAtOuterInset(
                bounds,
                access.Center,
                outward,
                MountainDrainInset);
            Vector3 toeApproach = PointAtOuterInset(
                bounds,
                access.Center,
                outward,
                CityGroundTraversalPlanner.MaximumAgentRadius);
            Rect traversal = ownsTunnel
                ? forecourt.DriveClearBounds
                : CreateCorridorBounds(
                    access.Center + outward * 0.25f,
                    toeApproach,
                    Mathf.Min(6f, access.Width - 0.8f));
            var parts = new List<CityFringeYardPartDescriptor>(72);

            Rect? runGap = ownsTunnel
                ? forecourt.DriveClearBounds
                : (Rect?)null;
            CityFringeYardStyle wallStyle = ResolveWallStyle(kind);
            float endTrim = kind == CityFringeYardKind.SouthFloodWorks
                ? 7.5f
                : LongEdgeMargin;

            if (kind != CityFringeYardKind.SouthFloodWorks)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    AddLongSurfaceRun(
                        layout,
                        surfaces,
                        bounds,
                        areaId,
                        side < 0
                            ? "service-track-roadward"
                            : "service-track-toeward",
                        CityFringeYardPartKind.ServiceTrack,
                        CityFringeYardStyle.ServiceGround,
                        tangent,
                        trackLine + outward * (side * 1.15f),
                        kind == CityFringeYardKind.SouthTunnelForecourt
                            ? MaximumTunnelTraceWidth
                            : ShoulderWidth,
                        SurfaceThickness,
                        LongEdgeMargin,
                        endTrim,
                        runGap,
                        parts,
                        NarrowSurfaceRunSegmentLength,
                        kind == CityFringeYardKind.SouthTunnelForecourt
                            ? ForefieldSurfaceLift
                            : SurfaceLift);
                }
            }
            if (!ownsTunnel)
            {
                bool isFloodWorks =
                    kind == CityFringeYardKind.SouthFloodWorks;
                AddGradedStrip(
                    layout,
                    surfaces,
                    areaId,
                    "access-apron",
                    CityFringeYardPartKind.AccessApron,
                    CityFringeYardStyle.ServiceGround,
                    access.Center + outward * 0.45f,
                    toeApproach,
                    isFloodWorks
                        ? ShoulderWidth
                        : Mathf.Min(5.4f, access.Width - 1f),
                    isFloodWorks
                        ? FloodTraceThickness
                        : SurfaceThickness,
                    isFloodWorks ? 2f : 4f,
                    null,
                    parts,
                    isFloodWorks
                        ? ForefieldSurfaceLift
                        : SurfaceLift);
            }

            AddForefield(
                layout,
                surfaces,
                bounds,
                areaId,
                kind,
                tangent,
                outward,
                access.Center,
                kind == CityFringeYardKind.SouthFloodWorks
                    ? drainLine
                    : trackLine,
                traversal,
                parts);

            AddLongSurfaceRun(
                layout,
                surfaces,
                bounds,
                areaId,
                "drain",
                CityFringeYardPartKind.DrainChannel,
                CityFringeYardStyle.Drainage,
                tangent,
                drainLine,
                0.72f,
                0.045f,
                LongEdgeMargin,
                endTrim,
                null,
                parts);

            Vector3 wallLine = PointAtOuterInset(
                bounds,
                access.Center,
                outward,
                MountainWallInset);
            AddRetainingRun(
                layout,
                surfaces,
                bounds,
                areaId,
                kind,
                wallStyle,
                tangent,
                wallLine,
                LongEdgeMargin,
                endTrim,
                runGap,
                traversal,
                parts);

            AddPoleAndCableLine(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                PointAtOuterInset(
                    bounds,
                    access.Center,
                    outward,
                    trackInset + 3.1f),
                traversal,
                kind == CityFringeYardKind.WestStoneTerraces
                    ? 40f
                    : 34f,
                parts);

            if (kind == CityFringeYardKind.WestIndustrialBelt)
            {
                AddRepairStockPocket(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    tangent,
                    outward,
                    trackLine,
                    traversal,
                    parts);
            }
            AddRockfallPockets(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                outward,
                access.Center,
                traversal,
                kind,
                parts);

            if (kind == CityFringeYardKind.WestStoneTerraces)
            {
                AddStoneCulvert(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    tangent,
                    outward,
                    traversal,
                    parts);
            }
            else if (kind == CityFringeYardKind.SouthFloodWorks)
            {
                AddFloodGabions(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    tangent,
                    outward,
                    traversal,
                    parts);
            }

            if (ownsTunnel)
            {
                AddTunnelForecourt(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    tangent,
                    forecourt,
                    parts);
            }

            return new CityFringeYardDescriptor(
                $"fringe-{areaId}",
                areaId,
                kind,
                bounds,
                access,
                traversal,
                parts);
        }
        private static CityFringeYardDescriptor CreateEastUtilityYard(
            CityLayout layout)
        {
            const string areaId = "yard-east";
            List<CitySurfaceDescriptor> surfaces =
                GetAreaSurfaces(layout, areaId);
            Rect bounds = CalculateBounds(surfaces);
            CityOpenAreaAccessDescriptor access =
                GetAccess(layout, null, areaId);
            Vector3 outward = FlattenCardinal(access.OutwardNormal);
            Vector3 tangent = ResolveLongAxis(outward);
            Vector3 roadLine = access.Center + outward * 18f;
            Rect traversal = CreateCorridorBounds(
                access.Center + outward * 0.25f,
                roadLine,
                Mathf.Min(6f, access.Width - 0.8f));
            var parts = new List<CityFringeYardPartDescriptor>(72);

            AddLongSurfaceRun(
                layout,
                surfaces,
                bounds,
                areaId,
                "east-service-road",
                CityFringeYardPartKind.ServiceTrack,
                CityFringeYardStyle.ServiceGround,
                tangent,
                roadLine,
                4.8f,
                SurfaceThickness,
                2f,
                2f,
                null,
                parts);
            AddGradedStrip(
                layout,
                surfaces,
                areaId,
                "east-access-apron",
                CityFringeYardPartKind.AccessApron,
                CityFringeYardStyle.ServiceGround,
                access.Center + outward * 0.45f,
                roadLine,
                Mathf.Min(5.5f, access.Width - 1f),
                SurfaceThickness,
                4f,
                null,
                parts);
            AddLongSurfaceRun(
                layout,
                surfaces,
                bounds,
                areaId,
                "east-drain",
                CityFringeYardPartKind.DrainChannel,
                CityFringeYardStyle.Drainage,
                tangent,
                access.Center + outward * 10.5f,
                0.8f,
                0.045f,
                2f,
                2f,
                null,
                parts);
            AddPoleAndCableLine(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                access.Center + outward * 27f,
                traversal,
                29f,
                parts);
            AddEastUtilitySheds(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                outward,
                access.Center,
                traversal,
                parts);
            AddEastSpoilBerms(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                outward,
                access.Center,
                traversal,
                parts);

            return new CityFringeYardDescriptor(
                "fringe-yard-east",
                areaId,
                CityFringeYardKind.EastUtilityEdge,
                bounds,
                access,
                traversal,
                parts);
        }
        private static void AddRetainingRun(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            CityFringeYardKind yardKind,
            CityFringeYardStyle style,
            Vector3 axis,
            Vector3 line,
            float startMargin,
            float endMargin,
            Rect? gap,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(
                bounds,
                axis,
                startMargin,
                endMargin,
                out float start,
                out float end);
            float length = end - start;
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / BeltRunSegmentLength));
            float pitch = length / segmentCount;
            for (int index = 0; index < segmentCount; index++)
            {
                float segmentLength = Mathf.Max(1f, pitch - 0.7f);
                float segmentMiddle = start + pitch * (index + 0.5f);
                float segmentMinimum = segmentMiddle - segmentLength * 0.5f;
                float segmentMaximum = segmentMiddle + segmentLength * 0.5f;
                Vector3 center = SetLongCoordinate(
                    line,
                    axis,
                    segmentMiddle);
                float variation = HashUnit(
                    layout.Seed,
                    $"{areaId}-retaining-{index:00}");
                float height;
                switch (yardKind)
                {
                    case CityFringeYardKind.WestStoneTerraces:
                        height = Mathf.Lerp(1.35f, 2.15f, variation);
                        break;
                    case CityFringeYardKind.SouthFloodWorks:
                        height = Mathf.Lerp(0.9f, 1.45f, variation);
                        break;
                    default:
                        height = Mathf.Lerp(1.55f, 2.65f, variation);
                        break;
                }

                Quaternion rotation = Quaternion.LookRotation(
                    axis,
                    Vector3.up);
                Vector3 size = new Vector3(0.76f, height, segmentLength);
                Rect footprint = CalculateFootprint(center, rotation, size);
                string stableId = $"{areaId}-retaining-{index:00}";
                if (TryGetRetainingCut(
                        footprint,
                        axis,
                        gap,
                        reserved,
                        out float cutMinimum,
                        out float cutMaximum))
                {
                    AddRetainingFragment(
                        layout,
                        surfaces,
                        areaId,
                        $"{stableId}-a",
                        yardKind,
                        style,
                        axis,
                        line,
                        segmentMinimum,
                        Mathf.Min(segmentMaximum, cutMinimum),
                        height,
                        rotation,
                        reserved,
                        parts);
                    AddRetainingFragment(
                        layout,
                        surfaces,
                        areaId,
                        $"{stableId}-b",
                        yardKind,
                        style,
                        axis,
                        line,
                        Mathf.Max(segmentMinimum, cutMaximum),
                        segmentMaximum,
                        height,
                        rotation,
                        reserved,
                        parts);
                    continue;
                }

                AddRetainingFragment(
                    layout,
                    surfaces,
                    areaId,
                    stableId,
                    yardKind,
                    style,
                    axis,
                    line,
                    segmentMinimum,
                    segmentMaximum,
                    height,
                    rotation,
                    reserved,
                    parts);

                if (index % 3 == 1)
                {
                    Vector3 buttressCenter = center -
                        (ResolveShortAxis(axis) * 0.55f);
                    AddGroundedPart(
                        layout,
                        surfaces,
                        areaId,
                        $"{areaId}-buttress-{index:00}",
                        CityFringeYardPartKind.Buttress,
                        style,
                        buttressCenter,
                        rotation,
                        new Vector3(1.1f, height * 0.72f, 0.72f),
                        true,
                        0.15f,
                        reserved,
                        parts);
                }
            }
        }

        private static void AddPoleAndCableLine(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 line,
            Rect reserved,
            float spacing,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(
                bounds,
                axis,
                8f,
                8f,
                out float start,
                out float end);
            int count = ResolvePoleCount(areaId, end - start, spacing);
            float pitch = (end - start) / (count - 1);
            var tops = new List<Vector3>(count);
            for (int index = 0; index < count; index++)
            {
                float coordinate = start + pitch * index;
                Vector3 center = SetLongCoordinate(
                    line,
                    axis,
                    coordinate);
                center = MovePoleOutsideReserved(
                    center,
                    axis,
                    start,
                    end,
                    reserved);
                float ground = SampleAreaTop(
                    layout,
                    surfaces,
                    ToXZ(center));
                var poleSize = new Vector3(0.22f, 4.45f, 0.22f);
                Rect footprint = Rect.MinMaxRect(
                    center.x - 0.2f,
                    center.z - 0.2f,
                    center.x + 0.2f,
                    center.z + 0.2f);
                if (footprint.Overlaps(reserved))
                {
                    throw new InvalidOperationException(
                        $"Unable to keep utility pole '{areaId}-{index:00}' " +
                        "outside its reserved route.");
                }

                parts.Add(new CityFringeYardPartDescriptor(
                    $"{areaId}-utility-pole-{index:00}",
                    areaId,
                    CityFringeYardPartKind.UtilityPole,
                    CityFringeYardStyle.Iron,
                    new Vector3(center.x, ground + poleSize.y * 0.5f, center.z),
                    Quaternion.identity,
                    poleSize,
                    true));
                Quaternion armRotation = Quaternion.LookRotation(
                    axis,
                    Vector3.up);
                parts.Add(new CityFringeYardPartDescriptor(
                    $"{areaId}-utility-arm-{index:00}",
                    areaId,
                    CityFringeYardPartKind.UtilityCrossarm,
                    CityFringeYardStyle.Iron,
                    new Vector3(center.x, ground + 4.22f, center.z),
                    armRotation,
                    new Vector3(1.45f, 0.12f, 0.14f),
                    false));
                tops.Add(new Vector3(center.x, ground + 4.08f, center.z));
            }

            for (int index = 1; index < tops.Count; index++)
            {
                AddCableSpan(
                    areaId,
                    index - 1,
                    tops[index - 1],
                    tops[index],
                    parts);
            }
        }

        private static void AddCableSpan(
            string areaId,
            int spanIndex,
            Vector3 start,
            Vector3 end,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 middle = (start + end) * 0.5f + Vector3.down * 0.34f;
            AddSuspendedSegment(
                areaId,
                $"{areaId}-cable-{spanIndex:00}-a",
                start,
                middle,
                parts);
            AddSuspendedSegment(
                areaId,
                $"{areaId}-cable-{spanIndex:00}-b",
                middle,
                end,
                parts);
        }

        private static void AddSuspendedSegment(
            string areaId,
            string stableId,
            Vector3 start,
            Vector3 end,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 delta = end - start;
            parts.Add(new CityFringeYardPartDescriptor(
                stableId,
                areaId,
                CityFringeYardPartKind.UtilityCable,
                CityFringeYardStyle.Iron,
                (start + end) * 0.5f,
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                new Vector3(0.055f, 0.055f, delta.magnitude),
                false));
        }

        private static void AddRepairStockPocket(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Vector3 trackLine,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 5f, 5f, out float start, out float end);
            Vector3 pad = SetLongCoordinate(
                trackLine - outward * 3.4f,
                axis,
                Mathf.Lerp(start, end, 0.62f));
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            for (int index = 0; index < 3; index++)
            {
                Vector3 sleeper = pad +
                    outward * (0.72f * (index - 1)) +
                    axis * 0.45f;
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-repair-stock-{index:00}",
                    CityFringeYardPartKind.RepairStack,
                    index == 2
                        ? CityFringeYardStyle.Iron
                        : CityFringeYardStyle.Concrete,
                    sleeper,
                    rotation,
                    new Vector3(0.42f, 0.30f + index * 0.08f, 3.1f),
                    true,
                    0.04f,
                    reserved,
                    parts);
            }
        }

        private static void AddRockfallPockets(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Vector3 anchor,
            Rect reserved,
            CityFringeYardKind kind,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 10f, 10f, out float start, out float end);
            int count = kind == CityFringeYardKind.SouthFloodWorks ? 3 : 2;
            for (int index = 0; index < count; index++)
            {
                float amount = (index + 1f) / (count + 1f);
                Vector3 center = PointAtOuterInset(
                    bounds,
                    anchor,
                    outward,
                    2.35f + index * 0.18f);
                center = SetLongCoordinate(
                    center,
                    axis,
                    Mathf.Lerp(start, end, amount));
                float unit = HashUnit(
                    layout.Seed,
                    $"{areaId}-rockfall-{index:00}");
                Quaternion rotation = Quaternion.Euler(
                    0f,
                    Mathf.Lerp(-18f, 18f, unit),
                    0f);
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-rockfall-{index:00}",
                    CityFringeYardPartKind.RockfallMass,
                    CityFringeYardStyle.Rock,
                    center,
                    rotation,
                    new Vector3(
                        Mathf.Lerp(1.0f, 1.7f, unit),
                        Mathf.Lerp(0.65f, 1.15f, unit),
                        Mathf.Lerp(1.2f, 2.2f, 1f - unit)),
                    true,
                    0.10f,
                    reserved,
                    parts);
            }
        }

        private static void AddStoneCulvert(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 8f, 8f, out float start, out float end);
            Vector3 center = PointAtOuterInset(
                bounds,
                Vector3.zero,
                outward,
                2.1f);
            center = SetLongCoordinate(
                center,
                axis,
                Mathf.Lerp(start, end, 0.72f));
            Vector3 shortAxis = ResolveShortAxis(axis);
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            if (CalculateFootprint(
                    center,
                    rotation,
                    new Vector3(2.1f, 1.3f, 1.2f))
                .Overlaps(reserved))
            {
                return;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-culvert-pier-{side}",
                    CityFringeYardPartKind.Buttress,
                    CityFringeYardStyle.OldMasonry,
                    center + shortAxis * (0.78f * side),
                    rotation,
                    new Vector3(0.55f, 1.05f, 1.15f),
                    true,
                    0.12f,
                    reserved,
                    parts);
            }

            float ground = SampleAreaTop(layout, surfaces, ToXZ(center));
            parts.Add(new CityFringeYardPartDescriptor(
                $"{areaId}-culvert-lintel",
                areaId,
                CityFringeYardPartKind.Buttress,
                CityFringeYardStyle.OldMasonry,
                new Vector3(center.x, ground + 1.02f, center.z),
                rotation,
                new Vector3(2.1f, 0.42f, 1.2f),
                true));
        }

        private static void AddFloodGabions(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 10f, 14f, out float start, out float end);
            for (int index = 0; index < 3; index++)
            {
                Vector3 center = PointAtOuterInset(
                    bounds,
                    Vector3.zero,
                    outward,
                    4.65f);
                center = SetLongCoordinate(
                    center,
                    axis,
                    Mathf.Lerp(start, end, 0.58f + index * 0.11f));
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-flood-gabion-{index:00}",
                    CityFringeYardPartKind.Gabion,
                    CityFringeYardStyle.Gabion,
                    center,
                    Quaternion.LookRotation(axis, Vector3.up),
                    new Vector3(1.55f, 0.82f + index * 0.12f, 4.2f),
                    true,
                    0.10f,
                    reserved,
                    parts);
            }
        }

        private static void AddTunnelForecourt(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            CityTunnelForecourtDescriptor forecourt,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 axis = forecourt.Axis;
            Vector3 start = forecourt.StreetAnchor + axis * 0.65f;
            Vector3 end = forecourt.PortalAnchor - axis * 0.72f;
            AddGradedStrip(
                layout,
                surfaces,
                areaId,
                "tunnel-approach",
                CityFringeYardPartKind.AccessApron,
                CityFringeYardStyle.ServiceGround,
                start,
                end,
                MaximumTunnelTraceWidth,
                SurfaceThickness,
                TunnelRunSegmentLength,
                null,
                parts,
                ForefieldSurfaceLift,
                0f);

            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                AddGradedStrip(
                    layout,
                    surfaces,
                    areaId,
                    $"tunnel-wheel-rut-{side}",
                    CityFringeYardPartKind.WheelRut,
                    CityFringeYardStyle.Drainage,
                    start + right * (1.1f * side),
                    end + right * (1.1f * side),
                    MaximumTunnelTraceWidth,
                    0.028f,
                    TunnelRunSegmentLength,
                    null,
                    parts,
                    ForefieldSurfaceLift,
                    0f);
            }

            Vector3 cheekStart = Vector3.Lerp(start, end, 0.48f);
            Vector3 cheekEnd = end - axis * 0.25f;
            float cheekLength = Vector3.Dot(cheekEnd - cheekStart, axis);
            const int returnCount = 3;
            float returnPitch = cheekLength / returnCount;
            float[] returnWidths = { 1.10f, 1.20f, 1.35f };
            float[] returnHeights = { 0.80f, 1.55f, 2.35f };
            for (int side = -1; side <= 1; side += 2)
            {
                for (int index = 0; index < returnCount; index++)
                {
                    float width = returnWidths[index];
                    float length = returnPitch;
                    float sideOffset =
                        forecourt.DriveClearWidth * 0.5f +
                        TunnelReturnInnerClearance +
                        width * 0.5f;
                    Vector3 center = cheekStart +
                        axis * (returnPitch * (index + 0.5f)) +
                        right * (sideOffset * side);
                    AddTunnelReturnSection(
                        layout,
                        surfaces,
                        areaId,
                        side,
                        index,
                        center,
                        axis,
                        width,
                        returnHeights[index],
                        length,
                        forecourt.DriveClearBounds,
                        parts);
                }
            }

            Vector3 drainCenter = PointAtOuterInset(
                bounds,
                forecourt.StreetAnchor,
                axis,
                MountainDrainInset);
            for (int index = -2; index <= 2; index++)
            {
                Vector3 bar = drainCenter + tangent * (index * 1.12f);
                float ground = SampleAreaTop(layout, surfaces, ToXZ(bar));
                parts.Add(new CityFringeYardPartDescriptor(
                    $"{areaId}-drain-cover-{index + 2:00}",
                    areaId,
                    CityFringeYardPartKind.DrainCover,
                    CityFringeYardStyle.Iron,
                    new Vector3(bar.x, ground + 0.025f, bar.z),
                    Quaternion.LookRotation(axis, Vector3.up),
                    new Vector3(0.10f, 0.07f, 1.18f),
                    false));
            }
        }

        private static void AddTunnelReturnSection(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            string areaId,
            int side,
            int index,
            Vector3 xzCenter,
            Vector3 axis,
            float width,
            float height,
            float length,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            Vector3 right =
                (rotation * Vector3.right) * (width * 0.5f);
            Vector3 forward =
                (rotation * Vector3.forward) * (length * 0.5f);
            float minimumGround = float.PositiveInfinity;
            for (int rightSign = -1; rightSign <= 1; rightSign += 2)
            {
                for (int forwardSign = -1;
                     forwardSign <= 1;
                     forwardSign += 2)
                {
                    Vector3 corner = xzCenter +
                        right * rightSign +
                        forward * forwardSign;
                    minimumGround = Mathf.Min(
                        minimumGround,
                        SampleAreaTop(layout, surfaces, ToXZ(corner)));
                }
            }

            var section = new CityFringeYardPartDescriptor(
                $"{areaId}-tunnel-return-{side}-{index:00}",
                areaId,
                CityFringeYardPartKind.TunnelCheek,
                CityFringeYardStyle.Concrete,
                new Vector3(
                    xzCenter.x,
                    minimumGround - TunnelReturnEmbed + height * 0.5f,
                    xzCenter.z),
                rotation,
                new Vector3(width, height, length),
                true);
            if (section.Footprint.Overlaps(reserved))
            {
                throw new InvalidOperationException(
                    $"Tunnel return '{section.StableId}' narrows the " +
                    "declared drive-clear corridor.");
            }

            parts.Add(section);
            const float capWidth = 0.14f;
            const float capHeight = 0.12f;
            Vector3 inside = -(rotation * Vector3.right) * side;
            Vector3 capCenter = section.Center +
                Vector3.up * (height * 0.5f + capHeight * 0.5f) +
                inside * (width * 0.5f - capWidth * 0.5f);
            parts.Add(new CityFringeYardPartDescriptor(
                $"{areaId}-tunnel-return-cap-{side}-{index:00}",
                areaId,
                CityFringeYardPartKind.RepairFrame,
                CityFringeYardStyle.Iron,
                capCenter,
                rotation,
                new Vector3(
                    capWidth,
                    capHeight,
                    length),
                false));
        }

        private static void AddEastUtilitySheds(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Vector3 anchor,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 12f, 12f, out float start, out float end);
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            for (int index = 0; index < 3; index++)
            {
                Vector3 center = anchor + outward * (35f + index * 2.2f);
                center = SetLongCoordinate(
                    center,
                    axis,
                    Mathf.Lerp(start, end, 0.18f + index * 0.32f));
                var size = new Vector3(5.2f, 3.2f, 7.2f);
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-utility-shed-{index:00}",
                    CityFringeYardPartKind.UtilityShed,
                    CityFringeYardStyle.UtilityPaint,
                    center,
                    rotation,
                    size,
                    true,
                    0.16f,
                    reserved,
                    parts);

                Vector3 doorCenter = center - outward * (size.x * 0.5f + 0.035f);
                float ground = SampleAreaTop(
                    layout,
                    surfaces,
                    ToXZ(doorCenter));
                parts.Add(new CityFringeYardPartDescriptor(
                    $"{areaId}-utility-door-{index:00}",
                    areaId,
                    CityFringeYardPartKind.UtilityDoor,
                    CityFringeYardStyle.Iron,
                    new Vector3(doorCenter.x, ground + 1.1f, doorCenter.z),
                    Quaternion.LookRotation(axis, Vector3.up),
                    new Vector3(0.10f, 2.2f, 2.0f),
                    false));
            }
        }

        private static void AddEastSpoilBerms(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 axis,
            Vector3 outward,
            Vector3 anchor,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, axis, 6f, 6f, out float start, out float end);
            const int count = 5;
            float pitch = (end - start) / count;
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            for (int index = 0; index < count; index++)
            {
                if (index == 2)
                {
                    continue;
                }

                Vector3 center = anchor + outward * 50f;
                center = SetLongCoordinate(
                    center,
                    axis,
                    start + pitch * (index + 0.5f));
                float unit = HashUnit(
                    layout.Seed,
                    $"{areaId}-spoil-berm-{index:00}");
                AddGroundedPart(
                    layout,
                    surfaces,
                    areaId,
                    $"{areaId}-spoil-berm-{index:00}",
                    CityFringeYardPartKind.EarthBerm,
                    CityFringeYardStyle.Rock,
                    center,
                    rotation,
                    new Vector3(
                        Mathf.Lerp(5.5f, 7f, unit),
                        Mathf.Lerp(0.65f, 1.15f, unit),
                        Mathf.Max(5f, pitch - 5f)),
                    true,
                    0.20f,
                    reserved,
                    parts);
            }
        }

        private static void AddLongSurfaceRun(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            string stableStem,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 axis,
            Vector3 line,
            float width,
            float thickness,
            float startMargin,
            float endMargin,
            Rect? gap,
            ICollection<CityFringeYardPartDescriptor> parts,
            float maximumSegmentLength = BeltRunSegmentLength,
            float surfaceLift = SurfaceLift,
            float segmentOverlap = SurfaceSegmentOverlap)
        {
            GetLongRange(
                bounds,
                axis,
                startMargin,
                endMargin,
                out float start,
                out float end);
            Vector3 runStart = SetLongCoordinate(line, axis, start);
            Vector3 runEnd = SetLongCoordinate(line, axis, end);
            AddGradedStrip(
                layout,
                surfaces,
                areaId,
                stableStem,
                kind,
                style,
                runStart,
                runEnd,
                width,
                thickness,
                maximumSegmentLength,
                gap,
                parts,
                surfaceLift,
                segmentOverlap);
        }

        private static void AddGradedStrip(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            string areaId,
            string stableStem,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 start,
            Vector3 end,
            float width,
            float thickness,
            float maximumSegmentLength,
            Rect? gap,
            ICollection<CityFringeYardPartDescriptor> parts,
            float surfaceLift = SurfaceLift,
            float segmentOverlap = SurfaceSegmentOverlap)
        {
            Vector3 flatDelta = new Vector3(
                end.x - start.x,
                0f,
                end.z - start.z);
            float runLength = flatDelta.magnitude;
            if (runLength <= 0.1f)
            {
                return;
            }

            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(runLength / maximumSegmentLength));
            for (int index = 0; index < segmentCount; index++)
            {
                float startAmount = index / (float)segmentCount;
                float endAmount = (index + 1f) / segmentCount;
                Vector3 topStart = Vector3.Lerp(start, end, startAmount);
                Vector3 topEnd = Vector3.Lerp(start, end, endAmount);
                topStart.y = SampleAreaTop(
                    layout,
                    surfaces,
                    ToXZ(topStart)) + surfaceLift;
                topEnd.y = SampleAreaTop(
                    layout,
                    surfaces,
                    ToXZ(topEnd)) + surfaceLift;
                Vector3 slope = topEnd - topStart;
                Quaternion rotation = Quaternion.LookRotation(
                    slope.normalized,
                    Vector3.up);
                Vector3 center = (topStart + topEnd) * 0.5f -
                    (rotation * Vector3.up) * (thickness * 0.5f);
                Vector3 size = new Vector3(
                    width,
                    thickness,
                    slope.magnitude + segmentOverlap);
                if (gap.HasValue &&
                    CalculateFootprint(center, rotation, size)
                        .Overlaps(gap.Value))
                {
                    continue;
                }

                parts.Add(new CityFringeYardPartDescriptor(
                    $"{areaId}-{stableStem}-{index:00}",
                    areaId,
                    kind,
                    style,
                    center,
                    rotation,
                    size,
                    false));
            }
        }

        private static bool AddGroundedPart(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            string areaId,
            string stableId,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 xzCenter,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement,
            float embed,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            float ground = SampleAreaTop(
                layout,
                surfaces,
                ToXZ(xzCenter));
            Vector3 center = new Vector3(
                xzCenter.x,
                ground + size.y * 0.5f - embed,
                xzCenter.z);
            var part = new CityFringeYardPartDescriptor(
                stableId,
                areaId,
                kind,
                style,
                center,
                rotation,
                size,
                blocksMovement);
            if (blocksMovement && part.Footprint.Overlaps(reserved))
            {
                return false;
            }

            parts.Add(part);
            return true;
        }

        private static CityFringeYardStyle ResolveWallStyle(
            CityFringeYardKind kind)
        {
            switch (kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    return CityFringeYardStyle.OldMasonry;
                case CityFringeYardKind.SouthFloodWorks:
                    return CityFringeYardStyle.Gabion;
                default:
                    return CityFringeYardStyle.Concrete;
            }
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
                        StringComparison.Ordinal))
                {
                    result.Add(surface);
                }
            }

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Missing fringe Yard surfaces for '{areaId}'.");
            }

            return result;
        }

        private static CityOpenAreaAccessDescriptor GetAccess(
            CityLayout layout,
            string accessId,
            string areaId)
        {
            bool found = false;
            CityOpenAreaAccessDescriptor result = default;
            for (int index = 0; index < layout.OpenAreaAccesses.Count; index++)
            {
                CityOpenAreaAccessDescriptor candidate =
                    layout.OpenAreaAccesses[index];
                bool idMatches = string.IsNullOrEmpty(accessId) ||
                                 string.Equals(
                                     candidate.Id,
                                     accessId,
                                     StringComparison.Ordinal);
                if (!idMatches ||
                    !string.Equals(
                        candidate.AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (found)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{areaId}' has more than one access.");
                }

                result = candidate;
                found = true;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"Missing fringe Yard access for '{areaId}'.");
            }

            return result;
        }

        private static Rect CalculateBounds(
            IReadOnlyList<CitySurfaceDescriptor> surfaces)
        {
            Rect result = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                result = Union(result, surfaces[index].WorldBounds);
            }

            return result;
        }

        private static float SampleAreaTop(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Vector2 worldXZ)
        {
            const float tolerance = 0.002f;
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (worldXZ.x >= bounds.xMin - tolerance &&
                    worldXZ.x <= bounds.xMax + tolerance &&
                    worldXZ.y >= bounds.yMin - tolerance &&
                    worldXZ.y <= bounds.yMax + tolerance)
                {
                    return CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        worldXZ);
                }
            }

            throw new InvalidOperationException(
                $"Fringe sample {worldXZ} lies outside its Yard surfaces.");
        }

        private static Vector3 PointAtOuterInset(
            Rect bounds,
            Vector3 anchor,
            Vector3 outward,
            float inset)
        {
            Vector3 result = anchor;
            if (outward.x > 0.5f)
            {
                result.x = bounds.xMax - inset;
            }
            else if (outward.x < -0.5f)
            {
                result.x = bounds.xMin + inset;
            }
            else if (outward.z > 0.5f)
            {
                result.z = bounds.yMax - inset;
            }
            else
            {
                result.z = bounds.yMin + inset;
            }

            return result;
        }

        private static Vector3 ResolveLongAxis(Vector3 outward)
        {
            return Mathf.Abs(outward.x) > 0.5f
                ? Vector3.forward
                : Vector3.right;
        }

        private static Vector3 ResolveShortAxis(Vector3 longAxis)
        {
            return Mathf.Abs(longAxis.x) > 0.5f
                ? Vector3.forward
                : Vector3.right;
        }

        private static void GetLongRange(
            Rect bounds,
            Vector3 axis,
            float startMargin,
            float endMargin,
            out float start,
            out float end)
        {
            if (Mathf.Abs(axis.x) > 0.5f)
            {
                start = bounds.xMin + startMargin;
                end = bounds.xMax - endMargin;
            }
            else
            {
                start = bounds.yMin + startMargin;
                end = bounds.yMax - endMargin;
            }
        }

        private static Vector3 SetLongCoordinate(
            Vector3 value,
            Vector3 axis,
            float coordinate)
        {
            if (Mathf.Abs(axis.x) > 0.5f)
            {
                value.x = coordinate;
            }
            else
            {
                value.z = coordinate;
            }

            return value;
        }

        private static Vector3 FlattenCardinal(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude <= 0.5f)
            {
                throw new InvalidOperationException(
                    "A fringe Yard requires a horizontal access axis.");
            }

            return flat.normalized;
        }

        private static Rect CreateCorridorBounds(
            Vector3 start,
            Vector3 end,
            float width)
        {
            float half = width * 0.5f;
            if (Mathf.Abs(end.x - start.x) >=
                Mathf.Abs(end.z - start.z))
            {
                return Rect.MinMaxRect(
                    Mathf.Min(start.x, end.x),
                    (start.z + end.z) * 0.5f - half,
                    Mathf.Max(start.x, end.x),
                    (start.z + end.z) * 0.5f + half);
            }

            return Rect.MinMaxRect(
                (start.x + end.x) * 0.5f - half,
                Mathf.Min(start.z, end.z),
                (start.x + end.x) * 0.5f + half,
                Mathf.Max(start.z, end.z));
        }

        private static Rect CalculateFootprint(
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            return new CityFringeYardPartDescriptor(
                string.Empty,
                string.Empty,
                default,
                default,
                center,
                rotation,
                size,
                false).Footprint;
        }

        private static Rect Union(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Min(left.xMin, right.xMin),
                Mathf.Min(left.yMin, right.yMin),
                Mathf.Max(left.xMax, right.xMax),
                Mathf.Max(left.yMax, right.yMax));
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static float HashUnit(int seed, string stableId)
        {
            unchecked
            {
                uint hash = 2166136261u ^ (uint)seed;
                for (int index = 0; index < stableId.Length; index++)
                {
                    hash ^= stableId[index];
                    hash *= 16777619u;
                }

                return (hash & 0xFFFFu) / 65535f;
            }
        }
    }
}
