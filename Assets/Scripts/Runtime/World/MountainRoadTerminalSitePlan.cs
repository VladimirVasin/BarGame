using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a site part belongs to. The group is not a build recipe — every
    /// ordinary part is one oriented box that the world builder batches by
    /// style — it is the semantic band the validator and the tests reason
    /// about, and the prefix of every stable id.
    /// </summary>
    public enum MountainRoadSiteGroup
    {
        /// <summary>The ploughed bank and its gear, left of the arrival.</summary>
        Ploughing = 0,

        /// <summary>Where the road stops: board, barrier, spares.</summary>
        RoadEnd = 1,

        /// <summary>The strip the cafe door opens onto.</summary>
        CafeThreshold = 2,

        /// <summary>The working yard behind the turning circle.</summary>
        ServiceYard = 3,

        /// <summary>The retaining wall, its two flights and the deck.</summary>
        Terrace = 4,

        /// <summary>The parapet, the bench and the edge itself.</summary>
        Brink = 5,

        /// <summary>The cut face the terrace was taken out of.</summary>
        RockCut = 6
    }

    /// <summary>
    /// One surface-and-colour pairing. Every style resolves to one of the
    /// fifteen sheets the mountain already prints or borrows and to a tint
    /// the appearance manifest already carries, so the site adds no sheet
    /// and refits no albedo compensation.
    /// </summary>
    public enum MountainRoadSiteStyle
    {
        /// <summary>Pushed, gritted, refrozen — not the snow that fell.</summary>
        DirtySnow = 0,
        Concrete = 1,

        /// <summary>Coursed and squared: the parapet and its coping.</summary>
        DressedStone = 2,

        /// <summary>The cut face and what has fallen off it.</summary>
        RawStone = 3,
        RustedIron = 4,
        PaintedSteel = 5,

        /// <summary>The red-and-white of a snow pole.</summary>
        PaleEnamel = 6,

        /// <summary>Enamel that has been outside for twenty winters.</summary>
        FadedSign = 7,
        Timber = 8,
        DeadTimber = 9
    }

    /// <summary>One oriented box of site dressing, in world space.</summary>
    public readonly struct MountainRoadSitePartDescriptor :
        IEquatable<MountainRoadSitePartDescriptor>
    {
        internal MountainRoadSitePartDescriptor(
            string stableId,
            MountainRoadSiteGroup group,
            MountainRoadSiteStyle style,
            Vector3 center,
            Vector3 size,
            float yawDegrees,
            bool blocksMovement)
        {
            StableId = stableId ?? string.Empty;
            Group = group;
            Style = style;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
            BlocksMovement = blocksMovement;
        }

        public string StableId { get; }
        public MountainRoadSiteGroup Group { get; }
        public MountainRoadSiteStyle Style { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public float YawDegrees { get; }
        public bool BlocksMovement { get; }

        /// <summary>
        /// The four XZ corners of the footprint, in order. Clearance work
        /// reads these rather than the axis-aligned bounds, because most of
        /// the yard is authored on the plateau's own axes and a few pieces
        /// deliberately are not.
        /// </summary>
        public void GetFootprintCorners(Vector2[] corners)
        {
            if (corners == null)
            {
                throw new ArgumentNullException(nameof(corners));
            }

            if (corners.Length < 4)
            {
                throw new ArgumentException(
                    "Four corners are required.",
                    nameof(corners));
            }

            float radians = YawDegrees * Mathf.Deg2Rad;
            var right = new Vector2(
                Mathf.Cos(radians),
                -Mathf.Sin(radians));
            var forward = new Vector2(
                Mathf.Sin(radians),
                Mathf.Cos(radians));
            var center = new Vector2(Center.x, Center.z);
            float halfX = Size.x * 0.5f;
            float halfZ = Size.z * 0.5f;
            corners[0] = center - right * halfX - forward * halfZ;
            corners[1] = center + right * halfX - forward * halfZ;
            corners[2] = center + right * halfX + forward * halfZ;
            corners[3] = center - right * halfX + forward * halfZ;
        }

        public bool Equals(MountainRoadSitePartDescriptor other)
        {
            return string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
                   Group == other.Group &&
                   Style == other.Style &&
                   Center == other.Center &&
                   Size == other.Size &&
                   Mathf.Approximately(YawDegrees, other.YawDegrees) &&
                   BlocksMovement == other.BlocksMovement;
        }

        public override bool Equals(object obj)
        {
            return obj is MountainRoadSitePartDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StableId.GetHashCode();
        }

        public override string ToString()
        {
            return $"{StableId} {Group}/{Style} @{Center} {Size}";
        }
    }

    /// <summary>
    /// A soft thing on a mast or over a load. It is separate from the
    /// ordinary parts because it is skinned cloth driven by the wind, and
    /// so can never join a static batch.
    /// </summary>
    public readonly struct MountainRoadSiteClothDescriptor :
        IEquatable<MountainRoadSiteClothDescriptor>
    {
        internal MountainRoadSiteClothDescriptor(
            string stableId,
            Vector3 anchor,
            float yawDegrees,
            float width,
            float height,
            bool torn)
        {
            StableId = stableId ?? string.Empty;
            Anchor = anchor;
            YawDegrees = yawDegrees;
            Width = width;
            Height = height;
            Torn = torn;
        }

        public string StableId { get; }
        public Vector3 Anchor { get; }
        public float YawDegrees { get; }
        public float Width { get; }
        public float Height { get; }
        public bool Torn { get; }

        public bool Equals(MountainRoadSiteClothDescriptor other)
        {
            return string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
                   Anchor == other.Anchor &&
                   Mathf.Approximately(YawDegrees, other.YawDegrees) &&
                   Mathf.Approximately(Width, other.Width) &&
                   Mathf.Approximately(Height, other.Height) &&
                   Torn == other.Torn;
        }

        public override bool Equals(object obj)
        {
            return obj is MountainRoadSiteClothDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StableId.GetHashCode();
        }
    }

    /// <summary>One slack run between two posts: a barrier chain or a guy.</summary>
    public readonly struct MountainRoadSiteChainDescriptor :
        IEquatable<MountainRoadSiteChainDescriptor>
    {
        internal MountainRoadSiteChainDescriptor(
            string stableId,
            Vector3 start,
            Vector3 end,
            float sag,
            float thickness)
        {
            StableId = stableId ?? string.Empty;
            Start = start;
            End = end;
            Sag = sag;
            Thickness = thickness;
        }

        public string StableId { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Sag { get; }
        public float Thickness { get; }

        public bool Equals(MountainRoadSiteChainDescriptor other)
        {
            return string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
                   Start == other.Start &&
                   End == other.End &&
                   Mathf.Approximately(Sag, other.Sag) &&
                   Mathf.Approximately(Thickness, other.Thickness);
        }

        public override bool Equals(object obj)
        {
            return obj is MountainRoadSiteChainDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StableId.GetHashCode();
        }
    }

    /// <summary>
    /// One sittable place on the summit, in the shape
    /// <see cref="CityBenchSeat"/> wants. The site owns two: the bench on
    /// the brink and one of the cafe's two deliberately empty stools.
    /// </summary>
    public readonly struct MountainRoadSiteSeatDescriptor
    {
        internal MountainRoadSiteSeatDescriptor(
            string stableId,
            Vector3 seatTopCenter,
            float seatWidth,
            float seatDepth,
            float groundY,
            Vector3 faceDirection)
        {
            StableId = stableId ?? string.Empty;
            SeatTopCenter = seatTopCenter;
            SeatWidth = seatWidth;
            SeatDepth = seatDepth;
            GroundY = groundY;
            FaceDirection = faceDirection.normalized;
        }

        public string StableId { get; }
        public Vector3 SeatTopCenter { get; }
        public float SeatWidth { get; }
        public float SeatDepth { get; }
        public float GroundY { get; }
        public Vector3 FaceDirection { get; }

        public CityBenchSeat ToBenchSeat()
        {
            return new CityBenchSeat(
                StableId,
                SeatTopCenter,
                SeatWidth,
                SeatDepth,
                GroundY,
                FaceDirection);
        }
    }

    /// <summary>
    /// The one practical the yard owns. It is described here and built by
    /// <see cref="MountainRoadAtmosphere"/>, so every real light on the
    /// mountain still has a single owner.
    /// </summary>
    public readonly struct MountainRoadSitePracticalDescriptor
    {
        internal MountainRoadSitePracticalDescriptor(
            string stableId,
            Vector3 position,
            Vector3 direction,
            float range,
            float spotAngle)
        {
            StableId = stableId ?? string.Empty;
            Position = position;
            Direction = direction.normalized;
            Range = range;
            SpotAngle = spotAngle;
        }

        public string StableId { get; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
        public float Range { get; }
        public float SpotAngle { get; }
    }

    /// <summary>
    /// The dressed summit: everything on the terminal plateau that is not
    /// the road surface, the cafe or the cableway.
    /// </summary>
    public sealed class MountainRoadTerminalSitePlan
    {
        /// <summary>
        /// A ceiling rather than a target. It exists so a future edit that
        /// starts generating parts in a loop fails a test instead of
        /// quietly doubling the summit's draw cost.
        /// </summary>
        public const int MaximumPartCount = 160;

        private readonly ReadOnlyCollection<MountainRoadSitePartDescriptor> parts;
        private readonly ReadOnlyCollection<MountainRoadSiteClothDescriptor> cloth;
        private readonly ReadOnlyCollection<MountainRoadSiteChainDescriptor> chains;

        internal MountainRoadTerminalSitePlan(
            IList<MountainRoadSitePartDescriptor> sourceParts,
            IList<MountainRoadSiteClothDescriptor> sourceCloth,
            IList<MountainRoadSiteChainDescriptor> sourceChains,
            float yardTopY,
            float terraceTopY,
            MountainRoadSiteSeatDescriptor brinkSeat,
            MountainRoadSiteSeatDescriptor counterSeat,
            MountainRoadSitePracticalDescriptor yardLamp)
        {
            if (sourceParts == null)
            {
                throw new ArgumentNullException(nameof(sourceParts));
            }

            parts = new ReadOnlyCollection<MountainRoadSitePartDescriptor>(
                new List<MountainRoadSitePartDescriptor>(sourceParts));
            cloth = new ReadOnlyCollection<MountainRoadSiteClothDescriptor>(
                new List<MountainRoadSiteClothDescriptor>(sourceCloth));
            chains = new ReadOnlyCollection<MountainRoadSiteChainDescriptor>(
                new List<MountainRoadSiteChainDescriptor>(sourceChains));
            YardTopY = yardTopY;
            TerraceTopY = terraceTopY;
            BrinkSeat = brinkSeat;
            CounterSeat = counterSeat;
            YardLamp = yardLamp;
        }

        public IReadOnlyList<MountainRoadSitePartDescriptor> Parts => parts;
        public IReadOnlyList<MountainRoadSiteClothDescriptor> Cloth => cloth;
        public IReadOnlyList<MountainRoadSiteChainDescriptor> Chains => chains;

        /// <summary>
        /// The two heights every part is measured from, so nothing on the
        /// summit can drift: the yard is the surface the player actually
        /// walks on, and the terrace is one retaining wall above it.
        /// </summary>
        public float YardTopY { get; }

        public float TerraceTopY { get; }
        public MountainRoadSiteSeatDescriptor BrinkSeat { get; }
        public MountainRoadSiteSeatDescriptor CounterSeat { get; }
        public MountainRoadSitePracticalDescriptor YardLamp { get; }

        public int GetCount(MountainRoadSiteGroup group)
        {
            int total = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                if (parts[index].Group == group)
                {
                    total++;
                }
            }

            return total;
        }

        public bool TryGetPart(
            string stableId,
            out MountainRoadSitePartDescriptor part)
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
}
