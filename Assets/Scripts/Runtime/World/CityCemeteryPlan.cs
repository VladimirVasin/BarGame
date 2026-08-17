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
    /// One night-scaled alley lamp. Lamps are real fixtures with a
    /// Light component rather than batched boxes, so the plan carries
    /// them separately from the part list and its budget.
    /// </summary>
    public readonly struct CityCemeteryLampDescriptor :
        IEquatable<CityCemeteryLampDescriptor>
    {
        public CityCemeteryLampDescriptor(
            string stableId,
            Vector3 groundPosition,
            float yawDegrees)
        {
            StableId = stableId ?? string.Empty;
            GroundPosition = groundPosition;
            YawDegrees = yawDegrees;
        }

        public string StableId { get; }
        public Vector3 GroundPosition { get; }
        public float YawDegrees { get; }

        public bool Equals(CityCemeteryLampDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
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
                hash = (hash * 397) ^ GroundPosition.GetHashCode();
                return (hash * 397) ^ YawDegrees.GetHashCode();
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
        private readonly int[] variantGraveCounts;

        internal CityCemeteryPlan(
            IList<CityCemeteryPartDescriptor> partSource,
            IList<CityCemeteryLampDescriptor> lampSource,
            Rect grounds,
            float groundTopY)
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

            Grounds = grounds;
            GroundTopY = groundTopY;

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
        public int Count => parts.Count;
        public Rect Grounds { get; }
        public float GroundTopY { get; }

        /// <summary>Distinct grave ordinals in the plan.</summary>
        public int GraveCount { get; }

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

        public int GetGraveVariantCount(CityCemeteryGraveVariant variant)
        {
            return variantGraveCounts[(int)variant];
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
