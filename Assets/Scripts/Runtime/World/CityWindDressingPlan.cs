using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The dressed zone a wind-dressing piece belongs to. Only zones
    /// that carry cloth are listed: the bar-side yard, the lighthouse
    /// island and the drained-lake block are deliberate zeroes — the
    /// yard is authored by subtraction, the island is sub-pixel scenery
    /// behind the fog, and the lake block has no dresser to hang from.
    /// </summary>
    public enum CityWindDressingZone
    {
        OldTown = 0,
        Residential = 1,
        Industrial = 2,
        Nightlife = 3,
        Park = 4,
        Seacoast = 5,
        Cemetery = 6,
        FringeYards = 7
    }

    /// <summary>
    /// The cloth and rope prop families. Every family hangs from a
    /// structure another plan already draws, or from the poles this
    /// plan draws itself.
    /// </summary>
    public enum CityWindDressingKind
    {
        MarketAwningRag = 0,
        ScaffoldShroud = 1,
        RopeEnd = 2,
        CourtyardLaundry = 3,
        RackTarp = 4,
        FireEscapeBanner = 5,
        // 6 was BillboardSkirt: every nightlife billboard rides a
        // tower whose roof is ~50 m up, where a torn hem is
        // sub-pixel. A deliberate hole — do not renumber or reuse.
        BandstandPennant = 7,
        PierNet = 8,
        MooringRopeEnd = 9,
        WreathRibbon = 10,
        ServiceTarp = 11,
        CableTail = 12,
        SlingRopeEnd = 13
    }

    /// <summary>
    /// The static parts this plan draws itself so no cloth ever floats:
    /// courtyard line poles, the sagging rope chords between them, and
    /// thin pin battens where an anchor structure has no member exactly
    /// at the pin line.
    /// </summary>
    public enum CityWindDressingSupportKind
    {
        LinePole = 0,
        RopeChord = 1,
        PinBatten = 2
    }

    /// <summary>
    /// One simulated cloth piece: the pivot is the panel's top-center
    /// pin point in world space, exactly as
    /// <see cref="ClothPanelFactory.CreateHangingRag"/> takes it.
    /// </summary>
    public readonly struct CityWindDressingClothDescriptor :
        IEquatable<CityWindDressingClothDescriptor>
    {
        public CityWindDressingClothDescriptor(
            string stableId,
            CityWindDressingKind kind,
            CityWindDressingZone zone,
            Vector3 position,
            float yawDegrees,
            float width,
            float height,
            Color color,
            int tornVariant,
            int columns,
            int rows,
            bool registerBody)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Zone = zone;
            Position = position;
            YawDegrees = yawDegrees;
            Width = width;
            Height = height;
            Color = color;
            TornVariant = tornVariant;
            Columns = columns;
            Rows = rows;
            RegisterBody = registerBody;
        }

        public string StableId { get; }
        public CityWindDressingKind Kind { get; }
        public CityWindDressingZone Zone { get; }
        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public float Width { get; }
        public float Height { get; }
        public Color Color { get; }
        public int TornVariant { get; }
        public int Columns { get; }
        public int Rows { get; }

        /// <summary>
        /// True only for cloth hanging at body height over walkable
        /// ground: the hero's capsule parts it instead of clipping.
        /// Capsule-list rewrites cost per registered cloth, so high
        /// and out-of-reach pieces stay out of the body registry.
        /// </summary>
        public bool RegisterBody { get; }

        /// <summary>
        /// Narrow strips read as rope and skip the cloth albedo sheet:
        /// at rope width the weave is sub-pixel and the factory's flat
        /// colour is the honest render.
        /// </summary>
        public bool IsRopeStrip =>
            Width <= CityWindDressingPlan.RopeStripMaximumWidth;

        public bool Equals(CityWindDressingClothDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Zone == other.Zone &&
                   Position.Equals(other.Position) &&
                   YawDegrees.Equals(other.YawDegrees) &&
                   Width.Equals(other.Width) &&
                   Height.Equals(other.Height) &&
                   Color.Equals(other.Color) &&
                   TornVariant == other.TornVariant &&
                   Columns == other.Columns &&
                   Rows == other.Rows &&
                   RegisterBody == other.RegisterBody;
        }

        public override bool Equals(object obj)
        {
            return obj is CityWindDressingClothDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (int)Zone;
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ YawDegrees.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                hash = (hash * 397) ^ Color.GetHashCode();
                hash = (hash * 397) ^ TornVariant;
                hash = (hash * 397) ^ Columns;
                hash = (hash * 397) ^ Rows;
                return (hash * 397) ^ (RegisterBody ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// One static support box. Only line poles block movement; rope
    /// chords and pin battens hang overhead.
    /// </summary>
    public readonly struct CityWindDressingSupportDescriptor :
        IEquatable<CityWindDressingSupportDescriptor>
    {
        public CityWindDressingSupportDescriptor(
            string stableId,
            CityWindDressingSupportKind kind,
            CityWindDressingZone zone,
            RuntimeOrientedBox box)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Zone = zone;
            Box = box;
        }

        public string StableId { get; }
        public CityWindDressingSupportKind Kind { get; }
        public CityWindDressingZone Zone { get; }
        public RuntimeOrientedBox Box { get; }

        public bool BlocksMovement =>
            CityWindDressingRules.BlocksMovement(Kind);

        public bool Equals(CityWindDressingSupportDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Zone == other.Zone &&
                   Box.Center.Equals(other.Box.Center) &&
                   Box.Rotation.Equals(other.Box.Rotation) &&
                   Box.Size.Equals(other.Box.Size);
        }

        public override bool Equals(object obj)
        {
            return obj is CityWindDressingSupportDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (int)Zone;
                hash = (hash * 397) ^ Box.Center.GetHashCode();
                hash = (hash * 397) ^ Box.Rotation.GetHashCode();
                return (hash * 397) ^ Box.Size.GetHashCode();
            }
        }
    }

    /// <summary>
    /// The immutable city-wide wind-dressing plan: every simulated
    /// cloth piece and every static support, budget-capped as a whole
    /// and per zone. The caps are the restraint mechanism the art
    /// bible asks for as much as they are a physics budget — each
    /// cloth is a live PhysX sim, paused only while culled.
    /// </summary>
    public sealed class CityWindDressingPlan
    {
        public const int MaximumClothCount = 64;

        /// <summary>
        /// At or under this width a piece reads as rope, not fabric,
        /// and skips the cloth albedo sheet.
        /// </summary>
        public const float RopeStripMaximumWidth = 0.12f;

        private readonly ReadOnlyCollection<
            CityWindDressingClothDescriptor> cloths;

        private readonly ReadOnlyCollection<
            CityWindDressingSupportDescriptor> supports;

        internal CityWindDressingPlan(
            IList<CityWindDressingClothDescriptor> clothSource,
            IList<CityWindDressingSupportDescriptor> supportSource)
        {
            var clothCopy = new List<CityWindDressingClothDescriptor>(
                clothSource);
            clothCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            cloths = new ReadOnlyCollection<
                CityWindDressingClothDescriptor>(clothCopy);

            var supportCopy =
                new List<CityWindDressingSupportDescriptor>(
                    supportSource);
            supportCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            supports = new ReadOnlyCollection<
                CityWindDressingSupportDescriptor>(supportCopy);
        }

        public IReadOnlyList<CityWindDressingClothDescriptor> Cloths =>
            cloths;

        public IReadOnlyList<CityWindDressingSupportDescriptor>
            Supports => supports;

        public int ClothCount => cloths.Count;

        public int GetClothCount(CityWindDressingZone zone)
        {
            int count = 0;
            for (int index = 0; index < cloths.Count; index++)
            {
                if (cloths[index].Zone == zone)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetClothCount(CityWindDressingKind kind)
        {
            int count = 0;
            for (int index = 0; index < cloths.Count; index++)
            {
                if (cloths[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static class CityWindDressingRules
    {
        public static bool BlocksMovement(
            CityWindDressingSupportKind kind)
        {
            switch (kind)
            {
                // Rope chords and pin battens hang overhead; only a
                // planted pole may stop a body.
                case CityWindDressingSupportKind.LinePole:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The per-zone cloth budget. These are maxima, not
        /// guarantees: a seed whose anchors leave no room simply
        /// hangs less.
        /// </summary>
        public static int MaximumClothCount(CityWindDressingZone zone)
        {
            switch (zone)
            {
                case CityWindDressingZone.OldTown:
                    return 12;
                case CityWindDressingZone.Residential:
                    return 14;
                case CityWindDressingZone.Industrial:
                    return 8;
                case CityWindDressingZone.Nightlife:
                    return 10;
                // §10 of the art bible: the park's emptiness matters
                // more than its object count.
                case CityWindDressingZone.Park:
                    return 1;
                case CityWindDressingZone.Seacoast:
                    return 4;
                case CityWindDressingZone.Cemetery:
                    return 2;
                case CityWindDressingZone.FringeYards:
                    return 6;
                default:
                    return 0;
            }
        }
    }
}
