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
            var world = new CityResidentWorld(plan, layout);
            var homes = new HashSet<int>();
            var work = new HashSet<int>();
            int playerHome = -1;
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!CityBusStopWaitPlanner.TryFindNearestNode(plan, lot.SidewalkArrivalPosition, out int node)) continue;
                if (lot.IsPlayerHome) playerHome = node;
                else if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential) homes.Add(node);
                else if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Industrial) work.Add(node);
            }
            Assert.That(homes, Is.Not.Empty);
            Assert.That(work, Is.Not.Empty);
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                int home = world.Destination(resident, CityResidentActivity.Home);
                Assert.That(homes.Contains(home), Is.True, "A resident home is an existing residential frontage.");
                Assert.That(home, Is.Not.EqualTo(playerHome), "The hero's home is not an ambient resident home.");
                Assert.That(world.Destination(resident, CityResidentActivity.ReturningHome), Is.EqualTo(home));
                bool industrialReachable = false;
                foreach (int workplace in work)
                    if (!float.IsPositiveInfinity(world.DistancesTo(home)[workplace])) industrialReachable = true;
                if (industrialReachable)
                    Assert.That(work.Contains(world.Destination(resident, CityResidentActivity.Work)), Is.True,
                        "A reachable workplace uses an industrial frontage.");
            }
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
