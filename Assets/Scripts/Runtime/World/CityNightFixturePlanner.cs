using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityNightFixturePlanner
    {
        public const float FirstLampEdgeT = 0.28f;
        public const float SecondLampEdgeT = 0.72f;
        public const int MaximumSignalIntersections = 6;

        private const float FixtureRoadClearance = 0.75f;
        private const uint LampSideSalt = 0x4C414D50u;
        private const uint SignalSelectionSalt = 0x53454C45u;
        private const uint SignalCornerSalt = 0x434F524Eu;
        private const uint SignalPhaseSalt = 0x50484153u;

        public static CityNightFixturePlan CreatePlan(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var streetLamps = new List<StreetLampDescriptor>(
                checked(layout.RoadEdges.Count * 2));
            CreateStreetLamps(layout, streetLamps);

            var trafficSignals = new List<TrafficSignalDescriptor>(
                MaximumSignalIntersections * 2);
            CreateTrafficSignals(layout, trafficSignals);

            return new CityNightFixturePlan(streetLamps, trafficSignals);
        }

        private static void CreateStreetLamps(
            CityLayout layout,
            ICollection<StreetLampDescriptor> target)
        {
            var sortedEdges = new List<RoadEdge>(layout.RoadEdges);
            sortedEdges.Sort(RoadEdge.Compare);

            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                uint sideHash = StableHash(
                    layout.Seed,
                    edge.A.x,
                    edge.A.y,
                    edge.B.x,
                    edge.B.y,
                    LampSideSalt);
                StreetLampSide firstSide = (sideHash & 1u) == 0u
                    ? StreetLampSide.Left
                    : StreetLampSide.Right;
                StreetLampSide secondSide = firstSide == StreetLampSide.Left
                    ? StreetLampSide.Right
                    : StreetLampSide.Left;

                target.Add(CreateStreetLamp(
                    layout,
                    edge,
                    FirstLampEdgeT,
                    firstSide));
                target.Add(CreateStreetLamp(
                    layout,
                    edge,
                    SecondLampEdgeT,
                    secondSide));
            }
        }

        private static StreetLampDescriptor CreateStreetLamp(
            CityLayout layout,
            RoadEdge edge,
            float edgeT,
            StreetLampSide side)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector3 tangent = (end - start).normalized;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            float sideMultiplier = side == StreetLampSide.Left ? 1f : -1f;
            Vector3 outward = left * sideMultiplier;
            float offset = (layout.RoadWidth * 0.5f) + FixtureRoadClearance;
            Vector3 centerlinePosition = Vector3.Lerp(start, end, edgeT);

            return new StreetLampDescriptor(
                edge,
                edgeT,
                side,
                centerlinePosition + (outward * offset),
                -outward);
        }

        private static void CreateTrafficSignals(
            CityLayout layout,
            ICollection<TrafficSignalDescriptor> target)
        {
            Dictionary<Vector2Int, int> degrees = CountNodeDegrees(layout);
            var candidates = new List<SignalCandidate>();

            foreach (KeyValuePair<Vector2Int, int> pair in degrees)
            {
                if (pair.Value < 3 ||
                    TouchesParkPath(layout, pair.Key))
                {
                    continue;
                }

                uint rank = StableHash(
                    layout.Seed,
                    pair.Key.x,
                    pair.Key.y,
                    SignalSelectionSalt);
                candidates.Add(new SignalCandidate(pair.Key, rank));
            }

            candidates.Sort(CompareSignalCandidates);
            int count = Mathf.Min(
                MaximumSignalIntersections,
                candidates.Count);
            for (int index = 0; index < count; index++)
            {
                CreateTrafficSignalPair(
                    layout,
                    candidates[index].Node,
                    target);
            }
        }

        private static bool TouchesParkPath(
            CityLayout layout,
            Vector2Int node)
        {
            for (int index = 0;
                 index < layout.RoadEdges.Count;
                 index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (edge.Contains(node) &&
                    layout.GetPathKind(edge) ==
                    CityPathKind.ParkPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<Vector2Int, int> CountNodeDegrees(
            CityLayout layout)
        {
            var degrees = new Dictionary<Vector2Int, int>(
                layout.Nodes.Count);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                degrees.Add(layout.Nodes[index], 0);
            }

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                degrees[edge.A] = degrees[edge.A] + 1;
                degrees[edge.B] = degrees[edge.B] + 1;
            }

            return degrees;
        }

        private static void CreateTrafficSignalPair(
            CityLayout layout,
            Vector2Int node,
            ICollection<TrafficSignalDescriptor> target)
        {
            uint cornerHash = StableHash(
                layout.Seed,
                node.x,
                node.y,
                SignalCornerSalt);
            float zSign = (cornerHash & 1u) == 0u ? 1f : -1f;
            Vector3 cornerDirection = new Vector3(1f, 0f, zSign);
            float offset = (layout.RoadWidth * 0.5f) + FixtureRoadClearance;
            Vector3 nodePosition = layout.GetNodeWorldPosition(node);
            Vector3 firstPosition = nodePosition + (cornerDirection * offset);
            Vector3 secondPosition = nodePosition - (cornerDirection * offset);
            float phase = HashToUnitFloat(StableHash(
                layout.Seed,
                node.x,
                node.y,
                SignalPhaseSalt));

            target.Add(new TrafficSignalDescriptor(
                node,
                0,
                firstPosition,
                (nodePosition - firstPosition).normalized,
                phase));
            target.Add(new TrafficSignalDescriptor(
                node,
                1,
                secondPosition,
                (nodePosition - secondPosition).normalized,
                phase));
        }

        private static int CompareSignalCandidates(
            SignalCandidate left,
            SignalCandidate right)
        {
            int rankComparison = left.Rank.CompareTo(right.Rank);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int xComparison = left.Node.x.CompareTo(right.Node.x);
            return xComparison != 0
                ? xComparison
                : left.Node.y.CompareTo(right.Node.y);
        }

        private static float HashToUnitFloat(uint hash)
        {
            return (hash >> 8) * (1f / 16777216f);
        }

        private static uint StableHash(
            int seed,
            int firstX,
            int firstY,
            int secondX,
            int secondY,
            uint salt)
        {
            uint hash = StableHash(
                unchecked((uint)seed),
                unchecked((uint)firstX));
            hash = StableHash(hash, unchecked((uint)firstY));
            hash = StableHash(hash, unchecked((uint)secondX));
            hash = StableHash(hash, unchecked((uint)secondY));
            return StableHash(hash, salt);
        }

        private static uint StableHash(
            int seed,
            int x,
            int y,
            uint salt)
        {
            uint hash = StableHash(
                unchecked((uint)seed),
                unchecked((uint)x));
            hash = StableHash(hash, unchecked((uint)y));
            return StableHash(hash, salt);
        }

        private static uint StableHash(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        private readonly struct SignalCandidate
        {
            public SignalCandidate(Vector2Int node, uint rank)
            {
                Node = node;
                Rank = rank;
            }

            public Vector2Int Node { get; }
            public uint Rank { get; }
        }
    }
}
