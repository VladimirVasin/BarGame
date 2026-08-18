using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityLakePartKind
    {
        Revetment = 0,
        PierPile = 1,
        PierBeam = 2,
        PierDeck = 3,
        PierRail = 4,
        Boat = 6,
        BoatRest = 7,
        Hut = 8,
        HutSign = 9,
        Slipway = 10,
        Bollard = 11,
        Reeds = 12,
        Rock = 13,
        Debris = 14
    }

    /// <summary>
    /// The material family a lake part batches and renders under. The
    /// timber styles are the same boards in different states: the deck a
    /// person walks on is grey and dry, what stands in the water is
    /// tarred, and the hulls carry municipal paint over the tar.
    /// </summary>
    public enum CityLakeStyle
    {
        BankClay = 0,
        Planking = 1,
        TarredTimber = 2,
        HullPaint = 3,
        HullTar = 4,
        Concrete = 5,
        Iron = 6,
        Reeds = 7,
        LakeStone = 8,
        PaintAccent = 9,
        Litter = 10
    }

    /// <summary>
    /// The four hire hulls. The planner cycles all four through the
    /// first four accepted boats, so the row read from the gate shows
    /// the whole vocabulary and the contract "a full boat row shows
    /// every variant" is checkable - the same guarantee the cemetery's
    /// nearest grave row gives.
    /// </summary>
    public enum CityLakeBoatVariant
    {
        PlankSkiff = 0,
        RoundHullDinghy = 1,
        StavedPunt = 2,
        HolledWreck = 3
    }

    public readonly struct CityLakePartDescriptor :
        IEquatable<CityLakePartDescriptor>
    {
        public CityLakePartDescriptor(
            string stableId,
            CityLakePartKind kind,
            CityLakeStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            int boatOrdinal,
            CityLakeBoatVariant variant)
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
        public CityLakePartKind Kind { get; }
        public CityLakeStyle Style { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }

        /// <summary>
        /// The hauled hull this part belongs to, or -1 for the
        /// revetment, the pier, the hut and the scatter, which carry no
        /// boat identity.
        /// </summary>
        public int BoatOrdinal { get; }
        public CityLakeBoatVariant Variant { get; }

        public bool BlocksMovement => CityLakeRules.BlocksMovement(Style);

        public bool Equals(CityLakePartDescriptor other)
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
            return obj is CityLakePartDescriptor other && Equals(other);
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
    /// The two fixtures the boat station lights itself with: the lamp
    /// still burning at the head of the pier, and the hooded bulb over
    /// the rental hut's door.
    /// </summary>
    public enum CityLakeLampKind
    {
        PierHead = 0,
        HutDoor = 1
    }

    /// <summary>
    /// One night-scaled lamp. Lamps are real fixtures with a Light
    /// component rather than batched boxes, so the plan carries them
    /// separately from the part list and its budget. The ground position
    /// is the point on the ground under the fixture and the yaw turns
    /// its local +Z toward the water (for the hut bulb: straight out of
    /// the hut door).
    /// </summary>
    public readonly struct CityLakeLampDescriptor :
        IEquatable<CityLakeLampDescriptor>
    {
        public CityLakeLampDescriptor(
            string stableId,
            CityLakeLampKind kind,
            Vector3 groundPosition,
            float yawDegrees)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            GroundPosition = groundPosition;
            YawDegrees = yawDegrees;
        }

        public string StableId { get; }
        public CityLakeLampKind Kind { get; }
        public Vector3 GroundPosition { get; }
        public float YawDegrees { get; }

        public bool Equals(CityLakeLampDescriptor other)
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
            return obj is CityLakeLampDescriptor other && Equals(other);
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
    /// The shape of the water and of the ground that falls to it.
    ///
    /// The blueprint gives the lake two by two cells of water: 52 metres
    /// of open square, which reads as a municipal reservoir rather than
    /// as a pond, and which meets its shore along a hard cell line. This
    /// is the value object that turns that square into a body of water:
    /// the visible waterline is inset from the cell bounds by the width
    /// of an authored bank and its corners are cut, so the shore always
    /// shows a bank behind it and the silhouette is never a rectangle.
    ///
    /// The water sheet, the bank ring and the bed cap are all derived
    /// from this one struct, which is the only reason they cannot
    /// disagree about where the water ends.
    /// </summary>
    public readonly struct CityLakeBasin
    {
        internal CityLakeBasin(
            Rect waterCellBounds,
            float bevelMeters,
            float bankFlatWidth,
            float bankSlopeWidth,
            float bankTopY,
            float waterTopY,
            float bedTopY)
        {
            WaterCellBounds = waterCellBounds;
            BevelMeters = bevelMeters;
            BankFlatWidth = bankFlatWidth;
            BankSlopeWidth = bankSlopeWidth;
            BankTopY = bankTopY;
            WaterTopY = waterTopY;
            BedTopY = bedTopY;
        }

        /// <summary>The union of the lake's Water cells.</summary>
        public Rect WaterCellBounds { get; }

        /// <summary>The 45-degree cut taken off each waterline corner.</summary>
        public float BevelMeters { get; }

        /// <summary>Level bank at shore height: the boats live here.</summary>
        public float BankFlatWidth { get; }

        /// <summary>The falling part of the bank - the откос proper.</summary>
        public float BankSlopeWidth { get; }

        public float BankTopY { get; }
        public float WaterTopY { get; }
        public float BedTopY { get; }

        public float BankWidth => BankFlatWidth + BankSlopeWidth;

        /// <summary>
        /// The axis-aligned bounds of the waterline, before the corners
        /// are cut. The octagon is this rect minus four corner triangles
        /// of leg <see cref="BevelMeters"/>.
        /// </summary>
        public Rect WaterlineBounds => new Rect(
            WaterCellBounds.xMin + BankWidth,
            WaterCellBounds.yMin + BankWidth,
            Mathf.Max(0f, WaterCellBounds.width - BankWidth * 2f),
            Mathf.Max(0f, WaterCellBounds.height - BankWidth * 2f));

        /// <summary>
        /// Whether a point lies in open water. The corner test is what
        /// makes this an octagon rather than the rect it is built from.
        /// </summary>
        public bool ContainsWaterline(Vector2 xz)
        {
            Rect water = WaterlineBounds;
            if (!water.Contains(xz))
            {
                return false;
            }

            float fromX = Mathf.Min(xz.x - water.xMin, water.xMax - xz.x);
            float fromZ = Mathf.Min(xz.y - water.yMin, water.yMax - xz.y);

            // Inside a cut corner both distances are small, and the cut
            // is the line where they sum to the bevel.
            return fromX + fromZ >= BevelMeters;
        }

        /// <summary>
        /// The waterline octagon, counter-clockwise from the corner
        /// nearest the rect's minimum. Eight points, or four when the
        /// bevel is zero.
        /// </summary>
        public Vector2[] CreateWaterlinePolygon()
        {
            Rect water = WaterlineBounds;
            float bevel = Mathf.Min(
                BevelMeters,
                Mathf.Min(water.width, water.height) * 0.5f);
            if (bevel <= 0f)
            {
                return new[]
                {
                    new Vector2(water.xMin, water.yMin),
                    new Vector2(water.xMax, water.yMin),
                    new Vector2(water.xMax, water.yMax),
                    new Vector2(water.xMin, water.yMax)
                };
            }

            return new[]
            {
                new Vector2(water.xMin + bevel, water.yMin),
                new Vector2(water.xMax - bevel, water.yMin),
                new Vector2(water.xMax, water.yMin + bevel),
                new Vector2(water.xMax, water.yMax - bevel),
                new Vector2(water.xMax - bevel, water.yMax),
                new Vector2(water.xMin + bevel, water.yMax),
                new Vector2(water.xMin, water.yMax - bevel),
                new Vector2(water.xMin, water.yMin + bevel)
            };
        }
    }

    public sealed class CityLakePlan
    {
        /// <summary>
        /// Hard budget for combined-mesh parts. Sized from the default
        /// city's measured dressing (~292 parts: the revetment ring, the
        /// pier, seven hauled hulls, the hut and its sign, the slipway
        /// and the bank scatter) plus 30% for seed variation and for a
        /// larger custom lake. Lamps are fixtures and do not count
        /// against it.
        /// </summary>
        public const int MaximumPartCount = 380;

        private readonly ReadOnlyCollection<CityLakePartDescriptor> parts;
        private readonly ReadOnlyCollection<CityLakeLampDescriptor> lamps;
        private readonly int[] variantBoatCounts;

        internal CityLakePlan(
            IList<CityLakePartDescriptor> partSource,
            IList<CityLakeLampDescriptor> lampSource,
            Rect grounds,
            CityLakeBasin basin)
        {
            var partCopy = new List<CityLakePartDescriptor>(partSource);
            partCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            parts = new ReadOnlyCollection<CityLakePartDescriptor>(
                partCopy);

            var lampCopy = new List<CityLakeLampDescriptor>(lampSource);
            lampCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            lamps = new ReadOnlyCollection<CityLakeLampDescriptor>(
                lampCopy);

            Grounds = grounds;
            Basin = basin;

            variantBoatCounts = new int[4];
            var seenOrdinals = new HashSet<int>();
            for (int index = 0; index < parts.Count; index++)
            {
                CityLakePartDescriptor part = parts[index];
                if (part.BoatOrdinal < 0 ||
                    !seenOrdinals.Add(part.BoatOrdinal))
                {
                    continue;
                }

                variantBoatCounts[(int)part.Variant]++;
            }

            BoatCount = seenOrdinals.Count;
        }

        public IReadOnlyList<CityLakePartDescriptor> Parts => parts;
        public IReadOnlyList<CityLakeLampDescriptor> Lamps => lamps;
        public int Count => parts.Count;

        /// <summary>The whole lake precinct: shore ring and water.</summary>
        public Rect Grounds { get; }

        public CityLakeBasin Basin { get; }

        public float GroundTopY => Basin.BankTopY;

        /// <summary>Distinct hauled hulls in the plan.</summary>
        public int BoatCount { get; }

        public int GetCount(CityLakePartKind kind)
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

        public int GetLampCount(CityLakeLampKind kind)
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

        public int GetBoatVariantCount(CityLakeBoatVariant variant)
        {
            return variantBoatCounts[(int)variant];
        }

        /// <summary>
        /// The first part whose id matches, or false. The fisherman's
        /// stance is read out of the pier this way, so the man and the
        /// boards he sits on cannot drift apart.
        /// </summary>
        public bool TryGetPart(
            string stableId,
            out CityLakePartDescriptor part)
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

    public static class CityLakeRules
    {
        public static bool BlocksMovement(CityLakeStyle style)
        {
            switch (style)
            {
                // Reeds part around a person, the sign face is flush on
                // a board that already collides, and the litter - a lost
                // oar, a coil of rope, a tyre - is ankle-high.
                case CityLakeStyle.Reeds:
                case CityLakeStyle.PaintAccent:
                case CityLakeStyle.Litter:
                    return false;
                default:
                    return true;
            }
        }
    }
}
