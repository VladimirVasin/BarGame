using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Places a small, stable set of shallow road patches near gutters. Each
    /// patch uses the road texture while dry, so it only separates from the
    /// carriageway when the weather registry raises its smoothness and lowers
    /// its value.
    /// </summary>
    internal static class CityPuddlePlanner
    {
        public const int MaximumPuddleCount = 42;
        public const float Thickness = 0.006f;
        public const float SurfaceOffset = 0.003f;

        /// <summary>How many puddles the flat open precincts may carry
        /// between them. Deliberately small: standing water in a yard
        /// is a place nobody drains, not weather.</summary>
        public const int MaximumOpenGroundPuddleCount = 16;

        private const float MinimumSurfaceSpan = 3.2f;
        private const float GutterInset = 0.28f;

        /// <summary>How far inside its own cell an open-ground puddle
        /// must sit. Only an open precinct's interior is guaranteed
        /// flat: its datum is shared across the whole area, but the
        /// terrain skin ramps toward whatever the neighbouring cell
        /// sits at, and a flat slab on a ramp buries one end.</summary>
        private const float OpenGroundEdgeInset = 4f;

        private const float MinimumOpenGroundSpan = 3f;

        public static IReadOnlyList<RuntimeOrientedBox> Create(
            CityStreetSurfacePlan streetPlan,
            int citySeed)
        {
            if (streetPlan == null)
            {
                throw new ArgumentNullException(nameof(streetPlan));
            }

            var candidates = new List<Candidate>();
            IReadOnlyList<RuntimeOrientedBox> streets =
                streetPlan.StreetGeometry;
            for (int index = 0; index < streets.Count; index++)
            {
                RuntimeOrientedBox surface = streets[index];
                float longest = Mathf.Max(surface.Size.x, surface.Size.z);
                float shortest = Mathf.Min(surface.Size.x, surface.Size.z);
                if (longest < MinimumSurfaceSpan || shortest < 0.7f)
                {
                    continue;
                }

                uint hash = CityExteriorAppearance.Mix(
                    unchecked((uint)citySeed),
                    unchecked((uint)(index + 1)));
                if ((hash % 100u) >= 76u)
                {
                    continue;
                }

                candidates.Add(
                    new Candidate(
                        hash,
                        index * 2,
                        CreatePatch(surface, hash)));
                if (longest >= 16f &&
                    ((hash >> 8) & 1u) != 0u)
                {
                    uint secondHash = CityExteriorAppearance.Mix(
                        hash,
                        0x6D2B79F5u);
                    candidates.Add(
                        new Candidate(
                            secondHash,
                            (index * 2) + 1,
                            CreatePatch(surface, secondHash)));
                }
            }

            candidates.Sort(CompareCandidates);
            int count = Mathf.Min(MaximumPuddleCount, candidates.Count);
            var result = new List<RuntimeOrientedBox>(count);
            for (int index = 0; index < count; index++)
            {
                result.Add(candidates[index].Patch);
            }

            return new ReadOnlyCollection<RuntimeOrientedBox>(result);
        }

        /// <summary>
        /// Standing water on the flat open precincts — the fringe
        /// yards, the cemetery terrace and the church ground. They are
        /// the only ground in the city that is dead level: every cell
        /// of an area declaring a street access is pinned to that one
        /// access datum, so a flat slab lies true on it. Streets get
        /// their own gutter patches from <see cref="Create"/>; the
        /// sloped buildable ground and the beach get none, because a
        /// six-millimetre box cannot follow a five-percent cross-fall.
        /// </summary>
        public static IReadOnlyList<RuntimeOrientedBox> CreateOpenGround(
            CityLayout layout,
            int citySeed)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var flatAreas = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                flatAreas.Add(layout.OpenAreaAccesses[index].AreaId);
            }

            var candidates = new List<Candidate>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!IsLevelOpenGround(surface) ||
                    !flatAreas.Contains(surface.AreaId))
                {
                    continue;
                }

                Rect interior = Inset(
                    surface.WorldBounds,
                    OpenGroundEdgeInset);
                if (interior.width < MinimumOpenGroundSpan ||
                    interior.height < MinimumOpenGroundSpan)
                {
                    continue;
                }

                uint hash = CityExteriorAppearance.Mix(
                    unchecked((uint)citySeed),
                    unchecked((uint)(0x9E3779B9 + index)));
                if ((hash % 100u) >= 34u)
                {
                    continue;
                }

                candidates.Add(
                    new Candidate(
                        hash,
                        index,
                        CreateGroundPatch(
                            interior,
                            surface.PhysicalTopY,
                            hash)));
            }

            candidates.Sort(CompareCandidates);
            int count = Mathf.Min(
                MaximumOpenGroundPuddleCount,
                candidates.Count);
            var result = new List<RuntimeOrientedBox>(count);
            for (int index = 0; index < count; index++)
            {
                result.Add(candidates[index].Patch);
            }

            return new ReadOnlyCollection<RuntimeOrientedBox>(result);
        }

        private static bool IsLevelOpenGround(
            CitySurfaceDescriptor surface)
        {
            if (!surface.IsWalkable || surface.IsWater)
            {
                return false;
            }

            switch (surface.Kind)
            {
                case CitySurfaceKind.OpenGround:
                case CitySurfaceKind.CemeteryGround:
                case CitySurfaceKind.ChurchGround:
                    return true;
                default:
                    // The park keeps its emptiness, the beach slopes to
                    // the waterline, and the buildable ground carries
                    // the valley's cross-fall.
                    return false;
            }
        }

        private static Rect Inset(Rect source, float amount)
        {
            return new Rect(
                source.xMin + amount,
                source.yMin + amount,
                Mathf.Max(0f, source.width - (amount * 2f)),
                Mathf.Max(0f, source.height - (amount * 2f)));
        }

        private static RuntimeOrientedBox CreateGroundPatch(
            Rect area,
            float topY,
            uint hash)
        {
            // Wider and squarer than a gutter patch: nothing channels
            // this water, so it spreads until the ground stops it.
            float sizeX = Mathf.Min(
                Mathf.Lerp(1.3f, 3.3f, Unit(hash, 0)),
                area.width);
            float sizeZ = Mathf.Min(
                Mathf.Lerp(1.1f, 2.6f, Unit(hash, 8)),
                area.height);
            float travelX = Mathf.Max(0f, (area.width - sizeX) * 0.5f);
            float travelZ = Mathf.Max(0f, (area.height - sizeZ) * 0.5f);
            var center = new Vector3(
                area.center.x +
                Mathf.Lerp(-travelX, travelX, Unit(hash, 16)),
                topY + SurfaceOffset,
                area.center.y +
                Mathf.Lerp(-travelZ, travelZ, Unit(hash, 24)));
            return new RuntimeOrientedBox(
                center,
                Quaternion.identity,
                new Vector3(sizeX, Thickness, sizeZ));
        }

        private static RuntimeOrientedBox CreatePatch(
            RuntimeOrientedBox surface,
            uint hash)
        {
            bool longAlongX = surface.Size.x >= surface.Size.z;
            float longSpan = longAlongX
                ? surface.Size.x
                : surface.Size.z;
            float crossSpan = longAlongX
                ? surface.Size.z
                : surface.Size.x;
            float longSize = Mathf.Min(
                Mathf.Lerp(0.9f, 2.45f, Unit(hash, 0)),
                Mathf.Max(0.55f, longSpan - 0.7f));
            float crossSize = Mathf.Min(
                Mathf.Lerp(0.38f, 0.92f, Unit(hash, 8)),
                Mathf.Max(0.28f, crossSpan - 0.55f));

            float longTravel = Mathf.Max(
                0f,
                (longSpan - longSize) * 0.5f - 0.35f);
            float longOffset = Mathf.Lerp(
                -longTravel,
                longTravel,
                Unit(hash, 16));
            float crossTravel = Mathf.Max(
                0f,
                (crossSpan - crossSize) * 0.5f - GutterInset);
            float crossOffset = (((hash >> 27) & 1u) == 0u
                    ? -1f
                    : 1f) *
                crossTravel;

            Vector3 localOffset = longAlongX
                ? new Vector3(longOffset, 0f, crossOffset)
                : new Vector3(crossOffset, 0f, longOffset);
            localOffset.y =
                (surface.Size.y * 0.5f) +
                SurfaceOffset;
            Vector3 size = longAlongX
                ? new Vector3(longSize, Thickness, crossSize)
                : new Vector3(crossSize, Thickness, longSize);
            return new RuntimeOrientedBox(
                surface.Center + surface.Rotation * localOffset,
                surface.Rotation,
                size);
        }

        private static float Unit(uint hash, int shift)
        {
            return ((hash >> shift) & 0xFFu) / 255f;
        }

        private static int CompareCandidates(
            Candidate first,
            Candidate second)
        {
            int hashOrder = first.Rank.CompareTo(second.Rank);
            return hashOrder != 0
                ? hashOrder
                : first.StableOrder.CompareTo(second.StableOrder);
        }

        private readonly struct Candidate
        {
            public Candidate(
                uint rank,
                int stableOrder,
                RuntimeOrientedBox patch)
            {
                Rank = rank;
                StableOrder = stableOrder;
                Patch = patch;
            }

            public uint Rank { get; }
            public int StableOrder { get; }
            public RuntimeOrientedBox Patch { get; }
        }
    }
}
