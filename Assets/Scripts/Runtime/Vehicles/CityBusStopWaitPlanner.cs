using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Derives the pavement wait points for Route 01 from the immutable bus
    /// and pedestrian plans. Pure geometry: no scene objects, no prefab.
    /// </summary>
    public static class CityBusStopWaitPlanner
    {
        /// <summary>
        /// Distance from the blue `01` pole to the sidewalk centreline. The
        /// pole itself stands <see
        /// cref="CityBusPlanner.RoadsidePoleOutsideRoadEdge"/> beyond the road
        /// strip and carries a collider, so nobody may wait on it.
        /// </summary>
        public const float PoleToSidewalkCenter =
            CityBusPlanner.RoadsidePoleOutsideRoadEdge +
            (CityStreetSurfacePlanner.SidewalkWidth * 0.5f);

        /// <summary>
        /// A `1 m` pavement minus a `0.35 m` agent cannot fit two walkers
        /// abreast, so wait slots queue along the lane instead. Both offsets
        /// sit in the clear span between the front door entry (`+3.05 m`) and
        /// the rear door entry (`-1.34 m`) of the halted body, so a waiting
        /// walker never stands in the doorway the hero is about to use.
        /// </summary>
        private static readonly float[] SlotLongitudinalOffsets =
        {
            0.30f,
            1.40f
        };

        /// <summary>
        /// A stop whose nearest pedestrian node is farther than this has no
        /// pavement a walker could reach, and is skipped rather than faked.
        /// </summary>
        public const float MaximumNodeDistance = 14f;

        public static int SlotsPerStop => SlotLongitudinalOffsets.Length;

        public static CityBusStopWaitPlan Create(
            CityBusPlan busPlan,
            CityPedestrianPlan pedestrianPlan,
            IWalkableArea walkableArea)
        {
            if (busPlan == null)
            {
                throw new ArgumentNullException(nameof(busPlan));
            }

            if (pedestrianPlan == null)
            {
                throw new ArgumentNullException(nameof(pedestrianPlan));
            }

            if (walkableArea == null)
            {
                throw new ArgumentNullException(nameof(walkableArea));
            }

            var waitPoints = new List<CityBusStopWaitPoint>(
                busPlan.Stops.Count);
            var slots = new List<Vector3>(SlotLongitudinalOffsets.Length);
            for (int index = 0; index < busPlan.Stops.Count; index++)
            {
                CityBusStopDescriptor stop = busPlan.Stops[index];
                Vector3 toRoad = Vector3.ProjectOnPlane(
                    stop.RoadsideForward,
                    Vector3.up);
                Vector3 along = Vector3.ProjectOnPlane(
                    stop.Forward,
                    Vector3.up);
                if (toRoad.sqrMagnitude <= 0.0001f ||
                    along.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                toRoad.Normalize();
                along.Normalize();
                Vector3 laneCenter = stop.ShelterPosition +
                                     (toRoad * PoleToSidewalkCenter);
                laneCenter.y = CityStreetSurfacePlanner.SidewalkTop;
                slots.Clear();
                for (int offset = 0;
                     offset < SlotLongitudinalOffsets.Length;
                     offset++)
                {
                    Vector3 candidate = laneCenter +
                        (along * SlotLongitudinalOffsets[offset]);
                    candidate.y = CityStreetSurfacePlanner.SidewalkTop;
                    if (walkableArea.Contains(
                            candidate,
                            CityPedestrianPlanner.AgentRadius))
                    {
                        slots.Add(candidate);
                    }
                }

                if (slots.Count == 0 ||
                    !TryFindNearestNode(
                        pedestrianPlan,
                        laneCenter,
                        out int nodeIndex))
                {
                    continue;
                }

                waitPoints.Add(new CityBusStopWaitPoint(
                    index,
                    stop.Id,
                    nodeIndex,
                    toRoad,
                    slots.ToArray(),
                    CreateNodeDistances(pedestrianPlan, nodeIndex)));
            }

            return new CityBusStopWaitPlan(waitPoints.ToArray());
        }

        /// <summary>
        /// Single-source Dijkstra from the stop node over the pedestrian
        /// graph, using the same binary-heap shape as the population
        /// director's player guidance.
        /// </summary>
        private static float[] CreateNodeDistances(
            CityPedestrianPlan plan,
            int sourceNodeIndex)
        {
            int count = plan.Nodes.Count;
            var distances = new float[count];
            for (int index = 0; index < count; index++)
            {
                distances[index] = float.PositiveInfinity;
            }

            distances[sourceNodeIndex] = 0f;
            var heap = new List<HeapEntry>(count)
            {
                new HeapEntry(sourceNodeIndex, 0f)
            };
            while (heap.Count > 0)
            {
                HeapEntry entry = Pop(heap);
                if (entry.Distance > distances[entry.NodeIndex] + 0.0001f)
                {
                    continue;
                }

                IReadOnlyList<int> linkIndices =
                    plan.GetLinkIndices(entry.NodeIndex);
                for (int index = 0; index < linkIndices.Count; index++)
                {
                    CityPedestrianLink link = plan.Links[linkIndices[index]];
                    int other = link.Other(entry.NodeIndex);
                    Vector3 offset = plan.Nodes[other].Position -
                                     plan.Nodes[entry.NodeIndex].Position;
                    offset.y = 0f;
                    float candidate = entry.Distance + offset.magnitude;
                    if (candidate < distances[other] - 0.0001f)
                    {
                        distances[other] = candidate;
                        Push(heap, new HeapEntry(other, candidate));
                    }
                }
            }

            return distances;
        }

        private static void Push(List<HeapEntry> heap, HeapEntry entry)
        {
            heap.Add(entry);
            int child = heap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (heap[parent].Distance <= heap[child].Distance)
                {
                    break;
                }

                (heap[parent], heap[child]) = (heap[child], heap[parent]);
                child = parent;
            }
        }

        private static HeapEntry Pop(List<HeapEntry> heap)
        {
            HeapEntry result = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int parent = 0;
            while (true)
            {
                int left = (parent * 2) + 1;
                int right = left + 1;
                int smallest = parent;
                if (left < heap.Count &&
                    heap[left].Distance < heap[smallest].Distance)
                {
                    smallest = left;
                }

                if (right < heap.Count &&
                    heap[right].Distance < heap[smallest].Distance)
                {
                    smallest = right;
                }

                if (smallest == parent)
                {
                    break;
                }

                (heap[parent], heap[smallest]) = (heap[smallest], heap[parent]);
                parent = smallest;
            }

            return result;
        }

        private readonly struct HeapEntry
        {
            public HeapEntry(int nodeIndex, float distance)
            {
                NodeIndex = nodeIndex;
                Distance = distance;
            }

            public int NodeIndex { get; }
            public float Distance { get; }
        }

        private static bool TryFindNearestNode(
            CityPedestrianPlan plan,
            Vector3 position,
            out int nodeIndex)
        {
            nodeIndex = -1;
            float best = MaximumNodeDistance * MaximumNodeDistance;
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                Vector3 offset = plan.Nodes[index].Position - position;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nodeIndex = index;
                }
            }

            return nodeIndex >= 0;
        }
    }
}
