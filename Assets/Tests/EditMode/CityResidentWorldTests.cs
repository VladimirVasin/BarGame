using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityResidentWorldTests
    {
        [Test]
        public void Samples_AreIndependentOfEarlierWalkingIntervalsAndSkippedDays()
        {
            CityPedestrianPlan plan = DisconnectedPavements();
            var visited = new CityResidentWorld(plan);
            var fresh = new CityResidentWorld(plan);
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                visited.TrySample(resident, 0, 420d, out _);
                visited.TrySample(resident, 0, 570d, out _);
                visited.TrySample(resident, 1, 1080d, out _);
            }

            int sampled = 0;
            foreach (double minute in new[] { 690d, 800d, 1080d, 1380d, 120d })
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                bool first = visited.TrySample(resident, 6, minute, out CityResidentWorld.StreetSample actual);
                bool second = fresh.TrySample(resident, 6, minute, out CityResidentWorld.StreetSample expected);
                Assert.That(first, Is.EqualTo(second), "A scene return must resolve the same resident state.");
                if (!first) continue;
                sampled++;
                Assert.That(Vector3.Distance(actual.Position, expected.Position), Is.LessThan(.0001f));
                Assert.That(actual.FromNode, Is.EqualTo(expected.FromNode));
                Assert.That(actual.ToNode, Is.EqualTo(expected.ToNode));
                Assert.That(actual.Destination, Is.EqualTo(expected.Destination));
                Assert.That(actual.Schedule.StartedAt, Is.EqualTo(expected.Schedule.StartedAt));
                AssertOnPavement(plan, actual);
            }
            Assert.That(sampled, Is.GreaterThan(0));
        }

        [Test]
        public void Destinations_StayInTheirHomeComponentAndUseActualCityFrontages()
        {
            CityPedestrianPlan disconnected = DisconnectedPavements();
            var isolated = new CityResidentWorld(disconnected);
            AssertConnectedDestinations(isolated, disconnected);

            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(layout,
                GameSessionState.DefaultCitySeed, CityStreetSurfacePlanner.Create(layout));
            CityHillsideBuilding summitHome = layout.SummitHome;
            Assert.That(summitHome, Is.Not.Null);
            string homePrefix = $"hill:quarter:summit:house:{summitHome.Lot.Cell.x}:{summitHome.Lot.Cell.y}:";
            int frontage = -1;
            for (int node = 0; node < plan.Nodes.Count; node++)
            {
                string id = plan.Nodes[node].Id;
                if (!id.StartsWith(homePrefix, StringComparison.Ordinal)) continue;
                Assert.That(id, Is.EqualTo(homePrefix + "gate"), "The shared pedestrian graph stops at the public frontage.");
                frontage = node;
            }
            Assert.That(frontage, Is.GreaterThanOrEqualTo(0));
            Assert.That(plan.GetLinkIndices(frontage).Count, Is.EqualTo(2), "The sidewalk stays open in both directions.");
            RoadWalkableArea pavement = CityPedestrianPlanner.CreateWalkableArea(plan);
            Assert.That(pavement.Contains(summitHome.StreetContactWorld, plan.AgentRadius), Is.True);
            Assert.That(pavement.Contains(summitHome.DoorWorld), Is.False, "Side steps cannot use the hero's doorway.");
            var ambientEntries = new List<CityAmbientWalkPlan.Entry>();
            var ambient = new CityAmbientWalkPlan(plan, layout);
            ambient.CollectEntries(summitHome.DoorWorld, 12f, ambientEntries);
            Assert.That(ambientEntries, Is.Not.Empty);
            foreach (var entry in ambientEntries)
                Assert.That(summitHome.CourtBounds.Contains(new Vector2(entry.Position.x, entry.Position.z)), Is.False,
                    "No background walker starts in the private entrance court.");
            for (int link = 0; link < plan.Links.Count; link++)
                if (plan.Links[link].Kind == CityPedestrianLinkKind.HomeEntrance)
                    Assert.That(ambient.Length(link), Is.Zero, "Background routes cannot continue into entrances.");
            var world = new CityResidentWorld(plan, layout);
            var homes = new HashSet<int>();
            var work = new HashSet<int>();
            Assert.That(CityBusStopWaitPlanner.TryFindNearestNode(plan, layout.PlayerHome.SidewalkArrivalPosition,
                out int playerHome), Is.True);
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!CityBusStopWaitPlanner.TryFindNearestNode(plan, lot.SidewalkArrivalPosition, out int node)) continue;
                if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential) homes.Add(node);
                else if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Industrial) work.Add(node);
            }
            var hillHomes = new HashSet<int>();
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                if (house.Lot.IsPlayerHome) continue;
                Vector2 court = house.CourtBounds.center;
                if (CityBusStopWaitPlanner.TryFindNearestNode(plan, new Vector3(court.x, house.DoorWorld.y, court.y), out int node))
                    hillHomes.Add(node);
            }
            homes.UnionWith(hillHomes);
            bool occupiedUphill = false;
            Assert.That(homes, Is.Not.Empty);
            Assert.That(work, Is.Not.Empty);
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                int home = world.Destination(resident, CityResidentActivity.Home);
                occupiedUphill |= hillHomes.Contains(home);
                Assert.That(homes.Contains(home), Is.True, "A resident home is an existing residential frontage.");
                Assert.That(home, Is.Not.EqualTo(playerHome), "The hero's home is not an ambient resident home.");
                Assert.That(world.Home(resident).Building, Is.Not.SameAs(layout.PlayerHome));
                Assert.That(world.Destination(resident, CityResidentActivity.ReturningHome), Is.EqualTo(home));
                if (hillHomes.Contains(home))
                {
                    int streetNode = world.HomeFrontage(resident);
                    Assert.That(streetNode, Is.Not.EqualTo(home));
                    bool joinsPublicPavement = false;
                    foreach (int link in plan.GetLinkIndices(streetNode))
                        joinsPublicPavement |= plan.Links[link].Kind != CityPedestrianLinkKind.HomeEntrance;
                    Assert.That(joinsPublicPavement, Is.True);
                    Assert.That(plan.GetLinkIndices(home).Count, Is.EqualTo(1));
                }
                bool industrialReachable = false;
                foreach (int workplace in work)
                    if (!float.IsPositiveInfinity(world.DistancesTo(home)[workplace])) industrialReachable = true;
                if (industrialReachable)
                    Assert.That(work.Contains(world.Destination(resident, CityResidentActivity.Work)), Is.True,
                        "A reachable workplace uses an industrial frontage.");
            }
            Assert.That(occupiedUphill, Is.True, "Upper residential courts must supply occupied home addresses.");
            AssertConnectedDestinations(world, plan);
        }

        [Test]
        public void RetainedJourney_ContinuesFromTheActualLastLegWithoutRewinding()
        {
            CityPedestrianPlan plan = DisconnectedPavements();
            var world = new CityResidentWorld(plan);
            const int resident = 0;
            int home = world.Destination(resident, CityResidentActivity.Home);
            int goal = world.Destination(resident, CityResidentActivity.Work);
            int from = -1;
            foreach (int link in plan.GetLinkIndices(goal))
            { from = plan.Links[link].Other(goal); break; }
            Assert.That(from, Is.GreaterThanOrEqualTo(0));
            Vector3 midway = Vector3.Lerp(plan.Nodes[from].Position, plan.Nodes[goal].Position, .5f);
            var root = new GameObject("Retained Resident Test");
            try
            {
                CityPedestrianActor actor = root.AddComponent<CityPedestrianActor>();
                actor.Initialize(CityPedestrianPlanner.CreateWalkableArea(plan), plan.AgentRadius);
                actor.PrepareSpawn(plan, new CityPedestrianSpawnAnchor("retained", midway, from, goal),
                    goal, CityResidentWorld.WalkingSpeed, 1f, 0f, 0, 1u);
                actor.SetResidentDestination(goal, world.DistancesTo(goal));
                world.Retain(resident, actor, 2, 500d);
                Assert.That(world.TrySample(resident, 2, 500d, out CityResidentWorld.StreetSample sample), Is.True,
                    "A retained final leg remains outdoors until its actual remaining distance is walked.");
                Assert.That(Vector3.Distance(sample.Position, midway), Is.LessThan(.001f));
                Assert.That(sample.FromNode, Is.EqualTo(from));
                Assert.That(sample.ToNode, Is.EqualTo(goal));
                Assert.That(world.TrySample(resident, 2, 500.1d, out CityResidentWorld.StreetSample later), Is.True);
                Assert.That(Vector3.Distance(later.Position, plan.Nodes[goal].Position),
                    Is.LessThan(Vector3.Distance(midway, plan.Nodes[goal].Position)));
                Assert.That(world.TrySample(resident, 2, 600d, out _), Is.False,
                    "Work becomes a logical indoor state after the remaining journey, without a fictitious extra leg.");
                Assert.That(home / 4, Is.EqualTo(goal / 4));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void HiddenServices_CarryResidentsToWorkAndHomeAndRestoreTheirSeats()
        {
            int seed = GameSessionState.DefaultCitySeed;
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default, CityGenerationSettings.Default, seed);
            CityPedestrianPlan pavement = CityPedestrianPlanner.Create(layout, seed, CityStreetSurfacePlanner.Create(layout));
            var world = new CityResidentWorld(pavement, layout);
            var lines = CityDistrictBusPlanner.Create(layout);
            var waits = new CityBusStopWaitPlan[lines.Count];
            var area = CityPedestrianPlanner.CreateWalkableArea(pavement);
            foreach (var platform in CityBusNetworkFurniture.Platforms(lines))
                area.Add(CityBusStopWaitPlanner.QueueApron(platform.line, platform.stop, layout));
            var events = new List<(double minute, int vehicle, int stop)>();
            const int days = 2;
            for (int line = 0; line < lines.Count; line++)
            {
                waits[line] = CityBusStopWaitPlanner.Create(lines[line], pavement, area, layout);
                Assert.That(waits[line].Count, Is.EqualTo(lines[line].Stops.Count),
                    $"Line {line + 1} must serve residents at every production platform.");
                if (lines[line].Number == 3)
                {
                    foreach (var point in waits[line].WaitPoints)
                    {
                        var stop = lines[line].Stops[point.StopIndex];
                        Vector3 kerb = CityBusStopWaitPlanner.StandingPosition(lines[line], stop, layout);
                        Assert.That(Vector3.Distance(pavement.Nodes[point.PedestrianNodeIndex].Position, kerb),
                            Is.LessThan(.5f), $"The hillside graph must reach the correct boarding-side kerb at {stop.Id}.");
                        foreach (Vector3 slot in point.WaitSlots)
                        {
                            float along = Vector3.Dot(slot - kerb, Vector3.ProjectOnPlane(stop.Forward, Vector3.up).normalized);
                            Vector3 passage = CityBusStopWaitPlanner.StandingPosition(lines[line], stop, layout, along);
                            Assert.That(Mathf.Abs(slot.y - passage.y), Is.LessThan(.2f), stop.Id);
                            Assert.That(Vector3.ProjectOnPlane(slot - passage, Vector3.up).magnitude,
                                Is.GreaterThan(pavement.AgentRadius * 2f), "Keep the queue off the through sidewalk.");
                        }
                    }
                }
                CityBusTimetable clock = CityBusTimetable.ForPlan(lines[line]);
                for (int member = 0; member < 2; member++)
                for (int stop = 0; stop < lines[line].Stops.Count; stop++)
                {
                    double start = CityBusTimetable.Wrap(clock.Arrival(stop) - member * clock.Duration * .5d, clock.Duration);
                    for (double time = start; time * GameTimeState.GameMinutesPerRealSecond < days * 1440d; time += clock.Duration)
                        events.Add((time * GameTimeState.GameMinutesPerRealSecond, 2 * line + member, stop));
                }
            }
            events.Sort((a, b) => a.minute != b.minute ? a.minute.CompareTo(b.minute) : a.vehicle.CompareTo(b.vehicle));
            var director = new CityResidentJourneyDirector(world, lines, waits);
            director.Reset(0);
            var workRiders = new HashSet<int>();
            var homeRiders = new HashSet<int>();
            var reachedWork = new HashSet<int>();
            var reachedLeisure = new HashSet<int>();
            var reachedHome = new HashSet<int>();
            bool restored = false;
            int vehicles = 2 * lines.Count;
            foreach (var visit in events)
            {
                director.Advance(visit.minute);
                director.ServiceHiddenStop(visit.vehicle, visit.stop);
                var aboard = new HashSet<int>();
                for (int vehicle = 0; vehicle < vehicles; vehicle++)
                for (int slot = 0; slot < CityBusActor.MaximumNpcOccupants; slot++)
                {
                    int id = director.RiderAt(vehicle, slot);
                    if (id < 0) continue;
                    Assert.That(aboard.Add(id), Is.True, "A person cannot occupy two seats.");
                    var state = director.GetState(id);
                    Assert.That(state.Vehicle, Is.EqualTo(vehicle));
                    Assert.That(state.Phase, Is.EqualTo(CityResidentJourneyPhase.Riding));
                    if (!CityResidentLifeRules.HasWorkSchedule(id)) continue;
                    if (state.Activity == CityResidentActivity.Work) workRiders.Add(id);
                    if (state.Activity == CityResidentActivity.ReturningHome || state.Activity == CityResidentActivity.Home)
                        homeRiders.Add(id);
                    if (!restored && state.Activity == CityResidentActivity.Work)
                    {
                        var checkpoint = director.Capture();
                        Assert.That(director.TryAlight(id, vehicle, state.ExitStop), Is.True);
                        director.Restore(checkpoint);
                        Assert.That(director.RiderAt(vehicle, slot), Is.EqualTo(id));
                        Assert.That(director.GetState(id).ExitStop, Is.EqualTo(state.ExitStop));
                        restored = true;
                    }
                }
                foreach (int id in workRiders)
                {
                    var state = director.GetState(id);
                    if (state.Phase == CityResidentJourneyPhase.AtDestination && state.Activity == CityResidentActivity.Work &&
                        state.CurrentNode == world.Workplace(id).PavementNode) reachedWork.Add(id);
                }
                foreach (int id in reachedWork)
                {
                    var state = director.GetState(id);
                    if (state.Phase == CityResidentJourneyPhase.AtDestination && state.Activity == CityResidentActivity.Leisure &&
                        state.CurrentNode == world.Destination(id, CityResidentActivity.Leisure))
                        reachedLeisure.Add(id);
                }
                foreach (int id in homeRiders)
                {
                    var state = director.GetState(id);
                    if (state.Phase == CityResidentJourneyPhase.AtDestination &&
                        state.CurrentNode == world.Home(id).PavementNode) reachedHome.Add(id);
                }
            }
            Assert.That(restored, Is.True, "A worker must ride before their seat can be checkpointed.");
            Assert.That(reachedWork, Is.Not.Empty, "A worker must complete the last mile after riding.");
            Assert.That(reachedLeisure, Is.Not.Empty, "A worker must stay at their scheduled evening destination.");
            reachedHome.IntersectWith(reachedLeisure);
            Assert.That(reachedHome, Is.Not.Empty, "The same worker must return to the original address after leisure.");
            foreach (int id in reachedHome)
            {
                Assert.That(world.Home(id).Building, Is.Not.Null);
                Assert.That(world.Workplace(id).Building, Is.Not.Null);
                Assert.That(world.Home(id).Building.IsPlayerHome, Is.False);
            }
        }

        [Test]
        public void FullBus_LeavesWaitingResidentsAndRestoresStandingPlaces()
        {
            // One residential frontage and one work frontage. Both have accessible
            // platforms, so a real morning cohort can exceed a single bus's capacity.
            Vector3 housing = Vector3.zero, work = Vector3.forward * 200f;
            var anchor = new CityPedestrianSpawnAnchor("housing", housing, 0, 1);
            var pavement = new CityPedestrianPlan(1, 1, 1u, .35f,
                new[] { new CityPedestrianNode("homes", housing, false), new CityPedestrianNode("work", work, false) },
                new[] { new CityPedestrianLink("walk", 0, 1, CityPedestrianLinkKind.Sidewalk) },
                new[] { anchor }, new[] { Rect.MinMaxRect(-1f, -1f, 1f, 201f) });
            var world = new CityResidentWorld(pavement);
            var road = new RoadEdge(Vector2Int.zero, Vector2Int.up);
            var busAnchor = new CityBusSpawnAnchor("ring", 0, 0f, housing, Vector3.forward, road);
            var link = new CityBusRouteLink("road", 0, 0, CityBusRouteLinkKind.Straight, road.B,
                new[] { new CityBusPathSample(housing, Vector3.forward, 0f), new CityBusPathSample(work, Vector3.back, 200f),
                    new CityBusPathSample(housing, Vector3.forward, 400f) }, float.PositiveInfinity, default);
            var route = new CityBusPlan(1, 1, 1u, 1.5f, CityBusDesignVehicle.Default,
                new[] { new CityBusRouteNode("ring", housing, Vector3.forward, road, road.A, road.B, new[] { 0 }) },
                new[] { link }, new[] { busAnchor },
                new[] { new CityBusStopDescriptor("homes", "", housing, 0, 0f, housing, Vector3.forward, road),
                    new CityBusStopDescriptor("work", "", work, 0, 200f, work, Vector3.back, road) },
                Array.Empty<CityBusClearanceFailure>(), 1, 1);
            var waits = new CityBusStopWaitPlan(new[] {
                new CityBusStopWaitPoint(0, "homes", 0, Vector3.right, new[] { housing }, world.DistancesTo(0)),
                new CityBusStopWaitPoint(1, "work", 1, Vector3.left, new[] { work }, world.DistancesTo(1)) });
            var journeys = new CityResidentJourneyDirector(world, new[] { route }, new[] { waits });
            journeys.Reset(600d);
            journeys.ServiceHiddenStop(0, 0);
            var manifest = new int[CityBusActor.MaximumNpcOccupants];
            var unique = new HashSet<int>();
            for (int slot = 0; slot < manifest.Length; slot++)
            {
                int id = journeys.RiderAt(0, slot);
                Assert.That(id, Is.GreaterThanOrEqualTo(0));
                Assert.That(unique.Add(id), Is.True);
                var rider = journeys.GetState(id);
                Assert.That(rider.PlaceIndex, Is.EqualTo(CityBusActor.NpcPlaceIndices[slot]));
                Assert.That(CityBusActor.IsStandingPlace(rider.PlaceIndex), Is.EqualTo(slot >= CityBusActor.NpcSeatIndices.Count));
                Assert.That(rider.ExitStop, Is.EqualTo(1));
                manifest[slot] = id;
            }
            int queued = journeys.NextWaiting(0, 0);
            Assert.That(queued, Is.GreaterThanOrEqualTo(0));
            Assert.That(journeys.TryBoard(queued, 0, 0), Is.False);
            Assert.That(journeys.GetState(queued).Phase, Is.EqualTo(CityResidentJourneyPhase.Waiting));
            var checkpoint = journeys.Capture();

            // Even a newly vacant seat does not silently move an existing standee.
            Assert.That(journeys.TryAlight(manifest[1], 0, 1), Is.True);
            Assert.That(journeys.GetState(manifest[manifest.Length - 1]).PlaceIndex, Is.EqualTo(CityBusActor.CabinCapacity - 1));
            journeys.ServiceHiddenStop(1, 0);
            Assert.That(journeys.RiderAt(1, 0), Is.EqualTo(queued));
            journeys.Restore(checkpoint);
            for (int slot = 0; slot < manifest.Length; slot++)
            {
                Assert.That(journeys.RiderAt(0, slot), Is.EqualTo(manifest[slot]));
                Assert.That(journeys.GetState(manifest[slot]).PlaceIndex, Is.EqualTo(CityBusActor.NpcPlaceIndices[slot]));
                Assert.That(journeys.RiderAt(1, slot), Is.EqualTo(-1));
            }
            Assert.That(journeys.NextWaiting(1, 0), Is.EqualTo(queued));
            journeys.ServiceHiddenStop(0, 1);
            for (int slot = 0; slot < manifest.Length; slot++)
                Assert.That(journeys.GetState(manifest[slot]).Vehicle, Is.EqualTo(-1));
        }

        private static void AssertConnectedDestinations(CityResidentWorld world, CityPedestrianPlan plan)
        {
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                int home = world.Destination(resident, CityResidentActivity.Home);
                foreach (CityResidentActivity activity in Enum.GetValues(typeof(CityResidentActivity)))
                {
                    int destination = world.Destination(resident, activity);
                    Assert.That(destination, Is.InRange(0, plan.Nodes.Count - 1));
                    Assert.That(float.IsPositiveInfinity(world.DistancesTo(home)[destination]), Is.False,
                        "A logical destination cannot send its resident across disconnected pavement.");
                }
            }
        }

        private static void AssertOnPavement(CityPedestrianPlan plan, CityResidentWorld.StreetSample sample)
        {
            bool linked = false;
            foreach (int index in plan.GetLinkIndices(sample.FromNode))
                if (plan.Links[index].Other(sample.FromNode) == sample.ToNode) linked = true;
            Assert.That(linked, Is.True, "Presentation must bind the sampled physical link pair.");
            Vector3 first = plan.Nodes[sample.FromNode].Position;
            Vector3 last = plan.Nodes[sample.ToNode].Position;
            Vector3 edge = last - first;
            float fraction = Mathf.Clamp01(Vector3.Dot(sample.Position - first, edge) / edge.sqrMagnitude);
            Assert.That(Vector3.Distance(sample.Position, first + edge * fraction), Is.LessThan(.001f));
        }

        private static CityPedestrianPlan DisconnectedPavements()
        {
            var nodes = new List<CityPedestrianNode>();
            var links = new List<CityPedestrianLink>();
            var anchors = new List<CityPedestrianSpawnAnchor>();
            for (int component = 0; component < 2; component++)
            {
                Vector3 offset = Vector3.right * component * 500f;
                int start = nodes.Count;
                foreach (Vector3 point in new[] { Vector3.zero, Vector3.right * 80f,
                             new Vector3(80f, 0f, 80f), Vector3.forward * 80f })
                    nodes.Add(new CityPedestrianNode("node:" + nodes.Count, offset + point, false));
                for (int leg = 0; leg < 4; leg++)
                {
                    int from = start + leg;
                    int to = start + (leg + 1) % 4;
                    links.Add(new CityPedestrianLink("link:" + links.Count, from, to,
                        CityPedestrianLinkKind.Sidewalk));
                    anchors.Add(new CityPedestrianSpawnAnchor("anchor:" + anchors.Count,
                        Vector3.Lerp(nodes[from].Position, nodes[to].Position, .5f), from, to));
                }
            }
            return new CityPedestrianPlan(7, 31, 29u, CityPedestrianPlanner.AgentRadius,
                nodes, links, anchors, new[] { Rect.MinMaxRect(-2f, -2f, 582f, 82f) });
        }
    }
}
