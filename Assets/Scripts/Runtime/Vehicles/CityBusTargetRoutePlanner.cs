using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        private const int MaximumRouteAssignmentAttempts = 4096;
        private const float CandidateEndInset = 0.05f;
        private const int MaximumRiverPointStopRoadEdgeHop = 5;
        private const float MaximumRiverPointStopDistance = 120f;

        private static List<RouteOccurrence> CreateTargetRoute(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            IReadOnlyList<TemporaryNode> nodes,
            IList<TemporaryLink> acceptedLinks)
        {
            List<StopTarget> targets = CreateStopTargets(layout);
            if (targets.Count == 0 || acceptedLinks.Count == 0)
            {
                return new List<RouteOccurrence>();
            }

            List<Vector3> entrancePoints =
                CreateEntranceClearancePoints(layout);
            var keptTargets = new List<StopTarget>(targets.Count);
            var candidatesByTarget = new List<List<StopCandidate>>(
                targets.Count);
            for (int index = 0; index < targets.Count; index++)
            {
                List<StopCandidate> candidates = CreateStopCandidates(
                    layout,
                    vehicle,
                    targets[index],
                    nodes,
                    acceptedLinks,
                    entrancePoints);
                if (candidates.Count == 0)
                {
                    if (targets[index].IsMandatory)
                    {
                        return new List<RouteOccurrence>();
                    }

                    continue;
                }

                keptTargets.Add(targets[index]);
                candidatesByTarget.Add(candidates);
            }

            if (candidatesByTarget.Count == 0)
            {
                return new List<RouteOccurrence>();
            }

            List<int>[] outgoing = CreateRouteAdjacency(
                nodes.Count,
                acceptedLinks,
                false);
            List<int>[] incoming = CreateRouteAdjacency(
                nodes.Count,
                acceptedLinks,
                true);
            if (!KeepCycleCapableCandidates(
                    keptTargets,
                    candidatesByTarget,
                    outgoing,
                    acceptedLinks))
            {
                return new List<RouteOccurrence>();
            }

            // Cap AFTER the cycle-capability prune: near river flanks the
            // best-sorted candidates are often dead stubs, and capping
            // before the prune left such a target with nothing.
            for (int index = 0; index < candidatesByTarget.Count; index++)
            {
                List<StopCandidate> candidates = candidatesByTarget[index];
                if (IsCoverageStopTargetKind(keptTargets[index].Kind) &&
                    candidates.Count > MaximumCoverageStopCandidates)
                {
                    candidates.RemoveRange(
                        MaximumCoverageStopCandidates,
                        candidates.Count - MaximumCoverageStopCandidates);
                }
            }
            List<RouteOccurrence> fallback = null;
            int assignmentAttempts = 0;
            for (int anchorIndex = 0;
                 anchorIndex < candidatesByTarget[0].Count;
                 anchorIndex++)
            {
                StopCandidate anchor =
                    candidatesByTarget[0][anchorIndex];
                bool[] reachableFromAnchor = MarkReachable(
                    anchor.Source.ToNodeIndex,
                    outgoing,
                    acceptedLinks,
                    false);
                bool[] canReachAnchor = MarkReachable(
                    anchor.Source.FromNodeIndex,
                    incoming,
                    acceptedLinks,
                    true);
                List<StopCandidate>[] compatible = FilterCompatibleCandidates(
                    candidatesByTarget,
                    reachableFromAnchor,
                    canReachAnchor);
                if (HasEmptyCandidateSet(compatible))
                {
                    continue;
                }

                var selected = new StopCandidate[candidatesByTarget.Count];
                selected[0] = anchor;
                var usedEdges = new HashSet<RoadEdge>
                {
                    anchor.Source.RoadEdge
                };
                if (TryAssignCandidates(
                        layout,
                        nodes,
                        1,
                        compatible,
                        selected,
                        usedEdges,
                        true,
                        outgoing,
                        acceptedLinks,
                        ref assignmentAttempts,
                        ref fallback,
                        out List<RouteOccurrence> winding))
                {
                    return winding;
                }

                if (assignmentAttempts >= MaximumRouteAssignmentAttempts)
                {
                    break;
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            assignmentAttempts = 0;
            for (int anchorIndex = 0;
                 anchorIndex < candidatesByTarget[0].Count;
                 anchorIndex++)
            {
                StopCandidate anchor =
                    candidatesByTarget[0][anchorIndex];
                bool[] reachableFromAnchor = MarkReachable(
                    anchor.Source.ToNodeIndex,
                    outgoing,
                    acceptedLinks,
                    false);
                bool[] canReachAnchor = MarkReachable(
                    anchor.Source.FromNodeIndex,
                    incoming,
                    acceptedLinks,
                    true);
                List<StopCandidate>[] compatible = FilterCompatibleCandidates(
                    candidatesByTarget,
                    reachableFromAnchor,
                    canReachAnchor);
                if (HasEmptyCandidateSet(compatible))
                {
                    continue;
                }

                var selected = new StopCandidate[candidatesByTarget.Count];
                selected[0] = anchor;
                if (TryAssignCandidates(
                        layout,
                        nodes,
                        1,
                        compatible,
                        selected,
                        new HashSet<RoadEdge>(),
                        false,
                        outgoing,
                        acceptedLinks,
                        ref assignmentAttempts,
                        ref fallback,
                        out List<RouteOccurrence> winding))
                {
                    return winding;
                }
            }

            if (fallback == null)
            {
                return new List<RouteOccurrence>();
            }

            return fallback;
        }

        private static List<StopTarget> CreateStopTargets(CityLayout layout)
        {
            var points = new List<CityDistrictPointOfInterestDescriptor>(
                layout.DistrictPointsOfInterest);
            points.Sort(ComparePointsOfInterest);
            var result = new List<StopTarget>(
                points.Count + (layout.PlayerHome == null ? 0 : 1));
            for (int index = 0; index < points.Count; index++)
            {
                CityDistrictPointOfInterestDescriptor point = points[index];
                var frontages = new List<RoadEdge>(point.Accesses.Count);
                var references = new List<Vector3>(point.Accesses.Count + 1)
                {
                    point.Center
                };
                var excluded = new List<Rect>(point.Accesses.Count + 1)
                {
                    point.PublicBounds
                };
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access =
                        point.Accesses[accessIndex];
                    frontages.Add(access.FrontageEdge);
                    references.Add(access.Center);
                    excluded.Add(access.ApproachBounds);
                }

                frontages.Sort(RoadEdge.Compare);
                result.Add(new StopTarget(
                    CityBusStopTargetKind.DistrictPointOfInterest,
                    point.Id,
                    point.Cell,
                    point.District,
                    frontages,
                    references,
                    excluded,
                    GetStopSuffix(point.District),
                    true));
            }

            BuildingLot home = layout.PlayerHome;
            if (home != null &&
                layout.TryGetFrontageEdge(home, out RoadEdge homeFrontage))
            {
                Bounds bounds = home.WorldBounds;
                result.Add(new StopTarget(
                    CityBusStopTargetKind.PlayerHome,
                    "player-home:" + layout.BlueprintId + ":" +
                    home.Cell.x + ":" + home.Cell.y,
                    home.Cell,
                    home.District,
                    new List<RoadEdge> { homeFrontage },
                    new List<Vector3>
                    {
                        home.ReturnPosition,
                        home.SidewalkArrivalPosition
                    },
                    new List<Rect>
                    {
                        Rect.MinMaxRect(
                            bounds.min.x,
                            bounds.min.z,
                            bounds.max.x,
                            bounds.max.z)
                    },
                    "home",
                    true));
            }

            List<StopTarget> coverage = CreateCoverageStopTargets(layout);
            result.AddRange(coverage);
            return coverage.Count > 0
                ? OrderAsPerimeterLoop(layout, result)
                : OrderAsServiceLoop(result);
        }

        /// <summary>
        /// Orders the targets as a closed service loop instead of by the
        /// district enum.
        /// <para>
        /// The enum order was nominal — Industrial, Nightlife, Residential,
        /// Old Town, then home — and carried no geography at all. On the
        /// default layout that walked the west edge, the south centre, the
        /// far north-east, back to the west edge and out to the east again:
        /// two full crossings of the city for a `1166 m` straight-line tour
        /// where `754 m` was available. A bus route that doubles back twice
        /// reads as a mistake however carefully the streets between the stops
        /// are proven.
        /// </para>
        /// <para>
        /// Numbering starts at the player's home, which is the one stop the
        /// hero can name, and a closed loop has no last stop to lose by it.
        /// </para>
        /// </summary>
        private static List<StopTarget> OrderAsServiceLoop(
            List<StopTarget> targets)
        {
            if (targets.Count <= 2)
            {
                return RotateToHome(targets);
            }

            List<StopTarget> best = targets.Count <= ExactTourLimit
                ? SolveExactTour(targets)
                : SolveHeuristicTour(targets);
            return RotateToHome(best);
        }

        /// <summary>
        /// Above this the exact search stops being cheap: it fixes the first
        /// target and permutes the rest, so the work is `(n-1)!`. The default
        /// layout has five targets; eight keeps the worst case at `5040`
        /// permutations, which is nothing beside the clearance sampling this
        /// planner already does.
        /// </summary>
        private const int ExactTourLimit = 8;

        private static List<StopTarget> SolveExactTour(
            List<StopTarget> targets)
        {
            var order = new int[targets.Count];
            for (int index = 0; index < order.Length; index++)
            {
                order[index] = index;
            }

            var bestOrder = (int[])order.Clone();
            float bestLength = MeasureTour(targets, order);
            PermuteTail(targets, order, 1, ref bestOrder, ref bestLength);
            var result = new List<StopTarget>(targets.Count);
            for (int index = 0; index < bestOrder.Length; index++)
            {
                result.Add(targets[bestOrder[index]]);
            }

            return result;
        }

        private static void PermuteTail(
            List<StopTarget> targets,
            int[] order,
            int start,
            ref int[] bestOrder,
            ref float bestLength)
        {
            if (start >= order.Length)
            {
                float length = MeasureTour(targets, order);
                // Ties are broken by the ordered target IDs so the same layout
                // always yields the same loop, whatever order the permutation
                // walk happens to reach them in.
                if (length < bestLength - TourLengthEpsilon ||
                    (Mathf.Abs(length - bestLength) <= TourLengthEpsilon &&
                     CompareTourIds(targets, order, bestOrder) < 0))
                {
                    bestLength = length;
                    bestOrder = (int[])order.Clone();
                }

                return;
            }

            for (int index = start; index < order.Length; index++)
            {
                (order[start], order[index]) = (order[index], order[start]);
                PermuteTail(targets, order, start + 1, ref bestOrder,
                    ref bestLength);
                (order[start], order[index]) = (order[index], order[start]);
            }
        }

        /// <summary>
        /// Nearest neighbour followed by 2-opt, for a layout with more targets
        /// than the exact search is willing to enumerate.
        /// </summary>
        private static List<StopTarget> SolveHeuristicTour(
            List<StopTarget> targets)
        {
            var remaining = new List<int>();
            for (int index = 1; index < targets.Count; index++)
            {
                remaining.Add(index);
            }

            var order = new List<int> { 0 };
            while (remaining.Count > 0)
            {
                int current = order[order.Count - 1];
                int bestIndex = 0;
                float bestDistance = float.PositiveInfinity;
                for (int index = 0; index < remaining.Count; index++)
                {
                    float distance = XzDistance(
                        GetTourPosition(targets[current]),
                        GetTourPosition(targets[remaining[index]]));
                    if (distance < bestDistance - TourLengthEpsilon ||
                        (Mathf.Abs(distance - bestDistance) <=
                             TourLengthEpsilon &&
                         string.CompareOrdinal(
                             targets[remaining[index]].Id,
                             targets[remaining[bestIndex]].Id) < 0))
                    {
                        bestDistance = distance;
                        bestIndex = index;
                    }
                }

                order.Add(remaining[bestIndex]);
                remaining.RemoveAt(bestIndex);
            }

            var current2Opt = order.ToArray();
            bool improved = true;
            while (improved)
            {
                improved = false;
                float length = MeasureTour(targets, current2Opt);
                for (int first = 1; first < current2Opt.Length - 1; first++)
                {
                    for (int second = first + 1;
                         second < current2Opt.Length;
                         second++)
                    {
                        var candidate = (int[])current2Opt.Clone();
                        System.Array.Reverse(
                            candidate,
                            first,
                            second - first + 1);
                        if (MeasureTour(targets, candidate) <
                            length - TourLengthEpsilon)
                        {
                            current2Opt = candidate;
                            improved = true;
                            length = MeasureTour(targets, candidate);
                        }
                    }
                }
            }

            var result = new List<StopTarget>(targets.Count);
            for (int index = 0; index < current2Opt.Length; index++)
            {
                result.Add(targets[current2Opt[index]]);
            }

            return result;
        }

        /// <summary>
        /// Rotates the closed loop so the player home is served first, and
        /// fixes the travel direction deterministically. A cycle and its
        /// reverse are the same length, so the ordered IDs decide.
        /// </summary>
        private static List<StopTarget> RotateToHome(List<StopTarget> targets)
        {
            List<StopTarget> forward = RotateToHomeForward(targets);
            if (forward.Count <= 2)
            {
                return forward;
            }

            var reverse = new List<StopTarget>(forward.Count) { forward[0] };
            for (int index = forward.Count - 1; index >= 1; index--)
            {
                reverse.Add(forward[index]);
            }

            return CompareTourIds(forward, reverse) <= 0 ? forward : reverse;
        }

        private const float TourLengthEpsilon = 0.0001f;

        private static float MeasureTour(
            List<StopTarget> targets,
            IReadOnlyList<int> order)
        {
            float total = 0f;
            for (int index = 0; index < order.Count; index++)
            {
                total += XzDistance(
                    GetTourPosition(targets[order[index]]),
                    GetTourPosition(
                        targets[order[(index + 1) % order.Count]]));
            }

            return total;
        }

        private static Vector3 GetTourPosition(StopTarget target)
        {
            return target.ReferencePositions.Count > 0
                ? target.ReferencePositions[0]
                : Vector3.zero;
        }

        private static int CompareTourIds(
            List<StopTarget> targets,
            IReadOnlyList<int> left,
            IReadOnlyList<int> right)
        {
            for (int index = 0; index < left.Count; index++)
            {
                int comparison = string.CompareOrdinal(
                    targets[left[index]].Id,
                    targets[right[index]].Id);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static int CompareTourIds(
            List<StopTarget> left,
            List<StopTarget> right)
        {
            for (int index = 0; index < left.Count; index++)
            {
                int comparison = string.CompareOrdinal(
                    left[index].Id,
                    right[index].Id);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static List<StopCandidate> CreateStopCandidates(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            StopTarget target,
            IReadOnlyList<TemporaryNode> nodes,
            IList<TemporaryLink> acceptedLinks,
            IReadOnlyList<Vector3> entrancePoints)
        {
            var result = new List<StopCandidate>();
            float safeEndDistance = vehicle.InflatedLength * 0.5f +
                                    StopJunctionClearance;
            for (int linkIndex = 0;
                 linkIndex < acceptedLinks.Count;
                 linkIndex++)
            {
                TemporaryLink link = acceptedLinks[linkIndex];
                if (!link.IsRoadSegment ||
                    layout.IsBusFurnitureExcluded(link.RoadEdge) ||
                    link.Length <=
                        (safeEndDistance + CandidateEndInset) * 2f)
                {
                    continue;
                }

                int roadEdgeHop = GetRoadEdgeHop(
                    link.RoadEdge,
                    target.FrontageEdges);
                bool usesRiverPointFallback =
                    layout.River.IsEnabled &&
                    target.Kind ==
                        CityBusStopTargetKind.DistrictPointOfInterest;
                // A gate whose own frontage edge is a graph cul-de-sac
                // (river flanks, dead map corners) is served from the
                // nearest live stretch of the same street instead.
                bool isCoverageTarget =
                    IsCoverageStopTargetKind(target.Kind);
                int maximumRoadEdgeHop = usesRiverPointFallback
                    ? MaximumRiverPointStopRoadEdgeHop
                    : isCoverageTarget
                        ? MaximumCoverageStopRoadEdgeHop
                        : 1;
                if (roadEdgeHop > maximumRoadEdgeHop)
                {
                    continue;
                }

                TemporaryNode node = nodes[link.FromNodeIndex];
                Vector2Int roadsideCell = GetRightSideCell(
                    node.FromGridNode,
                    node.ToGridNode);
                // A POI or home stop must not stand inside its own target
                // cell; a coverage stop is the opposite — its kerb SHOULD
                // face the precinct so the doors open onto the gate.
                bool forbidTargetCellKerb =
                    target.Kind ==
                        CityBusStopTargetKind.DistrictPointOfInterest ||
                    target.Kind == CityBusStopTargetKind.PlayerHome;
                if (forbidTargetCellKerb && roadsideCell == target.Cell)
                {
                    continue;
                }

                int districtPenalty = GetDistrictPenalty(
                    layout,
                    target,
                    roadsideCell);
                if (target.Kind ==
                        CityBusStopTargetKind.DistrictPointOfInterest &&
                    districtPenalty != 0)
                {
                    continue;
                }

                if (!TryCreateStopCandidate(
                        layout,
                        target,
                        link,
                        linkIndex,
                        roadsideCell,
                        roadEdgeHop,
                        districtPenalty,
                        safeEndDistance,
                        entrancePoints,
                        out StopCandidate candidate))
                {
                    continue;
                }

                if (usesRiverPointFallback &&
                    candidate.ReferenceDistance >
                    MaximumRiverPointStopDistance)
                {
                    continue;
                }

                if (isCoverageTarget &&
                    candidate.ReferenceDistance >
                    MaximumCoverageStopDistance)
                {
                    continue;
                }

                result.Add(candidate);
            }

            // On a river layout every candidate must sit on the target's own
            // bank: the closed loop may cross the river exactly twice, so the
            // stop banks have to match the bank-contiguous target order.
            if (layout.River != null && layout.River.IsEnabled)
            {
                RiverBank targetBank = GetTargetBank(
                    target,
                    layout.River.Definition.CorridorCellX);
                if (targetBank != RiverBank.Unknown)
                {
                    for (int index = result.Count - 1; index >= 0; index--)
                    {
                        if (GetRiverBank(
                                result[index].Source.RoadEdge,
                                layout.River.Definition.CorridorCellX) !=
                            targetBank)
                        {
                            result.RemoveAt(index);
                        }
                    }
                }
            }

            // A coverage stop's kerb side outranks its adjacency: the
            // gate's own edge is too short to host a shelter outside the
            // approach strip, so the hop-first order used to flip these
            // stops into the opposite driving direction — against the
            // counter-clockwise loop — and strangle the route search on
            // the sparse boundary columns.
            result.Sort(IsCoverageStopTargetKind(target.Kind)
                ? CompareCoverageStopCandidates
                : (System.Comparison<StopCandidate>)CompareStopCandidates);
            return result;
        }

        private static int CompareCoverageStopCandidates(
            StopCandidate left,
            StopCandidate right)
        {
            int districtComparison = left.DistrictPenalty.CompareTo(
                right.DistrictPenalty);
            if (districtComparison != 0)
            {
                return districtComparison;
            }

            // A flat kerb within the 120 m coverage ceiling beats a
            // nearer ramp: the supermarket's hop-first pick once put its
            // shelter on a grade whose door docks missed the physical
            // sidewalk by 6.5 cm.
            int gradeComparison = left.IsGraded.CompareTo(
                right.IsGraded);
            if (gradeComparison != 0)
            {
                return gradeComparison;
            }

            int hopComparison = left.RoadEdgeHop.CompareTo(
                right.RoadEdgeHop);
            if (hopComparison != 0)
            {
                return hopComparison;
            }

            int distanceComparison = left.ReferenceDistance.CompareTo(
                right.ReferenceDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int edgeComparison = RoadEdge.Compare(
                left.Source.RoadEdge,
                right.Source.RoadEdge);
            if (edgeComparison != 0)
            {
                return edgeComparison;
            }

            return left.SourceLinkIndex.CompareTo(
                right.SourceLinkIndex);
        }

        private static bool TryCreateStopCandidate(
            CityLayout layout,
            StopTarget target,
            TemporaryLink link,
            int sourceLinkIndex,
            Vector2Int roadsideCell,
            int roadEdgeHop,
            int districtPenalty,
            float safeEndDistance,
            IReadOnlyList<Vector3> entrancePoints,
            out StopCandidate candidate)
        {
            float minimumDistance = safeEndDistance + CandidateEndInset;
            float maximumDistance = link.Length - minimumDistance;
            float bestReferenceDistance = float.PositiveInfinity;
            float bestDistanceAlongLink = 0f;
            Vector3 start = link.Samples[0].Position;
            Vector3 direction = link.Samples[0].Forward;
            direction.y = 0f;
            direction.Normalize();
            for (int index = 0;
                 index < target.ReferencePositions.Count;
                 index++)
            {
                Vector3 reference = target.ReferencePositions[index];
                Vector3 delta = reference - start;
                delta.y = 0f;
                float projected = Mathf.Clamp(
                    Vector3.Dot(delta, direction),
                    minimumDistance,
                    maximumDistance);
                EvaluateSamples(
                    link.Samples,
                    projected,
                    out Vector3 position,
                    out Vector3 forward);
                Vector3 right = new Vector3(
                    forward.z,
                    0f,
                    -forward.x).normalized;
                Vector3 shelter = GetShelterPosition(
                    layout,
                    position,
                    right);
                float distance = XzDistance(shelter, reference);
                if (distance < bestReferenceDistance)
                {
                    bestReferenceDistance = distance;
                    bestDistanceAlongLink = projected;
                }
            }

            // A placement that would crowd a bar, home or supermarket
            // door slides along the link to the nearest clear spot; the
            // reference distance is re-measured afterwards so the
            // candidate sort judges the pole where it actually stands.
            if (!TryClearBuildingEntrances(
                    layout,
                    entrancePoints,
                    link.Samples,
                    minimumDistance,
                    maximumDistance,
                    ref bestDistanceAlongLink))
            {
                candidate = null;
                return false;
            }

            EvaluateSamples(
                link.Samples,
                bestDistanceAlongLink,
                out Vector3 stopPosition,
                out Vector3 stopForward);
            Vector3 stopRight = new Vector3(
                stopForward.z,
                0f,
                -stopForward.x).normalized;
            Vector3 shelterPosition = GetShelterPosition(
                layout,
                stopPosition,
                stopRight);
            if (IsInsideAny(shelterPosition, target.ExcludedBounds))
            {
                candidate = null;
                return false;
            }

            bestReferenceDistance = float.PositiveInfinity;
            for (int index = 0;
                 index < target.ReferencePositions.Count;
                 index++)
            {
                bestReferenceDistance = Mathf.Min(
                    bestReferenceDistance,
                    XzDistance(
                        shelterPosition,
                        target.ReferencePositions[index]));
            }

            candidate = new StopCandidate(
                target,
                link,
                sourceLinkIndex,
                roadsideCell,
                bestDistanceAlongLink,
                stopPosition,
                stopForward,
                shelterPosition,
                -stopRight,
                roadEdgeHop,
                districtPenalty,
                bestReferenceDistance,
                !IsLevelStopEdge(layout, link.RoadEdge));
            return true;
        }

        private static Vector3 GetShelterPosition(
            CityLayout layout,
            Vector3 roadPosition,
            Vector3 right)
        {
            float carriagewayWidth = layout.RoadWidth -
                (CityStreetSurfacePlanner.SidewalkWidth * 2f);
            float laneCenterOffset = Mathf.Max(
                0f,
                carriagewayWidth * 0.25f);
            Vector3 result = roadPosition +
                (right * (
                    (layout.RoadWidth * 0.5f) +
                    RoadsidePoleOutsideRoadEdge -
                    laneCenterOffset));
            result.y = roadPosition.y +
                (CityStreetSurfacePlanner.SidewalkTop -
                 CityStreetSurfacePlanner.RoadTop);
            return result;
        }

        private static int GetRoadEdgeHop(
            RoadEdge edge,
            IReadOnlyList<RoadEdge> frontages)
        {
            int best = int.MaxValue;
            for (int index = 0; index < frontages.Count; index++)
            {
                RoadEdge frontage = frontages[index];
                if (edge == frontage)
                {
                    return 0;
                }

                if (edge.Contains(frontage.A) ||
                    edge.Contains(frontage.B))
                {
                    best = 1;
                    continue;
                }

                int nodeDistance = Mathf.Min(
                    ManhattanDistance(edge.A, frontage.A),
                    ManhattanDistance(edge.A, frontage.B),
                    ManhattanDistance(edge.B, frontage.A),
                    ManhattanDistance(edge.B, frontage.B));
                best = Mathf.Min(best, nodeDistance + 1);
            }

            return best;
        }

        private static int ManhattanDistance(
            Vector2Int left,
            Vector2Int right) =>
            Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);

        private static int GetDistrictPenalty(
            CityLayout layout,
            StopTarget target,
            Vector2Int roadsideCell)
        {
            return layout.TryGetArea(
                       roadsideCell,
                       out CityAreaDefinition area) &&
                   area.Archetype == target.District
                ? 0
                : 1;
        }

        private static bool IsInsideAny(
            Vector3 point,
            IReadOnlyList<Rect> rectangles)
        {
            for (int index = 0; index < rectangles.Count; index++)
            {
                Rect rect = rectangles[index];
                if (point.x >= rect.xMin - GeometryTolerance &&
                    point.x <= rect.xMax + GeometryTolerance &&
                    point.z >= rect.yMin - GeometryTolerance &&
                    point.z <= rect.yMax + GeometryTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<StopCandidate>[] FilterCompatibleCandidates(
            IReadOnlyList<List<StopCandidate>> candidatesByTarget,
            IReadOnlyList<bool> reachableFromAnchor,
            IReadOnlyList<bool> canReachAnchor)
        {
            var result = new List<StopCandidate>[
                candidatesByTarget.Count];
            for (int targetIndex = 0;
                 targetIndex < candidatesByTarget.Count;
                 targetIndex++)
            {
                result[targetIndex] = new List<StopCandidate>();
                List<StopCandidate> source =
                    candidatesByTarget[targetIndex];
                for (int candidateIndex = 0;
                     candidateIndex < source.Count;
                     candidateIndex++)
                {
                    StopCandidate candidate = source[candidateIndex];
                    if (reachableFromAnchor[
                            candidate.Source.FromNodeIndex] &&
                        canReachAnchor[candidate.Source.ToNodeIndex])
                    {
                        result[targetIndex].Add(candidate);
                    }
                }
            }

            return result;
        }

        private static bool HasEmptyCandidateSet(
            IReadOnlyList<List<StopCandidate>> candidates)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].Count == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool KeepCycleCapableCandidates(
            List<StopTarget> targets,
            List<List<StopCandidate>> candidatesByTarget,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks)
        {
            for (int targetIndex = candidatesByTarget.Count - 1;
                 targetIndex >= 0;
                 targetIndex--)
            {
                List<StopCandidate> candidates =
                    candidatesByTarget[targetIndex];
                for (int candidateIndex = candidates.Count - 1;
                     candidateIndex >= 0;
                     candidateIndex--)
                {
                    StopCandidate candidate = candidates[candidateIndex];
                    bool[] reachable = MarkReachable(
                        candidate.Source.ToNodeIndex,
                        outgoing,
                        acceptedLinks,
                        false);
                    if (!reachable[candidate.Source.FromNodeIndex])
                    {
                        candidates.RemoveAt(candidateIndex);
                    }
                }

                if (candidates.Count == 0)
                {
                    if (targets[targetIndex].IsMandatory)
                    {
                        return false;
                    }

                    targets.RemoveAt(targetIndex);
                    candidatesByTarget.RemoveAt(targetIndex);
                }
            }

            return candidatesByTarget.Count > 0;
        }

        private static bool TryAssignCandidates(
            CityLayout layout,
            IReadOnlyList<TemporaryNode> nodes,
            int targetIndex,
            IReadOnlyList<List<StopCandidate>> candidates,
            StopCandidate[] selected,
            ISet<RoadEdge> usedEdges,
            bool requireUniqueEdges,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            ref int assignmentAttempts,
            ref List<RouteOccurrence> fallback,
            out List<RouteOccurrence> winding)
        {
            if (assignmentAttempts >= MaximumRouteAssignmentAttempts)
            {
                winding = null;
                return false;
            }

            if (targetIndex >= selected.Length)
            {
                assignmentAttempts++;
                if (!TryBuildClosedRoute(
                        layout,
                        nodes,
                        selected,
                        !requireUniqueEdges,
                        outgoing,
                        acceptedLinks,
                        out List<RouteOccurrence> route))
                {
                    winding = null;
                    return false;
                }

                if (fallback == null)
                {
                    fallback = route;
                }

                if (CountTurningManeuvers(route) > 4)
                {
                    winding = route;
                    return true;
                }

                winding = null;
                return false;
            }

            List<StopCandidate> options = candidates[targetIndex];
            for (int index = 0; index < options.Count; index++)
            {
                StopCandidate option = options[index];
                if (requireUniqueEdges &&
                    usedEdges.Contains(option.Source.RoadEdge))
                {
                    continue;
                }

                selected[targetIndex] = option;
                bool added = requireUniqueEdges &&
                             usedEdges.Add(option.Source.RoadEdge);
                if (TryAssignCandidates(
                        layout,
                        nodes,
                        targetIndex + 1,
                        candidates,
                        selected,
                        usedEdges,
                        requireUniqueEdges,
                        outgoing,
                        acceptedLinks,
                        ref assignmentAttempts,
                        ref fallback,
                        out winding))
                {
                    return true;
                }

                if (added)
                {
                    usedEdges.Remove(option.Source.RoadEdge);
                }

                if (assignmentAttempts >= MaximumRouteAssignmentAttempts)
                {
                    break;
                }
            }

            winding = null;
            return false;
        }

        /// <summary>
        /// The connector prohibition is DIRECTED: a stop belongs to one
        /// driving direction of its street, and only that direction is
        /// closed to connectors — the bus must never cruise past its own
        /// pole. The opposite direction stays open; that pass runs on
        /// the far side of the carriageway, as on any real street. An
        /// edge-based ban strangled the graph once the grand loop grew
        /// to eighteen targets, and the strict phase could no longer
        /// close at all.
        /// </summary>
        private static HashSet<DirectedKey> CreateStopStreetSet(
            IReadOnlyList<TemporaryNode> nodes,
            IReadOnlyList<StopCandidate> selected)
        {
            var result = new HashSet<DirectedKey>();
            for (int index = 0; index < selected.Count; index++)
            {
                TemporaryNode node =
                    nodes[selected[index].Source.FromNodeIndex];
                result.Add(new DirectedKey(
                    node.FromGridNode,
                    node.ToGridNode));
            }

            return result;
        }

        private static bool TryBuildClosedRoute(
            CityLayout layout,
            IReadOnlyList<TemporaryNode> nodes,
            IReadOnlyList<StopCandidate> selected,
            bool allowStopEdgeReuse,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            out List<RouteOccurrence> route)
        {
            if (layout.River != null && layout.River.IsEnabled)
            {
                return TryBuildRiverClosedRoute(
                    layout,
                    nodes,
                    selected,
                    allowStopEdgeReuse,
                    outgoing,
                    acceptedLinks,
                    out route);
            }

            route = new List<RouteOccurrence>();
            HashSet<DirectedKey> stopStreets = CreateStopStreetSet(
                nodes,
                selected);
            route.Add(new RouteOccurrence(
                selected[0].Source,
                selected[0]));
            StopCandidate current = selected[0];
            for (int targetIndex = 1;
                 targetIndex < selected.Count;
                 targetIndex++)
            {
                StopCandidate next = selected[targetIndex];
                if (!TryAppendConnector(
                        current.Source.ToNodeIndex,
                        next.Source.FromNodeIndex,
                        nodes,
                        stopStreets,
                        allowStopEdgeReuse,
                        outgoing,
                        acceptedLinks,
                        route))
                {
                    route.Clear();
                    return false;
                }

                route.Add(new RouteOccurrence(next.Source, next));
                current = next;
            }

            if (!TryAppendConnector(
                    current.Source.ToNodeIndex,
                    selected[0].Source.FromNodeIndex,
                    nodes,
                    stopStreets,
                    allowStopEdgeReuse,
                    outgoing,
                    acceptedLinks,
                    route))
            {
                route.Clear();
                return false;
            }

            return route.Count > 0;
        }

        private static bool TraversesRequiredRiverBridges(
            CityLayout layout,
            IReadOnlyList<RouteOccurrence> route)
        {
            CityRiverPlan river = layout.River;
            if (river == null || !river.IsEnabled)
            {
                return true;
            }

            var counts = new Dictionary<RoadEdge, int>();
            for (int index = 0; index < river.Bridges.Count; index++)
            {
                CityRiverBridgeDescriptor bridge = river.Bridges[index];
                if (bridge.Definition.Role == CityBridgeRole.Road)
                {
                    counts.Add(bridge.Definition.CrossingEdge, 0);
                }
            }

            for (int index = 0; index < route.Count; index++)
            {
                TemporaryLink link = route[index].Source;
                if (link.IsRoadSegment &&
                    counts.TryGetValue(link.RoadEdge, out int count))
                {
                    counts[link.RoadEdge] = count + 1;
                }
            }

            if (counts.Count != 2)
            {
                return false;
            }

            foreach (int count in counts.Values)
            {
                if (count != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendConnector(
            int fromNodeIndex,
            int toNodeIndex,
            IReadOnlyList<TemporaryNode> nodes,
            ISet<DirectedKey> stopStreets,
            bool allowStopEdgeReuse,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            ICollection<RouteOccurrence> route)
        {
            if (!TryFindPath(
                    fromNodeIndex,
                    toNodeIndex,
                    null,
                    stopStreets,
                    nodes,
                    outgoing,
                    acceptedLinks,
                    out List<int> path) &&
                (!allowStopEdgeReuse ||
                !TryFindPath(
                    fromNodeIndex,
                    toNodeIndex,
                    null,
                    null,
                    nodes,
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

        /// <summary>
        /// Shortest connector by travelled metres, not by link count. A
        /// breadth-first search counted a two-edge wide-right macro as one
        /// hop, so it preferred zigzags full of right turns over a plain
        /// boulevard run and the loop came out as spaghetti; weighting by
        /// sample length makes connectors hug the straight streets.
        /// </summary>
        private static bool TryFindPath(
            int fromNodeIndex,
            int toNodeIndex,
            ISet<RoadEdge> forbiddenRoadEdges,
            ISet<DirectedKey> forbiddenStreets,
            IReadOnlyList<TemporaryNode> nodes,
            IReadOnlyList<List<int>> outgoing,
            IList<TemporaryLink> acceptedLinks,
            out List<int> path)
        {
            path = new List<int>();
            if (fromNodeIndex == toNodeIndex)
            {
                return true;
            }

            var previousLink = new int[outgoing.Count];
            var distances = new float[outgoing.Count];
            var closed = new bool[outgoing.Count];
            for (int index = 0; index < previousLink.Length; index++)
            {
                previousLink[index] = -1;
                distances[index] = float.PositiveInfinity;
            }

            distances[fromNodeIndex] = 0f;
            var heap = new List<PathHeapEntry>
            {
                new PathHeapEntry(fromNodeIndex, 0f)
            };
            while (heap.Count > 0)
            {
                PathHeapEntry entry = PopPathEntry(heap);
                if (closed[entry.NodeIndex])
                {
                    continue;
                }

                closed[entry.NodeIndex] = true;
                if (entry.NodeIndex == toNodeIndex)
                {
                    break;
                }

                IReadOnlyList<int> links = outgoing[entry.NodeIndex];
                for (int index = 0; index < links.Count; index++)
                {
                    int linkIndex = links[index];
                    TemporaryLink link = acceptedLinks[linkIndex];
                    if (forbiddenRoadEdges != null &&
                        (link.OccupiesPrimaryRoadEdge &&
                         forbiddenRoadEdges.Contains(link.RoadEdge) ||
                         link.HasSecondaryRoadEdge &&
                         forbiddenRoadEdges.Contains(
                             link.SecondaryRoadEdge)))
                    {
                        continue;
                    }

                    if (forbiddenStreets != null &&
                        (link.OccupiesPrimaryRoadEdge &&
                         forbiddenStreets.Contains(new DirectedKey(
                             nodes[link.FromNodeIndex].FromGridNode,
                             nodes[link.FromNodeIndex].ToGridNode)) ||
                         link.HasSecondaryRoadEdge &&
                         forbiddenStreets.Contains(new DirectedKey(
                             nodes[link.ToNodeIndex].FromGridNode,
                             nodes[link.ToNodeIndex].ToGridNode))))
                    {
                        continue;
                    }

                    int nextNodeIndex = link.ToNodeIndex;
                    if (closed[nextNodeIndex])
                    {
                        continue;
                    }

                    float candidate = distances[entry.NodeIndex] +
                                      link.Length;
                    if (candidate <
                        distances[nextNodeIndex] - GeometryTolerance)
                    {
                        distances[nextNodeIndex] = candidate;
                        previousLink[nextNodeIndex] = linkIndex;
                        heap.Add(new PathHeapEntry(
                            nextNodeIndex,
                            candidate));
                        BubbleUpPathEntry(heap);
                    }
                }
            }

            if (!closed[toNodeIndex])
            {
                return false;
            }

            int current = toNodeIndex;
            while (current != fromNodeIndex)
            {
                int linkIndex = previousLink[current];
                if (linkIndex < 0)
                {
                    path.Clear();
                    return false;
                }

                path.Add(linkIndex);
                current = acceptedLinks[linkIndex].FromNodeIndex;
            }

            path.Reverse();
            return true;
        }

        private static PathHeapEntry PopPathEntry(List<PathHeapEntry> heap)
        {
            PathHeapEntry result = heap[0];
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
                    ComparePathEntries(heap[left], heap[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < heap.Count &&
                    ComparePathEntries(heap[right], heap[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == parent)
                {
                    break;
                }

                (heap[parent], heap[smallest]) =
                    (heap[smallest], heap[parent]);
                parent = smallest;
            }

            return result;
        }

        private static void BubbleUpPathEntry(List<PathHeapEntry> heap)
        {
            int child = heap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (ComparePathEntries(heap[parent], heap[child]) <= 0)
                {
                    break;
                }

                (heap[parent], heap[child]) = (heap[child], heap[parent]);
                child = parent;
            }
        }

        /// <summary>
        /// Distance first, node index as the tie-break, so equally distant
        /// frontier nodes always settle in the same order and the loop
        /// stays deterministic across runs.
        /// </summary>
        private static int ComparePathEntries(
            PathHeapEntry left,
            PathHeapEntry right)
        {
            int comparison = left.Distance.CompareTo(right.Distance);
            return comparison != 0
                ? comparison
                : left.NodeIndex.CompareTo(right.NodeIndex);
        }

        private readonly struct PathHeapEntry
        {
            public PathHeapEntry(int nodeIndex, float distance)
            {
                NodeIndex = nodeIndex;
                Distance = distance;
            }

            public int NodeIndex { get; }
            public float Distance { get; }
        }

        private static List<int>[] CreateRouteAdjacency(
            int nodeCount,
            IList<TemporaryLink> acceptedLinks,
            bool reverse)
        {
            var result = new List<int>[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                result[index] = new List<int>();
            }

            for (int index = 0; index < acceptedLinks.Count; index++)
            {
                TemporaryLink link = acceptedLinks[index];
                result[reverse
                    ? link.ToNodeIndex
                    : link.FromNodeIndex].Add(index);
            }

            for (int index = 0; index < result.Length; index++)
            {
                result[index].Sort((left, right) =>
                {
                    int idComparison = string.CompareOrdinal(
                        acceptedLinks[left].Id,
                        acceptedLinks[right].Id);
                    return idComparison != 0
                        ? idComparison
                        : left.CompareTo(right);
                });
            }

            return result;
        }

        private static bool[] MarkReachable(
            int startNodeIndex,
            IReadOnlyList<List<int>> adjacency,
            IList<TemporaryLink> acceptedLinks,
            bool reverse)
        {
            var result = new bool[adjacency.Count];
            var queue = new Queue<int>();
            result[startNodeIndex] = true;
            queue.Enqueue(startNodeIndex);
            while (queue.Count > 0)
            {
                int nodeIndex = queue.Dequeue();
                IReadOnlyList<int> links = adjacency[nodeIndex];
                for (int index = 0; index < links.Count; index++)
                {
                    TemporaryLink link = acceptedLinks[links[index]];
                    int nextNodeIndex = reverse
                        ? link.FromNodeIndex
                        : link.ToNodeIndex;
                    if (result[nextNodeIndex])
                    {
                        continue;
                    }

                    result[nextNodeIndex] = true;
                    queue.Enqueue(nextNodeIndex);
                }
            }

            return result;
        }

        private static int CountTurningManeuvers(
            IReadOnlyList<RouteOccurrence> route)
        {
            int result = 0;
            for (int index = 0; index < route.Count; index++)
            {
                if (route[index].Source.Kind !=
                    CityBusRouteLinkKind.Straight)
                {
                    result++;
                }
            }

            return result;
        }

        private static List<CityBusStopDescriptor> CreateTargetStops(
            CityLayout layout,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> links)
        {
            var result = new List<CityBusStopDescriptor>();
            float distanceBeforeLink = 0f;
            for (int index = 0; index < metadata.Count; index++)
            {
                RouteLinkMetadata entry = metadata[index];
                CityBusRouteLink link = links[entry.FinalLinkIndex];
                StopCandidate candidate = entry.StopCandidate;
                if (candidate != null)
                {
                    int sequenceIndex = result.Count;
                    string suffix = candidate.Target.Suffix;
                    string blueprintKey =
                        layout.BlueprintId.Replace('-', '_');
                    result.Add(new CityBusStopDescriptor(
                        "bus-stop:" + layout.BlueprintId +
                        ":route-01:" + suffix,
                        string.Empty,
                        "bus.stop." + blueprintKey + "." +
                        suffix.Replace('-', '_'),
                        sequenceIndex,
                        distanceBeforeLink +
                            candidate.DistanceAlongLink,
                        candidate.Target.District,
                        candidate.ShelterPosition,
                        candidate.RoadsideForward,
                        entry.FinalLinkIndex,
                        candidate.DistanceAlongLink,
                        candidate.Position,
                        candidate.Forward,
                        candidate.Source.RoadEdge,
                        candidate.Target.Kind,
                        candidate.Target.Id,
                        candidate.Target.Cell));
                }

                distanceBeforeLink += link.Length;
            }

            return result;
        }

        private static int ComparePointsOfInterest(
            CityDistrictPointOfInterestDescriptor left,
            CityDistrictPointOfInterestDescriptor right)
        {
            int districtComparison = GetDistrictOrder(left.District)
                .CompareTo(GetDistrictOrder(right.District));
            return districtComparison != 0
                ? districtComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        private static int CompareStopCandidates(
            StopCandidate left,
            StopCandidate right)
        {
            int hopComparison = left.RoadEdgeHop.CompareTo(
                right.RoadEdgeHop);
            if (hopComparison != 0)
            {
                return hopComparison;
            }

            int districtComparison = left.DistrictPenalty.CompareTo(
                right.DistrictPenalty);
            if (districtComparison != 0)
            {
                return districtComparison;
            }

            int gradeComparison = left.IsGraded.CompareTo(
                right.IsGraded);
            if (gradeComparison != 0)
            {
                return gradeComparison;
            }

            int distanceComparison = left.ReferenceDistance.CompareTo(
                right.ReferenceDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int edgeComparison = RoadEdge.Compare(
                left.Source.RoadEdge,
                right.Source.RoadEdge);
            if (edgeComparison != 0)
            {
                return edgeComparison;
            }

            return left.SourceLinkIndex.CompareTo(
                right.SourceLinkIndex);
        }

        private static int GetDistrictOrder(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.Industrial:
                    return 0;
                case CityDistrictKind.Nightlife:
                    return 1;
                case CityDistrictKind.Residential:
                    return 2;
                case CityDistrictKind.OldTown:
                    return 3;
                default:
                    return 4 + (int)district;
            }
        }

        private static float XzDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }

        private sealed class StopTarget
        {
            public StopTarget(
                CityBusStopTargetKind kind,
                string id,
                Vector2Int cell,
                CityDistrictKind district,
                List<RoadEdge> frontageEdges,
                List<Vector3> referencePositions,
                List<Rect> excludedBounds,
                string suffix,
                bool isMandatory)
            {
                Kind = kind;
                Id = id ?? string.Empty;
                Cell = cell;
                District = district;
                FrontageEdges = frontageEdges;
                ReferencePositions = referencePositions;
                ExcludedBounds = excludedBounds;
                Suffix = suffix ?? string.Empty;
                IsMandatory = isMandatory;
            }

            public CityBusStopTargetKind Kind { get; }
            public string Id { get; }
            public Vector2Int Cell { get; }
            public CityDistrictKind District { get; }
            public IReadOnlyList<RoadEdge> FrontageEdges { get; }
            public IReadOnlyList<Vector3> ReferencePositions { get; }
            public IReadOnlyList<Rect> ExcludedBounds { get; }
            public string Suffix { get; }

            /// <summary>
            /// A mandatory target with no viable stop candidate fails the
            /// whole route; a coverage target is dropped instead, so one
            /// unlucky gate never erases the entire bus.
            /// </summary>
            public bool IsMandatory { get; }
        }

        private sealed class StopCandidate
        {
            public StopCandidate(
                StopTarget target,
                TemporaryLink source,
                int sourceLinkIndex,
                Vector2Int roadsideCell,
                float distanceAlongLink,
                Vector3 position,
                Vector3 forward,
                Vector3 shelterPosition,
                Vector3 roadsideForward,
                int roadEdgeHop,
                int districtPenalty,
                float referenceDistance,
                bool isGraded)
            {
                Target = target;
                Source = source;
                SourceLinkIndex = sourceLinkIndex;
                RoadsideCell = roadsideCell;
                DistanceAlongLink = distanceAlongLink;
                Position = position;
                Forward = forward;
                ShelterPosition = shelterPosition;
                RoadsideForward = roadsideForward;
                RoadEdgeHop = roadEdgeHop;
                DistrictPenalty = districtPenalty;
                ReferenceDistance = referenceDistance;
                IsGraded = isGraded;
            }

            public StopTarget Target { get; }
            public TemporaryLink Source { get; }
            public int SourceLinkIndex { get; }
            public Vector2Int RoadsideCell { get; }
            public float DistanceAlongLink { get; }
            public Vector3 Position { get; }
            public Vector3 Forward { get; }
            public Vector3 ShelterPosition { get; }
            public Vector3 RoadsideForward { get; }
            public int RoadEdgeHop { get; }
            public int DistrictPenalty { get; }
            public float ReferenceDistance { get; }

            /// <summary>
            /// True when the stop edge carries a vehicle grade. Level
            /// kerbs sort first: a shelter on a slope leans visibly and
            /// its door docks drift from the physical sidewalk height,
            /// so a graded stop is a last resort for mandatory service.
            /// </summary>
            public bool IsGraded { get; }
        }

        private sealed class RouteOccurrence
        {
            public RouteOccurrence(
                TemporaryLink source,
                StopCandidate stopCandidate)
            {
                Source = source;
                StopCandidate = stopCandidate;
            }

            public TemporaryLink Source { get; }
            public StopCandidate StopCandidate { get; }
        }
    }
}
