using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public static class CityStreetIntersectionSelector
    {
        public const int MaximumIntersectionCount = 6;

        private const float FixtureRoadClearance = 0.75f;
        private const float PublicSpaceFixtureClearance = 1f;
        private const uint SelectionSalt = 0x53454C45u;
        private const uint CornerSalt = 0x434F524Eu;

        public static IReadOnlyList<Vector2Int> Select(
            CityLayout layout,
            int maximumCount = MaximumIntersectionCount)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (maximumCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            }

            Dictionary<Vector2Int, int> degrees = CountNodeDegrees(layout);
            var candidates = new List<IntersectionCandidate>();
            foreach (KeyValuePair<Vector2Int, int> pair in degrees)
            {
                if (pair.Value < 3 || TouchesParkPath(layout, pair.Key))
                {
                    continue;
                }

                uint rank = StableHash(
                    layout.Seed,
                    pair.Key.x,
                    pair.Key.y,
                    SelectionSalt);
                candidates.Add(new IntersectionCandidate(pair.Key, rank));
            }

            candidates.Sort(CompareCandidates);
            var selected = new List<Vector2Int>(
                Mathf.Min(maximumCount, candidates.Count));
            for (int index = 0;
                 index < candidates.Count && selected.Count < maximumCount;
                 index++)
            {
                Vector2Int node = candidates[index].Node;
                if (CanPlacePairedFixtures(layout, node))
                {
                    selected.Add(node);
                }
            }

            return new ReadOnlyCollection<Vector2Int>(selected);
        }

        internal static Vector3 GetPairedFixtureOffset(
            CityLayout layout,
            Vector2Int node)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            uint cornerHash = StableHash(
                layout.Seed,
                node.x,
                node.y,
                CornerSalt);
            float zSign = (cornerHash & 1u) == 0u ? 1f : -1f;
            float offset =
                (layout.RoadWidth * 0.5f) + FixtureRoadClearance;
            return new Vector3(offset, 0f, zSign * offset);
        }

        private static bool CanPlacePairedFixtures(
            CityLayout layout,
            Vector2Int node)
        {
            Vector3 center = layout.GetNodeWorldPosition(node);
            Vector3 offset = GetPairedFixtureOffset(layout, node);
            return !IsFixtureBlocked(layout, center + offset) &&
                   !IsFixtureBlocked(layout, center - offset);
        }

        private static bool IsFixtureBlocked(
            CityLayout layout,
            Vector3 position)
        {
            if (layout.IsWater(position))
            {
                return true;
            }

            Vector2 point = new Vector2(position.x, position.z);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor pointOfInterest =
                    layout.DistrictPointsOfInterest[index];
                if (ContainsExpanded(
                        pointOfInterest.PublicBounds,
                        point,
                        PublicSpaceFixtureClearance))
                {
                    return true;
                }

                for (int accessIndex = 0;
                     accessIndex < pointOfInterest.Accesses.Count;
                     accessIndex++)
                {
                    if (ContainsExpanded(
                            pointOfInterest.Accesses[accessIndex]
                                .ApproachBounds,
                            point,
                            PublicSpaceFixtureClearance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsExpanded(
            Rect bounds,
            Vector2 point,
            float expansion)
        {
            return point.x >= bounds.xMin - expansion &&
                   point.x <= bounds.xMax + expansion &&
                   point.y >= bounds.yMin - expansion &&
                   point.y <= bounds.yMax + expansion;
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
                    layout.GetPathKind(edge) == CityPathKind.ParkPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareCandidates(
            IntersectionCandidate left,
            IntersectionCandidate right)
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

        private readonly struct IntersectionCandidate
        {
            public IntersectionCandidate(Vector2Int node, uint rank)
            {
                Node = node;
                Rank = rank;
            }

            public Vector2Int Node { get; }
            public uint Rank { get; }
        }
    }
}
