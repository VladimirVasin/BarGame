using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    [Category("CityRiver")]
    public sealed class CityRiverPlannerTests
    {
        private CityLayout layout;

        [SetUp]
        public void SetUp()
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        [Test]
        public void DefaultCity_InsertsOneColumnAndPreservesCanonicalLots()
        {
            CityRiverDefinition river = layout.Blueprint.River;

            Assert.That(river, Is.Not.Null);
            Assert.That(river.CorridorCellX, Is.EqualTo(6));
            Assert.That(
                river.CoreMaximumZExclusive - river.CoreMinimumZ,
                Is.EqualTo(12));
            Assert.That(layout.BuildingLots, Has.Count.EqualTo(144));
            Assert.That(
                layout.BuildingLots.Select(lot => lot.Cell.x).Max(),
                Is.EqualTo(12));
            for (int z = river.CoreMinimumZ;
                 z < river.CoreMaximumZExclusive;
                 z++)
            {
                Assert.That(
                    layout.Blueprint.ContainsCell(
                        new Vector2Int(river.CorridorCellX, z)),
                    Is.False);
            }

            BuildingLot home = layout.BuildingLots.Single(
                lot => lot.IsPlayerHome);
            Assert.That(home.Cell, Is.EqualTo(new Vector2Int(12, 5)));
            Assert.That(
                layout.BuildingLots.Any(lot =>
                    lot.IsBar && lot.Cell == new Vector2Int(12, 6)),
                Is.True);
        }

        [Test]
        public void AuthoredPark_DoesNotDependOnMutableParkDimensions()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.ParkBlocksX = 0;
            settings.ParkBlocksZ = 0;

            Assert.DoesNotThrow(() => CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                settings,
                GameSessionState.DefaultCitySeed));
        }

        [Test]
        public void Crossings_AreExactlyTwoRoadBridgesAndOneParkBridge()
        {
            CityRiverDefinition river = layout.River.Definition;
            RoadEdge[] crossings = layout.RoadEdges
                .Where(river.CrossesCorridor)
                .OrderBy(edge => edge.A.y)
                .ToArray();

            Assert.That(crossings, Has.Length.EqualTo(3));
            CollectionAssert.AreEquivalent(
                river.Bridges.Select(bridge => bridge.CrossingEdge),
                crossings);
            Assert.That(
                crossings.Count(edge =>
                    layout.GetPathKind(edge) == CityPathKind.Street),
                Is.EqualTo(2));
            Assert.That(
                crossings.Count(edge =>
                    layout.GetPathKind(edge) == CityPathKind.ParkPath),
                Is.EqualTo(1));
            IReadOnlyList<Vector2Int> busIntersections =
                CityBusIntersectionSelector.Select(layout);
            foreach (CityRiverBridgeDescriptor bridge in
                     layout.River.Bridges.Where(candidate =>
                         candidate.Definition.CarriesRoadTraffic))
            {
                Assert.That(
                    busIntersections,
                    Does.Contain(bridge.Definition.CrossingEdge.A));
                Assert.That(
                    busIntersections,
                    Does.Contain(bridge.Definition.CrossingEdge.B));
                foreach (Vector2Int node in new[]
                         {
                             bridge.Definition.CrossingEdge.A,
                             bridge.Definition.CrossingEdge.B
                         })
                {
                    Vector3 center = layout.GetNodeWorldPosition(node);
                    float offset =
                        CityBusIntersectionSelector.GetCornerCenterOffset(
                            layout);
                    float halfWidth =
                        CityStreetSurfacePlanner.SidewalkWidth * 0.5f;
                    for (int xSign = -1; xSign <= 1; xSign += 2)
                    {
                        for (int zSign = -1; zSign <= 1; zSign += 2)
                        {
                            Rect corner = Rect.MinMaxRect(
                                center.x + xSign * offset - halfWidth,
                                center.z + zSign * offset - halfWidth,
                                center.x + xSign * offset + halfWidth,
                                center.z + zSign * offset + halfWidth);
                            Assert.That(
                                layout.River.Landings.Any(landing =>
                                    HasPositiveOverlap(
                                        corner,
                                        landing.StairBounds) ||
                                    HasPositiveOverlap(
                                        corner,
                                        landing.PlatformBounds)),
                                Is.False,
                                $"{bridge.Definition.Id}:{node}:{xSign}:" +
                                zSign);
                        }
                    }
                }
            }
            Assert.That(
                CountComponents(layout.RoadEdges.Where(edge =>
                    !river.CrossesCorridor(edge))),
                Is.EqualTo(2));
            Assert.That(
                CountComponents(layout.RoadEdges.Where(edge =>
                    layout.GetPathKind(edge) == CityPathKind.Street)),
                Is.EqualTo(1));
        }

        [Test]
        public void ParkAndEmbankments_StaySplitAroundWater()
        {
            Assert.That(layout.Park.Cells, Has.Count.EqualTo(16));
            Assert.That(layout.Park.Regions, Has.Count.EqualTo(2));
            Assert.That(
                layout.Park.Regions.All(region => region.Cells.Count == 8),
                Is.True);
            Assert.That(layout.River.Promenades, Has.Count.EqualTo(2));
            Assert.That(
                layout.River.Promenades.All(promenade =>
                    promenade.Bounds.width ==
                    layout.River.Definition.PromenadeWidth),
                Is.True);

            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            foreach (CityRiverPromenadeDescriptor promenade in
                     layout.River.Promenades)
            {
                float seamX = promenade.WestBank
                    ? promenade.Bounds.xMin
                    : promenade.Bounds.xMax;
                float radius =
                    CityGroundTraversalPlanner.MaximumAgentRadius;
                foreach (float x in new[]
                         {
                             seamX - radius,
                             seamX,
                             seamX + radius
                         })
                {
                    Assert.That(
                        walkable.Contains(
                            new Vector3(
                                x,
                                0f,
                                promenade.Bounds.center.y),
                            radius),
                        Is.True,
                        $"{promenade.Id}:{x}");
                }
            }

            CityRiverSegmentDescriptor water = layout.River.Segments[4];
            var waterCenter = new Vector3(
                water.WaterBounds.center.x,
                water.AverageWaterY,
                water.WaterBounds.center.y);
            Assert.That(layout.IsWater(waterCenter), Is.True);
            Assert.That(walkable.Contains(waterCenter, 0.2f), Is.False);

            CityRiverSegmentDescriptor mouth = layout.River.Segments.Last();
            var mouthCenter = new Vector3(
                mouth.WaterBounds.center.x,
                mouth.AverageWaterY,
                mouth.WaterBounds.center.y);
            Assert.That(
                walkable.Contains(mouthCenter, 0.2f),
                Is.False,
                "The clipped estuary must not retain beach walkability.");
            CitySurfaceDescriptor sea = layout.Surfaces.First(surface =>
                surface.Kind == CitySurfaceKind.Water &&
                Mathf.Abs(
                    surface.WorldBounds.yMin -
                    mouth.WaterBounds.yMax) < 0.01f);
            Assert.That(
                CityRiverPlanner.ResolveWaterY(
                    layout.River.Definition,
                    layout.River.Definition.CoreMaximumZExclusive + 1) +
                CitySurfaceDescriptor.WaterTopOffset,
                Is.EqualTo(sea.PhysicalTopY).Within(0.001f));

            for (int index = 0; index < layout.River.Bridges.Count; index++)
            {
                CityRiverBridgeDescriptor bridge =
                    layout.River.Bridges[index];
                var center = new Vector3(
                    bridge.DeckBounds.center.x,
                    bridge.AverageY,
                    bridge.DeckBounds.center.y);
                Assert.That(layout.IsWater(center), Is.False);
                Assert.That(walkable.Contains(center, 0.2f), Is.True);
                Assert.That(
                    bridge.SpanBounds.xMin,
                    Is.LessThanOrEqualTo(water.WaterBounds.xMin),
                    bridge.Definition.Id);
                Assert.That(
                    bridge.SpanBounds.xMax,
                    Is.GreaterThanOrEqualTo(water.WaterBounds.xMax),
                    bridge.Definition.Id);
                foreach (CityRiverPromenadeDescriptor promenade in
                         layout.River.Promenades)
                {
                    Assert.That(
                        bridge.SpanBounds.Overlaps(promenade.Bounds),
                        Is.False,
                        $"{bridge.Definition.Id} spans over " +
                        promenade.Id);
                }
            }
        }

        [Test]
        public void RoadBridges_OwnFourReachableLowerLandings()
        {
            Assert.That(layout.River.Landings, Has.Count.EqualTo(4));
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            foreach (CityRiverLandingDescriptor landing in
                     layout.River.Landings)
            {
                CityRiverBridgeDescriptor bridge = layout.River.Bridges
                    .Single(candidate => string.Equals(
                        candidate.Definition.Id,
                        landing.BridgeId,
                        StringComparison.Ordinal));
                Assert.That(bridge.Definition.HasLowerLandings, Is.True);
                Assert.That(landing.StepCount, Is.InRange(8, 10));
                Assert.That(landing.UpperY, Is.GreaterThan(landing.LowerY));
                Assert.That(
                    (landing.UpperY - landing.LowerY) /
                    landing.StepCount,
                    Is.LessThanOrEqualTo(0.28f),
                    landing.Id);
                Assert.That(
                    landing.PlatformBounds.Overlaps(bridge.DeckBounds),
                    Is.False);
                Assert.That(
                    walkable.Contains(new Vector3(
                        landing.StairBounds.center.x,
                        landing.LowerY,
                        landing.StairBounds.center.y), 0.2f),
                    Is.True,
                    landing.Id);
                Assert.That(
                    walkable.Contains(new Vector3(
                        landing.PlatformBounds.center.x,
                        landing.LowerY,
                        landing.PlatformBounds.center.y), 0.2f),
                    Is.True,
                    landing.Id);
            }

            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            foreach (StreetLampDescriptor lamp in night.StreetLamps)
            {
                var point = new Vector2(lamp.Position.x, lamp.Position.z);
                Assert.That(
                    layout.River.Promenades.Any(promenade =>
                        promenade.Bounds.Contains(point)),
                    Is.False,
                    lamp.Edge.ToString());
                Assert.That(
                    layout.River.Landings.Any(landing =>
                        landing.StairBounds.Contains(point) ||
                        landing.PlatformBounds.Contains(point)),
                    Is.False,
                    lamp.Edge.ToString());
            }

            float lampClearance =
                CityGroundTraversalPlanner.MaximumAgentRadius + 0.10f;
            foreach (Vector3 lamp in
                     CityRiverWorldBuilder.CreatePromenadeLampPositions(
                         layout))
            {
                Assert.That(
                    layout.River.Landings.Any(landing =>
                        ContainsWithClearance(
                            landing.StairBounds,
                            lamp,
                            lampClearance) ||
                        ContainsWithClearance(
                            landing.PlatformBounds,
                            lamp,
                            lampClearance)),
                    Is.False,
                    lamp.ToString());
                float nearestLane = layout.River.Promenades.Min(promenade =>
                {
                    float laneInset = Mathf.Min(
                        promenade.Bounds.width * 0.5f,
                        CityPedestrianPlanner.AgentRadius + 0.1f);
                    float laneX = promenade.WestBank
                        ? promenade.Bounds.xMin + laneInset
                        : promenade.Bounds.xMax - laneInset;
                    return Mathf.Abs(lamp.x - laneX);
                });
                Assert.That(nearestLane, Is.GreaterThan(lampClearance));
            }
        }

        [Test]
        public void TimberFootbridge_UsesItsAuthoredNarrowDeck()
        {
            CityRiverBridgeDescriptor footbridge = layout.River.Bridges
                .Single(bridge => bridge.Definition.Role ==
                    CityBridgeRole.ParkFootbridge);
            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);
            RuntimeOrientedBox deck = surfaces.ParkPathGeometry.Single(box =>
                Mathf.Abs(box.Center.x - footbridge.DeckBounds.center.x) <
                    0.01f &&
                Mathf.Abs(box.Center.z - footbridge.DeckBounds.center.y) <
                    0.01f &&
                Mathf.Abs(box.Size.x - footbridge.Definition.DeckWidth) <
                    0.01f);

            Assert.That(
                deck.Size.x,
                Is.EqualTo(CityRiverDefinition.ParkFootbridgeWidth)
                    .Within(0.001f));
            Assert.That(deck.Size.x, Is.LessThan(layout.RoadWidth));
            foreach (Vector2Int node in new[]
                     {
                         footbridge.Definition.CrossingEdge.A,
                         footbridge.Definition.CrossingEdge.B
                     })
            {
                Vector3 center = layout.GetNodeWorldPosition(node);
                RuntimeOrientedBox pad = surfaces.StreetGeometry
                    .Concat(surfaces.ParkPathGeometry)
                    .Single(box => Vector2.Distance(
                        new Vector2(box.Center.x, box.Center.z),
                        new Vector2(center.x, center.z)) < 0.01f);
                Assert.That(
                    Mathf.Min(pad.Size.x, pad.Size.z),
                    Is.EqualTo(layout.RoadWidth).Within(0.001f));
            }
        }

        [Test]
        public void BridgeCrossings_EndOnTheirSpanAndLeaveTheBanksPaved()
        {
            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);
            List<RuntimeOrientedBox> travelled = surfaces.StreetGeometry
                .Concat(surfaces.ParkPathGeometry)
                .ToList();

            foreach (CityRiverBridgeDescriptor bridge in layout.River.Bridges)
            {
                Rect span = bridge.SpanBounds;
                RuntimeOrientedBox deck = travelled.Single(box =>
                    Vector2.Distance(
                        new Vector2(box.Center.x, box.Center.z),
                        span.center) < 0.01f);
                Rect deckFootprint = CreateFootprint(deck);
                Assert.That(
                    deckFootprint.xMin,
                    Is.EqualTo(span.xMin).Within(0.001f),
                    bridge.Definition.Id);
                Assert.That(
                    deckFootprint.xMax,
                    Is.EqualTo(span.xMax).Within(0.001f),
                    bridge.Definition.Id);

                foreach (CityRiverPromenadeDescriptor promenade in
                         layout.River.Promenades)
                {
                    Rect approach = promenade.WestBank
                        ? Rect.MinMaxRect(
                            promenade.Bounds.xMin + 0.01f,
                            span.yMin + 0.01f,
                            span.xMin - 0.01f,
                            span.yMax - 0.01f)
                        : Rect.MinMaxRect(
                            span.xMax + 0.01f,
                            span.yMin + 0.01f,
                            promenade.Bounds.xMax - 0.01f,
                            span.yMax - 0.01f);
                    Assert.That(approach.width, Is.GreaterThan(0f));
                    foreach (RuntimeOrientedBox box in travelled)
                    {
                        Assert.That(
                            HasPositiveOverlap(
                                CreateFootprint(box),
                                approach),
                            Is.False,
                            $"{bridge.Definition.Id} {promenade.Id}");
                    }
                }
            }
        }

        [Test]
        public void WorldBuilder_CreatesWaterBridgesAndFourPhysicalLandings()
        {
            var parent = new GameObject("River Test Parent");
            try
            {
                CityMountainBoundaryPlan mountainPlan =
                    CityMountainBoundaryPlanner.Create(layout);
                GameObject river = CityRiverWorldBuilder.Build(
                    parent.transform,
                    layout,
                    mountainPlan);

                Assert.That(river, Is.Not.Null);
                Transform water = river.transform.Find("Flowing Water");
                Transform bridges = river.transform.Find("River Bridges");
                Transform landings = river.transform.Find(
                    "Lower River Landings");
                Transform rails = river.transform.Find("Quay Guard Rails");
                Transform walls = river.transform.Find("Granite Quay Walls");
                Assert.That(water, Is.Not.Null);
                Assert.That(
                    water.childCount,
                    Is.EqualTo(layout.River.Segments.Count));
                Assert.That(bridges, Is.Not.Null);
                Assert.That(bridges.childCount, Is.EqualTo(3));
                CityRiverBridgeDescriptor footbridge = layout.River.Bridges
                    .Single(bridge => bridge.Definition.Role ==
                        CityBridgeRole.ParkFootbridge);
                Transform timberRoot = bridges.Find(
                    "Central Park Timber Footbridge");
                Renderer timber = timberRoot.Find("Timber Deck Planks")
                    .GetComponent<Renderer>();
                Assert.That(
                    timber.bounds.max.y,
                    Is.EqualTo(
                        footbridge.AverageY +
                        CityStreetSurfacePlanner.RoadTop +
                        CityRiverWorldBuilder.SurfaceClearance)
                        .Within(0.01f),
                    "The timber deck must clear the park path top plane " +
                    "it is laid on.");
                Assert.That(
                    timber.bounds.size.z,
                    Is.GreaterThan(footbridge.Definition.DeckWidth),
                    "The timber deck must overhang the park path sides.");
                MeshFilter timberStructure = timberRoot.Find(
                        "Timber Bridge Structure")
                    .GetComponent<MeshFilter>();
                float timberDeckY = footbridge.AverageY +
                                    CityStreetSurfacePlanner.RoadTop;
                Vector3[] elevatedTimberVertices = timberStructure
                    .sharedMesh.vertices
                    .Select(timberStructure.transform.TransformPoint)
                    .Where(vertex => vertex.y > timberDeckY + 0.20f)
                    .ToArray();
                Assert.That(elevatedTimberVertices, Is.Not.Empty);
                Assert.That(
                    elevatedTimberVertices.Min(vertex => vertex.x),
                    Is.GreaterThanOrEqualTo(
                        footbridge.SpanBounds.xMin - 0.001f));
                Assert.That(
                    elevatedTimberVertices.Max(vertex => vertex.x),
                    Is.LessThanOrEqualTo(
                        footbridge.SpanBounds.xMax + 0.001f));
                Assert.That(rails, Is.Not.Null);
                Assert.That(
                    rails.Find("West Quay South End Rail"),
                    Is.Null);
                Assert.That(
                    rails.Find("East Quay South End Rail"),
                    Is.Null);
                // The north seals came off when the seacoast arrived:
                // its quay stairs bridge the step onto the sand, so a
                // rail there would wall off a walk that now exists —
                // the same rule the river cave applies at the south.
                Assert.That(
                    rails.Find("West Quay North End Rail"),
                    Is.Null);
                Assert.That(
                    rails.Find("East Quay North End Rail"),
                    Is.Null);
                Collider[] railColliders =
                    rails.GetComponentsInChildren<Collider>();
                foreach (CityRiverBridgeDescriptor bridge in
                         layout.River.Bridges)
                {
                    float z = bridge.DeckBounds.center.y;
                    float westX = layout.River.Segments[0]
                        .WaterBounds.xMin - 0.48f;
                    float eastX = layout.River.Segments[0]
                        .WaterBounds.xMax + 0.48f;
                    Assert.That(
                        railColliders.Any(collider =>
                            ContainsXZ(collider.bounds, westX, z)),
                        Is.False,
                        bridge.Definition.Id + ":west");
                    Assert.That(
                        railColliders.Any(collider =>
                            ContainsXZ(collider.bounds, eastX, z)),
                        Is.False,
                        bridge.Definition.Id + ":east");
                }
                foreach (CityRiverBridgeDescriptor bridge in
                         layout.River.Bridges.Where(candidate =>
                             candidate.Definition.CarriesRoadTraffic))
                {
                    Transform bridgeRoot = bridges.Find(
                        $"{bridge.Definition.Id} Road Bridge");
                    Assert.That(bridgeRoot, Is.Not.Null);
                    Renderer underside = bridgeRoot.Find("Bridge Underside")
                        .GetComponent<Renderer>();
                    Assert.That(
                        underside.bounds.size.z,
                        Is.LessThan(bridge.Definition.DeckWidth),
                        $"{bridge.Definition.Id}: the underside must stay " +
                        "clear of the road surface side planes.");
                    float minimum = bridge.SpanBounds.xMin;
                    float maximum = bridge.SpanBounds.xMax;
                    foreach (string parapetName in new[]
                             {
                                 "Outer Parapet",
                                 "Landing Parapet"
                             })
                    {
                        Renderer parapet = bridgeRoot.Find(parapetName)
                            .GetComponent<Renderer>();
                        Assert.That(
                            parapet.bounds.min.x,
                            Is.GreaterThanOrEqualTo(minimum - 0.001f),
                            $"{bridge.Definition.Id}:{parapetName}:west");
                        Assert.That(
                            parapet.bounds.max.x,
                            Is.LessThanOrEqualTo(maximum + 0.001f),
                            $"{bridge.Definition.Id}:{parapetName}:east");
                    }
                }
                Assert.That(landings, Is.Not.Null);
                Assert.That(landings.childCount, Is.EqualTo(4));
                Assert.That(walls, Is.Not.Null);
                Assert.That(
                    walls.GetComponentsInChildren<Collider>().Count(
                        collider => collider.name.Contains(
                            "Lower Quay Frontage")),
                    Is.EqualTo(4));
                for (int index = 0; index < landings.childCount; index++)
                {
                    Transform landing = landings.GetChild(index);
                    Assert.That(
                        landing.Find("Granite Stair Flight"),
                        Is.Not.Null);
                    Assert.That(
                        landing.Find("Lower Waterside Platform"),
                        Is.Not.Null);
                    Assert.That(
                        landing.Find("Platform Landward Rail")
                            .GetComponent<Collider>(),
                        Is.Not.Null);
                    Assert.That(
                        landing.Find("Platform End Rail")
                            .GetComponent<Collider>(),
                        Is.Not.Null);
                    Assert.That(
                        landing.Find("Upper Platform Cut Guards")
                            .GetComponentsInChildren<Collider>().Length,
                        Is.GreaterThan(0));
                    Assert.That(
                        landing.GetComponentsInChildren<Collider>().Length,
                        Is.GreaterThan(0));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static int CountComponents(IEnumerable<RoadEdge> source)
        {
            RoadEdge[] edges = source.ToArray();
            var adjacency = new Dictionary<Vector2Int, List<Vector2Int>>();
            for (int index = 0; index < edges.Length; index++)
            {
                AddNeighbour(adjacency, edges[index].A, edges[index].B);
                AddNeighbour(adjacency, edges[index].B, edges[index].A);
            }

            var remaining = new HashSet<Vector2Int>(adjacency.Keys);
            int components = 0;
            var queue = new Queue<Vector2Int>();
            while (remaining.Count > 0)
            {
                components++;
                Vector2Int start = remaining.First();
                remaining.Remove(start);
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector2Int node = queue.Dequeue();
                    foreach (Vector2Int neighbour in adjacency[node])
                    {
                        if (remaining.Remove(neighbour))
                        {
                            queue.Enqueue(neighbour);
                        }
                    }
                }
            }

            return components;
        }

        private static void AddNeighbour(
            IDictionary<Vector2Int, List<Vector2Int>> adjacency,
            Vector2Int node,
            Vector2Int neighbour)
        {
            if (!adjacency.TryGetValue(node, out List<Vector2Int> neighbours))
            {
                neighbours = new List<Vector2Int>();
                adjacency.Add(node, neighbours);
            }

            neighbours.Add(neighbour);
        }

        private static bool ContainsXZ(
            Bounds bounds,
            float x,
            float z) =>
            x >= bounds.min.x && x <= bounds.max.x &&
            z >= bounds.min.z && z <= bounds.max.z;

        private static Rect CreateFootprint(RuntimeOrientedBox box)
        {
            Vector3 half = box.Size * 0.5f;
            Vector3 right = box.Rotation * Vector3.right;
            Vector3 up = box.Rotation * Vector3.up;
            Vector3 forward = box.Rotation * Vector3.forward;
            float extentX = Mathf.Abs(right.x) * half.x +
                            Mathf.Abs(up.x) * half.y +
                            Mathf.Abs(forward.x) * half.z;
            float extentZ = Mathf.Abs(right.z) * half.x +
                            Mathf.Abs(up.z) * half.y +
                            Mathf.Abs(forward.z) * half.z;
            return Rect.MinMaxRect(
                box.Center.x - extentX,
                box.Center.z - extentZ,
                box.Center.x + extentX,
                box.Center.z + extentZ);
        }

        private static bool HasPositiveOverlap(Rect first, Rect second) =>
            Mathf.Min(first.xMax, second.xMax) >
                Mathf.Max(first.xMin, second.xMin) &&
            Mathf.Min(first.yMax, second.yMax) >
                Mathf.Max(first.yMin, second.yMin);

        private static bool ContainsWithClearance(
            Rect bounds,
            Vector3 point,
            float clearance) =>
            point.x >= bounds.xMin - clearance &&
            point.x <= bounds.xMax + clearance &&
            point.z >= bounds.yMin - clearance &&
            point.z <= bounds.yMax + clearance;
    }
}
