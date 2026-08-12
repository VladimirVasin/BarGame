using System;

namespace BarPromenade
{
    /// <summary>
    /// Population and cadence knobs for one pedestrian runtime. City and the
    /// Home balcony share the same director and graph contract but hold very
    /// different anchor counts, so each declares its own profile instead of
    /// reading a single global constant.
    /// </summary>
    public sealed class CityPedestrianPopulationProfile
    {
        public const int DefaultDaytimePopulation = 8;
        public const int DefaultNightPopulation = 3;
        public const int DefaultPoolSize = 13;
        public const int DefaultSpawnsPerEvent = 2;
        public const float DefaultPeerSeparation = 12f;
        public const int DefaultWalkersPerStreetLane = 2;
        public const int DefaultApproachGuidedPopulation = 2;

        public static readonly CityPedestrianPopulationProfile City =
            new CityPedestrianPopulationProfile(
                "city",
                DefaultDaytimePopulation,
                DefaultNightPopulation,
                DefaultPoolSize,
                DefaultSpawnsPerEvent,
                DefaultPeerSeparation,
                DefaultWalkersPerStreetLane,
                DefaultApproachGuidedPopulation,
                false);

        // The balcony reconstruction keeps a far smaller anchor set than the
        // full city, and its director is enabled only while the Balcony shot
        // is live, so it fills without waiting out a first-event delay.
        public static readonly CityPedestrianPopulationProfile HomeBalcony =
            new CityPedestrianPopulationProfile(
                "home_balcony",
                5,
                2,
                8,
                2,
                8f,
                2,
                2,
                true);

        public CityPedestrianPopulationProfile(
            string id,
            int daytimePopulation,
            int nightPopulation,
            int poolSize,
            int maximumSpawnsPerEvent,
            float minimumPeerSeparation,
            int maximumWalkersPerStreetLane,
            int approachGuidedPopulation,
            bool fillsImmediately)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A pedestrian population profile requires an ID.",
                    nameof(id));
            }

            if (daytimePopulation <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(daytimePopulation),
                    "A profile must allow at least one daytime walker.");
            }

            if (nightPopulation <= 0 ||
                nightPopulation > daytimePopulation)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nightPopulation),
                    "Night population must be positive and must not exceed " +
                    "the daytime population.");
            }

            if (poolSize < daytimePopulation)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poolSize),
                    "The presentation pool must cover the daytime population.");
            }

            if (maximumSpawnsPerEvent <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSpawnsPerEvent));
            }

            if (!IsFinite(minimumPeerSeparation) ||
                minimumPeerSeparation < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumPeerSeparation));
            }

            if (maximumWalkersPerStreetLane <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumWalkersPerStreetLane));
            }

            if (approachGuidedPopulation < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(approachGuidedPopulation));
            }

            Id = id;
            DaytimePopulation = daytimePopulation;
            NightPopulation = nightPopulation;
            PoolSize = poolSize;
            MaximumSpawnsPerEvent = maximumSpawnsPerEvent;
            MinimumPeerSeparation = minimumPeerSeparation;
            MaximumWalkersPerStreetLane = maximumWalkersPerStreetLane;
            ApproachGuidedPopulation = approachGuidedPopulation;
            FillsImmediately = fillsImmediately;
        }

        public string Id { get; }

        /// <summary>Simultaneously active walkers between dawn and dusk.</summary>
        public int DaytimePopulation { get; }

        /// <summary>Simultaneously active walkers during strict night.</summary>
        public int NightPopulation { get; }

        /// <summary>Pooled presentations; larger than the daytime population so
        /// a repeat encounter can show a different mix.</summary>
        public int PoolSize { get; }

        /// <summary>Upper bound on walkers activated by one spawn event.</summary>
        public int MaximumSpawnsPerEvent { get; }

        /// <summary>Planar distance a candidate anchor must keep from every
        /// already-active walker so the population spreads out.</summary>
        public float MinimumPeerSeparation { get; }

        /// <summary>Walkers allowed to share one sidewalk lane, which is one
        /// side of one street edge.</summary>
        public int MaximumWalkersPerStreetLane { get; }

        /// <summary>Walkers that may follow player-directed approach guidance
        /// at the same time. The rest pick a seeded direction, so traffic
        /// flows both ways instead of converging on the hero.</summary>
        public int ApproachGuidedPopulation { get; }

        /// <summary>Skips the long first-event delay when the runtime is
        /// enabled, used where enabling is itself a composition boundary.</summary>
        public bool FillsImmediately { get; }

        public int GetPopulation(bool nightMode)
        {
            return nightMode ? NightPopulation : DaytimePopulation;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
