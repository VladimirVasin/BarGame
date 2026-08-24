using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The grand-loop contract of Route 01: every notable destination in
    /// the city lies within a walk of some stop, the along-loop spacing
    /// stays regular, and open-area stops actually sit by their gates.
    /// </summary>
    public sealed class CityBusCoverageTests
    {
        [Test]
        public void EveryStopKeepsItsPavementWaitPoint()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            CityBusStopWaitPlan waitPlan = CityBusStopWaitPlanner.Create(
                busPlan,
                pedestrianPlan,
                walkableArea);

            Assert.That(
                waitPlan.Count,
                Is.EqualTo(busPlan.Stops.Count),
                "A stop whose pavement the wait planner silently skips " +
                "is a stop nobody can be served at: " +
                string.Join(
                    ", ",
                    busPlan.Stops
                        .Where(stop => !waitPlan.WaitPoints.Any(point =>
                            point.StopIndex == stop.SequenceIndex))
                        .Select(stop => stop.Id)));
        }

        [Test]
        public void EveryDestination_IsWithinWalkOfAStop()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            CityBusStopWaitPlan waitPlan = CityBusStopWaitPlanner.Create(
                busPlan,
                pedestrianPlan,
                walkableArea);
            Assert.That(waitPlan.Count, Is.GreaterThan(0));

            var destinations = new List<(string id, Vector3 position)>();
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                destinations.Add((access.Id, access.Center));
            }

            for (int index = 0; index < layout.Park.Gates.Count; index++)
            {
                CityParkGateDescriptor gate = layout.Park.Gates[index];
                destinations.Add((gate.Id, gate.Center));
            }

            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    destinations.Add((
                        point.Id + ":" + accessIndex,
                        point.Accesses[accessIndex].Center));
                }
            }

            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (lot.IsBar || lot.IsSupermarket ||
                    lot == layout.PlayerHome)
                {
                    destinations.Add((
                        "lot:" + lot.Cell.x + ":" + lot.Cell.y,
                        lot.SidewalkArrivalPosition));
                }
            }

            float worstDestination = 0f;
            string worstDestinationId = string.Empty;
            var violations = new List<string>();
            foreach ((string id, Vector3 position) destination in
                destinations)
            {
                float walk = MeasureWalkToNearestStop(
                    pedestrianPlan,
                    waitPlan,
                    destination.position);
                if (walk > worstDestination)
                {
                    worstDestination = walk;
                    worstDestinationId = destination.id;
                }

                if (walk > CityBusPlanner.MaximumStopWalkDistance)
                {
                    violations.Add(
                        $"{destination.id}: {walk:F1} m");
                }
            }

            // The whole pavement graph is "any point of the city": every
            // street corner a walker can stand on must also be served.
            // Off-grid lanes (the seacoast esplanade, the mol) cross open
            // walkable ground where the player cuts straight across, so
            // they are measured by air distance with the interior
            // allowance instead of along the sparse NPC lane.
            GetRoadGridBounds(
                layout,
                out Vector2 gridMinimum,
                out Vector2 gridMaximum);
            float offGridBudget =
                CityBusPlanner.MaximumStopWalkDistance +
                CityBusPlanner.MaximumOpenAreaInteriorWalk;
            float worstNode = 0f;
            int worstNodeIndex = -1;
            int isolatedNodes = 0;
            for (int index = 0;
                 index < pedestrianPlan.Nodes.Count;
                 index++)
            {
                Vector3 nodePosition =
                    pedestrianPlan.Nodes[index].Position;
                bool onGrid =
                    nodePosition.x >= gridMinimum.x - 5f &&
                    nodePosition.x <= gridMaximum.x + 5f &&
                    nodePosition.z >= gridMinimum.y - 5f &&
                    nodePosition.z <= gridMaximum.y + 5f;
                if (!onGrid)
                {
                    float air = float.PositiveInfinity;
                    for (int stopIndex = 0;
                         stopIndex < busPlan.Stops.Count;
                         stopIndex++)
                    {
                        air = Mathf.Min(
                            air,
                            XzDistance(
                                busPlan.Stops[stopIndex].ShelterPosition,
                                nodePosition));
                    }

                    Assert.That(
                        air,
                        Is.LessThanOrEqualTo(offGridBudget),
                        $"off-grid pavement node {index} at " +
                        nodePosition);
                    continue;
                }

                float best = float.PositiveInfinity;
                for (int pointIndex = 0;
                     pointIndex < waitPlan.WaitPoints.Count;
                     pointIndex++)
                {
                    float distance = waitPlan
                        .WaitPoints[pointIndex].NodeDistances[index];
                    if (distance < best)
                    {
                        best = distance;
                    }
                }

                // An isolated NPC-only cycle (a park-path loop the
                // 2-core retains) has no street connection to walk in
                // from, so it cannot be held against the bus.
                if (float.IsPositiveInfinity(best))
                {
                    isolatedNodes++;
                    continue;
                }

                if (best > worstNode)
                {
                    worstNode = best;
                    worstNodeIndex = index;
                }
            }

            TestContext.Out.WriteLine(
                $"isolated pavement nodes: {isolatedNodes}");

            TestContext.Out.WriteLine(
                $"worst destination: {worstDestinationId} " +
                $"{worstDestination:F1} m; worst pavement node: " +
                $"{worstNodeIndex} {worstNode:F1} m at " +
                (worstNodeIndex >= 0
                    ? pedestrianPlan.Nodes[worstNodeIndex].Position
                        .ToString()
                    : "-"));
            Assert.That(
                violations,
                Is.Empty,
                "Destinations beyond the stop walk budget: " +
                string.Join(" | ", violations));
            // Named destinations hold the strict budget above; the raw
            // pavement sweep — every lane metre including the sunken
            // riverside walks that climb out only at their landings —
            // holds the destination budget plus the interior allowance.
            Assert.That(
                worstNode,
                Is.LessThanOrEqualTo(offGridBudget),
                "The farthest pavement node must stay within the " +
                "extended walk budget of some stop.");
        }

        [Test]
        public void OpenAreaInteriors_StayWithinReachOfSomeStop()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);

            float budget = CityBusPlanner.MaximumStopWalkDistance +
                           CityBusPlanner.MaximumOpenAreaInteriorWalk;
            float worst = 0f;
            string worstId = string.Empty;
            var violations = new List<string>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!surface.IsWalkable ||
                    surface.Feature == CityAreaFeatureKind.UrbanDistrict ||
                    surface.Feature == CityAreaFeatureKind.CentralPark)
                {
                    continue;
                }

                Vector3 center = surface.WorldBounds.center;
                float best = float.PositiveInfinity;
                for (int stopIndex = 0;
                     stopIndex < busPlan.Stops.Count;
                     stopIndex++)
                {
                    float distance = XzDistance(
                        busPlan.Stops[stopIndex].ShelterPosition,
                        center);
                    if (distance < best)
                    {
                        best = distance;
                    }
                }

                if (best > worst)
                {
                    worst = best;
                    worstId = surface.AreaId + ":" + surface.Cell;
                }

                if (best > budget)
                {
                    violations.Add(
                        $"{surface.AreaId}:{surface.Cell} {best:F1} m");
                }
            }

            TestContext.Out.WriteLine(
                $"worst open-area cell: {worstId} {worst:F1} m " +
                $"(budget {budget:F1})");
            Assert.That(
                violations,
                Is.Empty,
                "Open-area cells beyond any stop's reach: " +
                string.Join(" | ", violations));
        }

        [Test]
        public void StopSpacing_StaysRegularAlongTheLoop()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            IReadOnlyList<CityBusStopDescriptor> stops = busPlan.Stops;
            Assert.That(stops.Count, Is.GreaterThan(10));

            int withinTarget = 0;
            float worstGap = 0f;
            for (int index = 0; index < stops.Count; index++)
            {
                CityBusStopDescriptor current = stops[index];
                CityBusStopDescriptor next =
                    stops[(index + 1) % stops.Count];
                float gap = next.DistanceAlongLoop -
                            current.DistanceAlongLoop;
                if (index == stops.Count - 1)
                {
                    gap += busPlan.LoopLength;
                }

                worstGap = Mathf.Max(worstGap, gap);
                if (gap <= CityBusPlanner.MaximumStopGap)
                {
                    withinTarget++;
                }

                TestContext.Out.WriteLine(
                    $"{current.SequenceIndex:D2} " +
                    $"{current.DistanceAlongLoop,7:F1} m  " +
                    $"gap {gap,6:F1}  {current.TargetKind,-24} " +
                    current.Id);

                // Doubled-back approach corridors legitimately stretch a
                // few gaps past the target, but never beyond three ideal
                // intervals.
                Assert.That(
                    gap,
                    Is.LessThanOrEqualTo(
                        CityBusPlanner.TargetStopSpacing * 3f),
                    $"{current.Id} -> {next.Id}");
                // The minimum binds every pair: target stops closer than
                // it are coalesced by the planner, inserted stops never
                // land closer. Only home and the district points of
                // interest may compress — they are never dropped.
                bool bothMandatory =
                    IsMandatoryStopKind(current.TargetKind) &&
                    IsMandatoryStopKind(next.TargetKind);
                if (!bothMandatory)
                {
                    Assert.That(
                        gap,
                        Is.GreaterThanOrEqualTo(
                            CityBusPlanner.MinimumStopSpacing -
                            GeometryTolerance),
                        $"{current.Id} -> {next.Id}");
                }
            }

            TestContext.Out.WriteLine(
                $"stops={stops.Count} loop={busPlan.LoopLength:F1} " +
                $"worstGap={worstGap:F1} " +
                $"withinTarget={withinTarget}/{stops.Count}");

            // withinTarget stays a diagnostic print only. The old
            // "two thirds of gaps within MaximumStopGap" quota measured
            // an artefact: on the folded corridors that regularity was
            // held up by poles standing metres apart across the fold,
            // which the planar floor below now removes on purpose. The
            // real service guards are the mean interval here, the
            // three-interval ceiling above and the walk budgets in the
            // coverage tests — do not resurrect the quota.
            Assert.That(
                busPlan.LoopLength / stops.Count,
                Is.LessThanOrEqualTo(
                    CityBusPlanner.TargetStopSpacing * 1.5f),
                "The mean along-loop interval must stay near the " +
                "target spacing.");

            // No two poles anywhere on the map may stand closer than
            // the planar floor, whatever their order along the loop —
            // the same bus calls at both, so the pair is redundant by
            // construction. Only a pair of never-dropped stops (home,
            // district points of interest) may compress.
            for (int first = 0; first < stops.Count; first++)
            {
                for (int second = first + 1;
                     second < stops.Count;
                     second++)
                {
                    if (IsMandatoryStopKind(stops[first].TargetKind) &&
                        IsMandatoryStopKind(stops[second].TargetKind))
                    {
                        continue;
                    }

                    Vector3 delta = stops[first].ShelterPosition -
                                    stops[second].ShelterPosition;
                    delta.y = 0f;
                    Assert.That(
                        delta.magnitude,
                        Is.GreaterThanOrEqualTo(
                            CityBusPlanner.MinimumPlanarStopSpacing -
                            GeometryTolerance),
                        $"{stops[first].Id} and {stops[second].Id} " +
                        "stand on practically the same spot.");
                }
            }
        }

        [Test]
        public void OpenAreaStops_ServeTheirGates()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            var accessesById =
                new Dictionary<string, CityOpenAreaAccessDescriptor>();
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                accessesById[access.Id] = access;
            }

            // A surviving gate stop still sits by its own gate...
            int matched = 0;
            foreach (CityBusStopDescriptor stop in busPlan.Stops)
            {
                if (stop.TargetKind !=
                    CityBusStopTargetKind.OpenAreaAccess ||
                    !accessesById.TryGetValue(
                        stop.TargetId,
                        out CityOpenAreaAccessDescriptor access))
                {
                    continue;
                }

                matched++;
                Assert.That(
                    XzDistance(stop.ShelterPosition, access.Center),
                    Is.LessThanOrEqualTo(
                        CityBusPlanner.MaximumCoverageStopDistance),
                    stop.Id);
            }

            Assert.That(
                matched,
                Is.GreaterThan(0),
                "The loop must keep dedicated gate stops.");

            // ...and a gate whose own stop was coalesced away must still
            // have SOME pole within the walk budget as the crow flies —
            // the pole that absorbed it stands on the same street.
            foreach (CityOpenAreaAccessDescriptor access in
                accessesById.Values)
            {
                float best = float.PositiveInfinity;
                foreach (CityBusStopDescriptor stop in busPlan.Stops)
                {
                    best = Mathf.Min(
                        best,
                        XzDistance(
                            stop.ShelterPosition,
                            access.Center));
                }

                Assert.That(
                    best,
                    Is.LessThanOrEqualTo(
                        CityBusPlanner.MaximumStopWalkDistance),
                    access.Id);
            }
        }

        [Test]
        public void Stops_KeepClearOfBuildingEntrances()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);

            var violations = new List<string>();
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.IsBar &&
                    !lot.IsSupermarket &&
                    lot != layout.PlayerHome)
                {
                    continue;
                }

                Vector3 door = lot.SidewalkArrivalPosition;
                foreach (CityBusStopDescriptor stop in busPlan.Stops)
                {
                    Vector3 forward = stop.Forward;
                    forward.y = 0f;
                    forward.Normalize();
                    Vector3 wall = stop.ShelterPosition + (forward *
                        CityBusStopWorldBuilder.ShelterOffsetAlongLane);
                    float distance = Mathf.Min(
                        XzDistance(stop.ShelterPosition, door),
                        XzDistance(wall, door));
                    if (distance <
                        CityBusPlanner.BuildingEntranceClearance -
                        GeometryTolerance)
                    {
                        violations.Add(
                            $"{stop.Id} is {distance:F1} m from the " +
                            $"door of lot ({lot.Cell.x}, {lot.Cell.y})");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Stop furniture crowding a building entrance: " +
                string.Join(" | ", violations));
        }

        private static bool IsMandatoryStopKind(
            CityBusStopTargetKind kind)
        {
            return kind == CityBusStopTargetKind.PlayerHome ||
                   kind ==
                   CityBusStopTargetKind.DistrictPointOfInterest;
        }

        private const float GeometryTolerance = 0.001f;

        /// <summary>
        /// True walking distance: the walker may enter the pavement graph
        /// at ANY node, so the measure minimises entry leg plus graph
        /// distance over all of them. Entering only at the geometrically
        /// nearest node reads as infinite next to an isolated park-path
        /// cycle the 2-core happens to retain.
        /// </summary>
        private static float MeasureWalkToNearestStop(
            CityPedestrianPlan pedestrianPlan,
            CityBusStopWaitPlan waitPlan,
            Vector3 position)
        {
            float best = float.PositiveInfinity;
            for (int index = 0;
                 index < pedestrianPlan.Nodes.Count;
                 index++)
            {
                float entry = XzDistance(
                    pedestrianPlan.Nodes[index].Position,
                    position);
                if (entry >= best)
                {
                    continue;
                }

                for (int pointIndex = 0;
                     pointIndex < waitPlan.WaitPoints.Count;
                     pointIndex++)
                {
                    float total = entry + waitPlan
                        .WaitPoints[pointIndex].NodeDistances[index];
                    if (total < best)
                    {
                        best = total;
                    }
                }
            }

            return best;
        }

        private static void GetRoadGridBounds(
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

        [Test]
        public void EveryShelter_StandsOnThePhysicalPavement()
        {
            CreateContext(
                out CityLayout layout,
                out CityBusPlan busPlan,
                out _,
                out _);
            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);
            Assert.That(busPlan.Stops, Is.Not.Empty);

            // The shelter's Y is the local sidewalk top every consumer
            // trusts: the stop visual sits its boxes on it, the ride
            // plan grounds its door docks from it and the shelter
            // bench derives its plank and sit-entry heights from it.
            // An analytic grade-line height floated the home shelter
            // 8 cm above the boxed pavement and the bench refused its
            // sitter, so the built surface is the only accepted truth.
            var violations = new List<string>();
            for (int index = 0; index < busPlan.Stops.Count; index++)
            {
                CityBusStopDescriptor stop = busPlan.Stops[index];
                if (!CityBusPlanner.TryResolveShelterGroundTop(
                        layout,
                        surfaces,
                        stop.ShelterPosition,
                        out float surfaceTop))
                {
                    violations.Add(
                        stop.Id + ": no physical surface under the " +
                        "shelter");
                    continue;
                }

                float drift = stop.ShelterPosition.y - surfaceTop;
                if (Mathf.Abs(drift) > 0.001f)
                {
                    violations.Add(
                        stop.Id + ": shelter floats " +
                        drift.ToString("F3") + " m off the pavement");
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Every shelter must stand exactly on the surface the " +
                "walker grounds on: " +
                string.Join("; ", violations));
        }

        private static void CreateContext(
            out CityLayout layout,
            out CityBusPlan busPlan,
            out CityPedestrianPlan pedestrianPlan,
            out RoadWalkableArea walkableArea)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            busPlan = CityBusPlanner.Create(layout);
            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(layout);
            pedestrianPlan = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed,
                streetSurfaces);
            walkableArea = RoadWalkableArea.FromLayout(layout);
        }

        private static float XzDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }
    }
}
