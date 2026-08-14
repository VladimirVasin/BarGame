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
                Assert.That(
                    incidentKinds.Length,
                    Is.GreaterThanOrEqualTo(2),
                    $"Pedestrian graph node '{node.Id}' is a dead end.");
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
                                elevation,
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
                        elevation.TrySampleSurface(
                            new Vector2(
                                node.Position.x,
                                node.Position.z),
                            CitySurfaceRole.RoadTop,
                            out float roadHeight,
                            out _),
                        Is.True,
                        node.Id);
                    Assert.That(
                        node.Position.y,
                        Is.EqualTo(roadHeight)
                            .Within(PositionTolerance),
                        node.Id);
                    sampledRoadNodes++;
                }

                if (node.Id.StartsWith("junction:") &&
                    !node.Id.Contains(":seam:"))
                {
                    string[] idParts = node.Id.Split(':');
                    var gridNode = new Vector2Int(
                        int.Parse(idParts[1]),
                        int.Parse(idParts[2]));
                    float expectedHeight =
                        layout.GetNodeWorldPosition(gridNode).y +
                        CityStreetSurfacePlanner.SidewalkTop;
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
                        Is.EqualTo(CityPedestrianLinkKind.Sidewalk),
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
            CityElevationPlan elevation,
            CityStreetSurfacePlan streetSurfacePlan,
            Vector3 position)
        {
            Assert.That(
                elevation.TrySampleSurface(
                    new Vector2(position.x, position.z),
                    CitySurfaceRole.RoadTop,
                    out float height,
                    out _),
                Is.True);
            for (int index = 0;
                 index < streetSurfacePlan.SidewalkGeometry.Count;
                 index++)
            {
                RuntimeOrientedBox box =
                    streetSurfacePlan.SidewalkGeometry[index];
                Vector3 normal = box.Rotation * Vector3.up;
                if (Mathf.Abs(normal.y) <= PositionTolerance)
                {
                    continue;
                }

                Vector3 planePoint = box.Center +
                                     normal * (box.Size.y * 0.5f);
                float top = planePoint.y -
                    ((normal.x * (position.x - planePoint.x) +
                      normal.z * (position.z - planePoint.z)) /
                     normal.y);
                Vector3 local = Quaternion.Inverse(box.Rotation) *
                    (new Vector3(position.x, top, position.z) -
                     box.Center);
                if (Mathf.Abs(local.x) <=
                        box.Size.x * 0.5f + PositionTolerance &&
                    Mathf.Abs(local.z) <=
                        box.Size.z * 0.5f + PositionTolerance)
                {
                    height = Mathf.Max(height, top);
                }
            }

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
