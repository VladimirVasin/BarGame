using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPedestrianLinkKind
    {
        Sidewalk = 0,
        Turn,
        Crosswalk
    }

    public sealed class CityPedestrianNode :
        IEquatable<CityPedestrianNode>
    {
        internal CityPedestrianNode(
            string id,
            Vector3 position,
            bool isCrosswalkEntry)
        {
            Id = id ?? string.Empty;
            Position = position;
            IsCrosswalkEntry = isCrosswalkEntry;
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public bool IsCrosswalkEntry { get; }

        public bool Equals(CityPedestrianNode other)
        {
            return other != null &&
                   string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Position.Equals(other.Position) &&
                   IsCrosswalkEntry == other.IsCrosswalkEntry;
        }

        public override bool Equals(object obj)
        {
            return obj is CityPedestrianNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ Position.GetHashCode();
                return (hash * 397) ^ IsCrosswalkEntry.GetHashCode();
            }
        }
    }

    public sealed class CityPedestrianLink :
        IEquatable<CityPedestrianLink>
    {
        internal CityPedestrianLink(
            string id,
            int firstNodeIndex,
            int secondNodeIndex,
            CityPedestrianLinkKind kind)
        {
            Id = id ?? string.Empty;
            FirstNodeIndex = firstNodeIndex;
            SecondNodeIndex = secondNodeIndex;
            Kind = kind;
        }

        public string Id { get; }
        public int FirstNodeIndex { get; }
        public int SecondNodeIndex { get; }
        public CityPedestrianLinkKind Kind { get; }

        public int Other(int nodeIndex)
        {
            if (nodeIndex == FirstNodeIndex)
            {
                return SecondNodeIndex;
            }

            if (nodeIndex == SecondNodeIndex)
            {
                return FirstNodeIndex;
            }

            throw new ArgumentOutOfRangeException(
                nameof(nodeIndex),
                "The requested node does not belong to this link.");
        }

        public bool Equals(CityPedestrianLink other)
        {
            return other != null &&
                   string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   FirstNodeIndex == other.FirstNodeIndex &&
                   SecondNodeIndex == other.SecondNodeIndex &&
                   Kind == other.Kind;
        }

        public override bool Equals(object obj)
        {
            return obj is CityPedestrianLink other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ FirstNodeIndex;
                hash = (hash * 397) ^ SecondNodeIndex;
                return (hash * 397) ^ (int)Kind;
            }
        }
    }

    public sealed class CityPedestrianSpawnAnchor :
        IEquatable<CityPedestrianSpawnAnchor>
    {
        internal CityPedestrianSpawnAnchor(
            string id,
            Vector3 position,
            int firstNodeIndex,
            int secondNodeIndex)
        {
            Id = id ?? string.Empty;
            Position = position;
            FirstNodeIndex = firstNodeIndex;
            SecondNodeIndex = secondNodeIndex;
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public int FirstNodeIndex { get; }
        public int SecondNodeIndex { get; }

        public bool Equals(CityPedestrianSpawnAnchor other)
        {
            return other != null &&
                   string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Position.Equals(other.Position) &&
                   FirstNodeIndex == other.FirstNodeIndex &&
                   SecondNodeIndex == other.SecondNodeIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is CityPedestrianSpawnAnchor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ FirstNodeIndex;
                return (hash * 397) ^ SecondNodeIndex;
            }
        }
    }

    public sealed class CityPedestrianPlan
    {
        private readonly ReadOnlyCollection<CityPedestrianNode> nodes;
        private readonly ReadOnlyCollection<CityPedestrianLink> links;
        private readonly ReadOnlyCollection<CityPedestrianSpawnAnchor>
            spawnAnchors;
        private readonly ReadOnlyCollection<Rect> navigationRectangles;
        private readonly IReadOnlyList<int>[] linkIndicesByNode;

        internal CityPedestrianPlan(
            int layoutSeed,
            int populationSeed,
            uint stableSeed,
            float agentRadius,
            IList<CityPedestrianNode> sourceNodes,
            IList<CityPedestrianLink> sourceLinks,
            IList<CityPedestrianSpawnAnchor> sourceSpawnAnchors,
            IList<Rect> sourceNavigationRectangles)
        {
            LayoutSeed = layoutSeed;
            PopulationSeed = populationSeed;
            StableSeed = stableSeed;
            if (!IsFinite(agentRadius) || agentRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(agentRadius));
            }

            AgentRadius = agentRadius;
            nodes = Copy(sourceNodes, nameof(sourceNodes));
            links = Copy(sourceLinks, nameof(sourceLinks));
            spawnAnchors = Copy(
                sourceSpawnAnchors,
                nameof(sourceSpawnAnchors));
            navigationRectangles = Copy(
                sourceNavigationRectangles,
                nameof(sourceNavigationRectangles));

            var mutableAdjacency = new List<int>[nodes.Count];
            for (int index = 0; index < mutableAdjacency.Length; index++)
            {
                mutableAdjacency[index] = new List<int>();
            }

            for (int index = 0; index < links.Count; index++)
            {
                CityPedestrianLink link = links[index] ??
                    throw new ArgumentException(
                        "Pedestrian links cannot contain null entries.",
                        nameof(sourceLinks));
                ValidateNodeIndex(link.FirstNodeIndex, nameof(sourceLinks));
                ValidateNodeIndex(link.SecondNodeIndex, nameof(sourceLinks));
                if (link.FirstNodeIndex == link.SecondNodeIndex)
                {
                    throw new ArgumentException(
                        "A pedestrian link requires two different nodes.",
                        nameof(sourceLinks));
                }

                mutableAdjacency[link.FirstNodeIndex].Add(index);
                mutableAdjacency[link.SecondNodeIndex].Add(index);
            }

            linkIndicesByNode = new IReadOnlyList<int>[nodes.Count];
            for (int index = 0; index < mutableAdjacency.Length; index++)
            {
                linkIndicesByNode[index] =
                    new ReadOnlyCollection<int>(mutableAdjacency[index]);
            }

            for (int index = 0; index < spawnAnchors.Count; index++)
            {
                CityPedestrianSpawnAnchor anchor = spawnAnchors[index] ??
                    throw new ArgumentException(
                        "Pedestrian spawn anchors cannot contain null entries.",
                        nameof(sourceSpawnAnchors));
                ValidateNodeIndex(
                    anchor.FirstNodeIndex,
                    nameof(sourceSpawnAnchors));
                ValidateNodeIndex(
                    anchor.SecondNodeIndex,
                    nameof(sourceSpawnAnchors));
                if (anchor.FirstNodeIndex == anchor.SecondNodeIndex)
                {
                    throw new ArgumentException(
                        "A spawn anchor requires two route directions.",
                        nameof(sourceSpawnAnchors));
                }
            }
        }

        public int LayoutSeed { get; }
        public int PopulationSeed { get; }
        public uint StableSeed { get; }
        public float AgentRadius { get; }
        public IReadOnlyList<CityPedestrianNode> Nodes => nodes;
        public IReadOnlyList<CityPedestrianLink> Links => links;
        public IReadOnlyList<CityPedestrianSpawnAnchor> SpawnAnchors =>
            spawnAnchors;
        public IReadOnlyList<Rect> NavigationRectangles =>
            navigationRectangles;
        public int Count => spawnAnchors.Count;

        public IReadOnlyList<int> GetLinkIndices(int nodeIndex)
        {
            ValidateNodeIndex(nodeIndex, nameof(nodeIndex));
            return linkIndicesByNode[nodeIndex];
        }

        private void ValidateNodeIndex(int nodeIndex, string parameterName)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.Count)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static ReadOnlyCollection<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
