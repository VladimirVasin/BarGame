using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Who is on which bench seat. The hero's sit interaction and the
    /// pedestrian rest controller claim through the same registry, so a
    /// walker never lowers himself onto the hero and the hero's prompt
    /// disappears from a taken seat.
    /// </summary>
    public static class CityBenchSeatClaims
    {
        private static readonly Dictionary<string, object> claims =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public static bool IsClaimed(string benchId)
        {
            return !string.IsNullOrEmpty(benchId) &&
                   claims.ContainsKey(benchId);
        }

        public static bool IsClaimedByOther(string benchId, object owner)
        {
            return !string.IsNullOrEmpty(benchId) &&
                   claims.TryGetValue(benchId, out object holder) &&
                   !ReferenceEquals(holder, owner);
        }

        public static bool TryClaim(string benchId, object owner)
        {
            if (string.IsNullOrEmpty(benchId) || owner == null)
            {
                return false;
            }

            if (claims.TryGetValue(benchId, out object holder))
            {
                return ReferenceEquals(holder, owner);
            }

            claims[benchId] = owner;
            return true;
        }

        public static void Release(string benchId, object owner)
        {
            if (!string.IsNullOrEmpty(benchId) &&
                claims.TryGetValue(benchId, out object holder) &&
                ReferenceEquals(holder, owner))
            {
                claims.Remove(benchId);
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClaims()
        {
            claims.Clear();
        }
    }

    /// <summary>
    /// One bench seat a pedestrian can rest on: where to stand, where
    /// to sit, which way the sitter faces, and the graph guidance that
    /// walks him there.
    /// </summary>
    public sealed class CityBenchRestPoint
    {
        internal CityBenchRestPoint(
            string benchId,
            int nodeIndex,
            Vector3 standSlot,
            Vector3 seatPosition,
            Vector3 sitFacing,
            IReadOnlyList<float> nodeDistances)
        {
            BenchId = benchId;
            NodeIndex = nodeIndex;
            StandSlot = standSlot;
            SeatPosition = seatPosition;
            SitFacing = sitFacing;
            NodeDistances = nodeDistances;
        }

        public string BenchId { get; }
        public int NodeIndex { get; }

        /// <summary>Ground point in front of the seat.</summary>
        public Vector3 StandSlot { get; }

        /// <summary>Seat-top centre the sitter is planted on.</summary>
        public Vector3 SeatPosition { get; }

        /// <summary>Direction the seated body faces.</summary>
        public Vector3 SitFacing { get; }

        /// <summary>Dijkstra field from the bench's graph node.</summary>
        public IReadOnlyList<float> NodeDistances { get; }
    }

    public sealed class CityBenchRestPlan
    {
        internal CityBenchRestPlan(IList<CityBenchRestPoint> points)
        {
            Points = new ReadOnlyCollection<CityBenchRestPoint>(
                new List<CityBenchRestPoint>(points));
        }

        public IReadOnlyList<CityBenchRestPoint> Points { get; }
    }

    /// <summary>
    /// Derives the pedestrian-usable subset of the sittable benches from
    /// the same seat plans the hero's sit interactions use. The pavement
    /// network ends at the kerb, so the last metres to a seat are a
    /// scripted crossing — a bench only qualifies when that crossing is
    /// short, which keeps walkers off the bar-side yard bench and
    /// off anything the network cannot honestly reach.
    /// </summary>
    public static class CityBenchRestPlanner
    {
        /// <summary>
        /// The longest scripted off-network crossing a walker will make
        /// from the bench's graph node to its seat slot. Deliberately
        /// tighter than the bus-stop reach: the crossing ignores props,
        /// so it must stay too short to read as ghosting.
        /// </summary>
        public const float MaximumCrossingDistance = 6f;

        public static CityBenchRestPlan Create(
            IReadOnlyList<CityBenchSitPlan> benchPlans,
            CityPedestrianPlan pedestrianPlan)
        {
            if (benchPlans == null)
            {
                throw new ArgumentNullException(nameof(benchPlans));
            }

            if (pedestrianPlan == null)
            {
                throw new ArgumentNullException(nameof(pedestrianPlan));
            }

            var points = new List<CityBenchRestPoint>(benchPlans.Count);
            for (int index = 0; index < benchPlans.Count; index++)
            {
                CityBenchSitPlan bench = benchPlans[index];
                if (!bench.IsPresent)
                {
                    continue;
                }

                // The bar-side yard bench belongs to its authored scene.
                // An ambient stranger resting beside the circling rider
                // would break the yard's loneliness, and the graph crossing
                // into this roadless gap would cut through a building.
                if (string.Equals(
                        bench.Id,
                        CityBenchSitPlan.HomeYardBenchId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 standSlot = bench.EntryRootPosition;
                if (!CityBusStopWaitPlanner.TryFindNearestNode(
                        pedestrianPlan,
                        standSlot,
                        out int nodeIndex))
                {
                    continue;
                }

                Vector3 crossing =
                    pedestrianPlan.Nodes[nodeIndex].Position - standSlot;
                crossing.y = 0f;
                if (crossing.magnitude > MaximumCrossingDistance)
                {
                    continue;
                }

                Vector3 sitFacing =
                    bench.EntryRotation * Vector3.forward;
                sitFacing.y = 0f;
                if (sitFacing.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                points.Add(new CityBenchRestPoint(
                    bench.Id,
                    nodeIndex,
                    standSlot,
                    bench.InteractionPosition,
                    sitFacing.normalized,
                    CityBusStopWaitPlanner.CreateNodeDistances(
                        pedestrianPlan,
                        nodeIndex)));
            }

            return new CityBenchRestPlan(points);
        }
    }
}
