using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CitySeacoastPartKind
    {
        MolBlock = 0,
        MolDeck = 1,
        MolParapet = 2,
        MolStair = 3,
        // 4 was BeaconTower: the light left the mol head for the
        // lighthouse island offshore. A deliberate hole, like the
        // lake's — do not renumber or reuse.
        DerrickCrane = 5,
        PortRuin = 6,
        MouthBank = 7,
        EsplanadeSlab = 8,
        EsplanadeParapet = 9,
        EsplanadeStair = 10,
        Bench = 11,
        PierPile = 12,
        PierBeam = 13,
        PierDeck = 14,
        PierRail = 15,
        Hut = 16,
        HutSign = 17,
        Slipway = 18,
        Bollard = 19,
        Boat = 20,
        BoatRest = 21,
        RottenPile = 22,
        Driftwood = 23,
        Barge = 24,
        FootbridgeDeck = 25,
        FootbridgePile = 26,
        FootbridgeRail = 27,
        BluffStair = 28,
        ShoreGrass = 29,
        Debris = 30
    }

    /// <summary>
    /// The material family a seacoast part batches and renders under.
    /// The timber styles are the lake's boards in the same three
    /// states — dry deck, tarred piles, painted hulls — because the
    /// boat station that owns most of them moved here plank by plank.
    /// The coast adds what the lake never had: sea-wall granite, port
    /// concrete, and the rust of a barge nobody is coming back for.
    /// </summary>
    public enum CitySeacoastStyle
    {
        Concrete = 0,
        Granite = 1,
        Planking = 2,
        TarredTimber = 3,
        HullPaint = 4,
        HullTar = 5,
        Iron = 6,
        RustIron = 7,
        Grass = 8,
        PaintAccent = 9,
        Litter = 10,
        Sand = 11
    }

    /// <summary>
    /// The four hire hulls, inherited from the boat station's lake
    /// years. The planner cycles all four through the first four
    /// accepted boats so the row read from the esplanade shows the
    /// whole vocabulary.
    /// </summary>
    public enum CitySeacoastBoatVariant
    {
        PlankSkiff = 0,
        RoundHullDinghy = 1,
        StavedPunt = 2,
        HolledWreck = 3
    }

    public readonly struct CitySeacoastPartDescriptor :
        IEquatable<CitySeacoastPartDescriptor>
    {
        public CitySeacoastPartDescriptor(
            string stableId,
            CitySeacoastPartKind kind,
            CitySeacoastStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            int boatOrdinal,
            CitySeacoastBoatVariant variant)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Style = style;
            Center = center;
            Rotation = rotation;
            Size = size;
            BoatOrdinal = boatOrdinal;
            Variant = variant;
        }

        public string StableId { get; }
        public CitySeacoastPartKind Kind { get; }
        public CitySeacoastStyle Style { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }

        /// <summary>
        /// The hauled hull this part belongs to, or -1 for everything
        /// that carries no boat identity.
        /// </summary>
        public int BoatOrdinal { get; }
        public CitySeacoastBoatVariant Variant { get; }

        public bool BlocksMovement =>
            CitySeacoastRules.BlocksMovement(Style);

        public bool Equals(CitySeacoastPartDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Style == other.Style &&
                   Center.Equals(other.Center) &&
                   Rotation.Equals(other.Rotation) &&
                   Size.Equals(other.Size) &&
                   BoatOrdinal == other.BoatOrdinal &&
                   Variant == other.Variant;
        }

        public override bool Equals(object obj)
        {
            return obj is CitySeacoastPartDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (int)Style;
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Size.GetHashCode();
                hash = (hash * 397) ^ BoatOrdinal;
                return (hash * 397) ^ (int)Variant;
            }
        }
    }

    /// <summary>
    /// The fixtures the coast lights itself with. The hut bulb and the
    /// pier hand lamp arrived with the boat station; the esplanade
    /// lamps are the embankment's cast-iron family gone one shade
    /// dimmer. The navigation light is no longer the coast's: it
    /// stands on the lighthouse island offshore, planned by its own
    /// module.
    /// </summary>
    public enum CitySeacoastLampKind
    {
        // 0 was Beacon: moved to the lighthouse island. A deliberate
        // hole — do not renumber or reuse.
        PierHead = 1,
        HutDoor = 2,
        Esplanade = 3
    }

    /// <summary>
    /// One lamp. Lamps are real fixtures rather than batched boxes, so
    /// the plan carries them separately from the part list and its
    /// budget. The ground position is the point the fixture stands on
    /// and the yaw turns its local +Z toward the water.
    /// </summary>
    public readonly struct CitySeacoastLampDescriptor :
        IEquatable<CitySeacoastLampDescriptor>
    {
        public CitySeacoastLampDescriptor(
            string stableId,
            CitySeacoastLampKind kind,
            Vector3 groundPosition,
            float yawDegrees)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            GroundPosition = groundPosition;
            YawDegrees = yawDegrees;
        }

        public string StableId { get; }
        public CitySeacoastLampKind Kind { get; }
        public Vector3 GroundPosition { get; }
        public float YawDegrees { get; }

        public bool Equals(CitySeacoastLampDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   GroundPosition.Equals(other.GroundPosition) &&
                   YawDegrees.Equals(other.YawDegrees);
        }

        public override bool Equals(object obj)
        {
            return obj is CitySeacoastLampDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ GroundPosition.GetHashCode();
                return (hash * 397) ^ YawDegrees.GetHashCode();
            }
        }
    }

    /// <summary>
    /// The shape of the shore: where the sand ends, where the sea
    /// begins, where the river cuts through, and how the one long
    /// strip divides into its three moods.
    ///
    /// The blueprint gives the waterfront two full-width rows — sand
    /// ramping down from the boundary street, then open water to the
    /// edge of the map — and the waterline between them is a single
    /// straight east-west gridline. Everything the precinct plans is
    /// measured from that line, and the three zones are cut by the
    /// river corridor: the dead port west of the mouth, the esplanade
    /// and the transplanted boat station east of it, and the wild
    /// shore beyond.
    /// </summary>
    public readonly struct CitySeacoastFrame
    {
        internal CitySeacoastFrame(
            Rect beachRowBounds,
            Rect seaRowBounds,
            float waterlineZ,
            float seaTopY,
            float beachEdgeTopY,
            float channelXMin,
            float channelXMax,
            float mouthWaterY,
            Rect westZone,
            Rect centerZone,
            Rect eastZone)
        {
            BeachRowBounds = beachRowBounds;
            SeaRowBounds = seaRowBounds;
            WaterlineZ = waterlineZ;
            SeaTopY = seaTopY;
            BeachEdgeTopY = beachEdgeTopY;
            ChannelXMin = channelXMin;
            ChannelXMax = channelXMax;
            MouthWaterY = mouthWaterY;
            WestZone = westZone;
            CenterZone = centerZone;
            EastZone = eastZone;
        }

        /// <summary>The union of the waterfront's sand cells.</summary>
        public Rect BeachRowBounds { get; }

        /// <summary>The union of the waterfront's sea cells.</summary>
        public Rect SeaRowBounds { get; }

        /// <summary>The straight line where sand meets sea.</summary>
        public float WaterlineZ { get; }

        /// <summary>The sea's physical top. Adopted, never invented.</summary>
        public float SeaTopY { get; }

        /// <summary>The sand's top right at the waterline.</summary>
        public float BeachEdgeTopY { get; }

        /// <summary>The river channel's cut through the shore.</summary>
        public float ChannelXMin { get; }
        public float ChannelXMax { get; }

        /// <summary>
        /// The river's own surface at the waterline — the north edge
        /// of its final sheet. The mouth spill continues from exactly
        /// this height so the two sheets read as one water.
        /// </summary>
        public float MouthWaterY { get; }

        /// <summary>Dead port and mol, west of the river mouth.</summary>
        public Rect WestZone { get; }

        /// <summary>Esplanade and boat station, east of the mouth.</summary>
        public Rect CenterZone { get; }

        /// <summary>The wild shore out to the map's east edge.</summary>
        public Rect EastZone { get; }
    }

    public sealed class CitySeacoastPlan
    {
        /// <summary>
        /// Hard budget for combined-mesh parts. Sized from the default
        /// city's measured dressing (~380 parts: the mol, the port
        /// ruins, the mouth banks, the esplanade with its sea wall,
        /// the transplanted boat station, the footbridge, and the
        /// wild shore's piles, barge and scatter) plus
        /// headroom for seed variation. Lamps are fixtures and do not
        /// count against it.
        /// </summary>
        public const int MaximumPartCount = 560;

        private readonly ReadOnlyCollection<CitySeacoastPartDescriptor>
            parts;
        private readonly ReadOnlyCollection<CitySeacoastLampDescriptor>
            lamps;
        private readonly int[] variantBoatCounts;

        internal CitySeacoastPlan(
            IList<CitySeacoastPartDescriptor> partSource,
            IList<CitySeacoastLampDescriptor> lampSource,
            Rect grounds,
            CitySeacoastFrame frame)
        {
            var partCopy = new List<CitySeacoastPartDescriptor>(
                partSource);
            partCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            parts = new ReadOnlyCollection<CitySeacoastPartDescriptor>(
                partCopy);

            var lampCopy = new List<CitySeacoastLampDescriptor>(
                lampSource);
            lampCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            lamps = new ReadOnlyCollection<CitySeacoastLampDescriptor>(
                lampCopy);

            Grounds = grounds;
            Frame = frame;

            variantBoatCounts = new int[4];
            var seenOrdinals = new HashSet<int>();
            for (int index = 0; index < parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = parts[index];
                if (part.BoatOrdinal < 0 ||
                    !seenOrdinals.Add(part.BoatOrdinal))
                {
                    continue;
                }

                variantBoatCounts[(int)part.Variant]++;
            }

            BoatCount = seenOrdinals.Count;
        }

        public IReadOnlyList<CitySeacoastPartDescriptor> Parts => parts;
        public IReadOnlyList<CitySeacoastLampDescriptor> Lamps => lamps;
        public int Count => parts.Count;

        /// <summary>The whole coast precinct: sand row and sea row.</summary>
        public Rect Grounds { get; }

        public CitySeacoastFrame Frame { get; }

        /// <summary>Distinct hauled hulls in the plan.</summary>
        public int BoatCount { get; }

        public int GetCount(CitySeacoastPartKind kind)
        {
            int count = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetLampCount(CitySeacoastLampKind kind)
        {
            int count = 0;
            for (int index = 0; index < lamps.Count; index++)
            {
                if (lamps[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetBoatVariantCount(CitySeacoastBoatVariant variant)
        {
            return variantBoatCounts[(int)variant];
        }

        /// <summary>
        /// The first part whose id matches, or false. The fisherman's
        /// stance is read out of the pier this way, so the man and the
        /// boards he stands on cannot drift apart.
        /// </summary>
        public bool TryGetPart(
            string stableId,
            out CitySeacoastPartDescriptor part)
        {
            for (int index = 0; index < parts.Count; index++)
            {
                if (string.Equals(
                        parts[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    part = parts[index];
                    return true;
                }
            }

            part = default;
            return false;
        }
    }

    public static class CitySeacoastRules
    {
        public static bool BlocksMovement(CitySeacoastStyle style)
        {
            switch (style)
            {
                // Dune grass parts around a person, the sign face is
                // flush on a board that already collides, and the
                // litter - driftwood, a lost oar, a coil of rope - is
                // ankle-high.
                case CitySeacoastStyle.Grass:
                case CitySeacoastStyle.PaintAccent:
                case CitySeacoastStyle.Litter:
                    return false;
                default:
                    return true;
            }
        }
    }
}
