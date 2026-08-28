using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCemeteryPartKind
    {
        Alley = 0,
        FenceRail = 1,
        FencePost = 2,
        CornerPillar = 3,
        GatePillar = 4,
        GateArch = 5,
        GateLeaf = 6,
        GraveSlab = 7,
        GraveMonument = 8,
        GraveEnclosure = 9,
        GraveOffering = 10,
        TreeTrunk = 11,
        TreeCrown = 12,
        Bush = 13,
        Bench = 14,
        Lodge = 15
    }

    /// <summary>
    /// The material family a cemetery part batches and renders under.
    /// The three stone styles are the same granite/limestone silhouettes
    /// in different states of upkeep, so one seed mixes dark polished,
    /// pale and weathered monuments inside a single ground palette.
    /// </summary>
    public enum CityCemeteryStyle
    {
        Gravel = 0,
        Iron = 1,
        GraniteDark = 2,
        MarbleLight = 3,
        WeatheredConcrete = 4,
        Soil = 5,
        TrunkDark = 6,
        TrunkBirch = 7,
        FoliageDark = 8,
        Flowers = 9,
        Timber = 10
    }

    /// <summary>
    /// The six authored monument silhouettes. The planner guarantees the
    /// first six accepted graves cycle through every variant, so the row
    /// nearest the gate reads as a showcase of the whole vocabulary and
    /// the contract "at least five distinct grave types" is checkable.
    /// </summary>
    public enum CityCemeteryGraveVariant
    {
        ClassicStele = 0,
        ArchedHeadstone = 1,
        OrthodoxCross = 2,
        Obelisk = 3,
        FamilyMonument = 4,
        OvergrownSlab = 5
    }

    public readonly struct CityCemeteryPartDescriptor :
        IEquatable<CityCemeteryPartDescriptor>
    {
        public CityCemeteryPartDescriptor(
            string stableId,
            CityCemeteryPartKind kind,
            CityCemeteryStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            int graveOrdinal,
            CityCemeteryGraveVariant variant)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Style = style;
            Center = center;
            Rotation = rotation;
            Size = size;
            GraveOrdinal = graveOrdinal;
            Variant = variant;
        }

        public string StableId { get; }
        public CityCemeteryPartKind Kind { get; }
        public CityCemeteryStyle Style { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }

        /// <summary>
        /// The grave this part belongs to, or -1 for fence, gate, alley
        /// and vegetation parts that carry no grave identity.
        /// </summary>
        public int GraveOrdinal { get; }
        public CityCemeteryGraveVariant Variant { get; }

        public bool BlocksMovement =>
            CityCemeteryRules.BlocksMovement(Style);

        public bool Equals(CityCemeteryPartDescriptor other)
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
                   GraveOrdinal == other.GraveOrdinal &&
                   Variant == other.Variant;
        }

        public override bool Equals(object obj)
        {
            return obj is CityCemeteryPartDescriptor other &&
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
                hash = (hash * 397) ^ GraveOrdinal;
                return (hash * 397) ^ (int)Variant;
            }
        }
    }

    /// <summary>
    /// The two fixtures the cemetery lights itself with: the cast-iron
    /// mantles walking the main alley, and the single hooded bulb
    /// hanging under the gate lodge's eave over its doorstep.
    /// </summary>
    public enum CityCemeteryLampKind
    {
        Alley = 0,
        LodgePorch = 1
    }

    /// <summary>
    /// One night-scaled lamp. Lamps are real fixtures with a Light
    /// component rather than batched boxes, so the plan carries them
    /// separately from the part list and its budget. The ground
    /// position is the point on the ground under the fixture and the
    /// yaw turns its local +Z along the alley's depth axis (for the
    /// porch bulb: straight out of the lodge door).
    /// </summary>
    public readonly struct CityCemeteryLampDescriptor :
        IEquatable<CityCemeteryLampDescriptor>
    {
        public CityCemeteryLampDescriptor(
            string stableId,
            CityCemeteryLampKind kind,
            Vector3 groundPosition,
            float yawDegrees)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            GroundPosition = groundPosition;
            YawDegrees = yawDegrees;
        }

        public string StableId { get; }
        public CityCemeteryLampKind Kind { get; }
        public Vector3 GroundPosition { get; }
        public float YawDegrees { get; }

        public bool Equals(CityCemeteryLampDescriptor other)
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
            return obj is CityCemeteryLampDescriptor other &&
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
    /// What a cell of the burial lattice holds. Every cell inside the
    /// fence is one of the three: a monument stands on it, it is clear
    /// ground waiting for a burial, or something else already owns the
    /// square metre and nobody will ever be laid there.
    /// </summary>
    public enum CityCemeteryPlotState
    {
        /// <summary>A monument stands here.</summary>
        Occupied = 0,

        /// <summary>
        /// Clear, buriable ground: no alley, no lamp, no bench, no
        /// lodge, no tree and no street approach touches it. A new
        /// grave can be raised here without moving anything.
        /// </summary>
        Vacant = 1,

        /// <summary>
        /// Lattice ground that is not burial ground: the gravel
        /// alleys and their margin, the lamp and bench footprints,
        /// the watchman's pocket, the canonical street approach, or a
        /// tree or bush already standing on it.
        /// </summary>
        Obstructed = 2
    }

    /// <summary>
    /// One cell of the cemetery's burial lattice. The lattice covers
    /// the whole dressable interior at the grave pitch, so the three
    /// plot states partition the entire precinct: what is buried, what
    /// is free to bury, and what will never take a coffin.
    ///
    /// Geometry is fixed per cell by the same deterministic hash that
    /// poses the monuments, so a vacant plot already knows exactly
    /// where a future grave's ground point, heading and envelope will
    /// be — burying somebody later moves nothing that is already
    /// standing.
    /// </summary>
    public readonly struct CityCemeteryPlotDescriptor :
        IEquatable<CityCemeteryPlotDescriptor>
    {
        public CityCemeteryPlotDescriptor(
            string stableId,
            int row,
            int column,
            CityCemeteryPlotState state,
            int graveOrdinal,
            Vector3 ground,
            Quaternion yaw,
            Rect footprint)
        {
            StableId = stableId ?? string.Empty;
            Row = row;
            Column = column;
            State = state;
            GraveOrdinal = graveOrdinal;
            Ground = ground;
            Yaw = yaw;
            Footprint = footprint;
        }

        public string StableId { get; }

        /// <summary>Lattice row, counted from the gate inwards.</summary>
        public int Row { get; }

        /// <summary>Lattice column, counted from the lateral minimum.</summary>
        public int Column { get; }

        public CityCemeteryPlotState State { get; }

        /// <summary>
        /// The grave standing on this plot, or -1 for a vacant or
        /// obstructed one.
        /// </summary>
        public int GraveOrdinal { get; }

        /// <summary>
        /// Ground point of the monument: where the slab sits today for
        /// an occupied plot, and where it would sit for a vacant one.
        /// </summary>
        public Vector3 Ground { get; }

        /// <summary>Heading of the monument on this plot.</summary>
        public Quaternion Yaw { get; }

        /// <summary>
        /// XZ envelope the plot reserves, enclosure included. Two
        /// plots never overlap.
        /// </summary>
        public Rect Footprint { get; }

        public bool IsVacant => State == CityCemeteryPlotState.Vacant;

        public bool Equals(CityCemeteryPlotDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Row == other.Row &&
                   Column == other.Column &&
                   State == other.State &&
                   GraveOrdinal == other.GraveOrdinal &&
                   Ground.Equals(other.Ground) &&
                   Yaw.Equals(other.Yaw) &&
                   Footprint.Equals(other.Footprint);
        }

        public override bool Equals(object obj)
        {
            return obj is CityCemeteryPlotDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ Row;
                hash = (hash * 397) ^ Column;
                hash = (hash * 397) ^ (int)State;
                hash = (hash * 397) ^ GraveOrdinal;
                hash = (hash * 397) ^ Ground.GetHashCode();
                hash = (hash * 397) ^ Yaw.GetHashCode();
                return (hash * 397) ^ Footprint.GetHashCode();
            }
        }
    }

    public sealed class CityCemeteryPlan
    {
        /// <summary>
        /// Hard budget for combined-mesh parts. Sized from the default
        /// city's measured dressing (~480 parts: graves with posted
        /// enclosures, fence, gate, alleys, benches and vegetation)
        /// plus slack for seed variation. Lamps are fixtures and do
        /// not count against it.
        /// </summary>
        public const int MaximumPartCount = 560;

        private readonly ReadOnlyCollection<CityCemeteryPartDescriptor>
            parts;
        private readonly ReadOnlyCollection<CityCemeteryLampDescriptor>
            lamps;
        private readonly ReadOnlyCollection<CityCemeteryPlotDescriptor>
            plots;
        private readonly int[] variantGraveCounts;
        private readonly int[] plotStateCounts;

        internal CityCemeteryPlan(
            IList<CityCemeteryPartDescriptor> partSource,
            IList<CityCemeteryLampDescriptor> lampSource,
            IList<CityCemeteryPlotDescriptor> plotSource,
            Rect grounds,
            float groundTopY,
            CityChurchCemeteryPassagePlan churchPassage)
        {
            var partCopy = new List<CityCemeteryPartDescriptor>(
                partSource);
            partCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            parts = new ReadOnlyCollection<CityCemeteryPartDescriptor>(
                partCopy);

            var lampCopy = new List<CityCemeteryLampDescriptor>(
                lampSource);
            lampCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            lamps = new ReadOnlyCollection<CityCemeteryLampDescriptor>(
                lampCopy);

            var plotCopy = new List<CityCemeteryPlotDescriptor>(
                plotSource);
            plotCopy.Sort((left, right) => string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal));
            plots = new ReadOnlyCollection<CityCemeteryPlotDescriptor>(
                plotCopy);

            plotStateCounts = new int[3];
            for (int index = 0; index < plots.Count; index++)
            {
                plotStateCounts[(int)plots[index].State]++;
            }

            Grounds = grounds;
            GroundTopY = groundTopY;
            ChurchPassage = churchPassage;

            variantGraveCounts = new int[6];
            var seenOrdinals = new HashSet<int>();
            for (int index = 0; index < parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = parts[index];
                if (part.GraveOrdinal < 0 ||
                    !seenOrdinals.Add(part.GraveOrdinal))
                {
                    continue;
                }

                variantGraveCounts[(int)part.Variant]++;
            }

            GraveCount = seenOrdinals.Count;
        }

        public IReadOnlyList<CityCemeteryPartDescriptor> Parts => parts;
        public IReadOnlyList<CityCemeteryLampDescriptor> Lamps => lamps;

        /// <summary>
        /// Every cell of the burial lattice, in row-major order: the
        /// whole precinct divided into occupied, vacant and obstructed
        /// ground. Lamps and parts describe what stands in the
        /// cemetery; plots describe what the cemetery still has room
        /// for.
        /// </summary>
        public IReadOnlyList<CityCemeteryPlotDescriptor> Plots => plots;
        public int Count => parts.Count;
        public Rect Grounds { get; }
        public float GroundTopY { get; }

        /// <summary>
        /// The optional non-street opening into the adjoining church yard.
        /// It is null for cemetery-only and non-canonical layouts.
        /// </summary>
        public CityChurchCemeteryPassagePlan ChurchPassage { get; }

        /// <summary>Distinct grave ordinals in the plan.</summary>
        public int GraveCount { get; }

        /// <summary>Cells of the burial lattice, all states.</summary>
        public int PlotCount => plots.Count;

        /// <summary>Plots a monument already stands on.</summary>
        public int OccupiedPlotCount =>
            plotStateCounts[(int)CityCemeteryPlotState.Occupied];

        /// <summary>
        /// Plots a new grave can be raised on today, without moving
        /// anything already standing.
        /// </summary>
        public int VacantPlotCount =>
            plotStateCounts[(int)CityCemeteryPlotState.Vacant];

        /// <summary>Lattice cells that are not burial ground.</summary>
        public int ObstructedPlotCount =>
            plotStateCounts[(int)CityCemeteryPlotState.Obstructed];

        public int GetCount(CityCemeteryPartKind kind)
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

        public int GetLampCount(CityCemeteryLampKind kind)
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

        public int GetGraveVariantCount(CityCemeteryGraveVariant variant)
        {
            return variantGraveCounts[(int)variant];
        }

        public int GetPlotCount(CityCemeteryPlotState state)
        {
            return plotStateCounts[(int)state];
        }

        /// <summary>
        /// The next free plot for a burial, or false when the yard is
        /// full. Plots are ordered row-major from the gate inwards, so
        /// the graveyard fills the way a real one does: the rows
        /// nearest the entrance first.
        /// </summary>
        public bool TryGetNextVacantPlot(
            out CityCemeteryPlotDescriptor plot)
        {
            for (int index = 0; index < plots.Count; index++)
            {
                if (plots[index].State == CityCemeteryPlotState.Vacant)
                {
                    plot = plots[index];
                    return true;
                }
            }

            plot = default;
            return false;
        }
    }

    public static class CityCemeteryRules
    {
        public static bool BlocksMovement(CityCemeteryStyle style)
        {
            switch (style)
            {
                // Alley gravel and grave mounds are ground dressing, the
                // crowns float overhead, and flowers are ankle-high.
                case CityCemeteryStyle.Gravel:
                case CityCemeteryStyle.Soil:
                case CityCemeteryStyle.FoliageDark:
                case CityCemeteryStyle.Flowers:
                    return false;
                default:
                    return true;
            }
        }
    }
}
