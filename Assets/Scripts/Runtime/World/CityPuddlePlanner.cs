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

        private const float MinimumSurfaceSpan = 3.2f;
        private const float GutterInset = 0.28f;

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
