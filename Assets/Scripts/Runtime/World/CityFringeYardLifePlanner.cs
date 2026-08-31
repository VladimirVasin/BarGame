using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adds one small, grounded mason cart to the already-authored west stone
    /// terraces. The landmark pass owns the civil works; the other typed Yards
    /// deliberately receive no separate human-scale vignette.
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

                if (yard.Kind == CityFringeYardKind.WestStoneTerraces)
                {
                    parts.Add(CreateMasonCart(layout, yard, parts));
                }
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

        private static CityFringeYardPartDescriptor CreateMasonCart(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            IReadOnlyList<CityFringeYardPartDescriptor> parts)
        {
            var size = new Vector3(3.0f, 1.65f, 2.2f);

            Vector3 outward = yard.Access.OutwardNormal;
            outward.y = 0f;
            outward.Normalize();
            Vector3 tangent = Vector3.Cross(Vector3.up, outward).normalized;
            Quaternion rotation = Quaternion.LookRotation(
                -outward,
                Vector3.up);
            Vector3 anchor = ResolveSemanticAnchor(yard, parts);
            List<Vector2> offsets = CreateCandidateOffsets();

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
                        CityFringeYardPartKind.MasonCart,
                        CityFringeYardStyle.Timber,
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
            float[] depths = { 7f, 10f, 13f, 16f, 19f };
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
                            CityFringeYardPartKind.MasonCart,
                            CityFringeYardStyle.Timber,
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
                "for its mason cart.");
        }

        private static Vector3 ResolveSemanticAnchor(
            CityFringeYardDescriptor yard,
            IReadOnlyList<CityFringeYardPartDescriptor> parts)
        {
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].StableId.IndexOf(
                        "landmark-culvert-mouth",
                        StringComparison.Ordinal) >= 0)
                {
                    return parts[index].Center;
                }
            }

            return yard.Access.Center + yard.Access.OutwardNormal * 10f;
        }

        private static List<Vector2> CreateCandidateOffsets()
        {
            var result = new List<Vector2>(8);
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
                $"{yard.AreaId}-courtyard-life-mason-cart",
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

    }

    internal static class CityFringeYardLifeValidator
    {
        public const int ExpectedSceneCount = 1;
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
                bool expectsCart =
                    yard.Kind == CityFringeYardKind.WestStoneTerraces;
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
                    if (!expectsCart ||
                        part.Kind != CityFringeYardPartKind.MasonCart ||
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

                int expectedYardCount = expectsCart ? 1 : 0;
                if (yardCount != expectedYardCount)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{yard.AreaId}' requires exactly " +
                        $"{expectedYardCount} mason-cart scenes.");
                }
            }

            if (count != ExpectedSceneCount)
            {
                throw new InvalidOperationException(
                    "The default fringe requires exactly one mason cart.");
            }
        }

        public static bool IsLifeKind(CityFringeYardPartKind kind)
        {
            return kind == CityFringeYardPartKind.MasonCart;
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
