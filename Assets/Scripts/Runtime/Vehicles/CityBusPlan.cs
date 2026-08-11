using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusRouteLinkKind
    {
        Straight = 0,
        LeftTurn,
        RightTurn
    }

    public enum CityBusClearanceFailureKind
    {
        None = 0,
        OutsideStreetSurface,
        SidewalkOverlap,
        CurvatureTooTight,
        IntersectionApronUnavailable
    }

    public enum CityBusStopOrigin
    {
        MappedShelter = 0,
        RouteNative
    }

    public sealed class CityBusDesignVehicle
    {
        private CityBusDesignVehicle()
        {
        }

        public static CityBusDesignVehicle Default { get; } =
            new CityBusDesignVehicle();

        public float BodyLength => 8.25f;
        public float BodyWidth => 2.38f;
        public float BodyHeight => 2.95f;
        public float Wheelbase => 4.5f;
        public float FrontOverhang => 1.875f;
        public float RearOverhang => 1.875f;
        public float ClearanceMargin => 0.1f;
        public float MinimumBodyCenterTurnRadius => 4.5f;
        public Vector3 LocalForward => Vector3.forward;
        public float InflatedLength =>
            BodyLength + (ClearanceMargin * 2f);
        public float InflatedWidth =>
            BodyWidth + (ClearanceMargin * 2f);
        public float RearAxleLocalZ =>
            (-BodyLength * 0.5f) + RearOverhang;
        public float FrontAxleLocalZ => RearAxleLocalZ + Wheelbase;
    }

    public readonly struct CityBusPathSample
    {
        internal CityBusPathSample(
            Vector3 position,
            Vector3 forward,
            float distance)
        {
            Position = position;
            Forward = forward;
            Distance = distance;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public float Distance { get; }
    }

    public readonly struct CityBusClearanceResult
    {
        internal CityBusClearanceResult(
            bool isClear,
            CityBusClearanceFailureKind failureKind,
            int failedSampleIndex,
            Vector3 failedPosition,
            float provenClearanceMargin)
        {
            IsClear = isClear;
            FailureKind = failureKind;
            FailedSampleIndex = failedSampleIndex;
            FailedPosition = failedPosition;
            ProvenClearanceMargin = provenClearanceMargin;
        }

        public bool IsClear { get; }
        public CityBusClearanceFailureKind FailureKind { get; }
        public int FailedSampleIndex { get; }
        public Vector3 FailedPosition { get; }
        public float ProvenClearanceMargin { get; }
    }

    public sealed class CityBusRouteNode
    {
        internal CityBusRouteNode(
            string id,
            Vector3 position,
            Vector3 forward,
            RoadEdge roadEdge,
            Vector2Int fromGridNode,
            Vector2Int toGridNode,
            IList<int> outgoingLinkIndices)
        {
            Id = id ?? string.Empty;
            Position = position;
            Forward = forward;
            RoadEdge = roadEdge;
            FromGridNode = fromGridNode;
            ToGridNode = toGridNode;
            OutgoingLinkIndices = Copy(outgoingLinkIndices);
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public RoadEdge RoadEdge { get; }
        public Vector2Int FromGridNode { get; }
        public Vector2Int ToGridNode { get; }
        public IReadOnlyList<int> OutgoingLinkIndices { get; }

        private static IReadOnlyList<int> Copy(IList<int> source)
        {
            return new ReadOnlyCollection<int>(
                new List<int>(source ??
                    throw new ArgumentNullException(nameof(source))));
        }
    }

    public sealed class CityBusRouteLink
    {
        internal CityBusRouteLink(
            string id,
            int fromNodeIndex,
            int toNodeIndex,
            CityBusRouteLinkKind kind,
            Vector2Int junctionNode,
            IList<CityBusPathSample> samples,
            float minimumTurnRadius,
            CityBusClearanceResult clearance)
        {
            Id = id ?? string.Empty;
            FromNodeIndex = fromNodeIndex;
            ToNodeIndex = toNodeIndex;
            Kind = kind;
            JunctionNode = junctionNode;
            Samples = new ReadOnlyCollection<CityBusPathSample>(
                new List<CityBusPathSample>(samples ??
                    throw new ArgumentNullException(nameof(samples))));
            MinimumTurnRadius = minimumTurnRadius;
            Clearance = clearance;
            Length = Samples.Count == 0
                ? 0f
                : Samples[Samples.Count - 1].Distance;
        }

        public string Id { get; }
        public int FromNodeIndex { get; }
        public int ToNodeIndex { get; }
        public CityBusRouteLinkKind Kind { get; }
        public Vector2Int JunctionNode { get; }
        public IReadOnlyList<CityBusPathSample> Samples { get; }
        public float Length { get; }
        public float MinimumTurnRadius { get; }
        public CityBusClearanceResult Clearance { get; }
    }

    public sealed class CityBusClearanceFailure
    {
        internal CityBusClearanceFailure(
            string id,
            RoadEdge fromRoadEdge,
            RoadEdge toRoadEdge,
            Vector2Int fromGridNode,
            Vector2Int junctionNode,
            Vector2Int toGridNode,
            CityBusRouteLinkKind kind,
            IList<CityBusPathSample> samples,
            float minimumTurnRadius,
            CityBusClearanceResult clearance)
        {
            Id = id ?? string.Empty;
            FromRoadEdge = fromRoadEdge;
            ToRoadEdge = toRoadEdge;
            FromGridNode = fromGridNode;
            JunctionNode = junctionNode;
            ToGridNode = toGridNode;
            Kind = kind;
            Samples = new ReadOnlyCollection<CityBusPathSample>(
                new List<CityBusPathSample>(samples ??
                    throw new ArgumentNullException(nameof(samples))));
            MinimumTurnRadius = minimumTurnRadius;
            Clearance = clearance;
        }

        public string Id { get; }
        public RoadEdge FromRoadEdge { get; }
        public RoadEdge ToRoadEdge { get; }
        public Vector2Int FromGridNode { get; }
        public Vector2Int JunctionNode { get; }
        public Vector2Int ToGridNode { get; }
        public CityBusRouteLinkKind Kind { get; }
        public IReadOnlyList<CityBusPathSample> Samples { get; }
        public float MinimumTurnRadius { get; }
        public CityBusClearanceResult Clearance { get; }
    }

    public sealed class CityBusSpawnAnchor
    {
        internal CityBusSpawnAnchor(
            string id,
            int linkIndex,
            float distanceAlongLink,
            Vector3 position,
            Vector3 forward,
            RoadEdge roadEdge)
        {
            Id = id ?? string.Empty;
            LinkIndex = linkIndex;
            DistanceAlongLink = distanceAlongLink;
            Position = position;
            Forward = forward;
            RoadEdge = roadEdge;
        }

        public string Id { get; }
        public int LinkIndex { get; }
        public float DistanceAlongLink { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public RoadEdge RoadEdge { get; }
    }

    public sealed class CityBusStopDescriptor
    {
        internal CityBusStopDescriptor(
            string id,
            string sourceDecorationId,
            Vector3 shelterPosition,
            int linkIndex,
            float distanceAlongLink,
            Vector3 position,
            Vector3 forward,
            RoadEdge roadEdge)
            : this(
                id,
                sourceDecorationId,
                string.Empty,
                -1,
                distanceAlongLink,
                default,
                shelterPosition,
                -new Vector3(forward.z, 0f, -forward.x),
                linkIndex,
                distanceAlongLink,
                position,
                forward,
                roadEdge)
        {
        }

        internal CityBusStopDescriptor(
            string id,
            string sourceDecorationId,
            string nameLocalizationKey,
            int sequenceIndex,
            float distanceAlongLoop,
            CityDistrictKind district,
            Vector3 shelterPosition,
            Vector3 roadsideForward,
            int linkIndex,
            float distanceAlongLink,
            Vector3 position,
            Vector3 forward,
            RoadEdge roadEdge)
        {
            Id = id ?? string.Empty;
            SourceDecorationId = sourceDecorationId ?? string.Empty;
            NameLocalizationKey = nameLocalizationKey ?? string.Empty;
            SequenceIndex = sequenceIndex;
            DistanceAlongLoop = distanceAlongLoop;
            District = district;
            ShelterPosition = shelterPosition;
            RoadsideForward = roadsideForward;
            LinkIndex = linkIndex;
            DistanceAlongLink = distanceAlongLink;
            Position = position;
            Forward = forward;
            RoadEdge = roadEdge;
        }

        public string Id { get; }
        public string SourceDecorationId { get; }
        public string NameLocalizationKey { get; }
        public int SequenceIndex { get; }
        public float DistanceAlongLoop { get; }
        public CityDistrictKind District { get; }
        public CityBusStopOrigin Origin =>
            string.IsNullOrEmpty(SourceDecorationId)
                ? CityBusStopOrigin.RouteNative
                : CityBusStopOrigin.MappedShelter;
        public bool HasMappedShelter =>
            Origin == CityBusStopOrigin.MappedShelter;
        public Vector3 ShelterPosition { get; }
        public Vector3 RoadsideForward { get; }
        public int LinkIndex { get; }
        public float DistanceAlongLink { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public RoadEdge RoadEdge { get; }
    }

    public sealed class CityBusPlan
    {
        internal CityBusPlan(
            int layoutSeed,
            int decorationSeed,
            uint stableSeed,
            float laneCenterOffset,
            CityBusDesignVehicle vehicle,
            IList<CityBusRouteNode> nodes,
            IList<CityBusRouteLink> links,
            IList<CityBusSpawnAnchor> spawnAnchors,
            IList<CityBusStopDescriptor> stops,
            IList<CityBusClearanceFailure> clearanceFailures,
            int streetStateCount,
            int clearanceAcceptedLinkCount)
            : this(
                layoutSeed,
                decorationSeed,
                stableSeed,
                string.Empty,
                CreateSequentialIndices(links),
                SumLengths(links),
                laneCenterOffset,
                vehicle,
                nodes,
                links,
                spawnAnchors,
                stops,
                clearanceFailures,
                streetStateCount,
                clearanceAcceptedLinkCount)
        {
        }

        internal CityBusPlan(
            int layoutSeed,
            int decorationSeed,
            uint stableSeed,
            string routeId,
            IList<int> orderedLinkIndices,
            float loopLength,
            float laneCenterOffset,
            CityBusDesignVehicle vehicle,
            IList<CityBusRouteNode> nodes,
            IList<CityBusRouteLink> links,
            IList<CityBusSpawnAnchor> spawnAnchors,
            IList<CityBusStopDescriptor> stops,
            IList<CityBusClearanceFailure> clearanceFailures,
            int streetStateCount,
            int clearanceAcceptedLinkCount)
        {
            LayoutSeed = layoutSeed;
            DecorationSeed = decorationSeed;
            StableSeed = stableSeed;
            RouteId = routeId ?? string.Empty;
            OrderedLinkIndices = Copy(orderedLinkIndices);
            LoopLength = loopLength;
            LaneCenterOffset = laneCenterOffset;
            Vehicle = vehicle ??
                throw new ArgumentNullException(nameof(vehicle));
            Nodes = Copy(nodes);
            Links = Copy(links);
            SpawnAnchors = Copy(spawnAnchors);
            Stops = Copy(stops);
            ClearanceFailures = Copy(clearanceFailures);
            StreetStateCount = streetStateCount;
            ClearanceAcceptedLinkCount = clearanceAcceptedLinkCount;
            stopIndicesByLink = BuildStopIndex(stops, links.Count);
            nextOrderedLinkByLink = BuildOrderedSuccessors(
                orderedLinkIndices,
                links.Count);
        }

        private readonly IReadOnlyList<int>[] stopIndicesByLink;
        private readonly int[] nextOrderedLinkByLink;

        public int LayoutSeed { get; }
        public int DecorationSeed { get; }
        public uint StableSeed { get; }
        public string RouteId { get; }
        public IReadOnlyList<int> OrderedLinkIndices { get; }
        public float LoopLength { get; }
        public float LaneCenterOffset { get; }
        public CityBusDesignVehicle Vehicle { get; }
        public IReadOnlyList<CityBusRouteNode> Nodes { get; }
        public IReadOnlyList<CityBusRouteLink> Links { get; }
        public IReadOnlyList<CityBusSpawnAnchor> SpawnAnchors { get; }
        public IReadOnlyList<CityBusStopDescriptor> Stops { get; }
        public IReadOnlyList<CityBusClearanceFailure> ClearanceFailures
        {
            get;
        }
        public int StreetStateCount { get; }
        public int ClearanceAcceptedLinkCount { get; }
        public bool IsEmpty => Nodes.Count == 0 ||
                               Links.Count == 0 ||
                               SpawnAnchors.Count == 0;

        public IReadOnlyList<int> GetOutgoingLinkIndices(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= Nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeIndex));
            }

            return Nodes[nodeIndex].OutgoingLinkIndices;
        }

        public IReadOnlyList<int> GetStopIndices(int linkIndex)
        {
            if (linkIndex < 0 || linkIndex >= Links.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(linkIndex));
            }

            return stopIndicesByLink[linkIndex];
        }

        public int GetNextOrderedLinkIndex(int linkIndex)
        {
            if (linkIndex < 0 || linkIndex >= Links.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(linkIndex));
            }

            int next = nextOrderedLinkByLink[linkIndex];
            if (next < 0)
            {
                throw new InvalidOperationException(
                    "The route link does not belong to the ordered loop.");
            }

            return next;
        }

        private static IReadOnlyList<T> Copy<T>(IList<T> source)
        {
            return new ReadOnlyCollection<T>(
                new List<T>(source ??
                    throw new ArgumentNullException(nameof(source))));
        }

        private static IReadOnlyList<int>[] BuildStopIndex(
            IList<CityBusStopDescriptor> stops,
            int linkCount)
        {
            var mutable = new List<int>[linkCount];
            for (int index = 0; index < mutable.Length; index++)
            {
                mutable[index] = new List<int>();
            }

            for (int index = 0; index < stops.Count; index++)
            {
                CityBusStopDescriptor stop = stops[index];
                if (stop.LinkIndex < 0 || stop.LinkIndex >= linkCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(stops),
                        "A bus stop references an unknown route link.");
                }

                mutable[stop.LinkIndex].Add(index);
            }

            var result = new IReadOnlyList<int>[linkCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new ReadOnlyCollection<int>(
                    mutable[index]);
            }

            return result;
        }

        private static List<int> CreateSequentialIndices<T>(
            IList<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var result = new List<int>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                result.Add(index);
            }

            return result;
        }

        private static float SumLengths(IList<CityBusRouteLink> links)
        {
            if (links == null)
            {
                throw new ArgumentNullException(nameof(links));
            }

            float result = 0f;
            for (int index = 0; index < links.Count; index++)
            {
                result += links[index].Length;
            }

            return result;
        }

        private static int[] BuildOrderedSuccessors(
            IList<int> orderedLinkIndices,
            int linkCount)
        {
            if (orderedLinkIndices == null)
            {
                throw new ArgumentNullException(nameof(orderedLinkIndices));
            }

            var result = new int[linkCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = -1;
            }

            for (int index = 0;
                 index < orderedLinkIndices.Count;
                 index++)
            {
                int linkIndex = orderedLinkIndices[index];
                if (linkIndex < 0 || linkIndex >= linkCount ||
                    result[linkIndex] >= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(orderedLinkIndices),
                        "Ordered route links must be unique and in range.");
                }

                result[linkIndex] = orderedLinkIndices[
                    (index + 1) % orderedLinkIndices.Count];
            }

            return result;
        }
    }
}
