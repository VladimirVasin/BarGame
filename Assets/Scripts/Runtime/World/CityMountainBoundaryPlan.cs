using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityMountainBoundarySide
    {
        West = 0,
        South = 1
    }

    /// <summary>
    /// Stable, blueprint-owned vocabulary for the physical mountain rim.
    /// A definition is deliberately opt-in: merely placing similarly named
    /// yards in another blueprint does not silently acquire mountains.
    /// </summary>
    public sealed class CityMountainBoundaryDefinition
    {
        public const string WestSouthAreaId = "yard-west-south";
        public const string WestNorthAreaId = "yard-west-north";
        public const string SouthWestAreaId = "yard-south-west";
        public const string SouthEastAreaId = "yard-south-east";
        public const string TunnelAccessId = "yard-south-west-access";

        public const float RidgeStationSpacing = 22f;
        public const float RidgeMinimumHeight = 18f;
        public const float RidgeMaximumHeight = 27f;
        public const float RidgeMinimumDepth = 16f;
        public const float RidgeMaximumDepth = 22f;
        public const float NorthTaperHeight = 10f;

        public const float TunnelOpeningWidth = 8f;
        public const float TunnelOpeningHeight = 5.5f;
        public const float TunnelThroatDepth = 6f;
        public const float TunnelGateInset = 3.8f;
        public const float TunnelPortalDepth = 1.6f;

        public const float RiverNotchOutwardDepth = 20f;

        private static readonly CityMountainBoundaryDefinition
            DefaultDefinition = new CityMountainBoundaryDefinition(
                CityBlueprintCatalog.DefaultBlueprintId);

        private CityMountainBoundaryDefinition(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public string BlueprintId { get; }

        public static bool TryResolve(
            string blueprintId,
            out CityMountainBoundaryDefinition definition)
        {
            if (string.Equals(
                    blueprintId,
                    CityBlueprintCatalog.DefaultBlueprintId,
                    StringComparison.Ordinal))
            {
                definition = DefaultDefinition;
                return true;
            }

            definition = null;
            return false;
        }

        public static bool IsMountainFacingAreaId(string areaId)
        {
            return string.Equals(
                       areaId,
                       WestSouthAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       WestNorthAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       SouthWestAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       SouthEastAreaId,
                       StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// One cross-section sample of a ridge. WorldXZ is the inner toe on
    /// authoritative city ground. Moving Depth metres along OutwardNormal
    /// reaches the outer foot; the crest rises to PeakY between them.
    /// </summary>
    public readonly struct CityMountainRidgeStation :
        IEquatable<CityMountainRidgeStation>
    {
        public CityMountainRidgeStation(
            string stableId,
            Vector2 worldXZ,
            float baseY,
            float peakY,
            Vector3 outwardNormal,
            float depth,
            int seed)
        {
            StableId = stableId ?? string.Empty;
            WorldXZ = worldXZ;
            BaseY = baseY;
            PeakY = peakY;
            OutwardNormal = outwardNormal;
            Depth = depth;
            Seed = seed;
        }

        public string StableId { get; }
        public Vector2 WorldXZ { get; }
        public float BaseY { get; }
        public float PeakY { get; }
        public Vector3 OutwardNormal { get; }
        public float Depth { get; }
        public int Seed { get; }

        public Vector3 Toe => new Vector3(WorldXZ.x, BaseY, WorldXZ.y);

        public Vector3 OuterFoot => Toe + OutwardNormal * Depth;

        public bool Equals(CityMountainRidgeStation other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   WorldXZ.Equals(other.WorldXZ) &&
                   BaseY.Equals(other.BaseY) &&
                   PeakY.Equals(other.PeakY) &&
                   OutwardNormal.Equals(other.OutwardNormal) &&
                   Depth.Equals(other.Depth) &&
                   Seed == other.Seed;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainRidgeStation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ WorldXZ.GetHashCode();
                hash = (hash * 397) ^ BaseY.GetHashCode();
                hash = (hash * 397) ^ PeakY.GetHashCode();
                hash = (hash * 397) ^ OutwardNormal.GetHashCode();
                hash = (hash * 397) ^ Depth.GetHashCode();
                return (hash * 397) ^ Seed;
            }
        }
    }

    /// <summary>
    /// A continuous strip of physical mountain mesh. South strips are
    /// already split around both the tunnel and river gaps, so builders
    /// must never infer or cut openings themselves.
    /// </summary>
    public sealed class CityMountainRidgeDescriptor
    {
        private readonly ReadOnlyCollection<CityMountainRidgeStation>
            stations;

        internal CityMountainRidgeDescriptor(
            string stableId,
            string sourceAreaId,
            CityMountainBoundarySide side,
            IList<CityMountainRidgeStation> sourceStations,
            bool isSouthWestJoin)
        {
            StableId = stableId ?? string.Empty;
            SourceAreaId = sourceAreaId ?? string.Empty;
            Side = side;
            stations = new ReadOnlyCollection<CityMountainRidgeStation>(
                new List<CityMountainRidgeStation>(sourceStations));
            IsSouthWestJoin = isSouthWestJoin;
            XZBounds = CalculateBounds(stations);
        }

        public string StableId { get; }

        /// <summary>
        /// Area whose continuous top supplied BaseY. Empty only for the
        /// diagonal south-west join, whose endpoint samples are interpolated.
        /// </summary>
        public string SourceAreaId { get; }

        public CityMountainBoundarySide Side { get; }
        public IReadOnlyList<CityMountainRidgeStation> Stations => stations;
        public bool IsSouthWestJoin { get; }

        /// <summary>
        /// Complete XZ envelope including every station's outer foot.
        /// </summary>
        public Rect XZBounds { get; }

        public Vector2 StartXZ => stations.Count == 0
            ? Vector2.zero
            : stations[0].WorldXZ;

        public Vector2 EndXZ => stations.Count == 0
            ? Vector2.zero
            : stations[stations.Count - 1].WorldXZ;

        private static Rect CalculateBounds(
            IReadOnlyList<CityMountainRidgeStation> source)
        {
            if (source.Count == 0)
            {
                return default;
            }

            Vector2 firstOuter = ToXZ(source[0].OuterFoot);
            float xMin = Mathf.Min(source[0].WorldXZ.x, firstOuter.x);
            float xMax = Mathf.Max(source[0].WorldXZ.x, firstOuter.x);
            float zMin = Mathf.Min(source[0].WorldXZ.y, firstOuter.y);
            float zMax = Mathf.Max(source[0].WorldXZ.y, firstOuter.y);
            for (int index = 1; index < source.Count; index++)
            {
                CityMountainRidgeStation station = source[index];
                Vector2 outer = ToXZ(station.OuterFoot);
                xMin = Mathf.Min(xMin, station.WorldXZ.x, outer.x);
                xMax = Mathf.Max(xMax, station.WorldXZ.x, outer.x);
                zMin = Mathf.Min(zMin, station.WorldXZ.y, outer.y);
                zMax = Mathf.Max(zMax, station.WorldXZ.y, outer.y);
            }

            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }

    public readonly struct CityMountainRiverNotchDescriptor :
        IEquatable<CityMountainRiverNotchDescriptor>
    {
        public CityMountainRiverNotchDescriptor(
            string stableId,
            Rect openingBounds,
            Vector3 channelAxis,
            float baseY,
            float westPeakY,
            float eastPeakY)
        {
            StableId = stableId ?? string.Empty;
            OpeningBounds = openingBounds;
            ChannelAxis = channelAxis;
            BaseY = baseY;
            WestPeakY = westPeakY;
            EastPeakY = eastPeakY;
        }

        public string StableId { get; }
        public CityMountainBoundarySide Side =>
            CityMountainBoundarySide.South;
        public Rect OpeningBounds { get; }
        public Vector3 ChannelAxis { get; }
        public float BaseY { get; }
        public float WestPeakY { get; }
        public float EastPeakY { get; }
        public float ClearWidth => OpeningBounds.width;

        public bool Equals(CityMountainRiverNotchDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   OpeningBounds.Equals(other.OpeningBounds) &&
                   ChannelAxis.Equals(other.ChannelAxis) &&
                   BaseY.Equals(other.BaseY) &&
                   WestPeakY.Equals(other.WestPeakY) &&
                   EastPeakY.Equals(other.EastPeakY);
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainRiverNotchDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ OpeningBounds.GetHashCode();
                hash = (hash * 397) ^ ChannelAxis.GetHashCode();
                hash = (hash * 397) ^ BaseY.GetHashCode();
                hash = (hash * 397) ^ WestPeakY.GetHashCode();
                return (hash * 397) ^ EastPeakY.GetHashCode();
            }
        }
    }

    public readonly struct CityMountainTunnelDescriptor :
        IEquatable<CityMountainTunnelDescriptor>
    {
        public CityMountainTunnelDescriptor(
            string stableId,
            string targetAccessId,
            string areaId,
            Vector3 portalGroundCenter,
            Vector3 axis,
            Rect portalBounds,
            Rect approachBounds,
            float openingWidth,
            float openingHeight,
            float throatDepth,
            float gateInset,
            bool isSealed)
        {
            StableId = stableId ?? string.Empty;
            TargetAccessId = targetAccessId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            PortalGroundCenter = portalGroundCenter;
            Axis = axis;
            PortalBounds = portalBounds;
            ApproachBounds = approachBounds;
            OpeningWidth = openingWidth;
            OpeningHeight = openingHeight;
            ThroatDepth = throatDepth;
            GateInset = gateInset;
            IsSealed = isSealed;
        }

        public string StableId { get; }
        public string TargetAccessId { get; }
        public string AreaId { get; }
        public Vector3 PortalGroundCenter { get; }

        /// <summary>Direction from the city into the mountain.</summary>
        public Vector3 Axis { get; }

        public Rect PortalBounds { get; }
        public Rect ApproachBounds { get; }
        public float OpeningWidth { get; }
        public float OpeningHeight { get; }
        public float ThroatDepth { get; }
        public float GateInset { get; }
        public bool IsSealed { get; }

        public bool Equals(CityMountainTunnelDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       TargetAccessId,
                       other.TargetAccessId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       AreaId,
                       other.AreaId,
                       StringComparison.Ordinal) &&
                   PortalGroundCenter.Equals(other.PortalGroundCenter) &&
                   Axis.Equals(other.Axis) &&
                   PortalBounds.Equals(other.PortalBounds) &&
                   ApproachBounds.Equals(other.ApproachBounds) &&
                   OpeningWidth.Equals(other.OpeningWidth) &&
                   OpeningHeight.Equals(other.OpeningHeight) &&
                   ThroatDepth.Equals(other.ThroatDepth) &&
                   GateInset.Equals(other.GateInset) &&
                   IsSealed == other.IsSealed;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainTunnelDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    TargetAccessId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    AreaId ?? string.Empty);
                hash = (hash * 397) ^ PortalGroundCenter.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ PortalBounds.GetHashCode();
                hash = (hash * 397) ^ ApproachBounds.GetHashCode();
                hash = (hash * 397) ^ OpeningWidth.GetHashCode();
                hash = (hash * 397) ^ OpeningHeight.GetHashCode();
                hash = (hash * 397) ^ ThroatDepth.GetHashCode();
                hash = (hash * 397) ^ GateInset.GetHashCode();
                return (hash * 397) ^ IsSealed.GetHashCode();
            }
        }
    }

    public sealed class CityMountainBoundaryPlan
    {
        private static readonly CityMountainBoundaryPlan EmptyPlan =
            new CityMountainBoundaryPlan(
                null,
                Array.Empty<CityMountainRidgeDescriptor>(),
                false,
                default,
                false,
                default);

        private readonly ReadOnlyCollection<CityMountainRidgeDescriptor>
            ridges;

        internal CityMountainBoundaryPlan(
            CityMountainBoundaryDefinition definition,
            IList<CityMountainRidgeDescriptor> ridgeSource,
            bool hasRiverNotch,
            CityMountainRiverNotchDescriptor riverNotch,
            bool hasTunnel,
            CityMountainTunnelDescriptor tunnel)
        {
            Definition = definition;
            ridges = new ReadOnlyCollection<CityMountainRidgeDescriptor>(
                new List<CityMountainRidgeDescriptor>(ridgeSource));
            HasRiverNotch = hasRiverNotch;
            RiverNotch = riverNotch;
            HasTunnel = hasTunnel;
            Tunnel = tunnel;
        }

        public static CityMountainBoundaryPlan Empty => EmptyPlan;
        public CityMountainBoundaryDefinition Definition { get; }
        public bool IsEnabled => Definition != null;
        public IReadOnlyList<CityMountainRidgeDescriptor> Ridges => ridges;
        public int RidgeCount => ridges.Count;
        public bool HasRiverNotch { get; }
        public CityMountainRiverNotchDescriptor RiverNotch { get; }
        public bool HasTunnel { get; }
        public CityMountainTunnelDescriptor Tunnel { get; }

        public int GetRidgeCount(CityMountainBoundarySide side)
        {
            int count = 0;
            for (int index = 0; index < ridges.Count; index++)
            {
                if (ridges[index].Side == side)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
