using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        private static bool TryBuildRiverClosedRoute(
            CityLayout layout,
            IReadOnlyList<StopCandidate> selected,
            bool allowStopEdgeReuse,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            out List<RouteOccurrence> route)
        {
            route = new List<RouteOccurrence>();
            if (selected == null || selected.Count == 0)
            {
                return false;
            }

            CityRiverBridgeDescriptor[] roadBridges =
                GetOrderedRoadBridges(layout.River);
            if (roadBridges.Length != 2)
            {
                return false;
            }

            var banks = new RiverBank[selected.Count];
            var transitionConnectors = new List<int>(2);
            for (int index = 0; index < selected.Count; index++)
            {
                banks[index] = GetRiverBank(
                    selected[index].Source.RoadEdge,
                    layout.River.Definition.CorridorCellX);
                if (banks[index] == RiverBank.Unknown)
                {
                    return false;
                }
            }

            for (int index = 0; index < selected.Count; index++)
            {
                int next = (index + 1) % selected.Count;
                if (banks[index] != banks[next])
                {
                    transitionConnectors.Add(index);
                }
            }

            if (transitionConnectors.Count != 2)
            {
                return false;
            }

            var stopEdges = new HashSet<RoadEdge>();
            for (int index = 0; index < selected.Count; index++)
            {
                stopEdges.Add(selected[index].Source.RoadEdge);
            }

            var bridgeEdges = new[]
            {
                roadBridges[0].Definition.CrossingEdge,
                roadBridges[1].Definition.CrossingEdge
            };
            for (int assignment = 0; assignment < 2; assignment++)
            {
                var candidate = new List<RouteOccurrence>();
                candidate.Add(new RouteOccurrence(
                    selected[0].Source,
                    selected[0]));
                bool valid = true;
                int transitionOrdinal = 0;
                for (int connector = 0;
                     connector < selected.Count;
                     connector++)
                {
                    int next = (connector + 1) % selected.Count;
                    RoadEdge? requiredBridge = null;
                    if (banks[connector] != banks[next])
                    {
                        requiredBridge = bridgeEdges[
                            (transitionOrdinal + assignment) % 2];
                        transitionOrdinal++;
                    }

                    if (!TryAppendRiverConnector(
                            selected[connector].Source.ToNodeIndex,
                            selected[next].Source.FromNodeIndex,
                            requiredBridge,
                            bridgeEdges,
                            stopEdges,
                            allowStopEdgeReuse,
                            outgoing,
                            acceptedLinks,
                            candidate))
                    {
                        valid = false;
                        break;
                    }

                    if (next != 0)
                    {
                        candidate.Add(new RouteOccurrence(
                            selected[next].Source,
                            selected[next]));
                    }
                }

                if (valid &&
                    TraversesRequiredRiverBridges(layout, candidate))
                {
                    route = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryAppendRiverConnector(
            int fromNodeIndex,
            int toNodeIndex,
            RoadEdge? requiredBridge,
            IReadOnlyList<RoadEdge> bridgeEdges,
            ISet<RoadEdge> stopEdges,
            bool allowStopEdgeReuse,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            ICollection<RouteOccurrence> route)
        {
            if (!TryFindRiverConnector(
                    fromNodeIndex,
                    toNodeIndex,
                    requiredBridge,
                    bridgeEdges,
                    stopEdges,
                    outgoing,
                    acceptedLinks,
                    out List<int> path) &&
                (!allowStopEdgeReuse ||
                 !TryFindRiverConnector(
                     fromNodeIndex,
                     toNodeIndex,
                     requiredBridge,
                     bridgeEdges,
                     null,
                     outgoing,
                     acceptedLinks,
                     out path)))
            {
                return false;
            }

            for (int index = 0; index < path.Count; index++)
            {
                route.Add(new RouteOccurrence(
                    acceptedLinks[path[index]],
                    null));
            }

            return true;
        }

        private static bool TryFindRiverConnector(
            int fromNodeIndex,
            int toNodeIndex,
            RoadEdge? requiredBridge,
            IReadOnlyList<RoadEdge> bridgeEdges,
            ISet<RoadEdge> stopEdges,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            out List<int> path)
        {
            var forbidden = new HashSet<RoadEdge>(bridgeEdges);
            if (stopEdges != null)
            {
                forbidden.UnionWith(stopEdges);
            }

            if (!requiredBridge.HasValue)
            {
                return TryFindPath(
                    fromNodeIndex,
                    toNodeIndex,
                    forbidden,
                    outgoing,
                    acceptedLinks,
                    out path);
            }

            List<int> best = null;
            float bestLength = float.PositiveInfinity;
            for (int linkIndex = 0;
                 linkIndex < acceptedLinks.Count;
                 linkIndex++)
            {
                TemporaryLink bridgeLink = acceptedLinks[linkIndex];
                if (!bridgeLink.IsRoadSegment ||
                    bridgeLink.RoadEdge != requiredBridge.Value)
                {
                    continue;
                }

                if (!TryFindPath(
                        fromNodeIndex,
                        bridgeLink.FromNodeIndex,
                        forbidden,
                        outgoing,
                        acceptedLinks,
                        out List<int> prefix) ||
                    !TryFindPath(
                        bridgeLink.ToNodeIndex,
                        toNodeIndex,
                        forbidden,
                        outgoing,
                        acceptedLinks,
                        out List<int> suffix))
                {
                    continue;
                }

                var candidate = new List<int>(
                    prefix.Count + 1 + suffix.Count);
                candidate.AddRange(prefix);
                candidate.Add(linkIndex);
                candidate.AddRange(suffix);
                float length = MeasureTemporaryPath(
                    candidate,
                    acceptedLinks);
                if (best == null ||
                    length < bestLength - TourLengthEpsilon ||
                    (Math.Abs(length - bestLength) <= TourLengthEpsilon &&
                     CompareTemporaryPaths(
                         candidate,
                         best,
                         acceptedLinks) < 0))
                {
                    best = candidate;
                    bestLength = length;
                }
            }

            path = best ?? new List<int>();
            return best != null;
        }

        private static float MeasureTemporaryPath(
            IReadOnlyList<int> path,
            IList<TemporaryLink> links)
        {
            float result = 0f;
            for (int index = 0; index < path.Count; index++)
            {
                result += links[path[index]].Length;
            }

            return result;
        }

        private static int CompareTemporaryPaths(
            IReadOnlyList<int> left,
            IReadOnlyList<int> right,
            IList<TemporaryLink> links)
        {
            int count = Math.Min(left.Count, right.Count);
            for (int index = 0; index < count; index++)
            {
                int comparison = string.CompareOrdinal(
                    links[left[index]].Id,
                    links[right[index]].Id);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static CityRiverBridgeDescriptor[] GetOrderedRoadBridges(
            CityRiverPlan river)
        {
            var result = new List<CityRiverBridgeDescriptor>(2);
            for (int index = 0; index < river.Bridges.Count; index++)
            {
                CityRiverBridgeDescriptor bridge = river.Bridges[index];
                if (bridge.Definition.Role == CityBridgeRole.Road)
                {
                    result.Add(bridge);
                }
            }

            result.Sort((left, right) => left.Definition.CrossingEdge.A.y
                .CompareTo(right.Definition.CrossingEdge.A.y));
            return result.ToArray();
        }

        private static RiverBank GetRiverBank(
            RoadEdge edge,
            int corridorCellX)
        {
            if (edge.B.x <= corridorCellX)
            {
                return RiverBank.West;
            }

            if (edge.A.x >= corridorCellX + 1)
            {
                return RiverBank.East;
            }

            return RiverBank.Unknown;
        }

        private enum RiverBank
        {
            Unknown = 0,
            West,
            East
        }
    }
}
