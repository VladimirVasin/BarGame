using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    /// <summary>
    /// A district's permissible causal palette. It never owns positions and
    /// can therefore only filter physical descriptors supplied by the world.
    /// </summary>
    public sealed class CitySoundDistrictProfile
    {
        private readonly HashSet<CitySourceSoundId> cueSet;

        public CitySoundDistrictProfile(
            string stableId,
            CityDistrictKind district,
            IEnumerable<CitySourceSoundId> allowedCues)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A city sound district profile needs a stable ID.",
                    nameof(stableId));
            }

            if (!string.Equals(
                    stableId,
                    stableId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A profile stable ID may not have outer whitespace.",
                    nameof(stableId));
            }

            if (!Enum.IsDefined(typeof(CityDistrictKind), district))
            {
                throw new ArgumentOutOfRangeException(nameof(district));
            }

            if (allowedCues == null)
            {
                throw new ArgumentNullException(nameof(allowedCues));
            }

            var cues = new List<CitySourceSoundId>();
            cueSet = new HashSet<CitySourceSoundId>();
            foreach (CitySourceSoundId cue in allowedCues)
            {
                if (cue <= CitySourceSoundId.None ||
                    cue >= CitySourceSoundId.Count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(allowedCues),
                        cue,
                        "Profiles may contain only playable city cues.");
                }

                if (CitySoundSourceRules.GetExpectedDistrict(cue) != district)
                {
                    throw new ArgumentException(
                        $"Cue '{cue}' does not belong to {district}.",
                        nameof(allowedCues));
                }

                if (!cueSet.Add(cue))
                {
                    throw new ArgumentException(
                        $"Profile '{stableId}' repeats cue '{cue}'.",
                        nameof(allowedCues));
                }

                cues.Add(cue);
            }

            cues.Sort((first, second) =>
                ((int)first).CompareTo((int)second));
            StableId = stableId;
            District = district;
            AllowedCues = new ReadOnlyCollection<CitySourceSoundId>(cues);
        }

        public string StableId { get; }
        public CityDistrictKind District { get; }
        public IReadOnlyList<CitySourceSoundId> AllowedCues { get; }

        public bool Allows(CitySoundSourceDescriptor source)
        {
            return source != null &&
                   source.District == District &&
                   cueSet.Contains(source.Cue);
        }
    }

    /// <summary>
    /// Canonical district palettes. Empty profiles are deliberate silence;
    /// no profile manufactures an emitter to fill a perceived gap.
    /// </summary>
    public static class CitySoundDistrictProfiles
    {
        private static readonly CitySoundDistrictProfile OldTown =
            Create(
                "city.sound.old-town",
                CityDistrictKind.OldTown,
                CitySourceSoundId.WaterworksPipeLoop,
                CitySourceSoundId.WaterworksDrip);

        private static readonly CitySoundDistrictProfile Residential =
            Create(
                "city.sound.residential",
                CityDistrictKind.Residential,
                CitySourceSoundId.DryingYardClothLoop,
                CitySourceSoundId.DryingYardRopeCreak,
                CitySourceSoundId.DryingYardCarpetStrike);

        private static readonly CitySoundDistrictProfile Industrial =
            Create(
                "city.sound.industrial",
                CityDistrictKind.Industrial,
                CitySourceSoundId.IndustrialWeighbridgeMechanismLoop,
                CitySourceSoundId.IndustrialMetalStress);

        private static readonly CitySoundDistrictProfile Nightlife =
            Create(
                "city.sound.nightlife",
                CityDistrictKind.Nightlife,
                CitySourceSoundId.LastRouteRelayLoop,
                CitySourceSoundId.LastRouteIncompleteChime);

        private static readonly CitySoundDistrictProfile CentralPark =
            Create(
                "city.sound.central-park",
                CityDistrictKind.CentralPark,
                CitySourceSoundId.ParkFountainLoop,
                CitySourceSoundId.ParkSwingCreak);

        private static readonly CitySoundDistrictProfile NorthWaterfront =
            Create(
                "city.sound.north-waterfront",
                CityDistrictKind.NorthWaterfront);

        private static readonly CitySoundDistrictProfile Cemetery =
            Create(
                "city.sound.cemetery",
                CityDistrictKind.Cemetery);

        private static readonly CitySoundDistrictProfile Yard =
            Create("city.sound.yard", CityDistrictKind.Yard);

        private static readonly CitySoundDistrictProfile Church =
            Create("city.sound.church", CityDistrictKind.Church);

        public static CitySoundDistrictProfile Get(
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
                case CityDistrictKind.CentralPark:
                    return CentralPark;
                case CityDistrictKind.NorthWaterfront:
                    return NorthWaterfront;
                case CityDistrictKind.Cemetery:
                    return Cemetery;
                case CityDistrictKind.Yard:
                    return Yard;
                case CityDistrictKind.Church:
                    return Church;
                default:
                    throw new ArgumentOutOfRangeException(nameof(district));
            }
        }

        private static CitySoundDistrictProfile Create(
            string stableId,
            CityDistrictKind district,
            params CitySourceSoundId[] cues)
        {
            return new CitySoundDistrictProfile(
                stableId,
                district,
                cues ?? Array.Empty<CitySourceSoundId>());
        }
    }

    /// <summary>
    /// Immutable, stable-ID ordered city sound data ready for a runtime
    /// director. Full plans have no Profile; district views retain the exact
    /// descriptor instances selected from their source plan.
    /// </summary>
    public sealed class CitySoundscapePlan
    {
        private readonly Dictionary<string, CitySoundSourceDescriptor>
            byStableId;

        internal CitySoundscapePlan(
            int citySeed,
            IList<CitySoundSourceDescriptor> sources,
            CitySoundDistrictProfile profile)
        {
            CitySeed = citySeed;
            Profile = profile;

            var all = new List<CitySoundSourceDescriptor>(
                sources ??
                throw new ArgumentNullException(nameof(sources)));
            var loops = new List<CitySoundSourceDescriptor>();
            var scheduled = new List<CitySoundSourceDescriptor>();
            var triggered = new List<CitySoundSourceDescriptor>();
            byStableId = new Dictionary<string, CitySoundSourceDescriptor>(
                StringComparer.Ordinal);

            for (int index = 0; index < all.Count; index++)
            {
                CitySoundSourceDescriptor source = all[index];
                byStableId.Add(source.StableId, source);
                if (source.IsLooping)
                {
                    loops.Add(source);
                }
                else if (source.IsScheduled)
                {
                    scheduled.Add(source);
                }
                else
                {
                    triggered.Add(source);
                }
            }

            Sources = new ReadOnlyCollection<CitySoundSourceDescriptor>(all);
            LoopingSources =
                new ReadOnlyCollection<CitySoundSourceDescriptor>(loops);
            ScheduledSources =
                new ReadOnlyCollection<CitySoundSourceDescriptor>(scheduled);
            TriggeredSources =
                new ReadOnlyCollection<CitySoundSourceDescriptor>(triggered);
        }

        public int CitySeed { get; }
        public CitySoundDistrictProfile Profile { get; }
        public IReadOnlyList<CitySoundSourceDescriptor> Sources { get; }
        public IReadOnlyList<CitySoundSourceDescriptor> LoopingSources { get; }
        public IReadOnlyList<CitySoundSourceDescriptor> ScheduledSources
        {
            get;
        }
        public IReadOnlyList<CitySoundSourceDescriptor> TriggeredSources
        {
            get;
        }

        public bool IsDistrictView => Profile != null;

        public bool TryGetSource(
            string stableId,
            out CitySoundSourceDescriptor source)
        {
            return byStableId.TryGetValue(
                stableId ?? string.Empty,
                out source);
        }

        public CitySoundSourceDescriptor GetRequiredSource(string stableId)
        {
            if (!TryGetSource(stableId, out CitySoundSourceDescriptor source))
            {
                throw new KeyNotFoundException(
                    $"City sound source '{stableId}' is not in the plan.");
            }

            return source;
        }
    }
}
