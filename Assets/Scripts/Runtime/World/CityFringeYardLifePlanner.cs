using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adds one small, grounded scene to each already-authored fringe Yard.
    /// The landmark pass owns the civil works; this pass only selects a clear
    /// place beside them for the human-scale evidence of maintenance.
    /// </summary>
    internal static class CityFringeYardLifePlanner
    {
        private const float EdgeClearance = 0.40f;
        private const float RouteClearance = 0.70f;
        private const float SolidClearance = 0.28f;
        private const float MaximumCornerGroundRange = 0.16f;
        private const float GroundLift = 0.008f;

        public static CityFringeYardPlan Append(
            CityLayout layout,
            CityFringeYardPlan source)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!source.IsEnabled)
            {
                return source;
            }

            var yards = new List<CityFringeYardDescriptor>(
                source.Yards.Count);
            for (int index = 0; index < source.Yards.Count; index++)
            {
                CityFringeYardDescriptor yard = source.Yards[index];
                var parts = new List<CityFringeYardPartDescriptor>(
                    yard.Parts.Count + 1);
                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    parts.Add(yard.Parts[partIndex]);
                }

                parts.Add(CreateScene(layout, source, yard, parts));
                yards.Add(new CityFringeYardDescriptor(
                    yard.StableId,
                    yard.AreaId,
                    yard.Kind,
                    yard.AreaBounds,
                    yard.Access,
                    yard.TraversalBounds,
                    parts));
            }

            var practicals = new List<CityFringeYardPracticalDescriptor>(
                source.Practicals.Count);
            for (int index = 0;
                 index < source.Practicals.Count;
                 index++)
            {
                practicals.Add(source.Practicals[index]);
            }

            var result = new CityFringeYardPlan(
                yards,
                source.HasTunnelForecourt,
                source.TunnelForecourt,
                practicals);
            CityFringeYardLifeValidator.ValidateOrThrow(layout, result);
            return result;
        }

        private static CityFringeYardPartDescriptor CreateScene(
            CityLayout layout,
            CityFringeYardPlan plan,
            CityFringeYardDescriptor yard,
            IReadOnlyList<CityFringeYardPartDescriptor> parts)
        {
            ResolveSceneContract(
                yard.Kind,
                out CityFringeYardPartKind partKind,
                out CityFringeYardStyle style,
                out Vector3 size);

            Vector3 outward = yard.Access.OutwardNormal;
            outward.y = 0f;
            outward.Normalize();
            Vector3 tangent = Vector3.Cross(Vector3.up, outward).normalized;
            Quaternion rotation = Quaternion.LookRotation(
                -outward,
                Vector3.up);
            Vector3 anchor = ResolveSemanticAnchor(plan, yard, parts);
            List<Vector2> offsets = CreateCandidateOffsets(
                plan,
                yard,
                size);

            for (int index = 0; index < offsets.Count; index++)
            {
                Vector2 offset = offsets[index];
                Vector3 candidate = anchor +
                    tangent * offset.x +
                    outward * offset.y;
                if (TryCreateGroundedPart(
                        layout,
                        yard,
                        parts,
                        partKind,
                        style,
                        candidate,
                        rotation,
                        size,
                        out CityFringeYardPartDescriptor result))
                {
                    return result;
                }
            }

            // A semantic anchor can occasionally fall beside an unusually
            // dense seed-specific pile. Fall back to a sparse grid measured
            // from the declared street opening, never to a magic world point.
            float[] depths = yard.Kind == CityFringeYardKind.EastUtilityEdge
                ? new[] { 18f, 24f, 30f, 38f }
                : new[] { 7f, 10f, 13f, 16f, 19f };
            float[] lateral = { 8f, -8f, 13f, -13f, 19f, -19f, 27f, -27f };
            for (int depthIndex = 0;
                 depthIndex < depths.Length;
                 depthIndex++)
            {
                for (int lateralIndex = 0;
                     lateralIndex < lateral.Length;
                     lateralIndex++)
                {
                    Vector3 candidate = yard.Access.Center +
                        outward * depths[depthIndex] +
                        tangent * lateral[lateralIndex];
                    if (TryCreateGroundedPart(
                            layout,
                            yard,
                            parts,
                            partKind,
                            style,
                            candidate,
                            rotation,
                            size,
                            out CityFringeYardPartDescriptor result))
                    {
                        return result;
                    }
                }
            }

            throw new InvalidOperationException(
                $"Fringe Yard '{yard.AreaId}' has no clear grounded place " +
                $"for its '{partKind}' life scene.");
        }

        private static void ResolveSceneContract(
            CityFringeYardKind yardKind,
            out CityFringeYardPartKind partKind,
            out CityFringeYardStyle style,
            out Vector3 size)
        {
            switch (yardKind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    partKind = CityFringeYardPartKind.MasonCart;
                    style = CityFringeYardStyle.Timber;
                    size = new Vector3(3.0f, 1.65f, 2.2f);
                    return;
                case CityFringeYardKind.WestIndustrialBelt:
                    partKind = CityFringeYardPartKind.WinchServiceSet;
                    style = CityFringeYardStyle.Iron;
                    size = new Vector3(2.8f, 1.55f, 2.2f);
                    return;
                case CityFringeYardKind.SouthTunnelForecourt:
                    partKind = CityFringeYardPartKind.TunnelServiceSet;
                    style = CityFringeYardStyle.Timber;
                    size = new Vector3(3.4f, 1.70f, 2.4f);
                    return;
                case CityFringeYardKind.SouthFloodWorks:
                    partKind = CityFringeYardPartKind.FloodMaintenanceSet;
                    style = CityFringeYardStyle.Iron;
                    size = new Vector3(3.0f, 1.60f, 2.2f);
                    return;
                case CityFringeYardKind.EastUtilityEdge:
                    partKind = CityFringeYardPartKind.OpenHoodCar;
                    style = CityFringeYardStyle.DomesticPaint;
                    size = new Vector3(5.8f, 2.05f, 4.2f);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(yardKind),
                        yardKind,
                        null);
            }
        }

        private static Vector3 ResolveSemanticAnchor(
            CityFringeYardPlan plan,
            CityFringeYardDescriptor yard,
            IReadOnlyList<CityFringeYardPartDescriptor> parts)
        {
            string token;
            switch (yard.Kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    token = "landmark-culvert-mouth";
                    break;
                case CityFringeYardKind.WestIndustrialBelt:
                    token = "landmark-repair-winch";
                    break;
                case CityFringeYardKind.SouthFloodWorks:
                    token = "landmark-flood-gauge";
                    break;
                case CityFringeYardKind.EastUtilityEdge:
                    token = "utility-shed-01";
                    break;
                case CityFringeYardKind.SouthTunnelForecourt:
                    return Vector3.Lerp(
                        plan.TunnelForecourt.StreetAnchor,
                        plan.TunnelForecourt.PortalAnchor,
                        0.62f);
                default:
                    return yard.Access.Center +
                        yard.Access.OutwardNormal * 10f;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].StableId.IndexOf(
                        token,
                        StringComparison.Ordinal) >= 0)
                {
                    return parts[index].Center;
                }
            }

            return yard.Access.Center + yard.Access.OutwardNormal * 10f;
        }

        private static List<Vector2> CreateCandidateOffsets(
            CityFringeYardPlan plan,
            CityFringeYardDescriptor yard,
            Vector3 size)
        {
            var result = new List<Vector2>(16);
            if (yard.Kind == CityFringeYardKind.SouthTunnelForecourt)
            {
                float side = plan.TunnelForecourt.DriveClearWidth * 0.5f +
                    size.x * 0.5f + RouteClearance + 0.45f;
                result.Add(new Vector2(side, 0f));
                result.Add(new Vector2(-side, 0f));
                result.Add(new Vector2(side + 2.2f, -2.0f));
                result.Add(new Vector2(-side - 2.2f, -2.0f));
                result.Add(new Vector2(side + 3.8f, 2.4f));
                result.Add(new Vector2(-side - 3.8f, 2.4f));
                return result;
            }

            if (yard.Kind == CityFringeYardKind.EastUtilityEdge)
            {
                result.Add(new Vector2(0f, -10f));
                result.Add(new Vector2(9f, -9f));
                result.Add(new Vector2(-9f, -9f));
                result.Add(new Vector2(15f, -12f));
                result.Add(new Vector2(-15f, -12f));
                result.Add(new Vector2(0f, -17f));
                return result;
            }

            result.Add(new Vector2(4.8f, -2.6f));
            result.Add(new Vector2(-4.8f, -2.6f));
            result.Add(new Vector2(7.2f, 0f));
            result.Add(new Vector2(-7.2f, 0f));
            result.Add(new Vector2(9.5f, -4.2f));
            result.Add(new Vector2(-9.5f, -4.2f));
            result.Add(new Vector2(12.5f, 2.8f));
            result.Add(new Vector2(-12.5f, 2.8f));
            return result;
        }

        private static bool TryCreateGroundedPart(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            IReadOnlyList<CityFringeYardPartDescriptor> parts,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            out CityFringeYardPartDescriptor result)
        {
            var footprintProbe = new CityFringeYardPartDescriptor(
                "probe",
                yard.AreaId,
                kind,
                style,
                new Vector3(position.x, 0f, position.z),
                rotation,
                size,
                true);
            Rect footprint = footprintProbe.Footprint;
            if (!ContainsInset(yard.AreaBounds, footprint, EdgeClearance) ||
                OverlapsStrict(
                    footprint,
                    Expand(yard.Access.ApproachBounds, RouteClearance)) ||
                OverlapsStrict(
                    footprint,
                    Expand(yard.TraversalBounds, RouteClearance)))
            {
                result = default;
                return false;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityFringeYardPartDescriptor other = parts[index];
                if (!RequiresSceneClearance(other))
                {
                    continue;
                }

                if (OverlapsStrict(
                        footprint,
                        Expand(other.Footprint, SolidClearance)))
                {
                    result = default;
                    return false;
                }
            }

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            Vector2[] corners =
            {
                new Vector2(footprint.xMin, footprint.yMin),
                new Vector2(footprint.xMin, footprint.yMax),
                new Vector2(footprint.xMax, footprint.yMin),
                new Vector2(footprint.xMax, footprint.yMax),
                footprint.center
            };
            for (int index = 0; index < corners.Length; index++)
            {
                if (!TrySampleOwnerGround(
                        layout,
                        yard,
                        corners[index],
                        out float ground))
                {
                    result = default;
                    return false;
                }

                minimum = Mathf.Min(minimum, ground);
                maximum = Mathf.Max(maximum, ground);
            }

            if (maximum - minimum > MaximumCornerGroundRange)
            {
                result = default;
                return false;
            }

            Vector3 center = new Vector3(
                position.x,
                maximum + GroundLift + size.y * 0.5f,
                position.z);
            result = new CityFringeYardPartDescriptor(
                $"{yard.AreaId}-courtyard-life-{KindSlug(kind)}",
                yard.AreaId,
                kind,
                style,
                center,
                rotation,
                size,
                true);
            return true;
        }

        private static bool RequiresSceneClearance(
            CityFringeYardPartDescriptor part)
        {
            if (part.BlocksMovement)
            {
                return true;
            }

            switch (part.Kind)
            {
                case CityFringeYardPartKind.CulvertHeadwall:
                case CityFringeYardPartKind.RepairFrame:
                case CityFringeYardPartKind.UtilityDoor:
                case CityFringeYardPartKind.PracticalHousing:
                case CityFringeYardPartKind.FloodGauge:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TrySampleOwnerGround(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            Vector2 point,
            out float ground)
        {
            const float tolerance = 0.002f;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (!string.Equals(
                        surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal) ||
                    point.x < bounds.xMin - tolerance ||
                    point.x > bounds.xMax + tolerance ||
                    point.y < bounds.yMin - tolerance ||
                    point.y > bounds.yMax + tolerance)
                {
                    continue;
                }

                ground = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    surface,
                    point);
                return true;
            }

            ground = 0f;
            return false;
        }

        private static bool ContainsInset(
            Rect outer,
            Rect inner,
            float inset)
        {
            return inner.xMin >= outer.xMin + inset &&
                   inner.xMax <= outer.xMax - inset &&
                   inner.yMin >= outer.yMin + inset &&
                   inner.yMax <= outer.yMax - inset;
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            const float epsilon = 0.001f;
            return left.xMin < right.xMax - epsilon &&
                   left.xMax > right.xMin + epsilon &&
                   left.yMin < right.yMax - epsilon &&
                   left.yMax > right.yMin + epsilon;
        }

        private static string KindSlug(CityFringeYardPartKind kind)
        {
            switch (kind)
            {
                case CityFringeYardPartKind.MasonCart:
                    return "mason-cart";
                case CityFringeYardPartKind.WinchServiceSet:
                    return "winch-service";
                case CityFringeYardPartKind.TunnelServiceSet:
                    return "tunnel-service";
                case CityFringeYardPartKind.FloodMaintenanceSet:
                    return "flood-maintenance";
                case CityFringeYardPartKind.OpenHoodCar:
                    return "open-hood-car";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }
    }

    internal static class CityFringeYardLifeValidator
    {
        public const int ExpectedSceneCount = 5;
        private const float GroundTolerance = 0.22f;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityFringeYardPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            int count = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int yardIndex = 0;
                 yardIndex < plan.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor yard = plan.Yards[yardIndex];
                CityFringeYardPartKind expected = ExpectedKind(yard.Kind);
                int yardCount = 0;
                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    CityFringeYardPartDescriptor part =
                        yard.Parts[partIndex];
                    if (!IsLifeKind(part.Kind))
                    {
                        continue;
                    }

                    count++;
                    yardCount++;
                    if (part.Kind != expected ||
                        !part.BlocksMovement ||
                        !ids.Add(part.StableId) ||
                        part.Footprint.Overlaps(yard.Access.ApproachBounds) ||
                        part.Footprint.Overlaps(yard.TraversalBounds))
                    {
                        throw new InvalidOperationException(
                            $"Fringe life scene '{part.StableId}' does not " +
                            $"match its '{yard.Kind}' Yard contract.");
                    }

                    Vector2 sample = new Vector2(
                        part.Center.x,
                        part.Center.z);
                    if (!TrySampleOwnerGround(
                            layout,
                            yard,
                            sample,
                            out float ground) ||
                        Mathf.Abs(
                            part.Center.y - part.Size.y * 0.5f - ground) >
                        GroundTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Fringe life scene '{part.StableId}' is not " +
                            "grounded on its owner Yard.");
                    }
                }

                if (yardCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{yard.AreaId}' requires exactly one " +
                        "human-scale life scene.");
                }
            }

            if (count != ExpectedSceneCount)
            {
                throw new InvalidOperationException(
                    $"The default fringe requires exactly " +
                    $"{ExpectedSceneCount} life scenes.");
            }
        }

        public static bool IsLifeKind(CityFringeYardPartKind kind)
        {
            return kind == CityFringeYardPartKind.MasonCart ||
                   kind == CityFringeYardPartKind.WinchServiceSet ||
                   kind == CityFringeYardPartKind.TunnelServiceSet ||
                   kind == CityFringeYardPartKind.FloodMaintenanceSet ||
                   kind == CityFringeYardPartKind.OpenHoodCar;
        }

        private static CityFringeYardPartKind ExpectedKind(
            CityFringeYardKind kind)
        {
            switch (kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    return CityFringeYardPartKind.MasonCart;
                case CityFringeYardKind.WestIndustrialBelt:
                    return CityFringeYardPartKind.WinchServiceSet;
                case CityFringeYardKind.SouthTunnelForecourt:
                    return CityFringeYardPartKind.TunnelServiceSet;
                case CityFringeYardKind.SouthFloodWorks:
                    return CityFringeYardPartKind.FloodMaintenanceSet;
                case CityFringeYardKind.EastUtilityEdge:
                    return CityFringeYardPartKind.OpenHoodCar;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        private static bool TrySampleOwnerGround(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            Vector2 point,
            out float ground)
        {
            const float tolerance = 0.002f;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                Rect bounds = surface.WorldBounds;
                if (string.Equals(
                        surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal) &&
                    point.x >= bounds.xMin - tolerance &&
                    point.x <= bounds.xMax + tolerance &&
                    point.y >= bounds.yMin - tolerance &&
                    point.y <= bounds.yMax + tolerance)
                {
                    ground = CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        point);
                    return true;
                }
            }

            ground = 0f;
            return false;
        }
    }
}
