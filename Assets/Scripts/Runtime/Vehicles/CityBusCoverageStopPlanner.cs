using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Extends Route 01 from a five-stop point-of-interest tour into the
    /// grand city loop: every open-area gate, two park gates and the
    /// supermarket become coverage targets ordered along the road-grid
    /// perimeter, and a spacing pass inserts extra stops so no along-loop
    /// gap exceeds <see cref="MaximumStopGap"/>.
    /// </summary>
    public static partial class CityBusPlanner
    {
        /// <summary>
        /// Ideal along-loop distance between consecutive stops, about six
        /// 26 m blocks. With a 6 m/s cruise and a 10 s dwell per stop the
        /// effective bus speed stays near 4.2 m/s — still well above the
        /// 2.6 m/s walk, so riding remains worthwhile.
        /// </summary>
        public const float TargetStopSpacing = 150f;

        /// <summary>
        /// A gap longer than this gets intermediate stops: half of it is
        /// the farthest anyone on the loop walks to a pole (~40 s).
        /// </summary>
        public const float MaximumStopGap = 200f;

        /// <summary>
        /// Below this the 10 s dwells make the bus slower than walking.
        /// It binds every stop: inserted stops never land closer than
        /// three blocks to a neighbour, and target stops that end up
        /// closer are coalesced — the surviving pole serves both
        /// destinations, the way one kerb serves a whole square. Only a
        /// home/point-of-interest pair may compress, and none does on
        /// the production layout.
        /// </summary>
        public const float MinimumStopSpacing = 80f;

        /// <summary>
        /// Planar floor between any two poles anywhere on the map,
        /// regardless of their order along the loop. The along-loop
        /// coalesce cannot see the loop folding back on itself: the
        /// production roster carried poles 9.8-24.8 m apart across the
        /// eastern-column fold and the industrial corner, and on a
        /// single one-way loop such neighbours are redundant by
        /// construction — the same bus calls at both. The floor began
        /// at 30 m, sparing the named home+supermarket (33.2 m) and
        /// nightlife+cemetery (31.4 m) pairs; the user then flagged
        /// riding a 338 m leg to step off 33 m away, so it rose to
        /// 35 m and the retention ranks resolved them — the
        /// supermarket merges into the home stop, the cemetery gate
        /// into the nightlife point of interest, and the walk budgets
        /// in the coverage tests keep proving both destinations
        /// served. The tightest surviving pairs stand ~37 m apart.
        /// </summary>
        public const float MinimumPlanarStopSpacing = 35f;

        /// <summary>
        /// A planar drop must never tear a service hole this long into
        /// the loop; when the preferred member's removal would, the
        /// pair's other member goes instead, and when either removal
        /// would, both poles stay. Matches the along-loop ceiling the
        /// spacing test enforces.
        /// </summary>
        public const float MaximumCoalescedStopGap = TargetStopSpacing * 3f;

        /// <summary>
        /// Coverage budget proven by the coverage test: every notable
        /// destination lies within this pedestrian-graph distance of some
        /// stop's wait point.
        /// </summary>
        public const float MaximumStopWalkDistance = 150f;

        /// <summary>
        /// Extra Euclidean leg allowed past an open-area gate: the bus
        /// serves the gate, not the wasteland behind it. The deepest
        /// yard corners sit about 145 m from their gates; the beach is
        /// longer than that, which is why it gets spread stops instead
        /// of leaning on this allowance.
        /// </summary>
        public const float MaximumOpenAreaInteriorWalk = 150f;

        /// <summary>
        /// Coverage targets keep only their best few candidates so the
        /// route assignment search stays shallow at sixteen-plus targets.
        /// </summary>
        public const int MaximumCoverageStopCandidates = 3;

        /// <summary>
        /// How far along the road graph a coverage stop may drift from
        /// its gate's frontage edge. One is not enough: the frontages of
        /// the waterfront, the cemetery and the south yards end in
        /// river-flank or map-corner stubs the bus can never leave.
        /// </summary>
        public const int MaximumCoverageStopRoadEdgeHop = 4;

        /// <summary>
        /// Shelter-to-gate ceiling for a drifted coverage stop, matching
        /// the river point-of-interest fallback.
        /// </summary>
        public const float MaximumCoverageStopDistance = 120f;

        /// <summary>
        /// No stop furniture within this XZ distance of a bar, home or
        /// supermarket door's sidewalk point. Checked at the pole and at
        /// the shelter wall centre, so with the `4.25 m` back wall the
        /// nearest physical shelter piece keeps about `4.9 m` of
        /// daylight to the door — a kerb pole beside an entrance, never
        /// across it.
        /// </summary>
        public const float BuildingEntranceClearance = 7f;

        /// <summary>
        /// Resolution of the along-link slide that walks a blocked
        /// placement out of an entrance clearance zone.
        /// </summary>
        private const float EntranceSlideStep = 0.5f;

        /// <summary>
        /// A street edge counts as a park-gate frontage while its road
        /// rectangle lies within this distance of the gate centre — half
        /// the road width plus a sidewalk of slack.
        /// </summary>
        private const float GateFrontageSearchDistance = 5f;

        /// <summary>
        /// A gate stop anchors BESIDE the opening, not in its throat: the
        /// approach strip is half a road wide, and a shelter projected at
        /// the gate centre always lands inside it and gets rejected —
        /// which used to flip the stop into the opposite driving
        /// direction and strangle the strict route search on the sparse
        /// boundary columns.
        /// </summary>
        private const float GateStopSideOffset = 8f;

        private static List<StopTarget> CreateCoverageStopTargets(
            CityLayout layout)
        {
            var result = new List<StopTarget>();
            if (layout.OpenAreaAccesses.Count == 0)
            {
                // An undressed layout (no cemetery, yards or waterfront)
                // keeps the legacy point-of-interest tour untouched.
                return result;
            }

            AddOpenAreaAccessTargets(layout, result);
            AddWaterfrontSpreadTargets(layout, result);
            AddParkGateTargets(layout, result);
            AddSupermarketTarget(layout, result);
            return result;
        }

        /// <summary>
        /// The beach runs the full northern edge of the map — over 400 m —
        /// while its single access gate sits on the west bank. One stop
        /// cannot put the whole shore within walking distance, so the
        /// eastern half gets two spread targets that also pull the loop
        /// along the seaside boulevard.
        /// </summary>
        private static void AddWaterfrontSpreadTargets(
            CityLayout layout,
            List<StopTarget> result)
        {
            if (layout.River == null || !layout.River.IsEnabled)
            {
                return;
            }

            int corridorCellX = layout.River.Definition.CorridorCellX;
            var easternBeachEdges = new List<RoadEdge>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (!edge.IsHorizontal ||
                    layout.GetPathKind(edge) != CityPathKind.Street ||
                    GetRiverBank(edge, corridorCellX) != RiverBank.East)
                {
                    continue;
                }

                var northCell = new Vector2Int(edge.A.x, edge.A.y);
                if (layout.TryGetArea(
                        northCell,
                        out CityAreaDefinition area) &&
                    area.Archetype == CityDistrictKind.NorthWaterfront)
                {
                    easternBeachEdges.Add(edge);
                }
            }

            if (easternBeachEdges.Count == 0)
            {
                return;
            }

            easternBeachEdges.Sort(RoadEdge.Compare);
            RoadEdge farEdge =
                easternBeachEdges[easternBeachEdges.Count - 1];
            AddWaterfrontTarget(
                layout,
                farEdge,
                "north-waterfront-east",
                result);
            int westX = easternBeachEdges[0].A.x;
            int middleX = (westX + farEdge.A.x) / 2;
            RoadEdge middleEdge = easternBeachEdges[0];
            int bestDelta = int.MaxValue;
            for (int index = 0;
                 index < easternBeachEdges.Count;
                 index++)
            {
                int delta = Mathf.Abs(
                    easternBeachEdges[index].A.x - middleX);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    middleEdge = easternBeachEdges[index];
                }
            }

            if (middleEdge != farEdge)
            {
                AddWaterfrontTarget(
                    layout,
                    middleEdge,
                    "north-waterfront-wild",
                    result);
            }
        }

        private static void AddWaterfrontTarget(
            CityLayout layout,
            RoadEdge edge,
            string suffix,
            List<StopTarget> result)
        {
            Vector3 center =
                (layout.GetNodeWorldPosition(edge.A) +
                 layout.GetNodeWorldPosition(edge.B)) * 0.5f;
            center.z += layout.RoadWidth * 0.5f;
            result.Add(new StopTarget(
                CityBusStopTargetKind.OpenAreaAccess,
                "waterfront:" + layout.BlueprintId + ":" + suffix,
                new Vector2Int(edge.A.x, edge.A.y),
                CityDistrictKind.NorthWaterfront,
                new List<RoadEdge> { edge },
                new List<Vector3> { center },
                new List<Rect>(),
                suffix,
                false));
        }

        private static void AddOpenAreaAccessTargets(
            CityLayout layout,
            List<StopTarget> result)
        {
            var accesses = new List<CityOpenAreaAccessDescriptor>(
                layout.OpenAreaAccesses);
            accesses.Sort((left, right) =>
                string.CompareOrdinal(left.Id, right.Id));
            for (int index = 0; index < accesses.Count; index++)
            {
                CityOpenAreaAccessDescriptor access = accesses[index];
                if (!layout.TryGetArea(
                        access.Cell,
                        out CityAreaDefinition area))
                {
                    continue;
                }

                Vector3 first = layout.GetNodeWorldPosition(
                    access.FrontageEdge.A);
                Vector3 second = layout.GetNodeWorldPosition(
                    access.FrontageEdge.B);
                Vector3 along = second - first;
                along.y = 0f;
                along.Normalize();
                result.Add(new StopTarget(
                    CityBusStopTargetKind.OpenAreaAccess,
                    access.Id,
                    access.Cell,
                    area.Archetype,
                    new List<RoadEdge> { access.FrontageEdge },
                    new List<Vector3>
                    {
                        access.Center + (along * GateStopSideOffset),
                        access.Center - (along * GateStopSideOffset)
                    },
                    new List<Rect> { access.ApproachBounds },
                    access.AreaId,
                    false));
            }
        }

        private static void AddParkGateTargets(
            CityLayout layout,
            List<StopTarget> result)
        {
            CityParkPlan park = layout.Park;
            if (park == null || park.Gates.Count == 0)
            {
                return;
            }

            float splitX = GetParkSplitX(layout, park);
            int westIndex = -1;
            int eastIndex = -1;
            for (int index = 0; index < park.Gates.Count; index++)
            {
                CityParkGateDescriptor gate = park.Gates[index];
                if (gate.Center.x < splitX)
                {
                    if (westIndex < 0 ||
                        ComparesBeforeAsWestGate(
                            gate,
                            park.Gates[westIndex]))
                    {
                        westIndex = index;
                    }
                }
                else if (eastIndex < 0 ||
                         ComparesBeforeAsEastGate(
                             gate,
                             park.Gates[eastIndex]))
                {
                    eastIndex = index;
                }
            }

            if (westIndex >= 0)
            {
                AddParkGateTarget(
                    layout,
                    park.Gates[westIndex],
                    "park-west",
                    result);
            }

            if (eastIndex >= 0)
            {
                AddParkGateTarget(
                    layout,
                    park.Gates[eastIndex],
                    "park-east",
                    result);
            }
        }

        /// <summary>
        /// The outermost gate on each side of the river (or of the gate
        /// spread when there is no river) faces the surrounding district,
        /// which is where a kerbside stop can actually stand.
        /// </summary>
        private static bool ComparesBeforeAsWestGate(
            CityParkGateDescriptor candidate,
            CityParkGateDescriptor current)
        {
            if (candidate.Center.x != current.Center.x)
            {
                return candidate.Center.x < current.Center.x;
            }

            return string.CompareOrdinal(candidate.Id, current.Id) < 0;
        }

        private static bool ComparesBeforeAsEastGate(
            CityParkGateDescriptor candidate,
            CityParkGateDescriptor current)
        {
            if (candidate.Center.x != current.Center.x)
            {
                return candidate.Center.x > current.Center.x;
            }

            return string.CompareOrdinal(candidate.Id, current.Id) < 0;
        }

        private static float GetParkSplitX(
            CityLayout layout,
            CityParkPlan park)
        {
            if (layout.River != null && layout.River.IsEnabled)
            {
                return layout.WorldOrigin.x +
                       ((layout.River.Definition.CorridorCellX + 0.5f) *
                        layout.NodeSpacing.x);
            }

            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int index = 0; index < park.Gates.Count; index++)
            {
                float x = park.Gates[index].Center.x;
                minimum = Mathf.Min(minimum, x);
                maximum = Mathf.Max(maximum, x);
            }

            return (minimum + maximum) * 0.5f;
        }

        private static void AddParkGateTarget(
            CityLayout layout,
            CityParkGateDescriptor gate,
            string suffix,
            List<StopTarget> result)
        {
            List<RoadEdge> frontages = FindFrontageEdgesNear(
                layout,
                gate.Center);
            if (frontages.Count == 0)
            {
                return;
            }

            float clearance = (gate.Width * 0.5f) + 1f;
            var excluded = new Rect(
                gate.Center.x - clearance,
                gate.Center.z - clearance,
                clearance * 2f,
                clearance * 2f);
            result.Add(new StopTarget(
                CityBusStopTargetKind.ParkGate,
                gate.Id,
                GetCellAt(layout, gate.Center),
                CityDistrictKind.CentralPark,
                frontages,
                new List<Vector3> { gate.Center },
                new List<Rect> { excluded },
                suffix,
                false));
        }

        private static void AddSupermarketTarget(
            CityLayout layout,
            List<StopTarget> result)
        {
            BuildingLot lot = layout.Supermarket;
            if (lot == null ||
                !layout.TryGetFrontageEdge(lot, out RoadEdge frontage))
            {
                return;
            }

            Bounds bounds = lot.WorldBounds;
            result.Add(new StopTarget(
                CityBusStopTargetKind.Supermarket,
                "supermarket:" + layout.BlueprintId + ":" +
                lot.Cell.x + ":" + lot.Cell.y,
                lot.Cell,
                lot.District,
                new List<RoadEdge> { frontage },
                new List<Vector3>
                {
                    lot.ReturnPosition,
                    lot.SidewalkArrivalPosition
                },
                new List<Rect>
                {
                    Rect.MinMaxRect(
                        bounds.min.x,
                        bounds.min.z,
                        bounds.max.x,
                        bounds.max.z)
                },
                "supermarket",
                false));
        }

        /// <summary>
        /// Target candidates sort level-first: on the steepest ramps the
        /// door docks drift from the physical sidewalk height (the
        /// supermarket stop once missed it by 6.5 cm), and a target has
        /// the freedom to pick a flat kerb nearby. Grade stays a last
        /// resort rather than a ban — half the eastern loop climbs the
        /// plateau, and banning it outright empties whole stretches.
        /// </summary>
        private static bool IsLevelStopEdge(
            CityLayout layout,
            RoadEdge edge)
        {
            return layout.ElevationPlan.GetTransition(edge).Kind ==
                   CityElevationTransitionKind.Level;
        }

        /// <summary>
        /// The doors a stop must never crowd: every bar, the player home
        /// and the supermarket, each represented by its sidewalk arrival
        /// point.
        /// </summary>
        private static List<Vector3> CreateEntranceClearancePoints(
            CityLayout layout)
        {
            var result = new List<Vector3>();
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (lot.IsBar ||
                    lot.IsSupermarket ||
                    lot == layout.PlayerHome)
                {
                    result.Add(lot.SidewalkArrivalPosition);
                }
            }

            return result;
        }

        /// <summary>
        /// Slides a blocked placement along its link to the nearest spot
        /// whose pole and shelter wall both clear every entrance zone,
        /// preferring the forward direction on ties. False when no legal
        /// spot exists inside the safe span — the caller drops the
        /// candidate link instead of blocking a door.
        /// </summary>
        private static bool TryClearBuildingEntrances(
            CityLayout layout,
            IReadOnlyList<Vector3> entrancePoints,
            IReadOnlyList<CityBusPathSample> samples,
            float minimumDistance,
            float maximumDistance,
            ref float distanceAlongLink)
        {
            if (entrancePoints.Count == 0 ||
                IsEntranceClearPlacement(
                    layout,
                    entrancePoints,
                    samples,
                    distanceAlongLink))
            {
                return true;
            }

            for (float step = EntranceSlideStep; ;
                 step += EntranceSlideStep)
            {
                bool anyInRange = false;
                float forward = distanceAlongLink + step;
                if (forward <= maximumDistance)
                {
                    anyInRange = true;
                    if (IsEntranceClearPlacement(
                            layout,
                            entrancePoints,
                            samples,
                            forward))
                    {
                        distanceAlongLink = forward;
                        return true;
                    }
                }

                float backward = distanceAlongLink - step;
                if (backward >= minimumDistance)
                {
                    anyInRange = true;
                    if (IsEntranceClearPlacement(
                            layout,
                            entrancePoints,
                            samples,
                            backward))
                    {
                        distanceAlongLink = backward;
                        return true;
                    }
                }

                if (!anyInRange)
                {
                    return false;
                }
            }
        }

        private static bool IsEntranceClearPlacement(
            CityLayout layout,
            IReadOnlyList<Vector3> entrancePoints,
            IReadOnlyList<CityBusPathSample> samples,
            float distanceAlongLink)
        {
            EvaluateSamples(
                samples,
                distanceAlongLink,
                out Vector3 position,
                out Vector3 forward);
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 pole = GetShelterPosition(layout, position, right);
            Vector3 wall = pole + (forward *
                CityBusStopWorldBuilder.ShelterOffsetAlongLane);
            for (int index = 0; index < entrancePoints.Count; index++)
            {
                Vector3 entrance = entrancePoints[index];
                if (XzDistance(pole, entrance) <
                    BuildingEntranceClearance ||
                    XzDistance(wall, entrance) <
                    BuildingEntranceClearance)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<RoadEdge> FindFrontageEdgesNear(
            CityLayout layout,
            Vector3 center)
        {
            var found = new List<RoadEdge>();
            var distances = new Dictionary<RoadEdge, float>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Rect rect = layout.GetRoadRect(edge);
                float dx = Mathf.Max(
                    0f,
                    Mathf.Max(rect.xMin - center.x, center.x - rect.xMax));
                float dz = Mathf.Max(
                    0f,
                    Mathf.Max(rect.yMin - center.z, center.z - rect.yMax));
                float distance = Mathf.Sqrt((dx * dx) + (dz * dz));
                if (distance > GateFrontageSearchDistance)
                {
                    continue;
                }

                found.Add(edge);
                distances[edge] = distance;
            }

            found.Sort((left, right) =>
            {
                int comparison = distances[left].CompareTo(
                    distances[right]);
                return comparison != 0
                    ? comparison
                    : RoadEdge.Compare(left, right);
            });
            if (found.Count > 2)
            {
                found.RemoveRange(2, found.Count - 2);
            }

            return found;
        }

        private static Vector2Int GetCellAt(
            CityLayout layout,
            Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(
                    (position.x - layout.WorldOrigin.x) /
                    layout.NodeSpacing.x),
                Mathf.FloorToInt(
                    (position.z - layout.WorldOrigin.z) /
                    layout.NodeSpacing.y));
        }

        private static bool IsCoverageStopTargetKind(
            CityBusStopTargetKind kind)
        {
            return kind == CityBusStopTargetKind.OpenAreaAccess ||
                   kind == CityBusStopTargetKind.ParkGate ||
                   kind == CityBusStopTargetKind.Supermarket;
        }

        private static RiverBank GetTargetBank(
            StopTarget target,
            int corridorCellX)
        {
            for (int index = 0;
                 index < target.FrontageEdges.Count;
                 index++)
            {
                RiverBank bank = GetRiverBank(
                    target.FrontageEdges[index],
                    corridorCellX);
                if (bank != RiverBank.Unknown)
                {
                    return bank;
                }
            }

            return RiverBank.Unknown;
        }

        /// <summary>
        /// Orders the targets by their station along the road-grid
        /// perimeter, counter-clockwise from the south-west corner. The
        /// projection keeps a target's x on the horizontal arcs, so every
        /// west-bank target lands on the west half of the walk and the
        /// served order crosses the river exactly twice — the invariant
        /// <see cref="TryBuildRiverClosedRoute"/> demands. Unlike the
        /// service-loop tour the direction is never reversed: counter-
        /// clockwise keeps the right-hand kerb — and the doors — toward
        /// the outer precincts.
        /// </summary>
        private static List<StopTarget> OrderAsPerimeterLoop(
            CityLayout layout,
            List<StopTarget> targets)
        {
            if (targets.Count <= 2)
            {
                return RotateToHomeForward(targets);
            }

            GetRoadFootprintBounds(
                layout,
                out Vector2 minimum,
                out Vector2 maximum);
            var stations = new float[targets.Count];
            var order = new List<int>(targets.Count);
            for (int index = 0; index < targets.Count; index++)
            {
                stations[index] = GetPerimeterStation(
                    minimum,
                    maximum,
                    GetTourPosition(targets[index]));
                order.Add(index);
            }

            order.Sort((left, right) =>
            {
                int comparison = stations[left].CompareTo(stations[right]);
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(
                        targets[left].Id,
                        targets[right].Id);
            });
            var result = new List<StopTarget>(targets.Count);
            for (int index = 0; index < order.Count; index++)
            {
                result.Add(targets[order[index]]);
            }

            return RotateToHomeForward(result);
        }

        private static List<StopTarget> RotateToHomeForward(
            List<StopTarget> targets)
        {
            if (targets.Count == 0)
            {
                return targets;
            }

            int homeIndex = -1;
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].Kind == CityBusStopTargetKind.PlayerHome)
                {
                    homeIndex = index;
                    break;
                }
            }

            if (homeIndex < 0)
            {
                homeIndex = 0;
            }

            var forward = new List<StopTarget>(targets.Count);
            for (int index = 0; index < targets.Count; index++)
            {
                forward.Add(targets[(homeIndex + index) % targets.Count]);
            }

            return forward;
        }

        private static void GetRoadFootprintBounds(
            CityLayout layout,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                Vector3 position = layout.GetNodeWorldPosition(
                    layout.Nodes[index]);
                minimum.x = Mathf.Min(minimum.x, position.x);
                minimum.y = Mathf.Min(minimum.y, position.z);
                maximum.x = Mathf.Max(maximum.x, position.x);
                maximum.y = Mathf.Max(maximum.y, position.z);
            }
        }

        private static float GetPerimeterStation(
            Vector2 minimum,
            Vector2 maximum,
            Vector3 position)
        {
            float x = Mathf.Clamp(position.x, minimum.x, maximum.x);
            float z = Mathf.Clamp(position.z, minimum.y, maximum.y);
            float width = maximum.x - minimum.x;
            float height = maximum.y - minimum.y;
            float south = z - minimum.y;
            float east = maximum.x - x;
            float north = maximum.y - z;
            float west = x - minimum.x;
            float best = south;
            int edge = 0;
            if (east < best)
            {
                best = east;
                edge = 1;
            }

            if (north < best)
            {
                best = north;
                edge = 2;
            }

            if (west < best)
            {
                edge = 3;
            }

            switch (edge)
            {
                case 0:
                    return x - minimum.x;
                case 1:
                    return width + (z - minimum.y);
                case 2:
                    return width + height + (maximum.x - x);
                default:
                    return (2f * width) + height + (maximum.y - z);
            }
        }

        /// <summary>
        /// Drops the less essential stop of any pair closer than
        /// <see cref="MinimumStopSpacing"/> along the loop. Target stops
        /// anchor to destinations independently, so a gate, a park gate
        /// and a supermarket can pile onto neighbouring kerbs; a real
        /// city consolidates those into one pole serving the square. The
        /// route geometry is untouched — the bus still drives past the
        /// dropped gate, and the coverage tests prove the surviving
        /// neighbour keeps every destination within the walk budget.
        /// Home and the district points of interest are never dropped.
        /// </summary>
        private static List<CityBusStopDescriptor> CoalesceCloseStops(
            List<CityBusStopDescriptor> stops,
            float loopLength)
        {
            if (stops.Count < 3 || loopLength <= MinimumStopSpacing)
            {
                return stops;
            }

            var working = new List<CityBusStopDescriptor>(stops);
            bool removed = true;
            while (removed && working.Count > 2)
            {
                removed = false;
                for (int index = 0; index < working.Count; index++)
                {
                    CityBusStopDescriptor current = working[index];
                    int nextIndex = (index + 1) % working.Count;
                    CityBusStopDescriptor next = working[nextIndex];
                    float gap = next.DistanceAlongLoop -
                                current.DistanceAlongLoop;
                    if (index == working.Count - 1)
                    {
                        gap += loopLength;
                    }

                    if (gap >= MinimumStopSpacing)
                    {
                        continue;
                    }

                    int drop = SelectStopToDrop(current, next);
                    if (drop == 0)
                    {
                        continue;
                    }

                    working.RemoveAt(drop == 1 ? index : nextIndex);
                    removed = true;
                    break;
                }
            }

            return working.Count == stops.Count
                ? stops
                : RenumberStops(working);
        }

        /// <summary>
        /// Drops the less essential stop of any pair standing closer
        /// than <see cref="MinimumPlanarStopSpacing"/> across the map,
        /// whatever their order along the loop. The along-loop coalesce
        /// only compares consecutive stations, so it never sees the
        /// loop folding back on itself — poles metres apart on the
        /// eastern-column fold and around the industrial corner
        /// survived it. Every drop is tried empirically: the spacing
        /// insertion refills whatever hole the removal opens, and only
        /// when even the refilled loop keeps a gap past
        /// <see cref="MaximumCoalescedStopGap"/> does the drop roll
        /// back — first onto the pair's other member, then into a
        /// permanent protection of the pair. A forecast veto had kept
        /// the home+supermarket pair standing 33 m apart: it could not
        /// know the refill would stand a fresh pole mid-corridor.
        /// </summary>
        private static List<CityBusStopDescriptor> CoalescePlanarCloseStops(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            IReadOnlyList<TemporaryNode> nodes,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> finalLinks,
            float loopLength,
            List<CityBusStopDescriptor> stops)
        {
            if (stops.Count < 3)
            {
                return stops;
            }

            // Termination: refill insertions respect the planar floor
            // themselves (every insertion candidate checks clearance
            // against every standing pole), so a refill can never mint
            // a new planar violation and removals cannot bring two
            // surviving poles closer. Each iteration therefore either
            // permanently removes one member of a violating pair or
            // permanently protects that pair — the violation set only
            // shrinks, and the scan converges deterministically (fixed
            // pair order, one pair per iteration).
            var protectedPairs = new HashSet<string>();
            var working = new List<CityBusStopDescriptor>(stops);
            bool changed = false;
            while (TryFindPlanarViolation(
                       working,
                       protectedPairs,
                       out int first,
                       out int second))
            {
                string pairKey = PlanarPairKey(
                    working[first],
                    working[second]);
                int drop = SelectStopToDrop(
                    working[first],
                    working[second]);
                int preferredIndex = drop == 1 ? first : second;
                int otherIndex = drop == 1 ? second : first;
                if (TryDropWithRefill(
                        layout,
                        vehicle,
                        nodes,
                        metadata,
                        finalLinks,
                        loopLength,
                        working,
                        preferredIndex,
                        out List<CityBusStopDescriptor> refilled) ||
                    (GetStopRetentionRank(
                         working[otherIndex].TargetKind) != 0 &&
                     TryDropWithRefill(
                         layout,
                         vehicle,
                         nodes,
                         metadata,
                         finalLinks,
                         loopLength,
                         working,
                         otherIndex,
                         out refilled)))
                {
                    working = refilled;
                    changed = true;
                    continue;
                }

                protectedPairs.Add(pairKey);
            }

            return changed ? RenumberStops(working) : stops;
        }

        private static bool TryFindPlanarViolation(
            List<CityBusStopDescriptor> working,
            HashSet<string> protectedPairs,
            out int first,
            out int second)
        {
            for (first = 0; first < working.Count; first++)
            {
                for (second = first + 1;
                     second < working.Count;
                     second++)
                {
                    if (XzDistance(
                            working[first].ShelterPosition,
                            working[second].ShelterPosition) >=
                        MinimumPlanarStopSpacing)
                    {
                        continue;
                    }

                    if (SelectStopToDrop(
                            working[first],
                            working[second]) == 0)
                    {
                        continue;
                    }

                    if (protectedPairs.Contains(PlanarPairKey(
                            working[first],
                            working[second])))
                    {
                        continue;
                    }

                    return true;
                }
            }

            first = -1;
            second = -1;
            return false;
        }

        private static bool TryDropWithRefill(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            IReadOnlyList<TemporaryNode> nodes,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> finalLinks,
            float loopLength,
            List<CityBusStopDescriptor> working,
            int dropIndex,
            out List<CityBusStopDescriptor> result)
        {
            var reduced = new List<CityBusStopDescriptor>(working);
            reduced.RemoveAt(dropIndex);
            result = InsertSpacingStops(
                layout,
                vehicle,
                nodes,
                metadata,
                finalLinks,
                loopLength,
                reduced);
            return !HasServiceHole(result, loopLength);
        }

        private static bool HasServiceHole(
            IReadOnlyList<CityBusStopDescriptor> stops,
            float loopLength)
        {
            for (int index = 0; index < stops.Count; index++)
            {
                int next = (index + 1) % stops.Count;
                float gap = stops[next].DistanceAlongLoop -
                            stops[index].DistanceAlongLoop;
                if (gap < 0f)
                {
                    gap += loopLength;
                }

                if (gap > MaximumCoalescedStopGap)
                {
                    return true;
                }
            }

            return false;
        }

        private static string PlanarPairKey(
            CityBusStopDescriptor first,
            CityBusStopDescriptor second)
        {
            return string.CompareOrdinal(first.Id, second.Id) <= 0
                ? first.Id + "|" + second.Id
                : second.Id + "|" + first.Id;
        }

        /// <summary>
        /// 0 = keep both (a home/point-of-interest pair), 1 = drop the
        /// first, 2 = drop the second. Between coverage stops the more
        /// essential kind survives; equals keep the earlier one.
        /// </summary>
        private static int SelectStopToDrop(
            CityBusStopDescriptor first,
            CityBusStopDescriptor second)
        {
            int firstRank = GetStopRetentionRank(first.TargetKind);
            int secondRank = GetStopRetentionRank(second.TargetKind);
            if (firstRank == 0 && secondRank == 0)
            {
                return 0;
            }

            if (firstRank == 0)
            {
                return 2;
            }

            if (secondRank == 0)
            {
                return 1;
            }

            return firstRank > secondRank ? 1 : 2;
        }

        private static int GetStopRetentionRank(
            CityBusStopTargetKind kind)
        {
            switch (kind)
            {
                case CityBusStopTargetKind.PlayerHome:
                case CityBusStopTargetKind.DistrictPointOfInterest:
                    return 0;
                case CityBusStopTargetKind.OpenAreaAccess:
                    return 1;
                case CityBusStopTargetKind.ParkGate:
                    return 2;
                case CityBusStopTargetKind.Supermarket:
                    return 3;
                default:
                    return 4;
            }
        }

        private static List<CityBusStopDescriptor> RenumberStops(
            List<CityBusStopDescriptor> stops)
        {
            var result = new List<CityBusStopDescriptor>(stops.Count);
            for (int index = 0; index < stops.Count; index++)
            {
                CityBusStopDescriptor stop = stops[index];
                result.Add(new CityBusStopDescriptor(
                    stop.Id,
                    stop.SourceDecorationId,
                    stop.NameLocalizationKey,
                    index,
                    stop.DistanceAlongLoop,
                    stop.District,
                    stop.ShelterPosition,
                    stop.RoadsideForward,
                    stop.LinkIndex,
                    stop.DistanceAlongLink,
                    stop.Position,
                    stop.Forward,
                    stop.RoadEdge,
                    stop.TargetKind,
                    stop.TargetId,
                    stop.TargetCell));
            }

            return result;
        }

        /// <summary>
        /// Walks the closed loop and inserts plain kerbside stops wherever
        /// the along-loop gap between consecutive stops exceeds
        /// <see cref="MaximumStopGap"/>, following the decoration
        /// planner's minimum/coverage spacing idiom. Inserted stops reuse
        /// the target-stop geometry and can only land on a road segment
        /// the loop traverses exactly once, so the pole is served on every
        /// pass of its edge.
        /// </summary>
        private static List<CityBusStopDescriptor> InsertSpacingStops(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            IReadOnlyList<TemporaryNode> nodes,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> finalLinks,
            float loopLength,
            List<CityBusStopDescriptor> stops)
        {
            if (stops.Count < 2 ||
                metadata.Count == 0 ||
                loopLength <= MaximumStopGap)
            {
                return stops;
            }

            var linkStarts = new float[metadata.Count];
            float running = 0f;
            for (int index = 0; index < metadata.Count; index++)
            {
                linkStarts[index] = running;
                running += finalLinks[metadata[index].FinalLinkIndex]
                    .Length;
            }

            Dictionary<DirectedKey, int> directedUses =
                CountDirectedStreetUses(nodes, metadata);
            var usedLinkIndices = new HashSet<int>();
            for (int index = 0; index < stops.Count; index++)
            {
                usedLinkIndices.Add(stops[index].LinkIndex);
            }

            float safeEndDistance = (vehicle.InflatedLength * 0.5f) +
                                    StopJunctionClearance;
            float minimumEndInset = safeEndDistance + CandidateEndInset;
            GetRoadFootprintBounds(
                layout,
                out Vector2 minimum,
                out Vector2 maximum);
            List<Vector3> entrancePoints =
                CreateEntranceClearancePoints(layout);
            Dictionary<string, int> suffixCounts =
                SeedSpacingSuffixCounts(stops);
            var inserted = new List<CityBusStopDescriptor>();
            int originalCount = stops.Count;
            for (int index = 0; index < originalCount; index++)
            {
                CityBusStopDescriptor current = stops[index];
                CityBusStopDescriptor next =
                    stops[(index + 1) % originalCount];
                float gap = next.DistanceAlongLoop -
                            current.DistanceAlongLoop;
                if (index == originalCount - 1)
                {
                    gap += loopLength;
                }

                if (gap <= MaximumStopGap)
                {
                    continue;
                }

                InsertStopsIntoGap(
                    layout,
                    nodes,
                    metadata,
                    finalLinks,
                    linkStarts,
                    directedUses,
                    usedLinkIndices,
                    loopLength,
                    stops,
                    current,
                    gap,
                    minimumEndInset,
                    minimum,
                    maximum,
                    entrancePoints,
                    suffixCounts,
                    inserted);
            }

            if (inserted.Count == 0)
            {
                return stops;
            }

            var all = new List<CityBusStopDescriptor>(
                stops.Count + inserted.Count);
            all.AddRange(stops);
            all.AddRange(inserted);
            all.Sort((left, right) =>
            {
                int comparison = left.DistanceAlongLoop.CompareTo(
                    right.DistanceAlongLoop);
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(left.Id, right.Id);
            });
            return RenumberStops(all);
        }

        /// <summary>
        /// Counts how often the loop drives each DIRECTED street: road
        /// segments plus both spans of every wide-right macro. An
        /// inserted stop needs its own direction driven exactly once —
        /// the bus must never cruise past its own pole without serving
        /// it. The opposite direction is free to repeat: that pass runs
        /// on the far side of the carriageway, as in any real city.
        /// </summary>
        private static Dictionary<DirectedKey, int> CountDirectedStreetUses(
            IReadOnlyList<TemporaryNode> nodes,
            IList<RouteLinkMetadata> metadata)
        {
            var result = new Dictionary<DirectedKey, int>();
            for (int index = 0; index < metadata.Count; index++)
            {
                TemporaryLink link = metadata[index].Source;
                if (link.OccupiesPrimaryRoadEdge)
                {
                    TemporaryNode node = nodes[link.FromNodeIndex];
                    var key = new DirectedKey(
                        node.FromGridNode,
                        node.ToGridNode);
                    result.TryGetValue(key, out int uses);
                    result[key] = uses + 1;
                }

                if (link.HasSecondaryRoadEdge)
                {
                    TemporaryNode node = nodes[link.ToNodeIndex];
                    var key = new DirectedKey(
                        node.FromGridNode,
                        node.ToGridNode);
                    result.TryGetValue(key, out int uses);
                    result[key] = uses + 1;
                }
            }

            return result;
        }

        private static void InsertStopsIntoGap(
            CityLayout layout,
            IReadOnlyList<TemporaryNode> nodes,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> finalLinks,
            float[] linkStarts,
            Dictionary<DirectedKey, int> directedUses,
            ISet<int> usedLinkIndices,
            float loopLength,
            IReadOnlyList<CityBusStopDescriptor> existingStops,
            CityBusStopDescriptor gapStart,
            float gap,
            float minimumEndInset,
            Vector2 boundsMinimum,
            Vector2 boundsMaximum,
            IReadOnlyList<Vector3> entrancePoints,
            Dictionary<string, int> suffixCounts,
            List<CityBusStopDescriptor> inserted)
        {
            // Stations measure from the gap's start stop so the wrap gap
            // (last stop back to home) stays monotonic.
            var gapLinks = new List<int>();
            var gapStations = new List<float>();
            int cursor = gapStart.LinkIndex;
            float station = linkStarts[cursor];
            for (int step = 0; step < metadata.Count; step++)
            {
                cursor = (cursor + 1) % metadata.Count;
                station = linkStarts[cursor];
                if (station < gapStart.DistanceAlongLoop)
                {
                    station += loopLength;
                }

                if (station >= gapStart.DistanceAlongLoop + gap)
                {
                    break;
                }

                gapLinks.Add(cursor);
                gapStations.Add(station);
            }

            int insertCount =
                Mathf.CeilToInt(gap / TargetStopSpacing) - 1;
            float previousStation = gapStart.DistanceAlongLoop;
            float nextStation = gapStart.DistanceAlongLoop + gap;
            for (int slot = 1; slot <= insertCount; slot++)
            {
                float ideal = gapStart.DistanceAlongLoop +
                              (gap * slot / (insertCount + 1f));
                int bestGapIndex = -1;
                float bestPlacement = 0f;
                float bestStation = 0f;
                float bestScore = float.PositiveInfinity;
                for (int gapIndex = 0;
                     gapIndex < gapLinks.Count;
                     gapIndex++)
                {
                    int metadataIndex = gapLinks[gapIndex];
                    RouteLinkMetadata entry = metadata[metadataIndex];
                    if (!entry.Source.IsRoadSegment ||
                        usedLinkIndices.Contains(entry.FinalLinkIndex) ||
                        layout.IsBusFurnitureExcluded(
                            entry.Source.RoadEdge))
                    {
                        continue;
                    }

                    TemporaryNode linkNode =
                        nodes[entry.Source.FromNodeIndex];
                    directedUses.TryGetValue(
                        new DirectedKey(
                            linkNode.FromGridNode,
                            linkNode.ToGridNode),
                        out int uses);
                    if (uses != 1)
                    {
                        continue;
                    }

                    CityBusRouteLink link =
                        finalLinks[entry.FinalLinkIndex];
                    if (link.Length <= minimumEndInset * 2f)
                    {
                        continue;
                    }

                    float placement = Mathf.Clamp(
                        ideal - gapStations[gapIndex],
                        minimumEndInset,
                        link.Length - minimumEndInset);
                    if (!TryClearBuildingEntrances(
                            layout,
                            entrancePoints,
                            link.Samples,
                            minimumEndInset,
                            link.Length - minimumEndInset,
                            ref placement))
                    {
                        continue;
                    }

                    float candidateStation =
                        gapStations[gapIndex] + placement;
                    if (candidateStation - previousStation <
                        MinimumStopSpacing ||
                        nextStation - candidateStation <
                        MinimumStopSpacing)
                    {
                        continue;
                    }

                    // Along-loop spacing alone cannot see the loop fold
                    // back on itself: a candidate a whole gap away by
                    // station can stand on the next kerb over. Planar
                    // clearance against every pole already planned keeps
                    // the planar coalesce from having to drop this
                    // insertion right back out.
                    if (!IsPlanarClearOfStops(
                            layout,
                            finalLinks[entry.FinalLinkIndex],
                            placement,
                            existingStops,
                            inserted))
                    {
                        continue;
                    }

                    float score = Mathf.Abs(candidateStation - ideal);
                    if (score < bestScore - GeometryTolerance)
                    {
                        bestScore = score;
                        bestGapIndex = gapIndex;
                        bestPlacement = placement;
                        bestStation = candidateStation;
                    }
                }

                if (bestGapIndex < 0)
                {
                    continue;
                }

                int bestMetadataIndex = gapLinks[bestGapIndex];
                CityBusStopDescriptor stop = CreateSpacingStop(
                    layout,
                    nodes,
                    metadata[bestMetadataIndex],
                    finalLinks[metadata[bestMetadataIndex].FinalLinkIndex],
                    bestPlacement,
                    bestStation >= loopLength
                        ? bestStation - loopLength
                        : bestStation,
                    boundsMinimum,
                    boundsMaximum,
                    suffixCounts);
                inserted.Add(stop);
                usedLinkIndices.Add(stop.LinkIndex);
                previousStation = bestStation;
            }
        }

        private static bool IsPlanarClearOfStops(
            CityLayout layout,
            CityBusRouteLink link,
            float placement,
            IReadOnlyList<CityBusStopDescriptor> existingStops,
            IReadOnlyList<CityBusStopDescriptor> inserted)
        {
            EvaluateSamples(
                link.Samples,
                placement,
                out Vector3 position,
                out Vector3 forward);
            Vector3 right = new Vector3(
                forward.z,
                0f,
                -forward.x).normalized;
            Vector3 shelter = GetShelterPosition(layout, position, right);
            return IsPlanarClearOf(shelter, existingStops) &&
                   IsPlanarClearOf(shelter, inserted);
        }

        private static bool IsPlanarClearOf(
            Vector3 shelterPosition,
            IReadOnlyList<CityBusStopDescriptor> stops)
        {
            for (int index = 0; index < stops.Count; index++)
            {
                if (XzDistance(
                        shelterPosition,
                        stops[index].ShelterPosition) <
                    MinimumPlanarStopSpacing)
                {
                    return false;
                }
            }

            return true;
        }

        private static CityBusStopDescriptor CreateSpacingStop(
            CityLayout layout,
            IReadOnlyList<TemporaryNode> nodes,
            RouteLinkMetadata entry,
            CityBusRouteLink link,
            float distanceAlongLink,
            float distanceAlongLoop,
            Vector2 boundsMinimum,
            Vector2 boundsMaximum,
            Dictionary<string, int> suffixCounts)
        {
            EvaluateSamples(
                link.Samples,
                distanceAlongLink,
                out Vector3 position,
                out Vector3 forward);
            Vector3 right = new Vector3(
                forward.z,
                0f,
                -forward.x).normalized;
            Vector3 shelter = GetShelterPosition(layout, position, right);
            TemporaryNode node = nodes[entry.Source.FromNodeIndex];
            Vector2Int rightCell = GetRightSideCell(
                node.FromGridNode,
                node.ToGridNode);
            CityDistrictKind district;
            if (layout.TryGetArea(
                    rightCell,
                    out CityAreaDefinition rightArea))
            {
                district = rightArea.Archetype;
            }
            else
            {
                Vector2Int leftCell = GetRightSideCell(
                    node.ToGridNode,
                    node.FromGridNode);
                district = layout.TryGetArea(
                    leftCell,
                    out CityAreaDefinition leftArea)
                    ? leftArea.Archetype
                    : CityDistrictKind.OldTown;
            }

            string suffix = CreateSpacingSuffix(
                position,
                boundsMinimum,
                boundsMaximum,
                suffixCounts);
            string blueprintKey = layout.BlueprintId.Replace('-', '_');
            return new CityBusStopDescriptor(
                "bus-stop:" + layout.BlueprintId + ":route-01:" + suffix,
                string.Empty,
                "bus.stop." + blueprintKey + "." +
                suffix.Replace('-', '_'),
                0,
                distanceAlongLoop,
                district,
                shelter,
                -right,
                entry.FinalLinkIndex,
                distanceAlongLink,
                position,
                forward,
                entry.Source.RoadEdge,
                CityBusStopTargetKind.LoopSpacing,
                string.Empty,
                rightCell);
        }

        /// <summary>
        /// A refill insertion can run again after the planar coalesce,
        /// so the side counters must resume from the poles already
        /// standing — restarting them from zero minted a second
        /// "loop-east" id on the probe roster.
        /// </summary>
        private static Dictionary<string, int> SeedSpacingSuffixCounts(
            IReadOnlyList<CityBusStopDescriptor> stops)
        {
            var counts = new Dictionary<string, int>();
            string[] sides =
            {
                "loop-east",
                "loop-west",
                "loop-north",
                "loop-south"
            };
            const string marker = ":route-01:";
            for (int index = 0; index < stops.Count; index++)
            {
                string id = stops[index].Id;
                int markerIndex = id.LastIndexOf(
                    marker,
                    System.StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    continue;
                }

                string suffix = id.Substring(
                    markerIndex + marker.Length);
                for (int sideIndex = 0;
                     sideIndex < sides.Length;
                     sideIndex++)
                {
                    string side = sides[sideIndex];
                    if (!suffix.StartsWith(
                            side,
                            System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int uses = 1;
                    if (suffix.Length > side.Length)
                    {
                        if (suffix[side.Length] != '-' ||
                            !int.TryParse(
                                suffix.Substring(side.Length + 1),
                                out uses))
                        {
                            continue;
                        }
                    }

                    counts.TryGetValue(side, out int existing);
                    counts[side] = Mathf.Max(existing, uses);
                    break;
                }
            }

            return counts;
        }

        private static string CreateSpacingSuffix(
            Vector3 position,
            Vector2 boundsMinimum,
            Vector2 boundsMaximum,
            Dictionary<string, int> suffixCounts)
        {
            Vector2 center = (boundsMinimum + boundsMaximum) * 0.5f;
            float halfWidth = Mathf.Max(
                1f,
                (boundsMaximum.x - boundsMinimum.x) * 0.5f);
            float halfHeight = Mathf.Max(
                1f,
                (boundsMaximum.y - boundsMinimum.y) * 0.5f);
            float dx = (position.x - center.x) / halfWidth;
            float dz = (position.z - center.y) / halfHeight;
            string side = Mathf.Abs(dx) >= Mathf.Abs(dz)
                ? (dx >= 0f ? "loop-east" : "loop-west")
                : (dz >= 0f ? "loop-north" : "loop-south");
            suffixCounts.TryGetValue(side, out int uses);
            suffixCounts[side] = uses + 1;
            return uses == 0 ? side : side + "-" + (uses + 1);
        }
    }
}
