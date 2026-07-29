using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class BarNpcPlanner
    {
        public const int TargetNpcCount = 12;
        public const int MaximumNpcCount = 14;
        public const int MaximumMobileNpcCount = 2;
        public const float DefaultRouteLength = 2.2f;

        private const uint AnchorRankSalt = 0x414E4348u;
        private const uint DefinitionSalt = 0x4445464Eu;
        private const uint ScaleSalt = 0x5343414Cu;

        public static BarNpcPlan Create(
            BarInteriorLayoutPlan layout,
            int desiredCount = TargetNpcCount)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return Create(
                layout.CitySeed,
                layout.BarId,
                layout.Activity,
                layout.NpcAnchors,
                desiredCount);
        }

        public static BarNpcPlan Create(
            int citySeed,
            string barId,
            BarActivityKind activity,
            IReadOnlyList<BarNpcAnchor> anchors,
            int desiredCount = TargetNpcCount)
        {
            return Create(
                citySeed,
                barId,
                activity,
                anchors,
                Array.Empty<BarNpcRoute>(),
                desiredCount);
        }

        public static BarNpcPlan Create(
            int citySeed,
            string barId,
            BarActivityKind activity,
            IReadOnlyList<BarNpcAnchor> anchors,
            IReadOnlyList<BarNpcRoute> routes,
            int desiredCount = TargetNpcCount)
        {
            if (string.IsNullOrWhiteSpace(barId))
            {
                throw new ArgumentException(
                    "A stable bar ID is required.",
                    nameof(barId));
            }

            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            if (routes == null)
            {
                throw new ArgumentNullException(nameof(routes));
            }

            string stableBarId = barId.Trim();
            int boundedDesiredCount = Math.Max(
                0,
                Math.Min(MaximumNpcCount, desiredCount));
            uint stableSeed = BarNpcStableHash.Combine(
                BarNpcStableHash.Combine(
                    unchecked((uint)citySeed),
                    BarNpcStableHash.String(stableBarId)),
                unchecked((uint)activity));
            List<BarNpcAnchor> candidates = CopyAndValidateAnchors(
                anchors);
            Dictionary<string, Vector3> routeEnds =
                CopyAndValidateRoutes(routes);
            candidates.Sort((left, right) => CompareByStableRank(
                stableSeed,
                left,
                right));

            int count = Math.Min(
                boundedDesiredCount,
                candidates.Count);
            var selected = new List<BarNpcAnchor>(count);
            AddRoleUpTo(
                candidates,
                selected,
                BarNpcRole.Bartender,
                2,
                count);
            AddRoleUpTo(
                candidates,
                selected,
                BarNpcRole.Walker,
                MaximumMobileNpcCount,
                count);
            AddRoleUpTo(
                candidates,
                selected,
                BarNpcRole.Performer,
                1,
                count);

            for (int index = 0;
                 index < candidates.Count &&
                 selected.Count < count;
                 index++)
            {
                BarNpcAnchor candidate = candidates[index];
                if (!ContainsAnchor(selected, candidate.Id))
                {
                    selected.Add(candidate);
                }
            }

            List<BarNpcDefinition> definitions =
                CreateDefinitions(
                    stableSeed,
                    selected,
                    routeEnds);
            definitions.Sort(CompareDefinitions);
            return new BarNpcPlan(
                citySeed,
                stableSeed,
                stableBarId,
                activity,
                boundedDesiredCount,
                definitions);
        }

        private static List<BarNpcAnchor> CopyAndValidateAnchors(
            IReadOnlyList<BarNpcAnchor> anchors)
        {
            var copy = new List<BarNpcAnchor>(anchors.Count);
            var ids = new HashSet<string>(
                StringComparer.Ordinal);
            for (int index = 0; index < anchors.Count; index++)
            {
                BarNpcAnchor anchor = anchors[index];
                if (string.IsNullOrWhiteSpace(anchor.Id))
                {
                    throw new ArgumentException(
                        "Every NPC anchor requires an ID.",
                        nameof(anchors));
                }

                if (!IsFinite(anchor.Position) ||
                    float.IsNaN(anchor.YawDegrees) ||
                    float.IsInfinity(anchor.YawDegrees) ||
                    float.IsNaN(anchor.AnimationPhase) ||
                    float.IsInfinity(anchor.AnimationPhase))
                {
                    throw new ArgumentException(
                        $"NPC anchor '{anchor.Id}' contains non-finite data.",
                        nameof(anchors));
                }

                if (!ids.Add(anchor.Id))
                {
                    throw new ArgumentException(
                        $"NPC anchor ID '{anchor.Id}' is duplicated.",
                        nameof(anchors));
                }

                copy.Add(anchor);
            }

            return copy;
        }

        private static Dictionary<string, Vector3>
            CopyAndValidateRoutes(
                IReadOnlyList<BarNpcRoute> routes)
        {
            var routeEnds = new Dictionary<string, Vector3>(
                routes.Count,
                StringComparer.Ordinal);
            for (int index = 0; index < routes.Count; index++)
            {
                BarNpcRoute route = routes[index];
                if (string.IsNullOrWhiteSpace(route.Id) ||
                    !routeEnds.TryAdd(
                        route.Id,
                        route.EndPosition))
                {
                    throw new ArgumentException(
                        "Bar NPC route IDs must be non-empty and unique.",
                        nameof(routes));
                }
            }

            return routeEnds;
        }

        private static List<BarNpcDefinition> CreateDefinitions(
            uint stableSeed,
            IReadOnlyList<BarNpcAnchor> anchors,
            IReadOnlyDictionary<string, Vector3> routeEnds)
        {
            var definitions =
                new List<BarNpcDefinition>(anchors.Count);
            int mobileCount = 0;
            for (int index = 0; index < anchors.Count; index++)
            {
                BarNpcAnchor anchor = anchors[index];
                Vector3 forward =
                    Quaternion.Euler(
                        0f,
                        anchor.YawDegrees,
                        0f) *
                    Vector3.forward;
                bool mobile =
                    anchor.IsMobile &&
                    mobileCount < MaximumMobileNpcCount;
                Vector3 routeEnd = anchor.Position;
                if (mobile)
                {
                    routeEnd = ResolveRouteEnd(
                        anchor,
                        forward,
                        routeEnds);
                    mobile =
                        (routeEnd - anchor.Position)
                        .sqrMagnitude > 0.01f;
                    if (mobile)
                    {
                        mobileCount++;
                    }
                }

                uint anchorSeed = BarNpcStableHash.Combine(
                    stableSeed,
                    BarNpcStableHash.String(anchor.Id));
                uint behaviorSeed = BarNpcStableHash.Combine(
                    anchorSeed,
                    DefinitionSalt);
                int visualVariant = PositiveModulo(
                    anchor.VisualVariant,
                    BarNpcSpriteLibrary.VariantCount);
                float phase = Mathf.Repeat(
                    anchor.AnimationPhase,
                    1f);
                float scale = anchor.Role ==
                              BarNpcRole.Bartender
                    ? 1f
                    : 0.95f +
                      BarNpcStableHash.ToUnitFloat(
                          BarNpcStableHash.Combine(
                              anchorSeed,
                              ScaleSalt)) *
                      0.1f;

                definitions.Add(new BarNpcDefinition(
                    $"bar-npc:{anchor.Id}",
                    anchor,
                    forward,
                    routeEnd,
                    mobile,
                    visualVariant,
                    behaviorSeed,
                    phase,
                    scale));
            }

            return definitions;
        }

        private static Vector3 ResolveRouteEnd(
            BarNpcAnchor anchor,
            Vector3 forward,
            IReadOnlyDictionary<string, Vector3> routeEnds)
        {
            if (!string.IsNullOrWhiteSpace(anchor.RouteId) &&
                routeEnds.TryGetValue(
                    anchor.RouteId,
                    out Vector3 explicitEnd))
            {
                return explicitEnd;
            }

            return anchor.Position +
                   forward * DefaultRouteLength;
        }

        private static void AddRoleUpTo(
            IReadOnlyList<BarNpcAnchor> candidates,
            ICollection<BarNpcAnchor> selected,
            BarNpcRole role,
            int maximumForRole,
            int totalLimit)
        {
            int added = 0;
            for (int index = 0;
                 index < candidates.Count &&
                 selected.Count < totalLimit &&
                 added < maximumForRole;
                 index++)
            {
                BarNpcAnchor candidate = candidates[index];
                if (candidate.Role != role ||
                    ContainsAnchor(selected, candidate.Id))
                {
                    continue;
                }

                selected.Add(candidate);
                added++;
            }
        }

        private static bool ContainsAnchor(
            IEnumerable<BarNpcAnchor> anchors,
            string id)
        {
            foreach (BarNpcAnchor anchor in anchors)
            {
                if (string.Equals(
                        anchor.Id,
                        id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareByStableRank(
            uint stableSeed,
            BarNpcAnchor left,
            BarNpcAnchor right)
        {
            uint leftRank = GetAnchorRank(stableSeed, left.Id);
            uint rightRank = GetAnchorRank(stableSeed, right.Id);
            int rankComparison = leftRank.CompareTo(rightRank);
            return rankComparison != 0
                ? rankComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        private static uint GetAnchorRank(
            uint stableSeed,
            string anchorId)
        {
            return BarNpcStableHash.Combine(
                BarNpcStableHash.Combine(
                    stableSeed,
                    BarNpcStableHash.String(anchorId)),
                AnchorRankSalt);
        }

        private static int CompareDefinitions(
            BarNpcDefinition left,
            BarNpcDefinition right)
        {
            return string.CompareOrdinal(left.Id, right.Id);
        }

        private static int PositiveModulo(
            int value,
            int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    internal static class BarNpcStableHash
    {
        public static uint String(string value)
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                hash ^= (byte)character;
                hash *= 16777619u;
                hash ^= (byte)(character >> 8);
                hash *= 16777619u;
            }

            return hash == 0u ? 0xA341316Cu : hash;
        }

        public static uint Combine(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu +
                    (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        public static float ToUnitFloat(uint hash)
        {
            return (hash >> 8) * (1f / 16777216f);
        }
    }
}
