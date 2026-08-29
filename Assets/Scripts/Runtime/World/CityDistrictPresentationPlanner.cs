using System;

namespace BarPromenade
{
    /// <summary>
    /// Pure deterministic catalog and block planner for the four urban
    /// districts. Facade windows consume the window channel now; the other
    /// channels remain stable inputs for later world-builder passes.
    /// </summary>
    public static class CityDistrictPresentationPlanner
    {
        /// <summary>
        /// Only a boundary-touching block blends. At the current 26-metre
        /// city grid this is the art bible's one-block transition band.
        /// </summary>
        public const int TransitionBlockSpan = 1;

        private const uint FrontageSalt = 0x44504652u; // "DPFR"
        private const uint MassSalt = 0x44504D53u; // "DPMS"
        private const uint WindowSalt = 0x4450574Eu; // "DPWN"
        private const uint LightSalt = 0x44504C54u; // "DPLT"
        private const uint WearSalt = 0x44505752u; // "DPWR"
        private const uint TransitionSalt = 0x44505452u; // "DPTR"

        private static readonly CityDistrictArtProfile OldTown =
            new CityDistrictArtProfile(
                "old-town",
                CityDistrictKind.OldTown,
                new CityDistrictFrontageProfile(
                    CityDistrictFrontageFamily.NarrowLayered,
                    0.72f,
                    0.64f,
                    0.78f),
                new CityDistrictMassProfile(
                    CityDistrictMassFamily.FragmentedPerimeter,
                    0.92f,
                    0.99f,
                    0.28f,
                    0.72f,
                    0.78f),
                new CityDistrictWindowProfile(
                    CityDistrictWindowFamily.NarrowIrregular,
                    0.25f),
                new CityDistrictLightProfile(
                    CityDistrictLightFamily.BrokenAmberPools,
                    0.38f,
                    0.84f,
                    0.62f,
                    0.04f),
                new CityDistrictWearProfile(
                    CityDistrictWearFamily.SootWaterAndPatch,
                    0.82f,
                    0.84f,
                    0.66f),
                new CityDistrictTransitionProfile(
                    CityDistrictNeighbourSet.Residential |
                    CityDistrictNeighbourSet.Industrial,
                    0.18f,
                    0.32f,
                    0.14f));

        private static readonly CityDistrictArtProfile Residential =
            new CityDistrictArtProfile(
                "residential",
                CityDistrictKind.Residential,
                new CityDistrictFrontageProfile(
                    CityDistrictFrontageFamily.DomesticBalcony,
                    0.78f,
                    0.42f,
                    0.52f),
                new CityDistrictMassProfile(
                    CityDistrictMassFamily.SetbackCourtyard,
                    0.76f,
                    0.90f,
                    0.18f,
                    0.58f,
                    0.38f),
                new CityDistrictWindowProfile(
                    CityDistrictWindowFamily.DomesticRows,
                    0.42f),
                new CityDistrictLightProfile(
                    CityDistrictLightFamily.DomesticWindowPools,
                    0.46f,
                    0.70f,
                    0.42f,
                    0.03f),
                new CityDistrictWearProfile(
                    CityDistrictWearFamily.RepairAndPersonalUse,
                    0.58f,
                    0.55f,
                    0.78f),
                new CityDistrictTransitionProfile(
                    CityDistrictNeighbourSet.OldTown |
                    CityDistrictNeighbourSet.Nightlife,
                    0.18f,
                    0.34f,
                    0.14f));

        private static readonly CityDistrictArtProfile Industrial =
            new CityDistrictArtProfile(
                "industrial",
                CityDistrictKind.Industrial,
                new CityDistrictFrontageProfile(
                    CityDistrictFrontageFamily.ProcessGate,
                    0.42f,
                    0.70f,
                    0.34f),
                new CityDistrictMassProfile(
                    CityDistrictMassFamily.LowWideProcess,
                    0.92f,
                    0.99f,
                    0f,
                    0.32f,
                    0.30f),
                new CityDistrictWindowProfile(
                    CityDistrictWindowFamily.SparseUtility,
                    0.14f),
                new CityDistrictLightProfile(
                    CityDistrictLightFamily.SparseTaskPools,
                    0.24f,
                    0.18f,
                    0.76f,
                    0.02f),
                new CityDistrictWearProfile(
                    CityDistrictWearFamily.RustAndProcess,
                    0.86f,
                    0.76f,
                    0.40f),
                new CityDistrictTransitionProfile(
                    CityDistrictNeighbourSet.OldTown |
                    CityDistrictNeighbourSet.Nightlife,
                    0.16f,
                    0.30f,
                    0.16f));

        private static readonly CityDistrictArtProfile Nightlife =
            new CityDistrictArtProfile(
                "nightlife",
                CityDistrictKind.Nightlife,
                new CityDistrictFrontageProfile(
                    CityDistrictFrontageFamily.ActiveGroundFloor,
                    0.90f,
                    0.48f,
                    0.82f),
                new CityDistrictMassProfile(
                    CityDistrictMassFamily.TallDense,
                    0.84f,
                    0.96f,
                    0.56f,
                    1f,
                    0.70f),
                new CityDistrictWindowProfile(
                    CityDistrictWindowFamily
                        .CommercialBaseResidentialRows,
                    0.24f),
                new CityDistrictLightProfile(
                    CityDistrictLightFamily.ThresholdSignals,
                    0.48f,
                    0.28f,
                    0.52f,
                    0.72f),
                new CityDistrictWearProfile(
                    CityDistrictWearFamily.SignageAndRunoff,
                    0.74f,
                    0.68f,
                    0.52f),
                new CityDistrictTransitionProfile(
                    CityDistrictNeighbourSet.Residential |
                    CityDistrictNeighbourSet.Industrial,
                    0.18f,
                    0.32f,
                    0.16f));

        public static CityDistrictArtProfile GetProfile(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return OldTown;
                case CityDistrictKind.Residential:
                    return Residential;
                case CityDistrictKind.Industrial:
                    return Industrial;
                case CityDistrictKind.Nightlife:
                    return Nightlife;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        "Only the four urban districts have art profiles.");
            }
        }

        public static bool CanTransition(
            CityDistrictKind first,
            CityDistrictKind second)
        {
            if (first == second ||
                !TryGetProfile(first, out CityDistrictArtProfile firstProfile) ||
                !TryGetProfile(
                    second,
                    out CityDistrictArtProfile secondProfile))
            {
                return false;
            }

            return firstProfile.Transition.Allows(second) &&
                   secondProfile.Transition.Allows(first);
        }

        public static CityDistrictPresentationPlan Create(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictKind dominantDistrict)
        {
            return CreateCore(
                citySeed,
                blockX,
                blockZ,
                dominantDistrict,
                null,
                TransitionBlockSpan);
        }

        /// <summary>
        /// Resolves only the key needed by the per-pane facade path. Keeping
        /// this focused avoids allocating a full presentation plan for every
        /// window while remaining bit-identical to <see cref="Create"/>.
        /// </summary>
        public static uint ResolveWindowVariationKey(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictKind district)
        {
            GetProfile(district);
            return StableHash(
                citySeed,
                blockX,
                blockZ,
                district,
                district,
                WindowSalt);
        }

        /// <summary>
        /// Creates a presentation decision for one block. Pass zero for a
        /// boundary-touching block; one or more for an interior block.
        /// </summary>
        public static CityDistrictPresentationPlan Create(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictKind dominantDistrict,
            CityDistrictKind neighbourDistrict,
            int boundaryDistanceBlocks)
        {
            if (boundaryDistanceBlocks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(boundaryDistanceBlocks));
            }

            if (!CanTransition(dominantDistrict, neighbourDistrict))
            {
                throw new ArgumentException(
                    $"Districts '{dominantDistrict}' and " +
                    $"'{neighbourDistrict}' do not share a direct " +
                    "presentation transition.",
                    nameof(neighbourDistrict));
            }

            return CreateCore(
                citySeed,
                blockX,
                blockZ,
                dominantDistrict,
                neighbourDistrict,
                boundaryDistanceBlocks);
        }

        private static CityDistrictPresentationPlan CreateCore(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictKind dominantDistrict,
            CityDistrictKind? neighbourDistrict,
            int boundaryDistanceBlocks)
        {
            CityDistrictArtProfile dominant =
                GetProfile(dominantDistrict);
            CityDistrictArtProfile neighbour = neighbourDistrict.HasValue
                ? GetProfile(neighbourDistrict.Value)
                : null;
            bool transitionActive =
                neighbour != null &&
                boundaryDistanceBlocks < TransitionBlockSpan;

            CityDistrictTransitionMotif motif =
                CityDistrictTransitionMotif.None;
            float motifInfluence = 0f;
            float lightInfluence = 0f;
            if (transitionActive)
            {
                uint transitionHash = StableHash(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    neighbourDistrict.Value,
                    TransitionSalt);
                motif = (CityDistrictTransitionMotif)(
                    1 + (transitionHash % 3u));
                float unit = ((transitionHash >> 8) & 0xFFFFu) /
                             65535f;
                motifInfluence = Lerp(
                    dominant.Transition.MinimumMotifInfluence,
                    dominant.Transition.MaximumMotifInfluence,
                    unit);
                lightInfluence = Clamp01(
                    motifInfluence +
                    dominant.Transition.LightBlendBias);
            }

            float frontageInfluence =
                motif == CityDistrictTransitionMotif.Frontage
                    ? motifInfluence
                    : 0f;
            float windowInfluence =
                motif == CityDistrictTransitionMotif.Window
                    ? motifInfluence
                    : 0f;
            float wearInfluence =
                motif == CityDistrictTransitionMotif.Wear
                    ? motifInfluence
                    : 0f;

            return new CityDistrictPresentationPlan(
                citySeed,
                blockX,
                blockZ,
                dominant,
                neighbour,
                CreateChannel(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    FrontageSalt,
                    frontageInfluence),
                CreateChannel(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    MassSalt,
                    0f),
                CreateChannel(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    WindowSalt,
                    windowInfluence),
                CreateChannel(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    LightSalt,
                    lightInfluence),
                CreateChannel(
                    citySeed,
                    blockX,
                    blockZ,
                    dominantDistrict,
                    WearSalt,
                    wearInfluence),
                new CityDistrictTransitionPresentation(
                    boundaryDistanceBlocks,
                    TransitionBlockSpan,
                    neighbourDistrict,
                    motif,
                    motifInfluence,
                    lightInfluence));
        }

        private static CityDistrictChannelPresentation CreateChannel(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictKind district,
            uint salt,
            float neighbourInfluence)
        {
            return new CityDistrictChannelPresentation(
                StableHash(
                    citySeed,
                    blockX,
                    blockZ,
                    district,
                    district,
                    salt),
                neighbourInfluence);
        }

        private static bool TryGetProfile(
            CityDistrictKind district,
            out CityDistrictArtProfile profile)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    profile = OldTown;
                    return true;
                case CityDistrictKind.Residential:
                    profile = Residential;
                    return true;
                case CityDistrictKind.Industrial:
                    profile = Industrial;
                    return true;
                case CityDistrictKind.Nightlife:
                    profile = Nightlife;
                    return true;
                default:
                    profile = null;
                    return false;
            }
        }

        private static float Lerp(float first, float second, float amount)
        {
            return first + ((second - first) * amount);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        private static uint StableHash(
            int seed,
            int blockX,
            int blockZ,
            CityDistrictKind dominant,
            CityDistrictKind neighbour,
            uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ salt;
                value = (value ^ (uint)blockX) * 16777619u;
                value = (value ^ (uint)blockZ) * 16777619u;
                value = (value ^ (uint)dominant) * 16777619u;
                value = (value ^ (uint)neighbour) * 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }
}
