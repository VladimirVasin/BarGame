using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityLayoutGenerator
    {
        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        public static CityLayout Generate(CityGenerationSettings settings, int seed)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            CityGenerationSettings snapshot = settings.Copy();
            List<Vector2Int> nodes = CreateNodes(snapshot);
            List<RoadEdge> allEdges = CreateAllEdges(snapshot);
            List<RoadEdge> roads = CreateRoadGraph(snapshot, seed, allEdges);
            EnsureEveryBlockHasFrontage(snapshot, seed, roads);
            roads.Sort(RoadEdge.Compare);

            Vector3 origin = new Vector3(
                -(snapshot.BlocksX * snapshot.NodeSpacing.x) * 0.5f,
                0f,
                -(snapshot.BlocksZ * snapshot.NodeSpacing.y) * 0.5f);
            List<BuildingLot> lots = CreateBuildingLots(
                snapshot,
                seed,
                origin,
                roads);
            Vector2Int spawnNode = new Vector2Int(
                snapshot.BlocksX / 2,
                snapshot.BlocksZ / 2);

            var layout = new CityLayout(
                seed,
                snapshot.BlockCount,
                snapshot.NodeSpacing,
                origin,
                snapshot.RoadWidth,
                nodes,
                roads,
                lots,
                spawnNode);
            layout.ValidateOrThrow();
            return layout;
        }

        private static List<Vector2Int> CreateNodes(CityGenerationSettings settings)
        {
            var nodes = new List<Vector2Int>(
                checked((settings.BlocksX + 1) * (settings.BlocksZ + 1)));
            for (int z = 0; z <= settings.BlocksZ; z++)
            {
                for (int x = 0; x <= settings.BlocksX; x++)
                {
                    nodes.Add(new Vector2Int(x, z));
                }
            }

            return nodes;
        }

        private static List<RoadEdge> CreateAllEdges(CityGenerationSettings settings)
        {
            int horizontalCount = settings.BlocksX * (settings.BlocksZ + 1);
            int verticalCount = (settings.BlocksX + 1) * settings.BlocksZ;
            var edges = new List<RoadEdge>(horizontalCount + verticalCount);

            for (int z = 0; z <= settings.BlocksZ; z++)
            {
                for (int x = 0; x <= settings.BlocksX; x++)
                {
                    Vector2Int node = new Vector2Int(x, z);
                    if (x < settings.BlocksX)
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.right));
                    }

                    if (z < settings.BlocksZ)
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.up));
                    }
                }
            }

            return edges;
        }

        private static List<RoadEdge> CreateRoadGraph(
            CityGenerationSettings settings,
            int seed,
            List<RoadEdge> allEdges)
        {
            var shuffled = new List<RoadEdge>(allEdges);
            var random = new DeterministicRandom(
                StableHash(seed, 0x47524150u));
            Shuffle(shuffled, ref random);

            int nodeWidth = settings.BlocksX + 1;
            int nodeCount = checked(nodeWidth * (settings.BlocksZ + 1));
            var sets = new DisjointSet(nodeCount);
            var roads = new List<RoadEdge>(nodeCount - 1);
            var roadSet = new HashSet<RoadEdge>();

            for (int index = 0; index < shuffled.Count; index++)
            {
                RoadEdge edge = shuffled[index];
                int first = ToNodeIndex(edge.A, nodeWidth);
                int second = ToNodeIndex(edge.B, nodeWidth);
                if (!sets.Union(first, second))
                {
                    continue;
                }

                roads.Add(edge);
                roadSet.Add(edge);
            }

            var loopRandom = new DeterministicRandom(
                StableHash(seed, 0x4C4F4F50u));
            for (int index = 0; index < allEdges.Count; index++)
            {
                RoadEdge edge = allEdges[index];
                if (roadSet.Contains(edge) ||
                    loopRandom.NextFloat() >= settings.LoopChance)
                {
                    continue;
                }

                roads.Add(edge);
                roadSet.Add(edge);
            }

            return roads;
        }

        private static void EnsureEveryBlockHasFrontage(
            CityGenerationSettings settings,
            int seed,
            List<RoadEdge> roads)
        {
            var roadSet = new HashSet<RoadEdge>(roads);
            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (HasAnyFrontage(cell, roadSet))
                    {
                        continue;
                    }

                    uint hash = StableHash(seed, x, z, 0x46524F4Eu);
                    int start = (int)(hash % (uint)CardinalDirections.Length);
                    RoadEdge added = RoadEdge.ForCellFrontage(
                        cell,
                        CardinalDirections[start]);
                    roads.Add(added);
                    roadSet.Add(added);
                }
            }
        }

        private static List<BuildingLot> CreateBuildingLots(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            List<RoadEdge> roads)
        {
            int lotCount = checked(settings.BlocksX * settings.BlocksZ);
            var roadSet = new HashSet<RoadEdge>(roads);
            var frontages = new Vector2Int[lotCount];
            var barCandidates = new List<int>(lotCount);

            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    int lotIndex = ToLotIndex(x, z, settings.BlocksX);
                    Vector2Int cell = new Vector2Int(x, z);
                    frontages[lotIndex] = ChooseFrontage(cell, seed, roadSet);
                    if (frontages[lotIndex] != Vector2Int.zero)
                    {
                        barCandidates.Add(lotIndex);
                    }
                }
            }

            if (barCandidates.Count < settings.BarCount)
            {
                throw new InvalidOperationException(
                    "The generated road graph has too few accessible bar lots.");
            }

            var barRandom = new DeterministicRandom(
                StableHash(seed, 0x42415253u));
            Shuffle(barCandidates, ref barRandom);
            var barLots = new HashSet<int>();
            for (int index = 0; index < settings.BarCount; index++)
            {
                barLots.Add(barCandidates[index]);
            }

            var lots = new List<BuildingLot>(lotCount);
            int barOrdinal = 0;
            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    int lotIndex = ToLotIndex(x, z, settings.BlocksX);
                    bool isBar = barLots.Contains(lotIndex);
                    BarActivityKind barActivity = BarActivityKind.None;
                    if (isBar)
                    {
                        barActivity =
                            BarActivityAssignment.Resolve(barOrdinal);
                        barOrdinal++;
                    }

                    lots.Add(CreateBuildingLot(
                        settings,
                        seed,
                        origin,
                        new Vector2Int(x, z),
                        frontages[lotIndex],
                        isBar,
                        barActivity));
                }
            }

            return lots;
        }

        private static BuildingLot CreateBuildingLot(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            Vector2Int cell,
            Vector2Int frontage,
            bool isBar,
            BarActivityKind barActivity)
        {
            var random = new DeterministicRandom(
                StableHash(seed, cell.x, cell.y, 0x4C4F5453u));
            float maximumWidth = settings.BlockWidth - (settings.BuildingInset * 2f);
            float maximumDepth = settings.BlockDepth - (settings.BuildingInset * 2f);
            var size = new Vector2(
                maximumWidth * random.Range(0.92f, 0.99f),
                maximumDepth * random.Range(0.92f, 0.99f));
            float height = random.Range(
                settings.MinimumBuildingHeight,
                settings.MaximumBuildingHeight);
            Vector3 center = origin + new Vector3(
                (cell.x + 0.5f) * settings.NodeSpacing.x,
                0f,
                (cell.y + 0.5f) * settings.NodeSpacing.y);

            Vector3 direction = new Vector3(frontage.x, 0f, frontage.y);
            float buildingHalfDistance =
                frontage.x != 0 ? size.x * 0.5f : size.y * 0.5f;
            float roadDistance =
                frontage.x != 0
                    ? settings.NodeSpacing.x * 0.5f
                    : settings.NodeSpacing.y * 0.5f;
            Vector3 doorPosition = center + (direction * buildingHalfDistance);
            Vector3 returnPosition = center + (direction * roadDistance);
            Color color = CreateBuildingColor(ref random, isBar);
            string barId = isBar
                ? $"bar-{unchecked((uint)seed):x8}-{cell.x:D2}-{cell.y:D2}"
                : string.Empty;

            return new BuildingLot(
                cell,
                center,
                size,
                height,
                color,
                isBar,
                barId,
                barActivity,
                frontage,
                doorPosition,
                returnPosition);
        }

        private static Color CreateBuildingColor(
            ref DeterministicRandom random,
            bool isBar)
        {
            if (isBar)
            {
                return new Color(
                    random.Range(0.62f, 0.88f),
                    random.Range(0.18f, 0.34f),
                    random.Range(0.12f, 0.26f),
                    1f);
            }

            float baseValue = random.Range(0.35f, 0.62f);
            return new Color(
                Mathf.Clamp01(baseValue + random.Range(-0.08f, 0.08f)),
                Mathf.Clamp01(baseValue + random.Range(-0.08f, 0.08f)),
                Mathf.Clamp01(baseValue + random.Range(-0.08f, 0.08f)),
                1f);
        }

        private static Vector2Int ChooseFrontage(
            Vector2Int cell,
            int seed,
            HashSet<RoadEdge> roads)
        {
            var available = new List<Vector2Int>(4);
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                Vector2Int direction = CardinalDirections[index];
                if (roads.Contains(RoadEdge.ForCellFrontage(cell, direction)))
                {
                    available.Add(direction);
                }
            }

            if (available.Count == 0)
            {
                return Vector2Int.zero;
            }

            uint hash = StableHash(seed, cell.x, cell.y, 0x444F4F52u);
            return available[(int)(hash % (uint)available.Count)];
        }

        private static bool HasAnyFrontage(
            Vector2Int cell,
            HashSet<RoadEdge> roads)
        {
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                if (roads.Contains(
                    RoadEdge.ForCellFrontage(cell, CardinalDirections[index])))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ToNodeIndex(Vector2Int node, int nodeWidth)
        {
            return (node.y * nodeWidth) + node.x;
        }

        private static int ToLotIndex(int x, int z, int blockWidth)
        {
            return (z * blockWidth) + x;
        }

        private static void Shuffle<T>(
            IList<T> items,
            ref DeterministicRandom random)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = random.NextInt(index + 1);
                T temporary = items[index];
                items[index] = items[other];
                items[other] = temporary;
            }
        }

        private static uint StableHash(int seed, uint salt)
        {
            return StableHash(unchecked((uint)seed), salt);
        }

        private static uint StableHash(int seed, int x, int z, uint salt)
        {
            uint hash = StableHash(unchecked((uint)seed), unchecked((uint)x));
            hash = StableHash(hash, unchecked((uint)z));
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

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(uint seed)
            {
                state = seed == 0u ? 0xA341316Cu : seed;
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
                }

                return (int)(((ulong)NextUInt() * (uint)exclusiveMaximum) >> 32);
            }

            public float NextFloat()
            {
                return (NextUInt() >> 8) * (1f / 16777216f);
            }

            public float Range(float minimum, float maximum)
            {
                return minimum + ((maximum - minimum) * NextFloat());
            }

            private uint NextUInt()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }

        private sealed class DisjointSet
        {
            private readonly int[] parent;
            private readonly byte[] rank;

            public DisjointSet(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                for (int index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            public bool Union(int first, int second)
            {
                int firstRoot = Find(first);
                int secondRoot = Find(second);
                if (firstRoot == secondRoot)
                {
                    return false;
                }

                if (rank[firstRoot] < rank[secondRoot])
                {
                    parent[firstRoot] = secondRoot;
                }
                else if (rank[firstRoot] > rank[secondRoot])
                {
                    parent[secondRoot] = firstRoot;
                }
                else
                {
                    parent[secondRoot] = firstRoot;
                    rank[firstRoot]++;
                }

                return true;
            }

            private int Find(int item)
            {
                int root = item;
                while (parent[root] != root)
                {
                    root = parent[root];
                }

                while (parent[item] != item)
                {
                    int next = parent[item];
                    parent[item] = root;
                    item = next;
                }

                return root;
            }
        }
    }
}
