using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The coast promenade's hold on the pedestrian graph. The
    /// regression these tests exist for is silent: the two-core prune
    /// deletes any chain that dangles, so a mis-anchored shore lane
    /// does not fail — it disappears, along with its nav rectangles
    /// and everything that walked it.
    /// </summary>
    public sealed class CityPedestrianSeacoastTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        [Test]
        public void DefaultCity_KeepsTheCoastLaneAfterThePrune()
        {
            CityLayout layout = CreateDefaultLayout();
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed);

            var ids = new HashSet<string>(
                plan.Nodes.Select(node => node.Id));
            foreach (string required in new[]
                     {
                         "coast:spur",
                         "coast:access",
                         "coast:quay:west",
                         "coast:quay:east",
                         "coast:bridge:west",
                         "coast:bridge:east",
                         "coast:pier",
                         "coast:shore",
                         "coast:ring:1",
                         "coast:ring:2",
                         "coast:ring:3",
                     })
            {
                Assert.That(
                    ids.Contains(required),
                    Is.True,
                    $"The prune swallowed '{required}'.");
            }

            // The promenades' north stubs used to be degree-one dead
            // ends the prune deleted a block short of the sand; the
            // quay junctions turn them into through-nodes.
            Assert.That(ids.Contains("river:west:north"), Is.True);
            Assert.That(ids.Contains("river:east:north"), Is.True);
        }

        [Test]
        public void DefaultCity_WalksTheShoreAsOnePieceOfTheCity()
        {
            CityLayout layout = CreateDefaultLayout();
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed);

            int shore = FindNode(plan, "coast:shore");
            Assert.That(shore, Is.GreaterThanOrEqualTo(0));

            var adjacency = new List<int>[plan.Nodes.Count];
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                adjacency[index] = new List<int>();
            }

            for (int index = 0; index < plan.Links.Count; index++)
            {
                CityPedestrianLink link = plan.Links[index];
                adjacency[link.FirstNodeIndex].Add(
                    link.SecondNodeIndex);
                adjacency[link.SecondNodeIndex].Add(
                    link.FirstNodeIndex);
            }

            var visited = new bool[plan.Nodes.Count];
            var pending = new Queue<int>();
            pending.Enqueue(shore);
            visited[shore] = true;
            int reached = 0;
            while (pending.Count > 0)
            {
                int node = pending.Dequeue();
                reached++;
                List<int> neighbours = adjacency[node];
                for (int index = 0; index < neighbours.Count; index++)
                {
                    if (!visited[neighbours[index]])
                    {
                        visited[neighbours[index]] = true;
                        pending.Enqueue(neighbours[index]);
                    }
                }
            }

            // The wild east shore is one walk away from the street
            // pavements and the whole river promenade, not an island
            // of its own. (The city graph is not one component — the
            // sidewalk rings connect through crosswalks where they
            // exist — so the honest claim is reach, not majority.)
            Assert.That(
                reached,
                Is.GreaterThan(150),
                "The coast lane is a disconnected island.");
            foreach (string landmark in new[]
                     {
                         "coast:spur",
                         "river:west:north",
                         "river:east:north",
                     })
            {
                int node = FindNode(plan, landmark);
                Assert.That(
                    node,
                    Is.GreaterThanOrEqualTo(0),
                    $"'{landmark}' is missing from the plan.");
                Assert.That(
                    visited[node],
                    Is.True,
                    $"The shore cannot reach '{landmark}'.");
            }
        }

        [Test]
        public void DefaultCity_BridgesTheQuayStepWithVisibleStairs()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);

            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.PromenadeStairWestId,
                    out CitySeacoastPartDescriptor west),
                Is.True,
                "The west quay lost its stair.");
            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.PromenadeStairEastId,
                    out CitySeacoastPartDescriptor east),
                Is.True,
                "The east quay lost its stair.");

            // Each stair stands at the promenade's own lane x, just
            // north of the boundary the rail used to seal.
            foreach (CityRiverPromenadeDescriptor promenade in
                     layout.River.Promenades)
            {
                CitySeacoastPartDescriptor stair = promenade.WestBank
                    ? west
                    : east;
                float laneX = promenade.WestBank
                    ? promenade.Bounds.xMin +
                      CitySeacoastPlanner.PromenadeLaneInset
                    : promenade.Bounds.xMax -
                      CitySeacoastPlanner.PromenadeLaneInset;
                Assert.That(
                    stair.Center.x,
                    Is.EqualTo(laneX).Within(0.01f));
                Assert.That(
                    stair.Center.z,
                    Is.GreaterThan(promenade.Bounds.yMax));
                Assert.That(
                    stair.Center.z,
                    Is.LessThan(promenade.Bounds.yMax + 4f));
            }
        }

        [Test]
        public void DefaultCity_OffersTheEsplanadeBenchesToSitters()
        {
            CityLayout layout = CreateDefaultLayout();
            RoadFencePlan fencePlan =
                RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    fencePlan,
                    nightPlan);
            List<CityBenchSitPlan> plans = CityBenchSitPlan.CreateAll(
                layout,
                CityOpenAreaDecorationPlanner.Create(layout),
                CityCemeteryPlanner.Create(layout),
                CityBusPlanner.Create(layout, decorationPlan),
                decorationPlan,
                CityStreetSurfacePlanner.Create(layout),
                CitySeacoastPlanner.Create(layout));

            List<CityBenchSitPlan> coastSeats = plans
                .Where(plan => plan.Id.StartsWith(
                    "seacoast-bench-",
                    System.StringComparison.Ordinal))
                .ToList();
            Assert.That(coastSeats.Count, Is.GreaterThanOrEqualTo(3));
            foreach (CityBenchSitPlan seat in coastSeats)
            {
                Assert.That(seat.IsPresent, Is.True);
            }
        }

        private static int FindNode(CityPedestrianPlan plan, string id)
        {
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                if (string.Equals(
                        plan.Nodes[index].Id,
                        id,
                        System.StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
