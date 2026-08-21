using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The five authored readings of the default city's typed perimeter
    /// Yards. Four belong to the old west/south municipal service belt; the
    /// east utility edge deliberately stays low and does not imply a ridge.
    /// </summary>
    public enum CityFringeYardKind
    {
        WestStoneTerraces = 0,
        WestIndustrialBelt = 1,
        SouthTunnelForecourt = 2,
        SouthFloodWorks = 3,
        EastUtilityEdge = 4
    }

    public enum CityFringeYardPartKind
    {
        ServiceTrack = 0,
        AccessApron = 1,
        WheelRut = 2,
        DrainChannel = 3,
        DrainCover = 4,
        RetainingSection = 5,
        Buttress = 6,
        UtilityPole = 7,
        UtilityCrossarm = 8,
        UtilityCable = 9,
        RepairPad = 10,
        RepairStack = 11,
        RockfallMass = 12,
        Gabion = 13,
        UtilityShed = 14,
        UtilityDoor = 15,
        EarthBerm = 16,
        TunnelCheek = 17,
        TerraceShelf = 18,
        CulvertHeadwall = 19,
        RepairFrame = 20,
        PipeStock = 21,
        PracticalHousing = 22,
        GabionCage = 23,
        FloodGauge = 24,
        SiltFan = 25
    }

    /// <summary>
    /// Small shared material vocabulary. It is intentionally made from
    /// existing city sheets: the edge needs authorship, not a parallel art
    /// pipeline.
    /// </summary>
    public enum CityFringeYardStyle
    {
        ServiceGround = 0,
        Drainage = 1,
        OldMasonry = 2,
        Concrete = 3,
        Gabion = 4,
        Iron = 5,
        UtilityPaint = 6,
        Rock = 7,
        ServiceTrack = 8
    }

    public readonly struct CityFringeYardPartDescriptor :
        IEquatable<CityFringeYardPartDescriptor>
    {
        public CityFringeYardPartDescriptor(
            string stableId,
            string areaId,
            CityFringeYardPartKind kind,
            CityFringeYardStyle style,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement)
        {
            StableId = stableId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            Kind = kind;
            Style = style;
            Center = center;
            Rotation = rotation;
            Size = size;
            BlocksMovement = blocksMovement;
        }

        public string StableId { get; }
        public string AreaId { get; }
        public CityFringeYardPartKind Kind { get; }
        public CityFringeYardStyle Style { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
        public bool BlocksMovement { get; }

        /// <summary>The conservative XZ envelope used by clearance tests.</summary>
        public Rect Footprint
        {
            get
            {
                Vector3 half = Size * 0.5f;
                Vector3 right = Rotation * Vector3.right;
                Vector3 up = Rotation * Vector3.up;
                Vector3 forward = Rotation * Vector3.forward;
                float halfX = Mathf.Abs(right.x) * half.x +
                              Mathf.Abs(up.x) * half.y +
                              Mathf.Abs(forward.x) * half.z;
                float halfZ = Mathf.Abs(right.z) * half.x +
                              Mathf.Abs(up.z) * half.y +
                              Mathf.Abs(forward.z) * half.z;
                return Rect.MinMaxRect(
                    Center.x - halfX,
                    Center.z - halfZ,
                    Center.x + halfX,
                    Center.z + halfZ);
            }
        }

        public bool Equals(CityFringeYardPartDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Style == other.Style &&
                   Center.Equals(other.Center) &&
                   Rotation.Equals(other.Rotation) &&
                   Size.Equals(other.Size) &&
                   BlocksMovement == other.BlocksMovement;
        }

        public override bool Equals(object obj)
        {
            return obj is CityFringeYardPartDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    AreaId ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (int)Style;
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Size.GetHashCode();
                return (hash * 397) ^ BlocksMovement.GetHashCode();
            }
        }
    }

    public sealed class CityFringeYardDescriptor
    {
        private readonly ReadOnlyCollection<CityFringeYardPartDescriptor>
            parts;

        internal CityFringeYardDescriptor(
            string stableId,
            string areaId,
            CityFringeYardKind kind,
            Rect areaBounds,
            CityOpenAreaAccessDescriptor access,
            Rect traversalBounds,
            IList<CityFringeYardPartDescriptor> sourceParts)
        {
            StableId = stableId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            Kind = kind;
            AreaBounds = areaBounds;
            Access = access;
            TraversalBounds = traversalBounds;
            parts = new ReadOnlyCollection<CityFringeYardPartDescriptor>(
                new List<CityFringeYardPartDescriptor>(sourceParts));
        }

        public string StableId { get; }
        public string AreaId { get; }
        public CityFringeYardKind Kind { get; }
        public Rect AreaBounds { get; }
        public CityOpenAreaAccessDescriptor Access { get; }

        /// <summary>
        /// Reserved route from the declared street opening to the service
        /// trace. For the south-west site it continues all the way to the
        /// sealed portal.
        /// </summary>
        public Rect TraversalBounds { get; }

        public IReadOnlyList<CityFringeYardPartDescriptor> Parts => parts;
    }

    public readonly struct CityTunnelForecourtDescriptor :
        IEquatable<CityTunnelForecourtDescriptor>
    {
        public CityTunnelForecourtDescriptor(
            string stableId,
            string areaId,
            string targetAccessId,
            string tunnelStableId,
            Vector3 streetAnchor,
            Vector3 portalAnchor,
            Vector3 axis,
            Rect approachBounds,
            Rect driveClearBounds,
            float approachWidth,
            float driveClearWidth,
            bool isSealed)
        {
            StableId = stableId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            TargetAccessId = targetAccessId ?? string.Empty;
            TunnelStableId = tunnelStableId ?? string.Empty;
            StreetAnchor = streetAnchor;
            PortalAnchor = portalAnchor;
            Axis = axis;
            ApproachBounds = approachBounds;
            DriveClearBounds = driveClearBounds;
            ApproachWidth = approachWidth;
            DriveClearWidth = driveClearWidth;
            IsSealed = isSealed;
        }

        public string StableId { get; }
        public string AreaId { get; }
        public string TargetAccessId { get; }
        public string TunnelStableId { get; }
        public Vector3 StreetAnchor { get; }
        public Vector3 PortalAnchor { get; }
        public Vector3 Axis { get; }
        public Rect ApproachBounds { get; }
        public Rect DriveClearBounds { get; }
        public float ApproachWidth { get; }
        public float DriveClearWidth { get; }
        public bool IsSealed { get; }

        public bool Equals(CityTunnelForecourtDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       TargetAccessId,
                       other.TargetAccessId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       TunnelStableId,
                       other.TunnelStableId,
                       StringComparison.Ordinal) &&
                   StreetAnchor.Equals(other.StreetAnchor) &&
                   PortalAnchor.Equals(other.PortalAnchor) &&
                   Axis.Equals(other.Axis) &&
                   ApproachBounds.Equals(other.ApproachBounds) &&
                   DriveClearBounds.Equals(other.DriveClearBounds) &&
                   ApproachWidth.Equals(other.ApproachWidth) &&
                   DriveClearWidth.Equals(other.DriveClearWidth) &&
                   IsSealed == other.IsSealed;
        }

        public override bool Equals(object obj)
        {
            return obj is CityTunnelForecourtDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    AreaId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    TargetAccessId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    TunnelStableId ?? string.Empty);
                hash = (hash * 397) ^ StreetAnchor.GetHashCode();
                hash = (hash * 397) ^ PortalAnchor.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ ApproachBounds.GetHashCode();
                hash = (hash * 397) ^ DriveClearBounds.GetHashCode();
                hash = (hash * 397) ^ ApproachWidth.GetHashCode();
                hash = (hash * 397) ^ DriveClearWidth.GetHashCode();
                return (hash * 397) ^ IsSealed.GetHashCode();
            }
        }
    }

    public sealed class CityFringeYardPlan
    {
        private static readonly CityFringeYardPlan EmptyPlan =
            new CityFringeYardPlan(
                Array.Empty<CityFringeYardDescriptor>(),
                false,
                default,
                Array.Empty<CityFringeYardPracticalDescriptor>());

        private readonly ReadOnlyCollection<CityFringeYardDescriptor> yards;
        private readonly ReadOnlyCollection<
            CityFringeYardPracticalDescriptor> practicals;

        internal CityFringeYardPlan(
            IList<CityFringeYardDescriptor> source,
            bool hasTunnelForecourt,
            CityTunnelForecourtDescriptor tunnelForecourt,
            IList<CityFringeYardPracticalDescriptor> sourcePracticals)
        {
            yards = new ReadOnlyCollection<CityFringeYardDescriptor>(
                new List<CityFringeYardDescriptor>(source));
            int partCount = 0;
            for (int index = 0; index < yards.Count; index++)
            {
                partCount += yards[index].Parts.Count;
            }

            PartCount = partCount;
            HasTunnelForecourt = hasTunnelForecourt;
            TunnelForecourt = tunnelForecourt;
            practicals = new ReadOnlyCollection<
                CityFringeYardPracticalDescriptor>(
                new List<CityFringeYardPracticalDescriptor>(
                    sourcePracticals));
        }

        public static CityFringeYardPlan Empty => EmptyPlan;
        public bool IsEnabled => yards.Count > 0;
        public IReadOnlyList<CityFringeYardDescriptor> Yards => yards;
        public int PartCount { get; }
        public bool HasTunnelForecourt { get; }
        public CityTunnelForecourtDescriptor TunnelForecourt { get; }
        public IReadOnlyList<CityFringeYardPracticalDescriptor> Practicals =>
            practicals;

        public bool TryGetYard(
            string areaId,
            out CityFringeYardDescriptor yard)
        {
            for (int index = 0; index < yards.Count; index++)
            {
                CityFringeYardDescriptor candidate = yards[index];
                if (string.Equals(
                        candidate.AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    yard = candidate;
                    return true;
                }
            }

            yard = null;
            return false;
        }
    }
}
