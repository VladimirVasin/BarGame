using System;

namespace BarPromenade
{
    [Flags]
    public enum CityDistrictNeighbourSet
    {
        None = 0,
        OldTown = 1 << 0,
        Residential = 1 << 1,
        Industrial = 1 << 2,
        Nightlife = 1 << 3
    }

    public enum CityDistrictFrontageFamily
    {
        NarrowLayered = 0,
        DomesticBalcony = 1,
        ProcessGate = 2,
        ActiveGroundFloor = 3
    }

    public enum CityDistrictMassFamily
    {
        FragmentedPerimeter = 0,
        SetbackCourtyard = 1,
        LowWideProcess = 2,
        TallDense = 3
    }

    public enum CityDistrictWindowFamily
    {
        NarrowIrregular = 0,
        OccupiedClusters = 1,
        SparseUtility = 2,
        DarkUpperActiveBase = 3
    }

    public enum CityDistrictLightFamily
    {
        BrokenAmberPools = 0,
        DomesticWindowPools = 1,
        SparseTaskPools = 2,
        ThresholdSignals = 3
    }

    public enum CityDistrictWearFamily
    {
        SootWaterAndPatch = 0,
        RepairAndPersonalUse = 1,
        RustAndProcess = 2,
        SignageAndRunoff = 3
    }

    /// <summary>
    /// Pure frontage authoring weights. Values are normalized design data,
    /// not dimensions consumed by the current world builder.
    /// </summary>
    public readonly struct CityDistrictFrontageProfile
    {
        internal CityDistrictFrontageProfile(
            CityDistrictFrontageFamily family,
            float activation,
            float recess,
            float fragmentation)
        {
            Family = family;
            Activation = activation;
            Recess = recess;
            Fragmentation = fragmentation;
        }

        public CityDistrictFrontageFamily Family { get; }
        public float Activation { get; }
        public float Recess { get; }
        public float Fragmentation { get; }
    }

    /// <summary>
    /// Pure massing constraints. Footprint and height ranges preserve the
    /// normalized ranges already used by <c>CityLayoutGenerator</c>.
    /// </summary>
    public readonly struct CityDistrictMassProfile
    {
        internal CityDistrictMassProfile(
            CityDistrictMassFamily family,
            float footprintMinimum,
            float footprintMaximum,
            float heightMinimum,
            float heightMaximum,
            float skylineIrregularity)
        {
            Family = family;
            FootprintMinimum = footprintMinimum;
            FootprintMaximum = footprintMaximum;
            HeightMinimum = heightMinimum;
            HeightMaximum = heightMaximum;
            SkylineIrregularity = skylineIrregularity;
        }

        public CityDistrictMassFamily Family { get; }
        public float FootprintMinimum { get; }
        public float FootprintMaximum { get; }
        public float HeightMinimum { get; }
        public float HeightMaximum { get; }
        public float SkylineIrregularity { get; }
    }

    public readonly struct CityDistrictWindowProfile
    {
        internal CityDistrictWindowProfile(
            CityDistrictWindowFamily family,
            float litWindowRatio,
            float warmShare,
            float rhythmRegularity)
        {
            Family = family;
            LitWindowRatio = litWindowRatio;
            WarmShare = warmShare;
            RhythmRegularity = rhythmRegularity;
        }

        public CityDistrictWindowFamily Family { get; }
        public float LitWindowRatio { get; }
        public float WarmShare { get; }
        public float RhythmRegularity { get; }
    }

    public readonly struct CityDistrictLightProfile
    {
        internal CityDistrictLightProfile(
            CityDistrictLightFamily family,
            float fixtureCoverage,
            float warmShare,
            float darkGapRatio,
            float signalShare)
        {
            Family = family;
            FixtureCoverage = fixtureCoverage;
            WarmShare = warmShare;
            DarkGapRatio = darkGapRatio;
            SignalShare = signalShare;
        }

        public CityDistrictLightFamily Family { get; }
        public float FixtureCoverage { get; }
        public float WarmShare { get; }
        public float DarkGapRatio { get; }
        public float SignalShare { get; }
    }

    public readonly struct CityDistrictWearProfile
    {
        internal CityDistrictWearProfile(
            CityDistrictWearFamily family,
            float amount,
            float verticalStreaks,
            float repairPatches)
        {
            Family = family;
            Amount = amount;
            VerticalStreaks = verticalStreaks;
            RepairPatches = repairPatches;
        }

        public CityDistrictWearFamily Family { get; }
        public float Amount { get; }
        public float VerticalStreaks { get; }
        public float RepairPatches { get; }
    }

    /// <summary>
    /// Per-district transition constraints. Motif influence stays below one
    /// half so the owning district remains visually dominant. Light receives
    /// an extra bias because it is allowed to blend more smoothly than decor.
    /// </summary>
    public readonly struct CityDistrictTransitionProfile
    {
        internal CityDistrictTransitionProfile(
            CityDistrictNeighbourSet allowedNeighbours,
            float minimumMotifInfluence,
            float maximumMotifInfluence,
            float lightBlendBias)
        {
            AllowedNeighbours = allowedNeighbours;
            MinimumMotifInfluence = minimumMotifInfluence;
            MaximumMotifInfluence = maximumMotifInfluence;
            LightBlendBias = lightBlendBias;
        }

        public CityDistrictNeighbourSet AllowedNeighbours { get; }
        public float MinimumMotifInfluence { get; }
        public float MaximumMotifInfluence { get; }
        public float LightBlendBias { get; }

        public bool Allows(CityDistrictKind district)
        {
            CityDistrictNeighbourSet flag;
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    flag = CityDistrictNeighbourSet.OldTown;
                    break;
                case CityDistrictKind.Residential:
                    flag = CityDistrictNeighbourSet.Residential;
                    break;
                case CityDistrictKind.Industrial:
                    flag = CityDistrictNeighbourSet.Industrial;
                    break;
                case CityDistrictKind.Nightlife:
                    flag = CityDistrictNeighbourSet.Nightlife;
                    break;
                default:
                    return false;
            }

            return (AllowedNeighbours & flag) != 0;
        }
    }

    /// <summary>
    /// Immutable, renderer-agnostic art direction for one urban district.
    /// It deliberately owns no Unity object, material, scene or collider
    /// reference, so planners and EditMode tests can use it as pure data.
    /// </summary>
    public sealed class CityDistrictArtProfile
    {
        internal CityDistrictArtProfile(
            string stableId,
            CityDistrictKind district,
            CityDistrictFrontageProfile frontage,
            CityDistrictMassProfile mass,
            CityDistrictWindowProfile window,
            CityDistrictLightProfile light,
            CityDistrictWearProfile wear,
            CityDistrictTransitionProfile transition)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A district art profile needs a stable id.",
                    nameof(stableId));
            }

            StableId = stableId;
            District = district;
            Frontage = frontage;
            Mass = mass;
            Window = window;
            Light = light;
            Wear = wear;
            Transition = transition;
            Validate();
        }

        public string StableId { get; }
        public CityDistrictKind District { get; }
        public CityDistrictFrontageProfile Frontage { get; }
        public CityDistrictMassProfile Mass { get; }
        public CityDistrictWindowProfile Window { get; }
        public CityDistrictLightProfile Light { get; }
        public CityDistrictWearProfile Wear { get; }
        public CityDistrictTransitionProfile Transition { get; }

        private void Validate()
        {
            RequireRange(
                nameof(Mass.FootprintMinimum),
                Mass.FootprintMinimum,
                Mass.FootprintMaximum);
            RequireRange(
                nameof(Mass.HeightMinimum),
                Mass.HeightMinimum,
                Mass.HeightMaximum);
            RequireUnit(nameof(Frontage.Activation), Frontage.Activation);
            RequireUnit(nameof(Frontage.Recess), Frontage.Recess);
            RequireUnit(
                nameof(Frontage.Fragmentation),
                Frontage.Fragmentation);
            RequireUnit(
                nameof(Mass.SkylineIrregularity),
                Mass.SkylineIrregularity);
            RequireUnit(nameof(Window.LitWindowRatio), Window.LitWindowRatio);
            RequireUnit(nameof(Window.WarmShare), Window.WarmShare);
            RequireUnit(
                nameof(Window.RhythmRegularity),
                Window.RhythmRegularity);
            RequireUnit(
                nameof(Light.FixtureCoverage),
                Light.FixtureCoverage);
            RequireUnit(nameof(Light.WarmShare), Light.WarmShare);
            RequireUnit(nameof(Light.DarkGapRatio), Light.DarkGapRatio);
            RequireUnit(nameof(Light.SignalShare), Light.SignalShare);
            RequireUnit(nameof(Wear.Amount), Wear.Amount);
            RequireUnit(nameof(Wear.VerticalStreaks), Wear.VerticalStreaks);
            RequireUnit(nameof(Wear.RepairPatches), Wear.RepairPatches);
            RequireRange(
                nameof(Transition.MinimumMotifInfluence),
                Transition.MinimumMotifInfluence,
                Transition.MaximumMotifInfluence);
            RequireUnit(
                nameof(Transition.LightBlendBias),
                Transition.LightBlendBias);

            if (Transition.MaximumMotifInfluence >= 0.5f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Transition.MaximumMotifInfluence),
                    "A neighbour motif may not overtake its district.");
            }
        }

        private static void RequireRange(
            string name,
            float minimum,
            float maximum)
        {
            RequireUnit(name, minimum);
            RequireUnit(name, maximum);
            if (minimum > maximum)
            {
                throw new ArgumentException(
                    $"Profile range '{name}' is reversed.",
                    name);
            }
        }

        private static void RequireUnit(string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "District art weights must be in the 0..1 range.");
            }
        }
    }
}
