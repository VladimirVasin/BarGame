using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The authored physical composition that owns a city sound. These are
    /// deliberately semantic owners, not anonymous world-space emitters.
    /// </summary>
    public enum CitySoundPhysicalOwnerKind
    {
        None = 0,
        OldTownWaterworksCourt = 1,
        ResidentialDryingYard = 2,
        IndustrialWeighbridge = 3,
        NightlifeLastRouteIsland = 4,
        ParkFountainAndStatue = 5,
        ParkPlayground = 6,
        Count = 7
    }

    /// <summary>
    /// A positive range belongs to an autonomous scheduled one-shot and is
    /// measured in game minutes (one game minute currently advances per real
    /// second). A zero interval belongs either to a loop or to a one-shot that
    /// is fired only by a real physical action.
    /// </summary>
    public readonly struct CitySoundScheduleInterval
    {
        public CitySoundScheduleInterval(
            float minimumSeconds,
            float maximumSeconds)
        {
            if (!IsFinite(minimumSeconds) ||
                !IsFinite(maximumSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumSeconds),
                    "Sound schedule intervals must be finite.");
            }

            bool none = minimumSeconds == 0f && maximumSeconds == 0f;
            if (!none &&
                (minimumSeconds <= 0f ||
                 maximumSeconds < minimumSeconds))
            {
                throw new ArgumentException(
                    "A scheduled interval needs 0 < minimum <= maximum.");
            }

            MinimumSeconds = minimumSeconds;
            MaximumSeconds = maximumSeconds;
        }

        public static CitySoundScheduleInterval None => default;

        public float MinimumSeconds { get; }
        public float MaximumSeconds { get; }
        public bool IsNone =>
            MinimumSeconds == 0f && MaximumSeconds == 0f;
        public bool IsScheduled => !IsNone;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// One immutable causal sound anchor. The bounds are those of the exact
    /// visible fixture or moving owner used by the runtime, not the district
    /// or lot around it.
    /// </summary>
    public sealed class CitySoundSourceDescriptor
    {
        public CitySoundSourceDescriptor(
            string stableId,
            CityDistrictKind district,
            CitySoundPhysicalOwnerKind physicalOwner,
            CitySourceSoundId cue,
            Vector3 worldPosition,
            Bounds physicalOwnerBounds,
            float audibleRadius,
            CitySourceSoundPlayback playback,
            CitySoundScheduleInterval scheduleInterval)
        {
            StableId = NormalizeStableId(stableId);
            District = district;
            PhysicalOwner = physicalOwner;
            Cue = cue;
            WorldPosition = worldPosition;
            PhysicalOwnerBounds = physicalOwnerBounds;
            AudibleRadius = audibleRadius;
            Playback = playback;
            ScheduleInterval = scheduleInterval;

            CitySoundSourceRules.ValidateOrThrow(this);
        }

        public string StableId { get; }
        public CityDistrictKind District { get; }
        public CitySoundPhysicalOwnerKind PhysicalOwner { get; }

        /// <summary>
        /// The semantic synthesis cue. Together with PhysicalOwner it states
        /// exactly what the player is hearing and what object emits it.
        /// </summary>
        public CitySourceSoundId Cue { get; }

        public Vector3 WorldPosition { get; }
        public Bounds PhysicalOwnerBounds { get; }
        public float AudibleRadius { get; }
        public CitySourceSoundPlayback Playback { get; }
        public CitySoundScheduleInterval ScheduleInterval { get; }
        public bool IsLooping => Playback == CitySourceSoundPlayback.Loop;
        public bool IsOneShot =>
            Playback == CitySourceSoundPlayback.OneShot;
        public bool IsScheduled =>
            IsOneShot && ScheduleInterval.IsScheduled;
        public bool IsTriggered =>
            IsOneShot && ScheduleInterval.IsNone;

        private static string NormalizeStableId(string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A city sound source needs a stable ID.",
                    nameof(stableId));
            }

            string trimmed = stableId.Trim();
            if (!string.Equals(
                    stableId,
                    trimmed,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A city sound stable ID may not have outer whitespace.",
                    nameof(stableId));
            }

            return stableId;
        }
    }

    internal static class CitySoundSourceRules
    {
        private const float AnchorBoundsTolerance = 0.05f;

        public static void ValidateOrThrow(
            CitySoundSourceDescriptor source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            RequireDefinedDistrict(source.District);
            RequireDefinedOwner(source.PhysicalOwner);
            RequireDefinedCue(source.Cue);
            RequireDefinedPlayback(source.Playback);
            RequireFinite(source.WorldPosition, nameof(source.WorldPosition));
            RequireValidBounds(source.PhysicalOwnerBounds);
            RequireFinitePositive(
                source.AudibleRadius,
                nameof(source.AudibleRadius));

            Bounds explainedBounds = source.PhysicalOwnerBounds;
            explainedBounds.Expand(AnchorBoundsTolerance * 2f);
            if (!explainedBounds.Contains(source.WorldPosition))
            {
                throw new ArgumentException(
                    $"Sound source '{source.StableId}' is not anchored to " +
                    "its physical owner's bounds.",
                    nameof(source));
            }

            CityDistrictKind expectedDistrict =
                GetExpectedDistrict(source.PhysicalOwner);
            if (source.District != expectedDistrict)
            {
                throw new ArgumentException(
                    $"Physical owner '{source.PhysicalOwner}' belongs to " +
                    $"{expectedDistrict}, not {source.District}.",
                    nameof(source));
            }

            if (!OwnerExplainsCue(source.PhysicalOwner, source.Cue))
            {
                throw new ArgumentException(
                    $"Physical owner '{source.PhysicalOwner}' does not " +
                    $"explain cue '{source.Cue}'.",
                    nameof(source));
            }

            CitySourceSoundPlayback expectedPlayback =
                CitySourceSoundSynthesis.GetDefinition(source.Cue).Playback;
            if (source.Playback != expectedPlayback)
            {
                throw new ArgumentException(
                    $"Cue '{source.Cue}' requires {expectedPlayback} " +
                    $"playback, not {source.Playback}.",
                    nameof(source));
            }

            if (source.IsLooping && !source.ScheduleInterval.IsNone)
            {
                throw new ArgumentException(
                    "Looping city sounds may not carry a schedule interval.",
                    nameof(source));
            }

            if (source.IsOneShot)
            {
                bool physicalTrigger = RequiresPhysicalTrigger(source.Cue);
                if (physicalTrigger != source.IsTriggered)
                {
                    throw new ArgumentException(
                        physicalTrigger
                            ? $"Cue '{source.Cue}' requires a real physical " +
                              "owner event and may not carry a timer."
                            : $"Autonomous cue '{source.Cue}' requires a " +
                              "positive schedule interval.",
                        nameof(source));
                }
            }
        }

        private static CityDistrictKind GetExpectedDistrict(
            CitySoundPhysicalOwnerKind owner)
        {
            switch (owner)
            {
                case CitySoundPhysicalOwnerKind.OldTownWaterworksCourt:
                    return CityDistrictKind.OldTown;
                case CitySoundPhysicalOwnerKind.ResidentialDryingYard:
                    return CityDistrictKind.Residential;
                case CitySoundPhysicalOwnerKind.IndustrialWeighbridge:
                    return CityDistrictKind.Industrial;
                case CitySoundPhysicalOwnerKind
                    .NightlifeLastRouteIsland:
                    return CityDistrictKind.Nightlife;
                case CitySoundPhysicalOwnerKind.ParkFountainAndStatue:
                case CitySoundPhysicalOwnerKind.ParkPlayground:
                    return CityDistrictKind.CentralPark;
                default:
                    throw new ArgumentOutOfRangeException(nameof(owner));
            }
        }

        private static bool OwnerExplainsCue(
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue)
        {
            switch (owner)
            {
                case CitySoundPhysicalOwnerKind.OldTownWaterworksCourt:
                    return cue == CitySourceSoundId.WaterworksPipeLoop ||
                           cue == CitySourceSoundId.WaterworksDrip;
                case CitySoundPhysicalOwnerKind.ResidentialDryingYard:
                    return cue == CitySourceSoundId.DryingYardClothLoop ||
                           cue == CitySourceSoundId.DryingYardRopeCreak ||
                           cue == CitySourceSoundId.DryingYardCarpetStrike;
                case CitySoundPhysicalOwnerKind.IndustrialWeighbridge:
                    return cue ==
                               CitySourceSoundId
                                   .IndustrialWeighbridgeMechanismLoop ||
                           cue ==
                               CitySourceSoundId.IndustrialMetalStress;
                case CitySoundPhysicalOwnerKind
                    .NightlifeLastRouteIsland:
                    return cue == CitySourceSoundId.LastRouteRelayLoop ||
                           cue ==
                               CitySourceSoundId.LastRouteIncompleteChime;
                case CitySoundPhysicalOwnerKind.ParkFountainAndStatue:
                    return cue == CitySourceSoundId.ParkFountainLoop;
                case CitySoundPhysicalOwnerKind.ParkPlayground:
                    return cue == CitySourceSoundId.ParkSwingCreak;
                default:
                    return false;
            }
        }

        private static bool RequiresPhysicalTrigger(
            CitySourceSoundId cue)
        {
            switch (cue)
            {
                case CitySourceSoundId.DryingYardCarpetStrike:
                case CitySourceSoundId.IndustrialMetalStress:
                case CitySourceSoundId.ParkSwingCreak:
                    return true;
                default:
                    return false;
            }
        }

        internal static CityDistrictKind GetExpectedDistrict(
            CitySourceSoundId cue)
        {
            switch (cue)
            {
                case CitySourceSoundId.WaterworksPipeLoop:
                case CitySourceSoundId.WaterworksDrip:
                    return CityDistrictKind.OldTown;
                case CitySourceSoundId.DryingYardClothLoop:
                case CitySourceSoundId.DryingYardRopeCreak:
                case CitySourceSoundId.DryingYardCarpetStrike:
                    return CityDistrictKind.Residential;
                case CitySourceSoundId
                    .IndustrialWeighbridgeMechanismLoop:
                case CitySourceSoundId.IndustrialMetalStress:
                    return CityDistrictKind.Industrial;
                case CitySourceSoundId.LastRouteRelayLoop:
                case CitySourceSoundId.LastRouteIncompleteChime:
                    return CityDistrictKind.Nightlife;
                case CitySourceSoundId.ParkFountainLoop:
                case CitySourceSoundId.ParkSwingCreak:
                    return CityDistrictKind.CentralPark;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cue));
            }
        }

        private static void RequireDefinedDistrict(CityDistrictKind district)
        {
            if (!Enum.IsDefined(typeof(CityDistrictKind), district))
            {
                throw new ArgumentOutOfRangeException(nameof(district));
            }
        }

        private static void RequireDefinedOwner(
            CitySoundPhysicalOwnerKind owner)
        {
            if (owner <= CitySoundPhysicalOwnerKind.None ||
                owner >= CitySoundPhysicalOwnerKind.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(owner));
            }
        }

        private static void RequireDefinedCue(CitySourceSoundId cue)
        {
            if (cue <= CitySourceSoundId.None ||
                cue >= CitySourceSoundId.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(cue));
            }
        }

        private static void RequireDefinedPlayback(
            CitySourceSoundPlayback playback)
        {
            if (!Enum.IsDefined(
                    typeof(CitySourceSoundPlayback),
                    playback))
            {
                throw new ArgumentOutOfRangeException(nameof(playback));
            }
        }

        private static void RequireValidBounds(Bounds bounds)
        {
            RequireFinite(bounds.center, nameof(bounds));
            Vector3 size = bounds.size;
            RequireFinite(size, nameof(bounds));
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                throw new ArgumentException(
                    "A sound's physical owner needs positive 3D bounds.",
                    nameof(bounds));
            }
        }

        private static void RequireFinite(Vector3 value, string name)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireFinitePositive(float value, string name)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
