using System;

namespace BarPromenade
{
    /// <summary>
    /// The one non-light neighbour motif admitted to a transition block.
    /// Mass is intentionally absent: the owning district always controls it.
    /// </summary>
    public enum CityDistrictTransitionMotif
    {
        None = 0,
        Frontage = 1,
        Window = 2,
        Wear = 3
    }

    /// <summary>
    /// Deterministic variation and neighbour influence for one presentation
    /// channel. A future builder may map <see cref="VariationKey"/> onto any
    /// family-specific variant count without changing the planner contract.
    /// </summary>
    public readonly struct CityDistrictChannelPresentation :
        IEquatable<CityDistrictChannelPresentation>
    {
        internal CityDistrictChannelPresentation(
            uint variationKey,
            float neighbourInfluence)
        {
            if (float.IsNaN(neighbourInfluence) ||
                float.IsInfinity(neighbourInfluence) ||
                neighbourInfluence < 0f || neighbourInfluence > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(neighbourInfluence));
            }

            VariationKey = variationKey;
            NeighbourInfluence = neighbourInfluence;
        }

        public uint VariationKey { get; }
        public float NeighbourInfluence { get; }
        public bool UsesNeighbour => NeighbourInfluence > 0f;

        public int SelectVariant(int variantCount)
        {
            if (variantCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(variantCount));
            }

            return (int)(VariationKey % (uint)variantCount);
        }

        public float Blend(float dominant, float neighbour)
        {
            return dominant + ((neighbour - dominant) *
                               NeighbourInfluence);
        }

        public bool Equals(CityDistrictChannelPresentation other)
        {
            return VariationKey == other.VariationKey &&
                   NeighbourInfluence.Equals(other.NeighbourInfluence);
        }

        public override bool Equals(object obj)
        {
            return obj is CityDistrictChannelPresentation other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)VariationKey * 397) ^
                       NeighbourInfluence.GetHashCode();
            }
        }
    }

    public readonly struct CityDistrictTransitionPresentation :
        IEquatable<CityDistrictTransitionPresentation>
    {
        internal CityDistrictTransitionPresentation(
            int boundaryDistanceBlocks,
            int spanBlocks,
            CityDistrictKind? neighbourDistrict,
            CityDistrictTransitionMotif motif,
            float motifInfluence,
            float lightInfluence)
        {
            BoundaryDistanceBlocks = boundaryDistanceBlocks;
            SpanBlocks = spanBlocks;
            NeighbourDistrict = neighbourDistrict;
            Motif = motif;
            MotifInfluence = motifInfluence;
            LightInfluence = lightInfluence;
        }

        /// <summary>
        /// Zero means the block touches the district boundary. With the
        /// current one-block span, any value of one or more is interior.
        /// </summary>
        public int BoundaryDistanceBlocks { get; }

        public int SpanBlocks { get; }
        public CityDistrictKind? NeighbourDistrict { get; }
        public CityDistrictTransitionMotif Motif { get; }
        public float MotifInfluence { get; }
        public float LightInfluence { get; }

        public bool IsActive =>
            NeighbourDistrict.HasValue &&
            BoundaryDistanceBlocks < SpanBlocks &&
            Motif != CityDistrictTransitionMotif.None;

        public bool Equals(CityDistrictTransitionPresentation other)
        {
            return BoundaryDistanceBlocks == other.BoundaryDistanceBlocks &&
                   SpanBlocks == other.SpanBlocks &&
                   NeighbourDistrict == other.NeighbourDistrict &&
                   Motif == other.Motif &&
                   MotifInfluence.Equals(other.MotifInfluence) &&
                   LightInfluence.Equals(other.LightInfluence);
        }

        public override bool Equals(object obj)
        {
            return obj is CityDistrictTransitionPresentation other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BoundaryDistanceBlocks;
                hash = (hash * 397) ^ SpanBlocks;
                hash = (hash * 397) ^ NeighbourDistrict.GetHashCode();
                hash = (hash * 397) ^ (int)Motif;
                hash = (hash * 397) ^ MotifInfluence.GetHashCode();
                return (hash * 397) ^ LightInfluence.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Immutable, pure presentation decision for one city block. It does not
    /// alter layout, land use, colliders or serialized scene state.
    /// </summary>
    public sealed class CityDistrictPresentationPlan
    {
        internal CityDistrictPresentationPlan(
            int citySeed,
            int blockX,
            int blockZ,
            CityDistrictArtProfile dominantProfile,
            CityDistrictArtProfile neighbourProfile,
            CityDistrictChannelPresentation frontage,
            CityDistrictChannelPresentation mass,
            CityDistrictChannelPresentation window,
            CityDistrictChannelPresentation light,
            CityDistrictChannelPresentation wear,
            CityDistrictTransitionPresentation transition)
        {
            CitySeed = citySeed;
            BlockX = blockX;
            BlockZ = blockZ;
            DominantProfile = dominantProfile ??
                throw new ArgumentNullException(nameof(dominantProfile));
            NeighbourProfile = neighbourProfile;
            Frontage = frontage;
            Mass = mass;
            Window = window;
            Light = light;
            Wear = wear;
            Transition = transition;
        }

        public int CitySeed { get; }
        public int BlockX { get; }
        public int BlockZ { get; }
        public CityDistrictArtProfile DominantProfile { get; }

        /// <summary>
        /// Null for a request with no neighbouring urban district. It may be
        /// non-null on an interior block, but every influence remains zero.
        /// </summary>
        public CityDistrictArtProfile NeighbourProfile { get; }

        public CityDistrictChannelPresentation Frontage { get; }
        public CityDistrictChannelPresentation Mass { get; }
        public CityDistrictChannelPresentation Window { get; }
        public CityDistrictChannelPresentation Light { get; }
        public CityDistrictChannelPresentation Wear { get; }
        public CityDistrictTransitionPresentation Transition { get; }

        public bool IsTransitionBlock => Transition.IsActive;
    }
}
