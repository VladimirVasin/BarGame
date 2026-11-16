using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Coverage of City service, including the published district network
    /// and the planner's single-line fallback.
    /// </summary>
    public sealed class CityBusCoverageTests
    {
        [Test]
        public void EveryStopKeepsItsPavementWaitPoint()
        {
            var layout = CreateLayout();
            var pedestrians = CityPedestrianPlanner.Create(layout, layout.Seed);
            var walkable = RoadWalkableArea.FromLayout(layout);
            foreach (var line in CityDistrictBusPlanner.Create(layout))
            {
                CityBusStopWaitPlan waits = CityBusStopWaitPlanner.Create(line, pedestrians, walkable, layout);
                Assert.That(waits.WaitPoints.Select(point => point.StopId),
                    Is.EquivalentTo(line.Stops.Select(stop => stop.Id)), line.RouteId);
            }
        }

        [Test]
        public void EveryDestination_IsWithinWalkOfAStop()
        {
            var layout = CreateLayout();
            var pedestrianPlan = CityPedestrianPlanner.Create(layout, layout.Seed);
            var walkableArea = RoadWalkableArea.FromLayout(layout);
            var network = CityDistrictBusPlanner.Create(layout);
            var waitPlan = new CityBusStopWaitPlan(network.SelectMany(line =>
                CityBusStopWaitPlanner.Create(line, pedestrianPlan, walkableArea, layout)
                    .WaitPoints).ToArray());
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
            if (layout.HasSummitHome)
                destinations.Add(("home-summit", layout.PlayerHome.SidewalkArrivalPosition));

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

            TestContext.Out.WriteLine(
                $"worst destination: {worstDestinationId} {worstDestination:F1} m");
            Assert.That(
                violations,
                Is.Empty,
                "Destinations beyond the stop walk budget: " +
                string.Join(" | ", violations));
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
        public void StopIntervals_AreDistinctAndCloseEachLine()
        {
            var layout = CreateLayout();
            foreach (CityBusPlan line in CityDistrictBusPlanner.Create(layout))
            {
                var stops = line.Stops;
                Assert.That(stops.Count, Is.GreaterThan(2), line.RouteId);
                Assert.That(stops.Select(stop => stop.SequenceIndex), Is.EqualTo(Enumerable.Range(0, stops.Count)));
                for (int index = 0; index < stops.Count; index++)
                {
                    var current = stops[index];
                    var next = stops[(index + 1) % stops.Count];
                    float gap = next.DistanceAlongLoop - current.DistanceAlongLoop;
                    if (index == stops.Count - 1) gap += line.LoopLength;
                    Assert.That(gap, Is.GreaterThan(GeometryTolerance), current.Id);

                    for (int other = index + 1; other < stops.Count; other++)
                        Assert.That(CityBusNetworkFurniture.SamePlatform(current.ShelterPosition, stops[other].ShelterPosition),
                            Is.False, current.Id + " shares a platform with " + stops[other].Id);
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

        [Test]
        public void EveryShelter_StandsOnThePhysicalPavement()
        {
            var layout = CreateLayout();
            var stops = CityBusNetworkFurniture.Stops(CityDistrictBusPlanner.Create(layout)).ToArray();
            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);
            var clearance = CityBusPlatformClearance.ForLayout(layout);
            Assert.That(stops, Is.Not.Empty);

            // The shelter's Y is the local sidewalk top every consumer
            // trusts: the stop visual sits its boxes on it, the ride
            // plan grounds its door docks from it and the shelter
            // bench derives its plank and sit-entry heights from it.
            // An analytic grade-line height floated the home shelter
            // 8 cm above the boxed pavement and the bench refused its
            // sitter, so the built surface is the only accepted truth.
            var violations = new List<string>();
            foreach (CityBusStopDescriptor stop in stops)
            {
                float surfaceTop;
                bool supported = stop.RoadEdge.A == stop.RoadEdge.B
                    ? clearance.TryHeight(stop.ShelterPosition, out surfaceTop)
                    : CityBusPlanner.TryResolveShelterGroundTop(layout, surfaces, stop.ShelterPosition, out surfaceTop);
                if (!supported)
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
            layout = CreateLayout();
            busPlan = CityBusPlanner.Create(layout);
            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(layout);
            pedestrianPlan = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed,
                streetSurfaces);
            walkableArea = RoadWalkableArea.FromLayout(layout);
        }

        private static CityLayout CreateLayout() => CityLayoutGenerator.Generate(
            CityBlueprintCatalog.Default, CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);

        private static float XzDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }
    }
}
