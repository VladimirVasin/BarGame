using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
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
        public void ParkBenches_FollowRealPathsInBothHalves()
        {
            Assert.That(layout.Park.Regions, Has.Count.EqualTo(2));
            Assert.That(
                layout.Park.Benches,
                Has.Count.EqualTo(
                    layout.Park.Regions.Count *
                    CityParkBenchPlanner.BenchCountPerRegion));

            CityStreetSurfacePlan surfaces =
                CityStreetSurfacePlanner.Create(layout);
            RoadEdge[] pathEdges = layout.RoadEdges
                .Where(edge =>
                    layout.GetPathKind(edge) == CityPathKind.ParkPath &&
                    !layout.IsRiverBridgeEdge(edge))
                .ToArray();
            Assert.That(pathEdges, Is.Not.Empty);

            foreach (CityParkRegionPlan region in layout.Park.Regions)
            {
                CityParkBenchDescriptor[] benches = layout.Park.Benches
                    .Where(bench => string.Equals(
                        bench.RegionId,
                        region.Id,
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.That(
                    benches,
                    Has.Length.EqualTo(
                        CityParkBenchPlanner.BenchCountPerRegion),
                    region.Id);

                foreach (CityParkBenchDescriptor bench in benches)
                {
                    Assert.That(
                        region.WalkableBounds.Contains(
                            new Vector2(
                                bench.Position.x,
                                bench.Position.z)),
                        Is.True,
                        bench.Id);

                    RoadEdge nearest = default;
                    Vector3 closest = default;
                    float nearestDistance = float.MaxValue;
                    foreach (RoadEdge edge in pathEdges)
                    {
                        Vector3 candidate = ClosestPointOnSegmentXZ(
                            bench.Position,
                            layout.GetNodeWorldPosition(edge.A),
                            layout.GetNodeWorldPosition(edge.B));
                        if (!region.WalkableBounds.Contains(
                                new Vector2(candidate.x, candidate.z)))
                        {
                            continue;
                        }

                        float distance = PlanarDistance(
                            bench.Position,
                            candidate);
                        if (distance < nearestDistance)
                        {
                            nearest = edge;
                            closest = candidate;
                            nearestDistance = distance;
                        }
                    }

                    Assert.That(
                        nearestDistance,
                        Is.LessThan(float.MaxValue),
                        $"{bench.Id} has no path in {region.Id}.");
                    float expectedOffset =
                        layout.GetTravelWidth(nearest) * 0.5f +
                        CityParkBenchDescriptor.SeatDepth * 0.5f +
                        CityParkBenchPlanner.PathEdgeGap;
                    Assert.That(
                        nearestDistance,
                        Is.EqualTo(expectedOffset).Within(0.001f),
                        $"{bench.Id} is not beside its path.");

                    Vector3 pathTangent =
                        layout.GetNodeWorldPosition(nearest.B) -
                        layout.GetNodeWorldPosition(nearest.A);
                    pathTangent.y = 0f;
                    pathTangent.Normalize();
                    Assert.That(
                        Mathf.Abs(Vector3.Dot(
                            bench.Tangent,
                            pathTangent)),
                        Is.EqualTo(1f).Within(0.001f),
                        $"{bench.Id} is not parallel to its path.");
                    Vector3 towardPath = closest - bench.Position;
                    towardPath.y = 0f;
                    towardPath.Normalize();
                    Assert.That(
                        Vector3.Dot(bench.Forward, towardPath),
                        Is.EqualTo(1f).Within(0.001f),
                        $"{bench.Id} faces away from its path.");

                    float timberNearEdge = nearestDistance -
                        CityParkBenchDescriptor.SeatDepth * 0.5f;
                    Assert.That(
                        timberNearEdge,
                        Is.GreaterThanOrEqualTo(
                            layout.GetTravelWidth(nearest) * 0.5f +
                            CityParkBenchPlanner.PathEdgeGap -
                            0.001f),
                        $"{bench.Id} blocks the path.");

                    Vector3 dock = bench.Position +
                        bench.Forward *
                        (CityParkBenchDescriptor.SeatDepth * 0.5f +
                         CityBenchSitPlan.EntryEdgeDistance);
                    foreach (float along in new[]
                             {
                                 -CityParkBenchDescriptor.SeatWidth * 0.5f,
                                 0f,
                                 CityParkBenchDescriptor.SeatWidth * 0.5f
                             })
                    {
                        Vector3 sample = dock + bench.Tangent * along;
                        Assert.That(
                            surfaces.ParkPathGeometry.Any(path =>
                                path.TrySampleTop(sample, out _)),
                            Is.True,
                            $"{bench.Id} has no path under its entry line.");
                    }

                    Assert.That(
                        layout.Park.TreePositions.Any(tree =>
                            PlanarDistance(tree, bench.Position) < 2.4f),
                        Is.False,
                        $"{bench.Id} intersects a park tree.");
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
        public void QuayWallLamps_HangLowOnBothWallFacesAtEvenPitch()
        {
            IReadOnlyList<Vector3> lamps =
                CityRiverWorldBuilder.CreateQuayWallLampPositions(
                    layout);
            Rect waterBounds = layout.River.Segments[0].WaterBounds;

            foreach (bool west in new[] { true, false })
            {
                float faceX = west
                    ? waterBounds.xMin
                    : waterBounds.xMax;
                List<Vector3> bank = lamps
                    .Where(lamp => west
                        ? lamp.x < waterBounds.center.x
                        : lamp.x > waterBounds.center.x)
                    .OrderBy(lamp => lamp.z)
                    .ToList();

                // A rhythm the fog can carry: enough fixtures that
                // two or three always burn inside its ~30 m of
                // legibility.
                Assert.That(
                    bank,
                    Has.Count.GreaterThanOrEqualTo(15),
                    west ? "west" : "east");

                for (int index = 0; index < bank.Count; index++)
                {
                    Vector3 lamp = bank[index];
                    Assert.That(
                        lamp.x,
                        Is.EqualTo(faceX).Within(0.001f),
                        lamp.ToString());

                    // The south cave approach stays dark and the
                    // north stair handoff stays clear.
                    Assert.That(lamp.z, Is.InRange(-143f, 151f));

                    foreach (CityRiverBridgeDescriptor bridge in
                             layout.River.Bridges)
                    {
                        Assert.That(
                            Mathf.Abs(
                                bridge.DeckBounds.center.y - lamp.z),
                            Is.GreaterThanOrEqualTo(6f),
                            lamp.ToString());
                    }

                    Assert.That(
                        layout.River.Landings.Any(landing =>
                            ContainsWithClearance(
                                landing.StairBounds, lamp, 1.0f) ||
                            ContainsWithClearance(
                                landing.PlatformBounds, lamp, 1.0f)),
                        Is.False,
                        lamp.ToString());

                    // The lens rides the falling water datum: low on
                    // the wall, above the visible surface, under the
                    // parapet.
                    float datum = SampleWaterDatum(
                        layout.River,
                        lamp.z);
                    Assert.That(
                        lamp.y - datum,
                        Is.EqualTo(1.02f).Within(0.001f),
                        lamp.ToString());
                    Assert.That(
                        lamp.y,
                        Is.GreaterThan(datum - 0.12f));
                    foreach (CityRiverPromenadeDescriptor promenade in
                             layout.River.Promenades.Where(candidate =>
                                 candidate.WestBank == west))
                    {
                        float promenadeY = Mathf.Lerp(
                            promenade.SouthY,
                            promenade.NorthY,
                            Mathf.InverseLerp(
                                promenade.Bounds.yMin,
                                promenade.Bounds.yMax,
                                lamp.z));
                        Assert.That(
                            lamp.y,
                            Is.LessThan(promenadeY),
                            lamp.ToString());
                    }

                    if (index > 0)
                    {
                        float step = lamp.z - bank[index - 1].z;
                        float multiple = step / 13f;
                        Assert.That(step, Is.GreaterThan(0f));
                        Assert.That(step, Is.LessThanOrEqualTo(39f));
                        Assert.That(
                            multiple,
                            Is.EqualTo(Mathf.Round(multiple))
                                .Within(0.001f),
                            lamp.ToString());
                    }
                }
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
        public void WorldBuilder_QuayFacesCoverCoplanarPavingAndBedSides()
        {
            var parent = new GameObject("Quay Face Test Parent");
            try
            {
                CityMountainBoundaryPlan mountainPlan =
                    CityMountainBoundaryPlanner.Create(layout);
                GameObject river = CityRiverWorldBuilder.Build(
                    parent.transform,
                    layout,
                    mountainPlan);

                Transform walls = river.transform.Find(
                    "Granite Quay Walls");
                AssertQuayWallFaces(
                    walls,
                    layout.River.Segments[0].WaterBounds);

                Assert.That(mountainPlan.HasRiverCave, Is.True);
                Transform caveWalls = river.transform
                    .Find("River Cave Extension")
                    .Find("Granite Quay Walls");
                AssertQuayWallFaces(
                    caveWalls,
                    mountainPlan.RiverCave.WaterApproachBounds);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
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
                Transform lamps = river.transform.Find(
                    "Embankment Lamps");
                Assert.That(lamps, Is.Not.Null);
                Assert.That(
                    lamps.GetComponentsInChildren<CityLightHalo>(true),
                    Has.Length.EqualTo(
                        CityRiverWorldBuilder
                            .CreateQuayWallLampPositions(layout)
                            .Count +
                        CityRiverWorldBuilder
                            .CreatePromenadeLampPositions(layout)
                            .Count),
                    "Every embankment lamp, wall and post alike, " +
                    "hangs its own fog halo.");
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
                // The full north seals came off when the seacoast
                // arrived. Its stairs open the logical three-metre
                // walk; only the extra structural lip by the water is
                // capped so it cannot look traversable.
                Assert.That(
                    rails.Find("West Quay North End Rail"),
                    Is.Null);
                Assert.That(
                    rails.Find("East Quay North End Rail"),
                    Is.Null);
                Assert.That(
                    rails.Find("West Quay North Water Lip Rail"),
                    Is.Not.Null,
                    "The non-walkable waterside lip needs a visible cap.");
                Assert.That(
                    rails.Find("East Quay North Water Lip Rail"),
                    Is.Not.Null,
                    "The non-walkable waterside lip needs a visible cap.");
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

        private static void AssertQuayWallFaces(
            Transform walls,
            Rect waterBounds)
        {
            Assert.That(walls, Is.Not.Null);
            Renderer[] renderers =
                walls.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                bool west = renderer.bounds.center.x < waterBounds.center.x;
                float waterEdgeX = west
                    ? waterBounds.xMin
                    : waterBounds.xMax;
                float waterReveal = west
                    ? renderer.bounds.max.x - waterEdgeX
                    : waterEdgeX - renderer.bounds.min.x;
                float landwardDepth = west
                    ? waterEdgeX - renderer.bounds.min.x
                    : renderer.bounds.max.x - waterEdgeX;

                Assert.That(
                    waterReveal,
                    Is.GreaterThanOrEqualTo(
                        CityRiverWorldBuilder.QuayWallWaterReveal -
                        0.001f),
                    $"'{renderer.name}' does not cover the paving/bed " +
                    "side plane at the water edge.");
                Assert.That(
                    landwardDepth,
                    Is.EqualTo(
                            CityRiverWorldBuilder.QuayWallLandwardDepth)
                        .Within(0.001f),
                    $"'{renderer.name}' moved its rail-side seat while " +
                    "covering the water face.");
            }
        }

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

        private static Vector3 ClosestPointOnSegmentXZ(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            delta.y = 0f;
            Vector3 offset = point - start;
            offset.y = 0f;
            float denominator = delta.sqrMagnitude;
            float amount = denominator > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(offset, delta) / denominator)
                : 0f;
            Vector3 result = Vector3.Lerp(start, end, amount);
            result.y = point.y;
            return result;
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt(x * x + z * z);
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

        private static float SampleWaterDatum(
            CityRiverPlan plan,
            float z)
        {
            CityRiverSegmentDescriptor segment = plan.Segments[0];
            for (int index = 0; index < plan.Segments.Count; index++)
            {
                segment = plan.Segments[index];
                if (z <= segment.WaterBounds.yMax)
                {
                    break;
                }
            }

            return Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                Mathf.InverseLerp(
                    segment.WaterBounds.yMin,
                    segment.WaterBounds.yMax,
                    z));
        }
    }
}
