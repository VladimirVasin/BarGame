using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Places ordinary park benches from the paths they actually serve.
    /// Every result stays on the lawn, runs along one linear ParkPath and
    /// faces it closely enough that the sit dock lands back on the path.
    /// </summary>
    public static class CityParkBenchPlanner
    {
        public const int BenchCountPerRegion = 4;
        public const float PathEdgeGap = 0.30f;

        internal static List<CityParkBenchDescriptor> Create(
            IReadOnlyList<CityParkRegionPlan> regions,
            CityGenerationSettings settings,
            Vector3 origin,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            if (regions == null)
            {
                throw new ArgumentNullException(nameof(regions));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
            }

            if (pathKinds == null)
            {
                throw new ArgumentNullException(nameof(pathKinds));
            }

            var candidatesByRegion = new Dictionary<
                string,
                List<BenchCandidate>>(StringComparer.Ordinal);
            for (int index = 0; index < regions.Count; index++)
            {
                candidatesByRegion.Add(
                    regions[index].Id,
                    new List<BenchCandidate>());
            }

            float centerlineOffset =
                settings.RoadWidth * 0.5f +
                CityParkBenchDescriptor.SeatDepth * 0.5f +
                PathEdgeGap;
            float[] amounts = { 0.28f, 0.72f };
            for (int edgeIndex = 0; edgeIndex < roads.Count; edgeIndex++)
            {
                RoadEdge edge = roads[edgeIndex];
                if (!pathKinds.TryGetValue(edge, out CityPathKind kind) ||
                    kind != CityPathKind.ParkPath ||
                    IsParkBridge(settings, edge))
                {
                    continue;
                }

                Vector3 start = GetNodeWorldPosition(
                    settings,
                    origin,
                    edge.A);
                Vector3 end = GetNodeWorldPosition(
                    settings,
                    origin,
                    edge.B);
                Vector3 tangent = end - start;
                tangent.y = 0f;
                if (tangent.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                tangent.Normalize();
                Vector3 normal = Vector3.Cross(Vector3.up, tangent);
                for (int amountIndex = 0;
                     amountIndex < amounts.Length;
                     amountIndex++)
                {
                    Vector3 pathPoint = Vector3.Lerp(
                        start,
                        end,
                        amounts[amountIndex]);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 position = pathPoint +
                                           normal *
                                           (side * centerlineOffset);
                        Vector3 forward = -normal * side;
                        AddCandidateToContainingRegion(
                            regions,
                            candidatesByRegion,
                            position,
                            tangent,
                            forward);
                    }
                }
            }

            var result = new List<CityParkBenchDescriptor>(
                checked(regions.Count * BenchCountPerRegion));
            for (int regionIndex = 0;
                 regionIndex < regions.Count;
                 regionIndex++)
            {
                CityParkRegionPlan region = regions[regionIndex];
                List<BenchCandidate> selected =
                    SelectDistributedCandidates(
                        region,
                        candidatesByRegion[region.Id]);
                for (int index = 0; index < selected.Count; index++)
                {
                    BenchCandidate candidate = selected[index];
                    result.Add(new CityParkBenchDescriptor(
                        $"{region.Id}-bench-{index + 1}",
                        region.Id,
                        candidate.Position,
                        candidate.Forward));
                }
            }

            return result;
        }

        private static void AddCandidateToContainingRegion(
            IReadOnlyList<CityParkRegionPlan> regions,
            IReadOnlyDictionary<string, List<BenchCandidate>> candidates,
            Vector3 position,
            Vector3 tangent,
            Vector3 forward)
        {
            for (int regionIndex = 0;
                 regionIndex < regions.Count;
                 regionIndex++)
            {
                CityParkRegionPlan region = regions[regionIndex];
                if (!ContainsBenchFootprint(
                        region.WalkableBounds,
                        position,
                        tangent,
                        forward))
                {
                    continue;
                }

                var candidate = new BenchCandidate(position, forward);
                List<BenchCandidate> regionCandidates =
                    candidates[region.Id];
                if (!ContainsCandidate(
                        regionCandidates,
                        candidate.Position))
                {
                    regionCandidates.Add(candidate);
                }
            }
        }

        private static bool IsParkBridge(
            CityGenerationSettings settings,
            RoadEdge edge)
        {
            return settings.Blueprint?.River != null &&
                   settings.Blueprint.River.TryGetBridge(edge, out _);
        }

        private static bool ContainsBenchFootprint(
            Rect bounds,
            Vector3 position,
            Vector3 tangent,
            Vector3 forward)
        {
            float halfWidth = CityParkBenchDescriptor.SeatWidth * 0.5f;
            float halfDepth = CityParkBenchDescriptor.SeatDepth * 0.5f;
            float extentX = Mathf.Abs(tangent.x) * halfWidth +
                            Mathf.Abs(forward.x) * halfDepth;
            float extentZ = Mathf.Abs(tangent.z) * halfWidth +
                            Mathf.Abs(forward.z) * halfDepth;
            return position.x - extentX >= bounds.xMin &&
                   position.x + extentX <= bounds.xMax &&
                   position.z - extentZ >= bounds.yMin &&
                   position.z + extentZ <= bounds.yMax;
        }

        private static bool ContainsCandidate(
            IReadOnlyList<BenchCandidate> candidates,
            Vector3 position)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                if (XzSquaredDistance(
                        candidates[index].Position,
                        position) < 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<BenchCandidate> SelectDistributedCandidates(
            CityParkRegionPlan region,
            IReadOnlyList<BenchCandidate> candidates)
        {
            var selected = new List<BenchCandidate>(BenchCountPerRegion);
            var used = new bool[candidates.Count];
            Rect bounds = region.WalkableBounds;
            Vector3[] targets =
            {
                new Vector3(
                    bounds.center.x,
                    0f,
                    Mathf.Lerp(bounds.yMin, bounds.yMax, 0.22f)),
                new Vector3(
                    bounds.center.x,
                    0f,
                    Mathf.Lerp(bounds.yMin, bounds.yMax, 0.78f)),
                new Vector3(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, 0.22f),
                    0f,
                    bounds.center.y),
                new Vector3(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, 0.78f),
                    0f,
                    bounds.center.y)
            };
            for (int targetIndex = 0;
                 targetIndex < targets.Length &&
                 selected.Count < BenchCountPerRegion;
                 targetIndex++)
            {
                int best = FindNearestUnusedCandidate(
                    candidates,
                    used,
                    targets[targetIndex]);
                if (best >= 0)
                {
                    used[best] = true;
                    selected.Add(candidates[best]);
                }
            }

            for (int index = 0;
                 index < candidates.Count &&
                 selected.Count < BenchCountPerRegion;
                 index++)
            {
                if (!used[index])
                {
                    selected.Add(candidates[index]);
                }
            }

            return selected;
        }

        private static int FindNearestUnusedCandidate(
            IReadOnlyList<BenchCandidate> candidates,
            IReadOnlyList<bool> used,
            Vector3 target)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }

                float distance = XzSquaredDistance(
                    candidates[index].Position,
                    target);
                if (distance < bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static Vector3 GetNodeWorldPosition(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int node)
        {
            return origin + new Vector3(
                node.x * settings.NodeSpacing.x,
                0f,
                node.y * settings.NodeSpacing.y);
        }

        private static float XzSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return x * x + z * z;
        }

        private readonly struct BenchCandidate
        {
            public BenchCandidate(Vector3 position, Vector3 forward)
            {
                Position = position;
                Forward = forward;
            }

            public Vector3 Position { get; }
            public Vector3 Forward { get; }
        }
    }
}
