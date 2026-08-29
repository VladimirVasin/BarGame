using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The complete audible vocabulary of the village. There is no generic
    /// ambience bed: every entry names the physical thing that owns it.
    /// </summary>
    public enum AlpineVillageSoundKind
    {
        None = 0,
        StationCableMetal = 1,
        GarlandWire = 2,
        DogBehindFence = 3,
        SourceWater = 4,
        FirewoodInMineCart = 5,
        WordlessHumBehindWall = 6,
        Count = 7
    }

    /// <summary>
    /// One immutable spatial emitter and the visible owner that explains it.
    /// OwnerPosition may differ from WorldPosition when a sound is deliberately
    /// hidden behind a wall or fence.
    /// </summary>
    public sealed class AlpineVillageSoundAnchorDescriptor
    {
        internal AlpineVillageSoundAnchorDescriptor(
            string stableId,
            string physicalOwnerStableId,
            AlpineVillageSoundKind kind,
            Vector3 worldPosition,
            Vector3 ownerPosition,
            Vector3 ownerForward)
        {
            StableId = RequireStableId(stableId, nameof(stableId));
            PhysicalOwnerStableId = RequireStableId(
                physicalOwnerStableId,
                nameof(physicalOwnerStableId));
            if (kind <= AlpineVillageSoundKind.None ||
                kind >= AlpineVillageSoundKind.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (!IsFinite(worldPosition) || !IsFinite(ownerPosition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldPosition),
                    "Village sound positions must be finite.");
            }

            Vector3 horizontalForward = new Vector3(
                ownerForward.x,
                0f,
                ownerForward.z);
            if (!IsFinite(ownerForward) ||
                horizontalForward.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "A causal sound owner needs a horizontal facing.",
                    nameof(ownerForward));
            }

            Kind = kind;
            WorldPosition = worldPosition;
            OwnerPosition = ownerPosition;
            OwnerForward = horizontalForward.normalized;
        }

        public string StableId { get; }
        public string PhysicalOwnerStableId { get; }
        public AlpineVillageSoundKind Kind { get; }

        /// <summary>The point the AudioSource occupies.</summary>
        public Vector3 WorldPosition { get; }

        /// <summary>
        /// The point occupied by the visible cause. For the dog this is the
        /// cable gate; the bark itself sits deeper in the yard.
        /// </summary>
        public Vector3 OwnerPosition { get; }

        public Vector3 OwnerForward { get; }

        public bool IsLooping =>
            AlpineVillageSoundSynthesis.GetDefinition(Kind).IsLoop;

        public bool IsScheduled =>
            AlpineVillageSoundSynthesis.GetDefinition(Kind)
                .ScheduleInterval.IsScheduled;

        private static string RequireStableId(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A village sound stable ID must be non-empty and trimmed.",
                    parameter);
            }

            return value;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Pure, validated and stable-order sound data for one village plan.
    /// </summary>
    public sealed class AlpineVillageSoundscapePlan
    {
        private readonly Dictionary<string, AlpineVillageSoundAnchorDescriptor>
            byStableId;
        private readonly Dictionary<AlpineVillageSoundKind,
            AlpineVillageSoundAnchorDescriptor> byKind;

        internal AlpineVillageSoundscapePlan(
            int seed,
            IList<AlpineVillageSoundAnchorDescriptor> sourceAnchors)
        {
            Seed = seed;
            var anchors = new List<AlpineVillageSoundAnchorDescriptor>(
                sourceAnchors ??
                throw new ArgumentNullException(nameof(sourceAnchors)));
            var loops = new List<AlpineVillageSoundAnchorDescriptor>();
            var scheduled = new List<AlpineVillageSoundAnchorDescriptor>();
            byStableId = new Dictionary<string,
                AlpineVillageSoundAnchorDescriptor>(StringComparer.Ordinal);
            byKind = new Dictionary<AlpineVillageSoundKind,
                AlpineVillageSoundAnchorDescriptor>();

            for (int index = 0; index < anchors.Count; index++)
            {
                AlpineVillageSoundAnchorDescriptor anchor = anchors[index] ??
                    throw new ArgumentException(
                        "A village sound plan cannot contain null anchors.",
                        nameof(sourceAnchors));
                byStableId.Add(anchor.StableId, anchor);
                byKind.Add(anchor.Kind, anchor);
                if (anchor.IsLooping)
                {
                    loops.Add(anchor);
                }
                else if (anchor.IsScheduled)
                {
                    scheduled.Add(anchor);
                }
                else
                {
                    throw new ArgumentException(
                        $"Village sound '{anchor.StableId}' is neither a " +
                        "loop nor an autonomous scheduled detail.",
                        nameof(sourceAnchors));
                }
            }

            Anchors = new ReadOnlyCollection<
                AlpineVillageSoundAnchorDescriptor>(anchors);
            LoopingAnchors = new ReadOnlyCollection<
                AlpineVillageSoundAnchorDescriptor>(loops);
            ScheduledAnchors = new ReadOnlyCollection<
                AlpineVillageSoundAnchorDescriptor>(scheduled);
            ValidateOrThrow();
        }

        public int Seed { get; }
        public IReadOnlyList<AlpineVillageSoundAnchorDescriptor> Anchors
        {
            get;
        }

        public IReadOnlyList<AlpineVillageSoundAnchorDescriptor> LoopingAnchors
        {
            get;
        }

        public IReadOnlyList<AlpineVillageSoundAnchorDescriptor>
            ScheduledAnchors
        {
            get;
        }

        public bool TryGetAnchor(
            string stableId,
            out AlpineVillageSoundAnchorDescriptor anchor)
        {
            return byStableId.TryGetValue(stableId ?? string.Empty, out anchor);
        }

        public AlpineVillageSoundAnchorDescriptor GetRequiredAnchor(
            string stableId)
        {
            if (!TryGetAnchor(
                    stableId,
                    out AlpineVillageSoundAnchorDescriptor anchor))
            {
                throw new KeyNotFoundException(
                    $"Village sound anchor '{stableId}' is not in the plan.");
            }

            return anchor;
        }

        public AlpineVillageSoundAnchorDescriptor GetRequiredAnchor(
            AlpineVillageSoundKind kind)
        {
            if (!byKind.TryGetValue(
                    kind,
                    out AlpineVillageSoundAnchorDescriptor anchor))
            {
                throw new KeyNotFoundException(
                    $"Village sound kind '{kind}' is not in the plan.");
            }

            return anchor;
        }

        public void ValidateOrThrow()
        {
            int expected = (int)AlpineVillageSoundKind.Count - 1;
            if (Anchors.Count != expected)
            {
                throw new InvalidOperationException(
                    $"The village soundscape needs exactly {expected} " +
                    $"causal anchors; it has {Anchors.Count}.");
            }

            for (int value = 1;
                 value < (int)AlpineVillageSoundKind.Count;
                 value++)
            {
                var kind = (AlpineVillageSoundKind)value;
                if (!byKind.ContainsKey(kind))
                {
                    throw new InvalidOperationException(
                        $"The village soundscape is missing '{kind}'.");
                }
            }
        }
    }

    /// <summary>
    /// Derives all sound positions from the same immutable village plan that
    /// places the station, lane, houses, chapel and adit dressing.
    /// </summary>
    public static class AlpineVillageSoundscapePlanner
    {
        public const string StationAnchorId =
            "village-sound-station-cable-metal";
        public const string GarlandAnchorId =
            "village-sound-garland-wire";
        public const string DogAnchorId =
            "village-sound-dog-behind-fence";
        public const string SourceWaterAnchorId =
            "village-sound-source-water";
        public const string FirewoodAnchorId =
            "village-sound-firewood";
        public const string WordlessHumAnchorId =
            "village-sound-wordless-hum";

        public const string GarlandOwnerStableId =
            AlpineVillageDressingPlanner.GarlandOwnerStableId;
        public const string StationMechanismOwnerStableId =
            AlpineVillageDressingPlanner.StationMechanismOwnerStableId;
        public const string CableGateOwnerStableId =
            AlpineVillageDressingPlanner.CableGateOwnerStableId;
        public const string SourceBowlOwnerStableId =
            AlpineVillageDressingPlanner.SourceBowlOwnerStableId;
        public const string FirewoodOwnerStableId =
            AlpineVillageDressingPlanner.FirewoodOwnerStableId;

        public const int AudibleGarlandSpanIndex =
            AlpineVillageDressingPlanner.AudibleGarlandSpanIndex;
        public const float DogHouseLaneFraction =
            AlpineVillageDressingPlanner.DogHouseLaneFraction;
        public const float CableGateLaneInset =
            AlpineVillageDressingPlanner.CableGateLaneInset;
        public const float DogDepthBehindGate =
            AlpineVillageDressingPlanner.DogDepthBehindGate;

        public static AlpineVillageSoundscapePlan Create(
            AlpineVillagePlan village)
        {
            if (village == null)
            {
                throw new ArgumentNullException(nameof(village));
            }

            village.ValidateOrThrow();
            AlpineVillagePlotDescriptor chapel = RequirePlot(
                village,
                AlpineVillagePlotKind.Chapel);
            AlpineVillagePlotDescriptor adit = RequirePlot(
                village,
                AlpineVillagePlotKind.Adit);
            AlpineVillagePlotDescriptor dogHouse = FindClosestHouse(
                village,
                village.Lane.Length * DogHouseLaneFraction,
                1,
                null);
            AlpineVillagePlotDescriptor humHouse = FindClosestHouse(
                village,
                village.Lane.Length * 0.34f,
                -1,
                dogHouse);

            var anchors = new List<AlpineVillageSoundAnchorDescriptor>(6)
            {
                CreateStationAnchor(village),
                CreateGarlandAnchor(village),
                CreateDogAnchor(village, dogHouse),
                CreateSourceWaterAnchor(village, chapel),
                CreateFirewoodAnchor(adit),
                CreateWordlessHumAnchor(humHouse)
            };
            return new AlpineVillageSoundscapePlan(village.Seed, anchors);
        }

        private static AlpineVillageSoundAnchorDescriptor CreateStationAnchor(
            AlpineVillagePlan village)
        {
            MountainCablewayNodeDescriptor station =
                village.Station.Cableway.Nodes[0];
            return new AlpineVillageSoundAnchorDescriptor(
                StationAnchorId,
                StationMechanismOwnerStableId,
                AlpineVillageSoundKind.StationCableMetal,
                station.CableCenter,
                station.CableCenter,
                village.Station.Cableway.LineForward);
        }

        private static AlpineVillageSoundAnchorDescriptor CreateGarlandAnchor(
            AlpineVillagePlan village)
        {
            AlpineVillageWorldBuilder.GetGarlandSpan(
                village,
                AudibleGarlandSpanIndex,
                out Vector3 left,
                out Vector3 right);
            Vector3 wireCentre =
                AlpineVillageWorldBuilder.SampleGarlandPoint(
                    left,
                    right,
                    0.5f);
            return new AlpineVillageSoundAnchorDescriptor(
                GarlandAnchorId,
                GarlandOwnerStableId,
                AlpineVillageSoundKind.GarlandWire,
                wireCentre,
                wireCentre,
                right - left);
        }

        private static AlpineVillageSoundAnchorDescriptor CreateDogAnchor(
            AlpineVillagePlan village,
            AlpineVillagePlotDescriptor house)
        {
            Vector3 awayFromLane = -house.Facing;
            Vector3 gate = AlpineVillageDressingPlanner
                .GetCableGatePosition(village, house);
            Vector3 dog = gate +
                awayFromLane * DogDepthBehindGate +
                Vector3.up * 0.58f;
            return new AlpineVillageSoundAnchorDescriptor(
                DogAnchorId,
                CableGateOwnerStableId,
                AlpineVillageSoundKind.DogBehindFence,
                dog,
                gate,
                house.Facing);
        }

        private static AlpineVillageSoundAnchorDescriptor
            CreateSourceWaterAnchor(
                AlpineVillagePlan village,
                AlpineVillagePlotDescriptor chapel)
        {
            // The path/dressing plan owns the bowl. Audio only lifts the
            // emitter into the water volume and never guesses another place.
            Vector3 bowl = AlpineVillagePathPlanner
                .GetChapelSourceBowlPosition(village, chapel);
            Vector3 source = bowl + Vector3.up * 0.35f;
            return new AlpineVillageSoundAnchorDescriptor(
                SourceWaterAnchorId,
                SourceBowlOwnerStableId,
                AlpineVillageSoundKind.SourceWater,
                source,
                bowl,
                chapel.Facing);
        }

        private static AlpineVillageSoundAnchorDescriptor CreateFirewoodAnchor(
            AlpineVillagePlotDescriptor adit)
        {
            Quaternion rotation = Quaternion.LookRotation(
                adit.Facing,
                Vector3.up);
            Vector3 local = new Vector3(
                adit.FootprintSize.x * 0.42f,
                0.42f,
                -adit.FootprintSize.y * 0.9f);
            Vector3 firewoodOwner = adit.GroundCenter + rotation * local;
            Vector3 firewoodSound = firewoodOwner + Vector3.up * 0.12f;
            return new AlpineVillageSoundAnchorDescriptor(
                FirewoodAnchorId,
                FirewoodOwnerStableId,
                AlpineVillageSoundKind.FirewoodInMineCart,
                firewoodSound,
                firewoodOwner,
                adit.Facing);
        }

        private static AlpineVillageSoundAnchorDescriptor
            CreateWordlessHumAnchor(AlpineVillagePlotDescriptor house)
        {
            Vector3 insideWall = house.GroundCenter +
                house.Facing * (house.FootprintSize.y * 0.22f) +
                Vector3.up * 1.55f;
            return new AlpineVillageSoundAnchorDescriptor(
                WordlessHumAnchorId,
                house.StableId,
                AlpineVillageSoundKind.WordlessHumBehindWall,
                insideWall,
                house.GroundCenter,
                house.Facing);
        }

        private static AlpineVillagePlotDescriptor RequirePlot(
            AlpineVillagePlan village,
            AlpineVillagePlotKind kind)
        {
            for (int index = 0; index < village.Plots.Count; index++)
            {
                if (village.Plots[index].Kind == kind)
                {
                    return village.Plots[index];
                }
            }

            throw new InvalidOperationException(
                $"The village soundscape needs a '{kind}' plot.");
        }

        private static AlpineVillagePlotDescriptor FindClosestHouse(
            AlpineVillagePlan village,
            float targetLaneDistance,
            int side,
            AlpineVillagePlotDescriptor excluded)
        {
            AlpineVillagePlotDescriptor best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < village.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor candidate = village.Plots[index];
                if (candidate.Kind != AlpineVillagePlotKind.House ||
                    candidate.Side != side ||
                    ReferenceEquals(candidate, excluded))
                {
                    continue;
                }

                float distance = Mathf.Abs(
                    candidate.LaneDistance - targetLaneDistance);
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best ?? throw new InvalidOperationException(
                $"The village soundscape needs a house on side {side}.");
        }
    }

    /// <summary>
    /// Pure state for one autonomous village detail.
    /// </summary>
    public readonly struct AlpineVillageSoundScheduleCursor
    {
        internal AlpineVillageSoundScheduleCursor(
            int seed,
            string sourceStableId,
            uint eventOrdinal,
            double nextEventTimeSeconds)
        {
            Seed = seed;
            SourceStableId = sourceStableId ?? string.Empty;
            EventOrdinal = eventOrdinal;
            NextEventTimeSeconds = nextEventTimeSeconds;
        }

        public int Seed { get; }
        public string SourceStableId { get; }
        public uint EventOrdinal { get; }
        public double NextEventTimeSeconds { get; }

        public bool IsDue(double nowSeconds)
        {
            return !double.IsNaN(nowSeconds) &&
                   !double.IsInfinity(nowSeconds) &&
                   nowSeconds >= NextEventTimeSeconds;
        }
    }

    /// <summary>
    /// Platform-stable timing for the dog and the occasional settling log.
    /// A hitch fires at most one event and schedules the next from the
    /// observed firing time, matching the established City sound scheduler.
    /// </summary>
    public static class AlpineVillageSoundSchedulePlanner
    {
        public static AlpineVillageSoundScheduleCursor Start(
            AlpineVillageSoundscapePlan plan,
            string sourceStableId,
            double nowSeconds)
        {
            RequireTime(nowSeconds);
            AlpineVillageSoundAnchorDescriptor anchor =
                GetScheduledAnchor(plan, sourceStableId);
            return CreateCursor(plan.Seed, anchor, 0u, nowSeconds);
        }

        public static AlpineVillageSoundScheduleCursor AdvanceAfterFiring(
            AlpineVillageSoundscapePlan plan,
            AlpineVillageSoundScheduleCursor current,
            double nowSeconds)
        {
            RequireTime(nowSeconds);
            AlpineVillageSoundAnchorDescriptor anchor =
                GetScheduledAnchor(plan, current.SourceStableId);
            if (plan.Seed != current.Seed)
            {
                throw new ArgumentException(
                    "The schedule cursor belongs to another village seed.",
                    nameof(current));
            }

            if (!current.IsDue(nowSeconds))
            {
                throw new InvalidOperationException(
                    $"Village sound '{current.SourceStableId}' is not due.");
            }

            if (current.EventOrdinal == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The village sound event ordinal is exhausted.");
            }

            return CreateCursor(
                plan.Seed,
                anchor,
                current.EventOrdinal + 1u,
                nowSeconds);
        }

        private static AlpineVillageSoundScheduleCursor CreateCursor(
            int seed,
            AlpineVillageSoundAnchorDescriptor anchor,
            uint eventOrdinal,
            double baseTimeSeconds)
        {
            CitySoundScheduleInterval interval =
                AlpineVillageSoundSynthesis.GetDefinition(anchor.Kind)
                    .ScheduleInterval;
            float unit = CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.SourceEvent(
                    seed,
                    anchor.StableId,
                    eventOrdinal));
            double delay = interval.MinimumSeconds +
                (interval.MaximumSeconds - interval.MinimumSeconds) * unit;
            return new AlpineVillageSoundScheduleCursor(
                seed,
                anchor.StableId,
                eventOrdinal,
                baseTimeSeconds + delay);
        }

        private static AlpineVillageSoundAnchorDescriptor GetScheduledAnchor(
            AlpineVillageSoundscapePlan plan,
            string stableId)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            AlpineVillageSoundAnchorDescriptor anchor =
                plan.GetRequiredAnchor(stableId);
            if (!anchor.IsScheduled)
            {
                throw new ArgumentException(
                    $"Village sound '{stableId}' is not scheduled.",
                    nameof(stableId));
            }

            return anchor;
        }

        private static void RequireTime(double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) ||
                double.IsInfinity(nowSeconds) ||
                nowSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            }
        }
    }
}
