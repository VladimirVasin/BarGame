using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Authors the previously empty ground between the ring road and the
    /// mountain-toe service belt. It adds a surface-led continuous road
    /// shoulder, a few traces ending at the existing service track, and three
    /// or four evenly distributed middle-distance compositions with
    /// Yard-specific civil-work vocabulary. Thin surface strokes and narrow
    /// primary marks stay collider-free; raised shelves, pipe cradles, low
    /// gabion returns and rockfall masses use the belt's physical contract.
    /// </summary>
    public static partial class CityFringeYardPlanner
    {
        internal const float ForefieldRoadBandMaximumDepth = 4f;
        internal const float ForefieldMiddleBandMaximumDepth = 14f;
        internal const float ForefieldToeBandMaximumDepth = 22f;
        internal const float MaximumForefieldAnchorGap = 40f;
        internal const float MaximumForefieldAnchorWidth = 2f;
        internal const float MaximumRoadShoulderWidth = 0.8f;
        internal const float MaximumTunnelTraceWidth = 0.36f;
        internal const float MaximumLowReturnDepth = 1.05f;
        internal const int MinimumForefieldAnchorCount = 3;
        internal const int MaximumForefieldAnchorCount = 4;

        private const float ShoulderCenterDepth = 2.05f;
        private const float ShoulderWidth = 0.76f;
        private const float SpurRoadDepth = 3.65f;
        private const float ForefieldSurfaceLift = 0.012f;
        private const float ForefieldSurfaceThickness = 0.032f;
        private const float FloodTraceThickness = 0.10f;
        private const float FloodSurfaceTraceWidth = 0.48f;
        private const int ServiceSpurCount = 3;
        private const int LowReturnSegmentCount = 3;
        private const float LowReturnTotalDepth = 3f;
        private const float LowReturnSegmentGap = 0.10f;

        private static void AddForefield(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            CityFringeYardKind kind,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            Vector3 serviceTrackLine,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            AddRoadShoulder(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                outward,
                roadAnchor,
                parts);
            AddServiceSpurs(
                layout,
                surfaces,
                bounds,
                areaId,
                kind,
                tangent,
                outward,
                roadAnchor,
                serviceTrackLine,
                parts);
            AddForefieldAnchors(
                layout,
                surfaces,
                bounds,
                areaId,
                kind,
                tangent,
                outward,
                roadAnchor,
                reserved,
                parts);
        }

        private static void AddRoadShoulder(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, tangent, 0.8f, 0.8f, out float start, out float end);
            Vector3 line = PointAtRoadDepth(
                bounds,
                roadAnchor,
                outward,
                ShoulderCenterDepth);
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                if (!string.Equals(
                        surface.AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                GetLongRange(
                    surface.WorldBounds,
                    tangent,
                    0f,
                    0f,
                    out float cellStart,
                    out float cellEnd);
                cellStart = Mathf.Max(start, cellStart);
                cellEnd = Mathf.Min(end, cellEnd);
                if (cellEnd - cellStart <= 0.1f)
                {
                    continue;
                }

                AddGradedStrip(
                    layout,
                    surfaces,
                    areaId,
                    $"road-shoulder-{surface.Cell.x}-{surface.Cell.y}",
                    CityFringeYardPartKind.RoadShoulder,
                    CityFringeYardStyle.ServiceGround,
                    SetLongCoordinate(line, tangent, cellStart),
                    SetLongCoordinate(line, tangent, cellEnd),
                    ShoulderWidth,
                    ForefieldSurfaceThickness,
                    cellEnd - cellStart + 0.1f,
                    null,
                    parts,
                    ForefieldSurfaceLift);
            }
        }

        private static void AddServiceSpurs(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            CityFringeYardKind kind,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            Vector3 serviceTrackLine,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, tangent, 10f, 10f, out float start, out float end);
            Vector3 roadLine = PointAtRoadDepth(
                bounds,
                roadAnchor,
                outward,
                SpurRoadDepth);
            for (int index = 0; index < ServiceSpurCount; index++)
            {
                float amount = (index + 1f) / (ServiceSpurCount + 1f);
                float coordinate = Mathf.Lerp(start, end, amount);
                AddGradedStrip(
                    layout,
                    surfaces,
                    areaId,
                    $"service-spur-{index:00}",
                    CityFringeYardPartKind.ServiceSpur,
                    CityFringeYardStyle.ServiceGround,
                    SetLongCoordinate(roadLine, tangent, coordinate),
                    SetLongCoordinate(serviceTrackLine, tangent, coordinate),
                    kind == CityFringeYardKind.SouthTunnelForecourt
                        ? MaximumTunnelTraceWidth
                        : kind == CityFringeYardKind.SouthFloodWorks
                            ? ShoulderWidth
                            : 2.35f,
                    kind == CityFringeYardKind.SouthFloodWorks
                        ? FloodTraceThickness
                        : ForefieldSurfaceThickness,
                    kind == CityFringeYardKind.SouthFloodWorks
                        ? 2f
                        : 8f,
                    null,
                    parts,
                    ForefieldSurfaceLift);
            }
        }

        private static void AddForefieldAnchors(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            CityFringeYardKind kind,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            GetLongRange(bounds, tangent, 0f, 0f, out float start, out float end);
            float length = end - start;
            int anchorCount = Mathf.Clamp(
                Mathf.CeilToInt(length / MaximumForefieldAnchorGap),
                MinimumForefieldAnchorCount,
                MaximumForefieldAnchorCount);
            for (int index = 0; index < anchorCount; index++)
            {
                float coordinate = start +
                    length * ((index + 0.5f) / anchorCount);
                float variation = HashUnit(
                    layout.Seed,
                    $"{areaId}-forefield-anchor-{index:00}");
                switch (kind)
                {
                    case CityFringeYardKind.WestStoneTerraces:
                        AddDrainageShelfComposition(
                            layout,
                            surfaces,
                            bounds,
                            areaId,
                            tangent,
                            outward,
                            roadAnchor,
                            coordinate,
                            index,
                            variation,
                            reserved,
                            parts);
                        break;
                    case CityFringeYardKind.WestIndustrialBelt:
                        AddRepairCradleComposition(
                            layout,
                            surfaces,
                            bounds,
                            areaId,
                            tangent,
                            outward,
                            roadAnchor,
                            coordinate,
                            index,
                            variation,
                            reserved,
                            parts);
                        break;
                    case CityFringeYardKind.SouthTunnelForecourt:
                        AddFreightStagingComposition(
                            layout,
                            surfaces,
                            bounds,
                            areaId,
                            tangent,
                            outward,
                            roadAnchor,
                            coordinate,
                            index,
                            variation,
                            reserved,
                            parts);
                        break;
                    case CityFringeYardKind.SouthFloodWorks:
                        AddOverflowComposition(
                            layout,
                            surfaces,
                            bounds,
                            areaId,
                            tangent,
                            outward,
                            roadAnchor,
                            coordinate,
                            index,
                            variation,
                            reserved,
                            parts);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(kind),
                            kind,
                            null);
                }
            }
        }

        private static void AddDrainageShelfComposition(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            int index,
            float variation,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
            AddForefieldAnchorStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                CityFringeYardStyle.Drainage,
                tangent,
                outward,
                roadAnchor,
                7.45f + variation * 0.55f,
                5.2f,
                coordinate,
                index,
                variation,
                parts);
            AddForefieldGroundedPart(
                layout,
                surfaces,
                bounds,
                areaId,
                $"{areaId}-forefield-shelf-{index:00}",
                CityFringeYardPartKind.TerraceShelf,
                CityFringeYardStyle.OldMasonry,
                outward,
                roadAnchor,
                11.15f,
                coordinate + (index % 2 == 0 ? 1.2f : -1.2f),
                tangent,
                rotation,
                new Vector3(3.25f, 0.26f, 5.4f),
                true,
                0.08f,
                reserved,
                parts);
            AddForefieldStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                $"forefield-wash-{index:00}",
                CityFringeYardPartKind.DrainChannel,
                CityFringeYardStyle.Drainage,
                tangent,
                outward,
                roadAnchor,
                coordinate,
                3.8f,
                13.25f,
                0.72f,
                parts);
        }

        private static void AddRepairCradleComposition(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            int index,
            float variation,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
            AddForefieldAnchorStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                CityFringeYardStyle.Concrete,
                tangent,
                outward,
                roadAnchor,
                8.55f,
                6.6f,
                coordinate,
                index,
                variation,
                parts);
            AddForefieldGroundedPart(
                layout,
                surfaces,
                bounds,
                areaId,
                $"{areaId}-forefield-pipe-{index:00}",
                CityFringeYardPartKind.PipeStock,
                CityFringeYardStyle.Iron,
                outward,
                roadAnchor,
                10.65f,
                coordinate + (index % 2 == 0 ? 0.7f : -0.7f),
                tangent,
                rotation,
                new Vector3(0.58f, 0.38f, 7.4f),
                true,
                0.07f,
                reserved,
                parts);
            AddForefieldGroundedPart(
                layout,
                surfaces,
                bounds,
                areaId,
                $"{areaId}-forefield-cradle-{index:00}",
                CityFringeYardPartKind.RepairFrame,
                CityFringeYardStyle.Concrete,
                outward,
                roadAnchor,
                10.65f,
                coordinate,
                tangent,
                rotation,
                new Vector3(2.7f, 0.30f, 0.56f),
                true,
                0.05f,
                reserved,
                parts);
        }

        private static void AddFreightStagingComposition(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            int index,
            float variation,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            AddForefieldStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                $"forefield-anchor-{index:00}",
                CityFringeYardPartKind.ForefieldAnchor,
                index % 2 == 0
                    ? CityFringeYardStyle.ServiceGround
                    : CityFringeYardStyle.Concrete,
                tangent,
                outward,
                roadAnchor,
                coordinate,
                5.15f,
                12.55f,
                MaximumTunnelTraceWidth,
                parts);
            for (int side = -1; side <= 1; side += 2)
            {
                AddForefieldStroke(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    $"forefield-freight-rut-{index:00}-{side}",
                    CityFringeYardPartKind.WheelRut,
                    CityFringeYardStyle.Drainage,
                    tangent,
                    outward,
                    roadAnchor,
                    coordinate + side * 1.35f,
                    4.15f,
                    13.35f,
                    MaximumTunnelTraceWidth,
                    parts);
            }
        }

        private static void AddOverflowComposition(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            int index,
            float variation,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            AddForefieldStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                $"forefield-anchor-{index:00}",
                CityFringeYardPartKind.ForefieldAnchor,
                CityFringeYardStyle.Drainage,
                tangent,
                outward,
                roadAnchor,
                coordinate,
                5.0f,
                12.5f,
                FloodSurfaceTraceWidth,
                parts);
            AddForefieldStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                $"forefield-overflow-{index:00}",
                CityFringeYardPartKind.DrainChannel,
                CityFringeYardStyle.Drainage,
                tangent,
                outward,
                roadAnchor,
                coordinate - 1.35f,
                3.8f,
                13.35f,
                0.46f,
                parts);
            AddSegmentedLowReturn(
                layout,
                surfaces,
                bounds,
                areaId,
                tangent,
                outward,
                roadAnchor,
                coordinate + 1.65f,
                index,
                reserved,
                parts);
        }

        private static void AddForefieldAnchorStroke(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            CityFringeYardStyle style,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float centerDepth,
            float depthLength,
            float coordinate,
            int index,
            float variation,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            float halfDepth = depthLength * 0.5f;
            AddForefieldStroke(
                layout,
                surfaces,
                bounds,
                areaId,
                $"forefield-anchor-{index:00}",
                CityFringeYardPartKind.ForefieldAnchor,
                style,
                tangent,
                outward,
                roadAnchor,
                coordinate,
                centerDepth - halfDepth,
                centerDepth + halfDepth,
                Mathf.Lerp(1.55f, MaximumForefieldAnchorWidth, variation),
                parts);
        }

        private static void AddSegmentedLowReturn(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            int anchorIndex,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            float segmentDepth =
                (LowReturnTotalDepth -
                 LowReturnSegmentGap * (LowReturnSegmentCount - 1)) /
                LowReturnSegmentCount;
            float step = segmentDepth + LowReturnSegmentGap;
            Quaternion rotation = Quaternion.LookRotation(
                tangent,
                Vector3.up);
            for (int segment = 0;
                 segment < LowReturnSegmentCount;
                 segment++)
            {
                float depth = 12.05f +
                    (segment - (LowReturnSegmentCount - 1) * 0.5f) * step;
                AddForefieldGroundedPart(
                    layout,
                    surfaces,
                    bounds,
                    areaId,
                    $"{areaId}-forefield-low-return-" +
                    $"{anchorIndex:00}-{segment:00}",
                    CityFringeYardPartKind.Gabion,
                    CityFringeYardStyle.Gabion,
                    outward,
                    roadAnchor,
                    depth,
                    coordinate,
                    tangent,
                    rotation,
                    new Vector3(segmentDepth, 0.52f, 5.2f),
                    true,
                    0.10f,
                    reserved,
                    parts);
            }
        }

        private static void AddForefieldStroke(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            string stableStem,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 tangent,
            Vector3 outward,
            Vector3 roadAnchor,
            float coordinate,
            float startDepth,
            float endDepth,
            float width,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 start = PointAtRoadDepth(
                bounds,
                roadAnchor,
                outward,
                startDepth);
            Vector3 end = PointAtRoadDepth(
                bounds,
                roadAnchor,
                outward,
                endDepth);
            AddGradedStrip(
                layout,
                surfaces,
                areaId,
                stableStem,
                kind,
                style,
                SetLongCoordinate(start, tangent, coordinate),
                SetLongCoordinate(end, tangent, coordinate),
                width,
                ForefieldSurfaceThickness,
                20f,
                null,
                parts,
                ForefieldSurfaceLift);
        }

        private static void AddForefieldGroundedPart(
            CityLayout layout,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            Rect bounds,
            string areaId,
            string stableId,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 outward,
            Vector3 roadAnchor,
            float depth,
            float coordinate,
            Vector3 tangent,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement,
            float embed,
            Rect reserved,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 center = PointAtRoadDepth(
                bounds,
                roadAnchor,
                outward,
                depth);
            center = SetLongCoordinate(center, tangent, coordinate);
            AddGroundedPart(
                layout,
                surfaces,
                areaId,
                stableId,
                kind,
                style,
                center,
                rotation,
                size,
                blocksMovement,
                embed,
                reserved,
                parts);
        }

        private static Vector3 PointAtRoadDepth(
            Rect bounds,
            Vector3 anchor,
            Vector3 outward,
            float depth)
        {
            Vector3 result = anchor;
            if (outward.x < -0.5f)
            {
                result.x = bounds.xMax - depth;
            }
            else if (outward.x > 0.5f)
            {
                result.x = bounds.xMin + depth;
            }
            else if (outward.z < -0.5f)
            {
                result.z = bounds.yMax - depth;
            }
            else
            {
                result.z = bounds.yMin + depth;
            }

            return result;
        }

        private static Vector3 MovePoleOutsideReserved(
            Vector3 center,
            Vector3 axis,
            float start,
            float end,
            Rect reserved)
        {
            const float poleHalfFootprint = 0.2f;
            const float clearance = 0.06f;
            Rect footprint = Rect.MinMaxRect(
                center.x - poleHalfFootprint,
                center.z - poleHalfFootprint,
                center.x + poleHalfFootprint,
                center.z + poleHalfFootprint);
            if (!footprint.Overlaps(reserved))
            {
                return center;
            }

            float coordinate = Mathf.Abs(axis.x) > 0.5f
                ? center.x
                : center.z;
            float reservedMinimum = Mathf.Abs(axis.x) > 0.5f
                ? reserved.xMin
                : reserved.yMin;
            float reservedMaximum = Mathf.Abs(axis.x) > 0.5f
                ? reserved.xMax
                : reserved.yMax;
            float before = reservedMinimum - poleHalfFootprint - clearance;
            float after = reservedMaximum + poleHalfFootprint + clearance;
            bool beforeFits = before >= start && before <= end;
            bool afterFits = after >= start && after <= end;
            if (!beforeFits && !afterFits)
            {
                return center;
            }

            float selected = !beforeFits
                ? after
                : !afterFits
                    ? before
                    : Mathf.Abs(coordinate - before) <=
                      Mathf.Abs(after - coordinate)
                        ? before
                        : after;
            return SetLongCoordinate(center, axis, selected);
        }

        private static int ResolvePoleCount(
            string areaId,
            float length,
            float maximumSpacing)
        {
            // The east utility edge is outside this pass and preserves its
            // established sparse line. Mountain Yards require enough
            // intervals, not enough poles rounded down, to respect the real
            // 34/40 m maximum after distribution.
            return CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    areaId)
                ? Mathf.Max(
                    2,
                    Mathf.CeilToInt(length / maximumSpacing) + 1)
                : Mathf.Max(
                    2,
                    Mathf.FloorToInt(length / maximumSpacing));
        }
    }
}
