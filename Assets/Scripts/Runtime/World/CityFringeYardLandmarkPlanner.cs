using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adds one large, legible service-work chapter to each mountain Yard.
    /// The existing planner owns the continuous track/drain/retaining grammar;
    /// this focused pass owns only the four macro anchors and their practicals.
    /// </summary>
    internal static class CityFringeYardLandmarkPlanner
    {
        private static readonly Vector3 LensSize =
            new Vector3(0.34f, 0.22f, 0.055f);

        public static CityFringeYardPlan CreatePlan(
            CityLayout layout,
            IList<CityFringeYardDescriptor> sourceYards,
            CityTunnelForecourtDescriptor forecourt)
        {
            var yards = new List<CityFringeYardDescriptor>(
                sourceYards.Count);
            var practicals = new List<CityFringeYardPracticalDescriptor>(4);
            for (int index = 0; index < sourceYards.Count; index++)
            {
                CityFringeYardDescriptor yard = sourceYards[index];
                if (yard.Kind == CityFringeYardKind.EastUtilityEdge)
                {
                    yards.Add(yard);
                    continue;
                }

                List<CityFringeYardPartDescriptor> parts =
                    CopyMountainParts(yard);
                CityFringeYardPracticalDescriptor practical;
                switch (yard.Kind)
                {
                    case CityFringeYardKind.WestStoneTerraces:
                        practical = AddStoneWaterworks(
                            layout,
                            yard,
                            parts);
                        break;
                    case CityFringeYardKind.WestIndustrialBelt:
                        practical = AddIndustrialRepairFrame(
                            layout,
                            yard,
                            parts);
                        break;
                    case CityFringeYardKind.SouthTunnelForecourt:
                        practical = AddTunnelPortalLight(
                            layout,
                            yard,
                            forecourt,
                            parts);
                        break;
                    case CityFringeYardKind.SouthFloodWorks:
                        practical = AddFloodGauge(
                            layout,
                            yard,
                            parts);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(yard.Kind),
                            yard.Kind,
                            null);
                }

                yards.Add(new CityFringeYardDescriptor(
                    yard.StableId,
                    yard.AreaId,
                    yard.Kind,
                    yard.AreaBounds,
                    yard.Access,
                    yard.TraversalBounds,
                    parts));
                practicals.Add(practical);
            }

            return new CityFringeYardPlan(
                yards,
                true,
                forecourt,
                practicals);
        }

        private static List<CityFringeYardPartDescriptor> CopyMountainParts(
            CityFringeYardDescriptor yard)
        {
            var result = new List<CityFringeYardPartDescriptor>(
                yard.Parts.Count);
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor part = yard.Parts[index];
                result.Add(new CityFringeYardPartDescriptor(
                    part.StableId,
                    part.AreaId,
                    part.Kind,
                    part.Style == CityFringeYardStyle.ServiceGround
                        ? CityFringeYardStyle.ServiceTrack
                        : part.Style,
                    part.Center,
                    part.Rotation,
                    part.Size,
                    part.BlocksMovement));
            }

            return result;
        }

        private static CityFringeYardPracticalDescriptor AddStoneWaterworks(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 tangent = ResolveTangent(yard);
            float anchorLong = Mathf.Lerp(
                LongMinimum(yard) + 8f,
                LongMaximum(yard) - 8f,
                0.72f);
            Vector3 headwall = PointAtDepth(yard, 20.05f, anchorLong);
            Quaternion wallRotation = Quaternion.LookRotation(
                tangent,
                Vector3.up);

            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-culvert-pier-{side}",
                    CityFringeYardPartKind.CulvertHeadwall,
                    CityFringeYardStyle.OldMasonry,
                    headwall + tangent * (1.28f * side),
                    wallRotation,
                    new Vector3(1.05f, 2.25f, 0.68f),
                    true);
            }

            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-culvert-lintel",
                CityFringeYardPartKind.CulvertHeadwall,
                CityFringeYardStyle.OldMasonry,
                headwall,
                wallRotation,
                new Vector3(1.05f, 0.46f, 3.25f),
                true,
                2.05f);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-culvert-mouth",
                CityFringeYardPartKind.CulvertHeadwall,
                CityFringeYardStyle.Drainage,
                PointAtDepth(yard, 20.62f, anchorLong),
                wallRotation,
                new Vector3(0.10f, 1.62f, 1.64f),
                false,
                0.08f);

            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-terrace-shelf-{side}",
                    CityFringeYardPartKind.TerraceShelf,
                    CityFringeYardStyle.OldMasonry,
                    PointAtDepth(
                        yard,
                        20.0f,
                        anchorLong + side * 7.1f),
                    wallRotation,
                    new Vector3(2.45f, 0.16f, 7.2f),
                    false,
                    0.07f);
            }

            Vector3 lampBase = PointAtDepth(yard, 19.12f, anchorLong);
            Quaternion lampRotation = Quaternion.LookRotation(
                -yard.Access.OutwardNormal,
                Vector3.up);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-practical-housing",
                CityFringeYardPartKind.PracticalHousing,
                CityFringeYardStyle.Iron,
                lampBase,
                lampRotation,
                new Vector3(0.48f, 0.34f, 0.42f),
                false,
                2.76f);
            Vector3 lampPosition = WithGroundY(layout, yard, lampBase, 2.92f);
            return CreatePractical(
                yard,
                CityFringeYardPracticalKind.CulvertLamp,
                lampPosition,
                (-yard.Access.OutwardNormal + Vector3.down * 1.15f)
                    .normalized,
                new Color(2.20f, 1.62f, 0.82f, 1f));
        }

        private static CityFringeYardPracticalDescriptor
            AddIndustrialRepairFrame(
                CityLayout layout,
                CityFringeYardDescriptor yard,
                ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 tangent = ResolveTangent(yard);
            float anchorLong = Mathf.Lerp(
                LongMinimum(yard) + 10f,
                LongMaximum(yard) - 10f,
                0.62f);
            Vector3 frame = PointAtDepth(yard, 11.75f, anchorLong);
            Quaternion rotation = Quaternion.LookRotation(
                tangent,
                Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-repair-post-{side}",
                    CityFringeYardPartKind.RepairFrame,
                    CityFringeYardStyle.Iron,
                    frame + tangent * (2.28f * side),
                    rotation,
                    new Vector3(0.28f, 3.70f, 0.28f),
                    true);
            }

            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-repair-beam",
                CityFringeYardPartKind.RepairFrame,
                CityFringeYardStyle.Iron,
                frame,
                rotation,
                new Vector3(0.32f, 0.30f, 4.92f),
                false,
                3.40f);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-repair-winch",
                CityFringeYardPartKind.RepairFrame,
                CityFringeYardStyle.Iron,
                frame,
                rotation,
                new Vector3(0.16f, 1.08f, 0.16f),
                false,
                2.28f);

            for (int index = 0; index < 3; index++)
            {
                AddPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-pipe-stock-{index:00}",
                    CityFringeYardPartKind.PipeStock,
                    index == 2
                        ? CityFringeYardStyle.Iron
                        : CityFringeYardStyle.Concrete,
                    PointAtDepth(
                        yard,
                        9.45f + index * 0.78f,
                        anchorLong - 8.9f),
                    rotation,
                    new Vector3(0.34f, 0.34f, 5.75f),
                    true,
                    0.06f + index * 0.12f);
            }

            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-practical-housing",
                CityFringeYardPartKind.PracticalHousing,
                CityFringeYardStyle.Iron,
                frame,
                Quaternion.LookRotation(
                    -yard.Access.OutwardNormal,
                    Vector3.up),
                new Vector3(0.50f, 0.34f, 0.40f),
                false,
                3.20f);
            Vector3 lampPosition = WithGroundY(layout, yard, frame, 3.38f);
            return CreatePractical(
                yard,
                CityFringeYardPracticalKind.RepairLamp,
                lampPosition,
                (-yard.Access.OutwardNormal + Vector3.down * 1.35f)
                    .normalized,
                new Color(1.28f, 2.15f, 1.62f, 1f));
        }

        private static CityFringeYardPracticalDescriptor AddTunnelPortalLight(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityTunnelForecourtDescriptor forecourt,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            Vector3 side = Vector3.Cross(Vector3.up, forecourt.Axis).normalized;
            float frameOffset = forecourt.DriveClearWidth * 0.5f +
                                0.24f + 1.35f + 0.14f;
            Vector3 roadwardPost = Vector3.Lerp(
                forecourt.StreetAnchor,
                forecourt.PortalAnchor,
                0.69f) + side * frameOffset;
            Vector3 portalPost = Vector3.Lerp(
                forecourt.StreetAnchor,
                forecourt.PortalAnchor,
                0.86f) + side * frameOffset;
            Quaternion tunnelRotation = Quaternion.LookRotation(
                forecourt.Axis,
                Vector3.up);
            CityFringeYardPartDescriptor firstPost =
                AddMinimumGroundedPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-return-service-post-00",
                    CityFringeYardPartKind.RepairFrame,
                    CityFringeYardStyle.Iron,
                    roadwardPost,
                    tunnelRotation,
                    new Vector3(0.28f, 4.25f, 0.28f),
                    true,
                    0.06f);
            CityFringeYardPartDescriptor secondPost =
                AddMinimumGroundedPart(
                    layout,
                    yard,
                    parts,
                    $"{yard.AreaId}-landmark-return-service-post-01",
                    CityFringeYardPartKind.RepairFrame,
                    CityFringeYardStyle.Iron,
                    portalPost,
                    tunnelRotation,
                    new Vector3(0.28f, 4.25f, 0.28f),
                    true,
                    0.06f);

            Vector3 firstTop = firstPost.Center +
                Vector3.up * (firstPost.Size.y * 0.5f);
            Vector3 secondTop = secondPost.Center +
                Vector3.up * (secondPost.Size.y * 0.5f);
            const float beamThickness = 0.22f;
            AddBeamBetween(
                yard,
                parts,
                $"{yard.AreaId}-landmark-return-service-beam",
                firstTop + Vector3.up * (beamThickness * 0.5f),
                secondTop + Vector3.up * (beamThickness * 0.5f),
                beamThickness);
            AddBeamBetween(
                yard,
                parts,
                $"{yard.AreaId}-landmark-return-service-brace",
                firstPost.Center - Vector3.up * 1.15f,
                secondTop - Vector3.up * 0.42f,
                0.16f);

            Vector3 housingCenter =
                CityTunnelPortalLightGeometry.ResolvePosition(forecourt);
            Vector3 lightForward =
                CityTunnelPortalLightGeometry.ResolveForward(forecourt);
            var housing = new CityFringeYardPartDescriptor(
                $"{yard.AreaId}-landmark-practical-housing",
                yard.AreaId,
                CityFringeYardPartKind.PracticalHousing,
                CityFringeYardStyle.Iron,
                housingCenter,
                Quaternion.LookRotation(lightForward, Vector3.up),
                CityTunnelPortalLightGeometry.HousingSize,
                false);
            RequireOverheadPortalHousing(layout, yard, housing);
            parts.Add(housing);

            return CreatePractical(
                yard,
                CityFringeYardPracticalKind.TunnelReturnLamp,
                housing.Center,
                lightForward,
                new Color(2.80f, 1.42f, 0.38f, 1f));
        }

        private static CityFringeYardPartDescriptor AddMinimumGroundedPart(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts,
            string stableId,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 xz,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement,
            float embed)
        {
            Vector3 right =
                (rotation * Vector3.right) * (size.x * 0.5f);
            Vector3 forward =
                (rotation * Vector3.forward) * (size.z * 0.5f);
            float minimumGround = float.PositiveInfinity;
            for (int rightSign = -1; rightSign <= 1; rightSign += 2)
            {
                for (int forwardSign = -1;
                     forwardSign <= 1;
                     forwardSign += 2)
                {
                    Vector3 corner = xz +
                        right * rightSign +
                        forward * forwardSign;
                    minimumGround = Mathf.Min(
                        minimumGround,
                        SampleYardTop(layout, yard, corner));
                }
            }

            var part = new CityFringeYardPartDescriptor(
                stableId,
                yard.AreaId,
                kind,
                style,
                new Vector3(
                    xz.x,
                    minimumGround - embed + size.y * 0.5f,
                    xz.z),
                rotation,
                size,
                blocksMovement);
            RequireContainedAndClear(yard, part);
            parts.Add(part);
            return part;
        }

        private static CityFringeYardPartDescriptor AddBeamBetween(
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts,
            string stableId,
            Vector3 start,
            Vector3 end,
            float thickness)
        {
            Vector3 delta = end - start;
            var part = new CityFringeYardPartDescriptor(
                stableId,
                yard.AreaId,
                CityFringeYardPartKind.RepairFrame,
                CityFringeYardStyle.Iron,
                (start + end) * 0.5f,
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                new Vector3(thickness, thickness, delta.magnitude),
                false);
            RequireContainedAndClear(yard, part);
            parts.Add(part);
            return part;
        }

        private static float SampleYardTop(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            const float tolerance = 0.002f;
            Vector2 sample = new Vector2(point.x, point.z);
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (string.Equals(
                        surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal) &&
                    sample.x >= bounds.xMin - tolerance &&
                    sample.x <= bounds.xMax + tolerance &&
                    sample.y >= bounds.yMin - tolerance &&
                    sample.y <= bounds.yMax + tolerance)
                {
                    return CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        sample);
                }
            }

            throw new InvalidOperationException(
                $"Fringe landmark '{yard.AreaId}' has no owner ground.");
        }

        private static CityFringeYardPracticalDescriptor AddFloodGauge(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts)
        {
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor gabion = yard.Parts[index];
                if (gabion.Kind != CityFringeYardPartKind.Gabion ||
                    gabion.StableId.IndexOf(
                        "flood-gabion",
                        StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                Vector3 axis = gabion.Rotation * Vector3.forward;
                Vector3 roadward = -yard.Access.OutwardNormal;
                AddElevatedPart(
                    yard,
                    parts,
                    $"{gabion.StableId}-cage-top",
                    gabion,
                    gabion.Center + Vector3.up *
                        (gabion.Size.y * 0.5f + 0.07f),
                    new Vector3(0.10f, 0.10f, gabion.Size.z + 0.18f));
                AddElevatedPart(
                    yard,
                    parts,
                    $"{gabion.StableId}-cage-face",
                    gabion,
                    gabion.Center + roadward *
                        (gabion.Size.x * 0.5f + 0.045f),
                    new Vector3(0.08f, 0.08f, gabion.Size.z + 0.18f));
                for (int side = -1; side <= 1; side += 2)
                {
                    AddElevatedPart(
                        yard,
                        parts,
                        $"{gabion.StableId}-cage-post-{side}",
                        gabion,
                        gabion.Center + axis *
                            (gabion.Size.z * 0.5f * side),
                        new Vector3(
                            0.10f,
                            gabion.Size.y + 0.16f,
                            0.10f));
                }
            }

            Vector3 tangent = ResolveTangent(yard);
            float gaugeLong = Mathf.Lerp(
                LongMinimum(yard) + 14f,
                LongMaximum(yard) - 14f,
                0.44f);
            Vector3 gauge = PointAtDepth(yard, 11.0f, gaugeLong);
            Quaternion tangentRotation = Quaternion.LookRotation(
                tangent,
                Vector3.up);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-flood-gauge",
                CityFringeYardPartKind.FloodGauge,
                CityFringeYardStyle.UtilityPaint,
                gauge,
                tangentRotation,
                new Vector3(0.16f, 4.15f, 0.16f),
                true);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-flood-wheel-a",
                CityFringeYardPartKind.FloodGauge,
                CityFringeYardStyle.Iron,
                gauge,
                tangentRotation,
                new Vector3(0.12f, 0.12f, 0.92f),
                false,
                1.42f);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-flood-wheel-b",
                CityFringeYardPartKind.FloodGauge,
                CityFringeYardStyle.Iron,
                gauge,
                Quaternion.LookRotation(
                    yard.Access.OutwardNormal,
                    Vector3.up),
                new Vector3(0.12f, 0.12f, 0.92f),
                false,
                1.42f);
            AddPart(
                layout,
                yard,
                parts,
                $"{yard.AreaId}-landmark-practical-housing",
                CityFringeYardPartKind.PracticalHousing,
                CityFringeYardStyle.Iron,
                gauge,
                Quaternion.LookRotation(
                    -yard.Access.OutwardNormal,
                    Vector3.up),
                new Vector3(0.44f, 0.30f, 0.38f),
                false,
                3.36f);

            Vector3 lampPosition = WithGroundY(layout, yard, gauge, 3.52f);
            return CreatePractical(
                yard,
                CityFringeYardPracticalKind.FloodGaugeLamp,
                lampPosition,
                (-yard.Access.OutwardNormal + Vector3.down * 1.10f)
                    .normalized,
                new Color(1.12f, 2.02f, 1.42f, 1f));
        }

        private static void AddElevatedPart(
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts,
            string stableId,
            CityFringeYardPartDescriptor source,
            Vector3 center,
            Vector3 size)
        {
            var part = new CityFringeYardPartDescriptor(
                stableId,
                yard.AreaId,
                CityFringeYardPartKind.GabionCage,
                CityFringeYardStyle.Iron,
                center,
                source.Rotation,
                size,
                false);
            RequireContainedAndClear(yard, part);
            parts.Add(part);
        }

        private static CityFringeYardPracticalDescriptor CreatePractical(
            CityFringeYardDescriptor yard,
            CityFringeYardPracticalKind kind,
            Vector3 position,
            Vector3 forward,
            Color litColor)
        {
            return new CityFringeYardPracticalDescriptor(
                $"{yard.AreaId}-service-practical",
                yard.AreaId,
                yard.Kind,
                kind,
                position,
                forward,
                LensSize,
                litColor);
        }

        private static void AddPart(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            ICollection<CityFringeYardPartDescriptor> parts,
            string stableId,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 xz,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement,
            float baseOffset = 0f)
        {
            Vector3 center = WithGroundY(
                layout,
                yard,
                xz,
                baseOffset + size.y * 0.5f);
            var part = new CityFringeYardPartDescriptor(
                stableId,
                yard.AreaId,
                kind,
                style,
                center,
                rotation,
                size,
                blocksMovement);
            RequireContainedAndClear(yard, part);
            parts.Add(part);
        }

        private static Vector3 WithGroundY(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            Vector3 xz,
            float height)
        {
            if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(xz.x, xz.z),
                    out float ground,
                    out CitySurfaceDescriptor surface) ||
                !string.Equals(
                    surface.AreaId,
                    yard.AreaId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fringe landmark '{yard.AreaId}' has no owner ground.");
            }

            return new Vector3(xz.x, ground + height, xz.z);
        }

        private static void RequireContainedAndClear(
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            Rect footprint = part.Footprint;
            const float tolerance = 0.035f;
            if (footprint.xMin < yard.AreaBounds.xMin - tolerance ||
                footprint.xMax > yard.AreaBounds.xMax + tolerance ||
                footprint.yMin < yard.AreaBounds.yMin - tolerance ||
                footprint.yMax > yard.AreaBounds.yMax + tolerance ||
                footprint.Overlaps(yard.Access.ApproachBounds) ||
                footprint.Overlaps(yard.TraversalBounds))
            {
                throw new InvalidOperationException(
                    $"Fringe landmark part '{part.StableId}' leaves or " +
                    "blocks its reserved Yard space.");
            }
        }

        private static void RequireOverheadPortalHousing(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor housing)
        {
            Rect footprint = housing.Footprint;
            const float tolerance = 0.035f;
            if (housing.BlocksMovement ||
                footprint.xMin < yard.AreaBounds.xMin - tolerance ||
                footprint.xMax > yard.AreaBounds.xMax + tolerance ||
                footprint.yMin < yard.AreaBounds.yMin - tolerance ||
                footprint.yMax > yard.AreaBounds.yMax + tolerance ||
                !footprint.Overlaps(yard.TraversalBounds))
            {
                throw new InvalidOperationException(
                    $"Tunnel portal housing '{housing.StableId}' is not " +
                    "safely centred over its reserved route.");
            }

            Vector3 localRight = housing.Rotation * Vector3.right;
            Vector3 localUp = housing.Rotation * Vector3.up;
            Vector3 localForward = housing.Rotation * Vector3.forward;
            float verticalExtent =
                Mathf.Abs(localRight.y) * housing.Size.x * 0.5f +
                Mathf.Abs(localUp.y) * housing.Size.y * 0.5f +
                Mathf.Abs(localForward.y) * housing.Size.z * 0.5f;
            float ground = SampleYardTop(layout, yard, housing.Center);
            if (housing.Center.y - verticalExtent - ground <
                CityTunnelPortalLightGeometry.MinimumHousingClearance)
            {
                throw new InvalidOperationException(
                    $"Tunnel portal housing '{housing.StableId}' hangs " +
                    "inside the traversal clearance.");
            }
        }

        private static Vector3 PointAtDepth(
            CityFringeYardDescriptor yard,
            float depth,
            float longCoordinate)
        {
            Vector3 outward = yard.Access.OutwardNormal;
            Vector3 point = yard.Access.Center;
            if (Mathf.Abs(outward.x) > 0.5f)
            {
                point.x = outward.x < 0f
                    ? yard.AreaBounds.xMax - depth
                    : yard.AreaBounds.xMin + depth;
                point.z = longCoordinate;
            }
            else
            {
                point.x = longCoordinate;
                point.z = outward.z < 0f
                    ? yard.AreaBounds.yMax - depth
                    : yard.AreaBounds.yMin + depth;
            }

            point.y = 0f;
            return point;
        }

        private static Vector3 ResolveTangent(CityFringeYardDescriptor yard)
        {
            return Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? Vector3.forward
                : Vector3.right;
        }

        private static float LongMinimum(CityFringeYardDescriptor yard)
        {
            return Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? yard.AreaBounds.yMin
                : yard.AreaBounds.xMin;
        }

        private static float LongMaximum(CityFringeYardDescriptor yard)
        {
            return Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? yard.AreaBounds.yMax
                : yard.AreaBounds.xMax;
        }
    }
}
