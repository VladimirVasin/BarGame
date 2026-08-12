using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where an ambient passenger stands while it waits for Route 01, and the
    /// pedestrian-graph node it uses to walk there and to rejoin the pavement
    /// after alighting.
    /// </summary>
    public sealed class CityBusStopWaitPoint
    {
        internal CityBusStopWaitPoint(
            int stopIndex,
            string stopId,
            int pedestrianNodeIndex,
            Vector3 facing,
            IReadOnlyList<Vector3> waitSlots,
            IReadOnlyList<float> nodeDistances)
        {
            StopIndex = stopIndex;
            StopId = stopId;
            PedestrianNodeIndex = pedestrianNodeIndex;
            Facing = facing;
            WaitSlots = waitSlots;
            NodeDistances = nodeDistances;
        }

        public int StopIndex { get; }
        public string StopId { get; }

        /// <summary>
        /// Nearest retained node on the sidewalk lane beside the stop. It is
        /// both the routing target for a walker heading to the stop and the
        /// point at which an alighting walker resumes ordinary roaming.
        /// </summary>
        public int PedestrianNodeIndex { get; }

        /// <summary>
        /// Planar direction toward the carriageway, so a waiting walker faces
        /// the road rather than the wall behind it.
        /// </summary>
        public Vector3 Facing { get; }

        public IReadOnlyList<Vector3> WaitSlots { get; }

        /// <summary>
        /// Graph distance from every pedestrian node to this stop, or
        /// <see cref="float.PositiveInfinity"/> where no route exists. The
        /// stops never move, so this is solved once in the plan rather than
        /// re-searched at runtime the way player guidance has to be.
        /// </summary>
        public IReadOnlyList<float> NodeDistances { get; }

        public bool IsReachableFrom(int nodeIndex)
        {
            return nodeIndex >= 0 &&
                   nodeIndex < NodeDistances.Count &&
                   !float.IsPositiveInfinity(NodeDistances[nodeIndex]);
        }
    }

    /// <summary>
    /// One immutable wait point per Route 01 stop that has usable pavement.
    /// </summary>
    public sealed class CityBusStopWaitPlan
    {
        private readonly Dictionary<int, CityBusStopWaitPoint> byStopIndex;

        internal CityBusStopWaitPlan(
            IReadOnlyList<CityBusStopWaitPoint> waitPoints)
        {
            WaitPoints = waitPoints ??
                throw new ArgumentNullException(nameof(waitPoints));
            byStopIndex =
                new Dictionary<int, CityBusStopWaitPoint>(waitPoints.Count);
            for (int index = 0; index < waitPoints.Count; index++)
            {
                CityBusStopWaitPoint point = waitPoints[index];
                byStopIndex[point.StopIndex] = point;
            }
        }

        public IReadOnlyList<CityBusStopWaitPoint> WaitPoints { get; }
        public int Count => WaitPoints.Count;

        public bool TryGetWaitPoint(
            int stopIndex,
            out CityBusStopWaitPoint waitPoint)
        {
            return byStopIndex.TryGetValue(stopIndex, out waitPoint);
        }
    }
}
