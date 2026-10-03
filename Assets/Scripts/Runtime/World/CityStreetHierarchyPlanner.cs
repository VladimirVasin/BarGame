using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chooses streets over the stable orthogonal identities. District axes
    /// lead the connected graph; short service links fill it without turning
    /// every block into another four-way intersection.
    /// </summary>
    internal static class CityStreetHierarchyPlanner
    {
        internal static List<RoadEdge> Replan(
            CityGenerationSettings settings,
            int seed,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> availableEdges,
            IReadOnlyList<RoadEdge> legacyRoads,
            IReadOnlyList<RoadEdge> requiredEdges)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (availableEdges == null) throw new ArgumentNullException(nameof(availableEdges));
            if (legacyRoads == null) throw new ArgumentNullException(nameof(legacyRoads));
            if (requiredEdges == null) throw new ArgumentNullException(nameof(requiredEdges));

            if (settings.Blueprint?.Id != CityBlueprintCatalog.DefaultBlueprintId ||
                settings.SpatialPlan == null || settings.SpatialPlan.IsUniform)
                return new List<RoadEdge>(legacyRoads);

            var nodeIndices = new Dictionary<Vector2Int, int>(nodes.Count);
            for (int index = 0; index < nodes.Count; index++)
                nodeIndices.Add(nodes[index], index);
            var available = new HashSet<RoadEdge>(availableEdges);
            var preserved = new HashSet<RoadEdge>(requiredEdges);
            for (int index = 0; index < legacyRoads.Count; index++)
                if (IsProtected(legacyRoads[index])) preserved.Add(legacyRoads[index]);

            var roads = new List<RoadEdge>();
            var selected = new HashSet<RoadEdge>();
            var sets = new NodeSets(nodes.Count);
            var orderedPreserved = new List<RoadEdge>(preserved);
            orderedPreserved.Sort(RoadEdge.Compare);
            for (int index = 0; index < orderedPreserved.Count; index++)
            {
                RoadEdge edge = orderedPreserved[index];
                if (!available.Contains(edge))
                    throw new InvalidOperationException($"Required street {edge} is unavailable.");
                Add(edge, roads, selected, sets, nodeIndices);
            }

            Dictionary<CityDistrictKind, DistrictShape> shapes =
                CreateDistrictShapes(settings.Blueprint);
            var candidates = new List<ScoredEdge>(available.Count);
            foreach (RoadEdge edge in available)
            {
                if (selected.Contains(edge) || IsProtected(edge)) continue;
                candidates.Add(Score(settings.Blueprint, shapes, edge, seed));
            }
            candidates.Sort(CompareCandidates);

            for (int index = 0; index < candidates.Count; index++)
            {
                RoadEdge edge = candidates[index].Edge;
                if (sets.Union(nodeIndices[edge.A], nodeIndices[edge.B]))
                {
                    roads.Add(edge);
                    selected.Add(edge);
                }
            }
            if (sets.ComponentCount > 1)
                throw new InvalidOperationException("District street hierarchy disconnected the city.");

            AddDistrictLoops(settings, candidates, shapes, roads, selected);
            roads.Sort(RoadEdge.Compare);
            return roads;
        }

        private static bool IsProtected(RoadEdge edge)
        {
            return edge.A.x >= 9 && edge.B.x >= 9 &&
                   edge.A.y >= 2 && edge.A.y <= 8 &&
                   edge.B.y >= 2 && edge.B.y <= 8;
        }

        private static void Add(RoadEdge edge, List<RoadEdge> roads,
            HashSet<RoadEdge> selected, NodeSets sets,
            Dictionary<Vector2Int, int> nodeIndices)
        {
            if (!selected.Add(edge)) return;
            roads.Add(edge);
            sets.Union(nodeIndices[edge.A], nodeIndices[edge.B]);
        }

        private static Dictionary<CityDistrictKind, DistrictShape> CreateDistrictShapes(
            CityBlueprint blueprint)
        {
            var result = new Dictionary<CityDistrictKind, DistrictShape>();
            for (int index = 0; index < blueprint.Cells.Count; index++)
            {
                CityBlueprintCell cell = blueprint.Cells[index];
                if (cell.Topology != CityCellTopologyKind.BuildableLand) continue;
                CityDistrictKind district = ResolvePhysicalDistrict(blueprint, cell.Cell);
                if (!result.TryGetValue(district, out DistrictShape shape))
                {
                    shape = new DistrictShape(cell.Cell);
                    result.Add(district, shape);
                }
                shape.Include(cell.Cell);
            }
            return result;
        }

        private static ScoredEdge Score(CityBlueprint blueprint,
            Dictionary<CityDistrictKind, DistrictShape> shapes, RoadEdge edge, int seed)
        {
            Vector2Int firstCell = edge.IsHorizontal ? edge.A + Vector2Int.down : edge.A + Vector2Int.left;
            Vector2Int secondCell = edge.A;
            bool hasFirst = TryUrban(blueprint, firstCell, out CityDistrictKind first);
            bool hasSecond = TryUrban(blueprint, secondCell, out CityDistrictKind second);
            CityDistrictKind district = hasFirst ? first : hasSecond ? second : CityDistrictKind.Yard;
            int priority = edge.IsHorizontal ? 32 : 34;
            bool primary = false;
            if (hasFirst && hasSecond && first != second)
            {
                // A seam remains a cross-neighbourhood connector rather than
                // borrowing either side's internal pattern.
                priority = 8;
            }
            else if (shapes.TryGetValue(district, out DistrictShape shape))
            {
                int x = edge.A.x - shape.Minimum.x;
                int z = edge.A.y - shape.Minimum.y;
                switch (district)
                {
                    case CityDistrictKind.OldTown:
                        // Preferred runs change row/column every two blocks.
                        // Closing links produce short offsets and T-junctions.
                        primary = edge.IsHorizontal
                            ? Mod(z + x / 2, 3) == 1
                            : Mod(x + z / 2, 3) == 1;
                        priority = primary ? edge.IsHorizontal ? 0 : 1
                            : edge.IsHorizontal ? 12 + Mod(z, 3) : 16 + Mod(x, 3);
                        break;
                    case CityDistrictKind.Residential:
                        primary = edge.IsVertical ? Mod(x, 3) == 1
                            : Mod(z, 4) == 1 + Mod(x / 3, 2);
                        priority = primary ? edge.IsVertical ? 0 : 4
                            : edge.IsHorizontal ? 16 : 24;
                        break;
                    case CityDistrictKind.Industrial:
                        primary = edge.IsHorizontal ? Mod(z, 3) == 1 : Mod(x, 4) == 1;
                        priority = primary ? edge.IsHorizontal ? 0 : 3
                            : edge.IsVertical ? 17 : 25;
                        break;
                    case CityDistrictKind.Nightlife:
                        primary = edge.IsVertical ? x == shape.Width / 2 : Mod(z, 3) == 1;
                        priority = primary ? edge.IsVertical ? 0 : 3
                            : edge.IsHorizontal ? 18 : 22;
                        break;
                }
            }
            return new ScoredEdge(edge, district, priority, primary, Hash(seed, edge));
        }

        private static void AddDistrictLoops(CityGenerationSettings settings,
            IReadOnlyList<ScoredEdge> candidates,
            Dictionary<CityDistrictKind, DistrictShape> shapes,
            List<RoadEdge> roads, HashSet<RoadEdge> selected)
        {
            var degree = new Dictionary<Vector2Int, int>();
            foreach (RoadEdge edge in roads)
            {
                Increment(degree, edge.A);
                Increment(degree, edge.B);
            }
            var budgets = new Dictionary<CityDistrictKind, int>();
            foreach (KeyValuePair<CityDistrictKind, DistrictShape> pair in shapes)
            {
                float density = pair.Key == CityDistrictKind.Industrial ? 0.12f :
                    pair.Key == CityDistrictKind.OldTown ? 0.35f : 0.24f;
                budgets.Add(pair.Key, Mathf.CeilToInt(pair.Value.CellCount *
                    Mathf.Clamp01(settings.LoopChance) * density));
            }
            for (int index = 0; index < candidates.Count; index++)
            {
                ScoredEdge candidate = candidates[index];
                RoadEdge edge = candidate.Edge;
                if (!candidate.Primary || selected.Contains(edge) ||
                    !budgets.TryGetValue(candidate.District, out int budget) || budget <= 0 ||
                    degree[edge.A] >= 3 || degree[edge.B] >= 3 ||
                    ClosesSingleBlock(edge, selected)) continue;
                selected.Add(edge);
                roads.Add(edge);
                budgets[candidate.District] = budget - 1;
                Increment(degree, edge.A);
                Increment(degree, edge.B);
            }
        }

        private static bool ClosesSingleBlock(RoadEdge edge, HashSet<RoadEdge> selected)
        {
            Vector2Int first = edge.IsHorizontal ? edge.A + Vector2Int.down : edge.A + Vector2Int.left;
            return HasOtherSides(first, edge, selected) || HasOtherSides(edge.A, edge, selected);
        }

        private static bool HasOtherSides(Vector2Int cell, RoadEdge proposed, HashSet<RoadEdge> selected)
        {
            return HasSide(cell, Vector2Int.left, proposed, selected) &&
                   HasSide(cell, Vector2Int.right, proposed, selected) &&
                   HasSide(cell, Vector2Int.up, proposed, selected) &&
                   HasSide(cell, Vector2Int.down, proposed, selected);
        }

        private static bool HasSide(Vector2Int cell, Vector2Int direction,
            RoadEdge proposed, HashSet<RoadEdge> selected)
        {
            RoadEdge side = RoadEdge.ForCellFrontage(cell, direction);
            return side == proposed || selected.Contains(side);
        }

        private static bool TryUrban(CityBlueprint blueprint, Vector2Int cell,
            out CityDistrictKind district)
        {
            if (blueprint.TryGetCell(cell, out CityBlueprintCell descriptor) &&
                descriptor.Topology == CityCellTopologyKind.BuildableLand)
            {
                district = ResolvePhysicalDistrict(blueprint, cell);
                return true;
            }
            district = CityDistrictKind.Yard;
            return false;
        }

        private static CityDistrictKind ResolvePhysicalDistrict(
            CityBlueprint blueprint, Vector2Int cell)
        {
            // Street grammar belongs to the physical coastal footprint. Moving
            // an editable urban archetype must leave traffic topology intact.
            bool east = cell.x > blueprint.River.CorridorCellX;
            bool north = cell.y >= blueprint.CenterNode.y;
            return north
                ? east ? CityDistrictKind.Residential : CityDistrictKind.OldTown
                : east ? CityDistrictKind.Nightlife : CityDistrictKind.Industrial;
        }

        private static int CompareCandidates(ScoredEdge left, ScoredEdge right)
        {
            int comparison = left.Priority.CompareTo(right.Priority);
            if (comparison == 0) comparison = left.Tie.CompareTo(right.Tie);
            return comparison != 0 ? comparison : RoadEdge.Compare(left.Edge, right.Edge);
        }

        private static uint Hash(int seed, RoadEdge edge)
        {
            unchecked
            {
                uint value = (uint)seed ^ 0x53545254u;
                value = (value ^ (uint)edge.A.x) * 0x9E3779B9u;
                value = (value ^ (uint)edge.A.y) * 0x85EBCA6Bu;
                value = (value ^ (uint)edge.B.x) * 0xC2B2AE35u;
                value = (value ^ (uint)edge.B.y) * 0x27D4EB2Fu;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                return value ^ (value >> 15);
            }
        }

        private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;

        private static void Increment(Dictionary<Vector2Int, int> degree, Vector2Int node)
        {
            degree.TryGetValue(node, out int count);
            degree[node] = count + 1;
        }

        private readonly struct ScoredEdge
        {
            internal ScoredEdge(RoadEdge edge, CityDistrictKind district,
                int priority, bool primary, uint tie)
            {
                Edge = edge;
                District = district;
                Priority = priority;
                Primary = primary;
                Tie = tie;
            }
            internal RoadEdge Edge { get; }
            internal CityDistrictKind District { get; }
            internal int Priority { get; }
            internal bool Primary { get; }
            internal uint Tie { get; }
        }

        private sealed class DistrictShape
        {
            internal DistrictShape(Vector2Int minimum) { Minimum = minimum; Maximum = minimum; }
            internal Vector2Int Minimum { get; private set; }
            private Vector2Int Maximum { get; set; }
            internal int CellCount { get; private set; }
            internal int Width => Maximum.x - Minimum.x + 1;
            internal void Include(Vector2Int cell)
            {
                Minimum = Vector2Int.Min(Minimum, cell);
                Maximum = Vector2Int.Max(Maximum, cell);
                CellCount++;
            }
        }

        private sealed class NodeSets
        {
            private readonly int[] parent;
            private readonly byte[] rank;
            internal NodeSets(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                ComponentCount = count;
                for (int index = 0; index < count; index++) parent[index] = index;
            }
            internal int ComponentCount { get; private set; }
            private int Find(int node)
            {
                while (parent[node] != node)
                {
                    parent[node] = parent[parent[node]];
                    node = parent[node];
                }
                return node;
            }
            internal bool Union(int first, int second)
            {
                first = Find(first);
                second = Find(second);
                if (first == second) return false;
                if (rank[first] < rank[second]) parent[first] = second;
                else
                {
                    parent[second] = first;
                    if (rank[first] == rank[second]) rank[first]++;
                }
                ComponentCount--;
                return true;
            }
        }
    }
}
