using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPedestrianPlannerTests
    {
        private const float PositionTolerance = 0.0001f;

        [Test]
        public void Create_WithSameLayoutAndSeed_ProducesIdenticalGraph()
        {
            CityLayout layout = CreateDefaultLayout(9017);

            CityPedestrianPlan first = CityPedestrianPlanner.Create(
                layout,
                4411);
            CityPedestrianPlan second = CityPedestrianPlanner.Create(
                layout,
                4411);
            CityStreetSurfacePlan streetSurfacePlan =
                CityStreetSurfacePlanner.Create(layout);
            CityPedestrianPlan suppliedSurface =
                CityPedestrianPlanner.Create(
                    layout,
                    4411,
                    streetSurfacePlan);

            Assert.That(second.StableSeed, Is.EqualTo(first.StableSeed));
            Assert.That(second.AgentRadius, Is.EqualTo(first.AgentRadius));
            CollectionAssert.AreEqual(first.Nodes, second.Nodes);
            CollectionAssert.AreEqual(first.Links, second.Links);
            CollectionAssert.AreEqual(
                first.SpawnAnchors,
                second.SpawnAnchors);
            CollectionAssert.AreEqual(
                first.NavigationRectangles,
                second.NavigationRectangles);
            CollectionAssert.AreEqual(first.Nodes, suppliedSurface.Nodes);
            CollectionAssert.AreEqual(first.Links, suppliedSurface.Links);
            CollectionAssert.AreEqual(
                first.SpawnAnchors,
                suppliedSurface.SpawnAnchors);
            CollectionAssert.AreEqual(
                first.NavigationRectangles,
                suppliedSurface.NavigationRectangles);
        }

        [Test]
        public void Create_BuildsRadiusSafeTurnsAndCrosswalkBranches()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                -28914);
            CityStreetSurfacePlan streetSurfacePlan =
                CityStreetSurfacePlanner.Create(layout);
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                2203,
                streetSurfacePlan);
            RoadWalkableArea walkableArea =
                CityPedestrianPlanner.CreateWalkableArea(plan);

            Assert.That(plan.Nodes, Is.Not.Empty);
            Assert.That(plan.Links, Is.Not.Empty);
            Assert.That(plan.SpawnAnchors, Is.Not.Empty);
            Assert.That(
                plan.Nodes.Select(node => node.Id),
                Is.Unique);
            Assert.That(
                plan.Links.Select(link => link.Id),
                Is.Unique);
            Assert.That(
                plan.SpawnAnchors.Select(anchor => anchor.Id),
                Is.Unique);
            Assert.That(
                plan.Links.Any(link =>
                    link.Kind == CityPedestrianLinkKind.Sidewalk),
                Is.True);
            Assert.That(
                plan.Links.Any(link =>
                    link.Kind == CityPedestrianLinkKind.Turn),
                Is.True);
            Assert.That(streetSurfacePlan.Crosswalks, Is.Not.Empty);
            Assert.That(
                plan.Nodes.Count(node => node.IsCrosswalkEntry),
                Is.EqualTo(streetSurfacePlan.Crosswalks.Count * 2));
            Assert.That(
                plan.Links.Count(link =>
                    link.Kind == CityPedestrianLinkKind.Crosswalk),
                Is.EqualTo(streetSurfacePlan.Crosswalks.Count * 3));

            for (int nodeIndex = 0;
                 nodeIndex < plan.Nodes.Count;
                 nodeIndex++)
            {
                CityPedestrianNode node = plan.Nodes[nodeIndex];
                CityPedestrianLinkKind[] incidentKinds = plan
                    .GetLinkIndices(nodeIndex)
                    .Select(linkIndex => plan.Links[linkIndex].Kind)
                    .ToArray();
                if (incidentKinds.Length == 1)
                    Assert.That(incidentKinds[0], Is.EqualTo(CityPedestrianLinkKind.HomeEntrance),
                        $"Public pedestrian graph node '{node.Id}' must have an onward route.");
                else
                    Assert.That(incidentKinds.Length, Is.GreaterThanOrEqualTo(2), node.Id);
                bool hasCrosswalk = incidentKinds.Contains(
                    CityPedestrianLinkKind.Crosswalk);
                bool hasSidewalk = incidentKinds.Contains(
                    CityPedestrianLinkKind.Sidewalk);
                if (node.IsCrosswalkEntry)
                {
                    Assert.That(hasCrosswalk, Is.True, node.Id);
                    Assert.That(hasSidewalk, Is.True, node.Id);
                }

                if (hasCrosswalk && incidentKinds.Any(kind =>
                        kind != CityPedestrianLinkKind.Crosswalk))
                {
                    Assert.That(node.IsCrosswalkEntry, Is.True, node.Id);
                }
            }

            for (int linkIndex = 0;
                 linkIndex < plan.Links.Count;
                 linkIndex++)
            {
                CityPedestrianLink link = plan.Links[linkIndex];
                Assert.That(
                    link.FirstNodeIndex,
                    Is.InRange(0, plan.Nodes.Count - 1));
                Assert.That(
                    link.SecondNodeIndex,
                    Is.InRange(0, plan.Nodes.Count - 1));
                Vector3 first = plan.Nodes[link.FirstNodeIndex].Position;
                Vector3 second = plan.Nodes[link.SecondNodeIndex].Position;
                Assert.That(
                    Mathf.Abs(first.x - second.x) <= PositionTolerance ||
                    Mathf.Abs(first.z - second.z) <= PositionTolerance,
                    Is.True,
                    $"Pedestrian link '{link.Id}' is not axis-aligned.");

                for (int sample = 0; sample <= 16; sample++)
                {
                    Vector3 position = Vector3.Lerp(
                        first,
                        second,
                        sample / 16f);
                    Assert.That(
                        walkableArea.Contains(position, plan.AgentRadius),
                        Is.True,
                        $"Pedestrian link '{link.Id}' leaves its " +
                        "radius-safe navigation area.");
                }
            }

            Dictionary<string, CityPedestrianLink> linksById =
                plan.Links.ToDictionary(link => link.Id);
            foreach (CityPedestrianSpawnAnchor anchor
                     in plan.SpawnAnchors)
            {
                Assert.That(anchor.Id, Does.StartWith("spawn:"));
                string linkId = anchor.Id.Substring("spawn:".Length);
                Assert.That(linksById.ContainsKey(linkId), Is.True);
                CityPedestrianLink link = linksById[linkId];
                Assert.That(
                    link.Kind,
                    Is.EqualTo(CityPedestrianLinkKind.Sidewalk));
                Assert.That(
                    anchor.FirstNodeIndex,
                    Is.EqualTo(link.FirstNodeIndex));
                Assert.That(
                    anchor.SecondNodeIndex,
                    Is.EqualTo(link.SecondNodeIndex));
                Assert.That(
                    anchor.Position,
                    Is.EqualTo(Vector3.Lerp(
                        plan.Nodes[link.FirstNodeIndex].Position,
                        plan.Nodes[link.SecondNodeIndex].Position,
                        0.5f)));
                Assert.That(
                    plan.GetLinkIndices(anchor.FirstNodeIndex).Count,
                    Is.GreaterThan(1));
                Assert.That(
                    plan.GetLinkIndices(anchor.SecondNodeIndex).Count,
                    Is.GreaterThan(1));
            }
        }

        [Test]
        public void Create_WithDifferentPopulationSeed_PreservesGraph()
        {
            CityLayout layout = CreateDefaultLayout(1297);

            CityPedestrianPlan first = CityPedestrianPlanner.Create(
                layout,
                1001);
            CityPedestrianPlan second = CityPedestrianPlanner.Create(
                layout,
                2002);

            Assert.That(second.StableSeed, Is.Not.EqualTo(first.StableSeed));
            CollectionAssert.AreEqual(first.Nodes, second.Nodes);
            CollectionAssert.AreEqual(first.Links, second.Links);
            CollectionAssert.AreEqual(
                first.SpawnAnchors,
                second.SpawnAnchors);
            CollectionAssert.AreEqual(
                first.NavigationRectangles,
                second.NavigationRectangles);
        }

        [Test]
        [Category("CityRiver")]
        public void DefaultRiver_ConnectsPromenadesAndTimberFootbridge()
        {
            CityLayout layout = CreateDefaultLayout(8219);
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                2027);
            CityRiverBridgeDescriptor footbridge = layout.River.Bridges
                .Single(bridge => bridge.Definition.Role ==
                    CityBridgeRole.ParkFootbridge);
            CityPedestrianLink crossing = plan.Links.Single(link =>
                link.Id == "river-footbridge:" +
                footbridge.Definition.Id);

            Assert.That(
                plan.Links.Any(link => link.Id.StartsWith(
                    "river-promenade:river-promenade-west:")),
                Is.True);
            Assert.That(
                plan.Links.Any(link => link.Id.StartsWith(
                    "river-promenade:river-promenade-east:")),
                Is.True);
            Assert.That(
                footbridge.DeckBounds.Contains(new Vector2(
                    plan.Nodes[crossing.FirstNodeIndex].Position.x,
                    plan.Nodes[crossing.FirstNodeIndex].Position.z)),
                Is.True);
            Assert.That(
                footbridge.DeckBounds.Contains(new Vector2(
                    plan.Nodes[crossing.SecondNodeIndex].Position.x,
                    plan.Nodes[crossing.SecondNodeIndex].Position.z)),
                Is.True);

            for (int edgeIndex = 0;
                 edgeIndex < layout.RoadEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = layout.RoadEdges[edgeIndex];
                if (!layout.IsRiverPedestrianSpawnExcluded(edge))
                {
                    continue;
                }

                string edgeId =
                    $"{edge.A.x}:{edge.A.y}:{edge.B.x}:{edge.B.y}";
                Assert.That(
                    plan.SpawnAnchors.Any(anchor =>
                        anchor.Id.Contains(edgeId)),
                    Is.False,
                    edge.ToString());
            }
        }

        [Test]
        public void Create_ElevatedCity_UsesLocalSurfacesAndSignatureStairs()
        {
            CityLayout layout = CreateDefaultLayout(481516);
            CityStreetSurfacePlan streetSurfacePlan =
                CityStreetSurfacePlanner.Create(layout);
            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                2203,
                streetSurfacePlan);
            CityElevationPlan elevation = layout.ElevationPlan;
            Assert.That(elevation.IsElevated, Is.True);
            Assert.That(elevation.SignatureStairs, Is.Not.Empty);

            string[] stairLanePrefixes = elevation.SignatureStairs
                .Select(stair => "lane:" + EdgeId(stair.Edge) + ":")
                .ToArray();
            int sampledSidewalkNodes = 0;
            int sampledRoadNodes = 0;
            foreach (CityPedestrianNode node in plan.Nodes)
            {
                bool belongsToStairEdge = stairLanePrefixes.Any(prefix =>
                    node.Id.StartsWith(prefix));
                if (node.Id.StartsWith("lane:") &&
                    !belongsToStairEdge)
                {
                    Assert.That(
                        node.Position.y,
                        Is.EqualTo(
                            ResolveNavigationSurfaceHeight(
                                streetSurfacePlan,
                                node.Position))
                            .Within(PositionTolerance),
                        node.Id);
                    sampledSidewalkNodes++;
                }

                if (node.Id.StartsWith("crosswalk:") &&
                    node.Id.Contains(":road:"))
                {
                    Assert.That(
                        node.Position.y,
                        Is.EqualTo(ResolveNavigationSurfaceHeight(streetSurfacePlan, node.Position))
                            .Within(PositionTolerance),
                        node.Id);
                    sampledRoadNodes++;
                }

                if (node.Id.StartsWith("junction:") &&
                    !node.Id.Contains(":seam:"))
                {
                    float expectedHeight = ResolveNavigationSurfaceHeight(streetSurfacePlan, node.Position);
                    Assert.That(
                        node.Position.y,
                        Is.EqualTo(expectedHeight)
                            .Within(PositionTolerance),
                        node.Id);
                }
            }

            Assert.That(sampledSidewalkNodes, Is.GreaterThan(0));
            Assert.That(sampledRoadNodes, Is.GreaterThan(0));
            Assert.That(
                plan.SpawnAnchors.Any(anchor =>
                    anchor.Position.y >
                    CityStreetSurfacePlanner.SidewalkTop + 0.5f),
                Is.True);

            foreach (CityElevationStairDescriptor stair in
                     elevation.SignatureStairs)
            {
                string linkPrefix = $"stair:{stair.Id}:";
                CityPedestrianLink[] stairLinks = plan.Links
                    .Where(link => link.Id.StartsWith(linkPrefix))
                    .ToArray();
                Assert.That(stairLinks, Is.Not.Empty, stair.Id);
                var nodeIndices = new HashSet<int>();
                foreach (CityPedestrianLink link in stairLinks)
                {
                    Assert.That(
                        link.Kind,
                        Is.EqualTo(CityPedestrianLinkKind.Stair),
                        link.Id);
                    nodeIndices.Add(link.FirstNodeIndex);
                    nodeIndices.Add(link.SecondNodeIndex);
                    Assert.That(
                        plan.SpawnAnchors.Any(anchor =>
                            anchor.Id == "spawn:" + link.Id),
                        Is.False,
                        link.Id);
                }

                float minimumHeight = nodeIndices.Min(index =>
                    plan.Nodes[index].Position.y);
                float maximumHeight = nodeIndices.Max(index =>
                    plan.Nodes[index].Position.y);
                Assert.That(
                    minimumHeight,
                    Is.EqualTo(
                        elevation.GetNodeElevation(stair.LowerNode) +
                        CityStreetSurfacePlanner.SidewalkTop)
                        .Within(PositionTolerance),
                    stair.Id);
                Assert.That(
                    maximumHeight,
                    Is.EqualTo(
                        elevation.GetNodeElevation(stair.UpperNode) +
                        CityStreetSurfacePlanner.SidewalkTop)
                        .Within(PositionTolerance),
                    stair.Id);
            }
            AssertCourtyardGraph(layout, streetSurfacePlan, plan);
        }

        private static void AssertCourtyardGraph(CityLayout layout, CityStreetSurfacePlan surfaces, CityPedestrianPlan plan)
        {
            RoadWalkableArea area = CityPedestrianPlanner.CreateWalkableArea(plan);
            CityPedestrianLink[] courtLinks = plan.Links.Where(link => link.Kind == CityPedestrianLinkKind.Courtyard).ToArray();
            Assert.That(courtLinks, Is.Not.Empty);
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                string prefix = $"courtyard:{block.Cell.x}:{block.Cell.y}:";
                CityPedestrianLink[] route = courtLinks.Where(link =>
                    link.Id.StartsWith(prefix + "walk:", StringComparison.Ordinal)).ToArray();
                int[] routeNodes = route
                    .SelectMany(link => new[] { link.FirstNodeIndex, link.SecondNodeIndex })
                    .Distinct()
                    .ToArray();
                Assert.That(routeNodes, Is.Not.Empty, prefix);
                // A gate can land exactly on an existing sidewalk node.
                int[] gateNodes = routeNodes
                    .Where(index => plan.GetLinkIndices(index).Any(link => plan.Links[link].Kind == CityPedestrianLinkKind.Sidewalk))
                    .ToArray();
                Assert.That(gateNodes, Has.Length.EqualTo(1), prefix);
                int gate = gateNodes[0];
                var visited = new HashSet<int> { gate };
                var pending = new Queue<int>(); pending.Enqueue(gate);
                while (pending.Count > 0)
                {
                    int node = pending.Dequeue();
                    foreach (int link in plan.GetLinkIndices(node))
                    {
                        int other = plan.Links[link].Other(node);
                        if (visited.Add(other)) pending.Enqueue(other);
                    }
                }
                Assert.That(routeNodes, Is.SubsetOf(visited), "The court walk must be connected to its actual street gate.");
                Assert.That(visited.Any(index => plan.Nodes[index].Id.StartsWith("lane:")), Is.True,
                    "A courtyard component must join the production street graph.");
                foreach (int node in Enumerable.Range(0, plan.Nodes.Count).Where(index => plan.Nodes[index].Id.StartsWith(prefix)))
                    Assert.That(plan.GetLinkIndices(node).Count, Is.GreaterThanOrEqualTo(2),
                        $"Courtyard graph node {plan.Nodes[node].Id} is a dead end.");
            }
            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
                Assert.That(courtLinks.Any(link => link.Id.StartsWith(
                    $"courtyard-connection:{connection.FirstCell.x}:{connection.FirstCell.y}:{connection.SecondCell.x}:{connection.SecondCell.y}:")), Is.True);
            foreach (CityPedestrianLink link in courtLinks)
            {
                Assert.That(link.Path, Is.Not.Null, link.Id);
                Assert.That(link.PathHeightSampler, Is.Not.Null, link.Id);
                Assert.That(plan.Nodes[link.FirstNodeIndex].IsCrosswalkEntry || plan.Nodes[link.SecondNodeIndex].IsCrosswalkEntry,
                    Is.False, "Courtyard shortcuts must not create a road crossing.");
                for (float distance = 0f; distance < link.Path.Length + .25f; distance += .25f)
                {
                    Vector2 point = link.Path.SampleDistance(distance).Position;
                    float top = SampleCourtyardNavigationTop(layout, surfaces, point);
                    Assert.That(link.PathHeightSampler(point), Is.EqualTo(top).Within(.001f), link.Id);
                    Assert.That(area.Contains(new Vector3(point.x, top, point.y), .35f), Is.True,
                        $"Courtyard link {link.Id} leaves its capsule-safe free ground at {point}.");
                }
            }
            foreach (CityPedestrianSpawnAnchor anchor in plan.SpawnAnchors)
            {
                Assert.That(anchor.Id, Does.Not.Contain("courtyard"));
                Assert.That(plan.GetLinkIndices(anchor.FirstNodeIndex).Select(index => plan.Links[index])
                    .Any(link => link.Other(anchor.FirstNodeIndex) == anchor.SecondNodeIndex &&
                        link.Kind != CityPedestrianLinkKind.Courtyard), Is.True, anchor.Id);
            }
        }

        private static float SampleCourtyardNavigationTop(CityLayout layout, CityStreetSurfacePlan surfaces, Vector2 point)
        {
            if (CityBuildingEntrancePlan.TrySampleCachedTop(layout, point, out float entranceTop))
                return entranceTop;
            if (surfaces.SupportPlan != null && surfaces.SupportPlan.TrySampleTop(point, out float surfaceTop, out _))
                return surfaceTop;
            if (surfaces.CurvedSidewalkPolygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)) ||
                layout.RoadGeometry.ObliqueJunction?.SidewalkPolygons.Any(
                    polygon => CityRoadPolygon.Contains(polygon, point)) == true)
            {
                Assert.That(layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.SidewalkTop, out float top, out _), Is.True);
                return top;
            }
            foreach (RuntimeOrientedBox box in surfaces.SidewalkGeometry)
                if (box.TrySampleTop(new Vector3(point.x, 0f, point.y), out float top)) return top;
            Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float ground, out _), Is.True);
            return ground;
        }

        [Test]
        public void Create_OnSmallCity_ReturnsAValidGraphSafely()
        {
            CityGenerationSettings smallSettings =
                CityGenerationSettings.Default;
            smallSettings.BlocksX = 1;
            smallSettings.BlocksZ = 1;
            smallSettings.BarCount = 0;
            smallSettings.ParkBlocksX = 0;
            smallSettings.ParkBlocksZ = 0;
            smallSettings.LoopChance = 1f;
            CityLayout smallLayout = CityLayoutGenerator.Generate(
                smallSettings,
                8123);

            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                smallLayout,
                12);

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Count, Is.LessThanOrEqualTo(plan.Links.Count));
            for (int index = 0; index < plan.Links.Count; index++)
            {
                Assert.That(
                    plan.Links[index].FirstNodeIndex,
                    Is.InRange(0, plan.Nodes.Count - 1));
                Assert.That(
                    plan.Links[index].SecondNodeIndex,
                    Is.InRange(0, plan.Nodes.Count - 1));
            }
        }

        [Test]
        public void Create_WithCapsuleNarrowCarriageway_OmitsCrosswalkLinks()
        {
            CityGenerationSettings settings = CreateDenseSettings();
            settings.RoadWidth =
                (CityStreetSurfacePlanner.SidewalkWidth * 2f) +
                (CityPedestrianPlanner.AgentRadius * 2f);
            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                -28914);
            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);

            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                2203,
                surfaces);

            Assert.That(surfaces.Crosswalks, Is.Not.Empty);
            Assert.That(
                plan.Links.Any(link =>
                    link.Kind == CityPedestrianLinkKind.Crosswalk),
                Is.False);
            Assert.That(
                plan.Nodes.Any(node => node.IsCrosswalkEntry),
                Is.False);
        }

        [Test]
        public void Create_WithNullLayout_Throws()
        {
            Assert.That(
                () => CityPedestrianPlanner.Create(null, 42),
                Throws.ArgumentNullException);
        }

        private static float ResolveNavigationSurfaceHeight(
            CityStreetSurfacePlan streetSurfacePlan,
            Vector3 position)
        {
            var point = new Vector2(position.x, position.z);
            Assert.That(streetSurfacePlan.SupportPlan.TrySampleTop(point, out float height, out _),
                Is.True, $"No built walking surface under {position:F3}.");
            return height;
        }

        private static CityLayout CreateDefaultLayout(int seed)
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                seed);
        }

        private static CityGenerationSettings CreateDenseSettings()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlocksX = 5;
            settings.BlocksZ = 5;
            settings.BarCount = 0;
            settings.LoopChance = 1f;
            return settings;
        }

        private static string EdgeId(RoadEdge edge)
        {
            return $"{edge.A.x}:{edge.A.y}:{edge.B.x}:{edge.B.y}";
        }
    }
}
