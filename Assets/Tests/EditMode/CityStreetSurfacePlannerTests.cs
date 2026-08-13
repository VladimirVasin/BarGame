using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityStreetSurfacePlannerTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Create_WithSameLayout_ProducesIdenticalGeometry()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                43819);

            CityStreetSurfacePlan first =
                CityStreetSurfacePlanner.Create(layout);
            CityStreetSurfacePlan second =
                CityStreetSurfacePlanner.Create(layout);

            Assert.That(
                first.CarriagewayWidth,
                Is.EqualTo(second.CarriagewayWidth));
            CollectionAssert.AreEqual(
                first.StreetSurfaces,
                second.StreetSurfaces);
            CollectionAssert.AreEqual(first.ParkPaths, second.ParkPaths);
            CollectionAssert.AreEqual(first.Sidewalks, second.Sidewalks);
            CollectionAssert.AreEqual(
                first.CenterMarkings,
                second.CenterMarkings);
            CollectionAssert.AreEqual(
                first.CrosswalkMarkings,
                second.CrosswalkMarkings);
            CollectionAssert.AreEqual(
                first.SidewalkWalkableRectangles,
                second.SidewalkWalkableRectangles);
            CollectionAssert.AreEqual(
                first.CrosswalkWalkableRectangles,
                second.CrosswalkWalkableRectangles);
            CollectionAssert.AreEqual(
                first.CrosswalkNodes,
                second.CrosswalkNodes);
            CollectionAssert.AreEqual(
                first.Crosswalks,
                second.Crosswalks);
        }

        [Test]
        public void Create_DefaultRoadV2_PreservesFullBusJunctionApron()
        {
            CityGenerationSettings settings = CreateDenseSettings();
            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                19743);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);

            Assert.That(
                layout.RoadWidth,
                Is.EqualTo(CityGenerationSettings.DefaultRoadWidth));
            Assert.That(
                layout.NodeSpacing,
                Is.EqualTo(new Vector2(26f, 26f)));
            Assert.That(
                CityStreetSurfacePlanner.SidewalkWidth,
                Is.EqualTo(1f));
            Assert.That(plan.CarriagewayWidth, Is.EqualTo(6f));
            IReadOnlyList<Vector2Int> busIntersections =
                CityBusIntersectionSelector.Select(layout);
            Assert.That(busIntersections, Is.Not.Empty);

            Vector3 nodePosition = layout.GetNodeWorldPosition(
                busIntersections[0]);
            float halfRoad = layout.RoadWidth * 0.5f;
            Rect junctionApron = Rect.MinMaxRect(
                nodePosition.x - halfRoad,
                nodePosition.z - halfRoad,
                nodePosition.x + halfRoad,
                nodePosition.z + halfRoad);
            Assert.That(
                plan.SidewalkWalkableRectangles.Any(sidewalk =>
                    HasPositiveOverlap(sidewalk, junctionApron)),
                Is.False,
                "Road v2.1 must keep the full 8 m junction apron clear of " +
                "raised sidewalk geometry for the design bus.");
            Assert.That(
                CityStreetSurfacePlanner.BusApproachApronLength,
                Is.EqualTo(4.5f));
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2Int direction = directions[index];
                Vector2Int node = busIntersections[0];
                if (!layout.HasRoad(node, node + direction))
                {
                    continue;
                }

                float length = CityStreetSurfacePlanner
                    .BusApproachApronLength;
                Vector2 center = new Vector2(
                    nodePosition.x + direction.x *
                        (halfRoad + length * 0.5f),
                    nodePosition.z + direction.y *
                        (halfRoad + length * 0.5f));
                Rect approachApron = direction.x != 0
                    ? Rect.MinMaxRect(
                        center.x - length * 0.5f,
                        center.y - halfRoad,
                        center.x + length * 0.5f,
                        center.y + halfRoad)
                    : Rect.MinMaxRect(
                        center.x - halfRoad,
                        center.y - length * 0.5f,
                        center.x + halfRoad,
                        center.y + length * 0.5f);
                Assert.That(
                    plan.SidewalkWalkableRectangles.Any(sidewalk =>
                        HasPositiveOverlap(sidewalk, approachApron)),
                    Is.False,
                    "Road v2.1 must cut raised curbs back from each bus " +
                    "junction's real Street approaches.");
            }
            Assert.That(
                plan.CrosswalkWalkableRectangles.Any(crosswalk =>
                    HasDimensions(
                        crosswalk,
                        CityStreetSurfacePlanner.CrosswalkDepth,
                        6f)),
                Is.True);
        }

        [Test]
        public void Create_DefaultThreeWayBusApron_ClosesWideSidewalkMouth()
        {
            CityLayout layout = HomeExteriorContextPlanner
                .Generate(GameSessionState.DefaultCitySeed)
                .Layout;
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            Vector2Int node = CityBusIntersectionSelector
                .Select(layout)
                .First(candidate =>
                    layout.RoadEdges.Count(edge =>
                        edge.Contains(candidate)) == 3);
            Vector2Int missingDirection = directions.Single(direction =>
                !layout.HasRoad(node, node + direction));
            Vector3 nodePosition = layout.GetNodeWorldPosition(node);
            float offset = CityBusIntersectionSelector
                .GetCornerCenterOffset(layout);
            float span = (offset * 2f) -
                         CityStreetSurfacePlanner.SidewalkWidth;
            Rect expected = missingDirection.x != 0
                ? new Rect(
                    nodePosition.x + (missingDirection.x * offset) -
                        (CityStreetSurfacePlanner.SidewalkWidth * 0.5f),
                    nodePosition.z - (span * 0.5f),
                    CityStreetSurfacePlanner.SidewalkWidth,
                    span)
                : new Rect(
                    nodePosition.x - (span * 0.5f),
                    nodePosition.z + (missingDirection.y * offset) -
                        (CityStreetSurfacePlanner.SidewalkWidth * 0.5f),
                    span,
                    CityStreetSurfacePlanner.SidewalkWidth);

            Assert.That(
                plan.SidewalkWalkableRectangles.Any(sidewalk =>
                    Approximately(sidewalk, expected)),
                Is.True,
                "A wide three-way bus apron must close its missing side " +
                "with one axis-aligned sidewalk spanning both corner pads.");
        }

        [Test]
        public void Create_KeepsRaisedSidewalksInsideStreetCorridors()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                9923);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);
            RoadEdge[] streetEdges = layout.RoadEdges
                .Where(edge =>
                    layout.GetPathKind(edge) == CityPathKind.Street)
                .OrderBy(edge => edge.A.x)
                .ThenBy(edge => edge.A.y)
                .ThenBy(edge => edge.B.x)
                .ThenBy(edge => edge.B.y)
                .ToArray();
            Rect[] streetCorridors = streetEdges
                .Select(layout.GetRoadRect)
                .ToArray();

            Assert.That(plan.StreetSurfaces, Has.Count.EqualTo(
                streetEdges.Length));
            Assert.That(
                plan.CarriagewayWidth,
                Is.EqualTo(
                    layout.RoadWidth -
                    (CityStreetSurfacePlanner.SidewalkWidth * 2f))
                    .Within(Tolerance));
            for (int index = 0; index < streetEdges.Length; index++)
            {
                RoadEdge edge = streetEdges[index];
                Bounds surface = plan.StreetSurfaces[index];
                float edgeLength = Vector3.Distance(
                    layout.GetNodeWorldPosition(edge.A),
                    layout.GetNodeWorldPosition(edge.B));
                Assert.That(
                    surface.max.y,
                    Is.EqualTo(CityStreetSurfacePlanner.RoadTop)
                        .Within(Tolerance));
                Assert.That(
                    surface.size.y,
                    Is.EqualTo(
                        CityStreetSurfacePlanner.RoadTop * 2f)
                        .Within(Tolerance));
                Assert.That(
                    edge.IsHorizontal
                        ? surface.size.x
                        : surface.size.z,
                    Is.EqualTo(edgeLength + layout.RoadWidth)
                        .Within(Tolerance));
                Assert.That(
                    edge.IsHorizontal
                        ? surface.size.z
                        : surface.size.x,
                    Is.EqualTo(
                            layout.ElevationPlan.TryGetSignatureStair(
                                edge,
                                out _)
                                ? plan.CarriagewayWidth
                                : layout.RoadWidth)
                        .Within(Tolerance));
            }

            Assert.That(
                plan.Sidewalks.Count,
                Is.GreaterThanOrEqualTo(streetEdges.Length * 2));
            Assert.That(
                plan.SidewalkWalkableRectangles,
                Has.Count.EqualTo(plan.Sidewalks.Count));
            for (int index = 0; index < plan.Sidewalks.Count; index++)
            {
                Bounds sidewalk = plan.Sidewalks[index];
                Rect walkable = plan.SidewalkWalkableRectangles[index];
                Assert.That(
                    sidewalk.min.y,
                    Is.EqualTo(CityStreetSurfacePlanner.RoadTop)
                        .Within(Tolerance));
                Assert.That(
                    sidewalk.max.y,
                    Is.EqualTo(CityStreetSurfacePlanner.SidewalkTop)
                        .Within(Tolerance));
                Assert.That(
                    walkable,
                    Is.EqualTo(CreateRect(sidewalk)));
                Assert.That(
                    streetCorridors.Any(corridor =>
                        Contains(corridor, walkable)) ||
                    IsBusIntersectionSidewalk(layout, walkable),
                    Is.True,
                    $"Sidewalk {walkable} left both its street corridor " +
                    "and every selected Road v2.1 junction sidewalk.");
                Assert.That(
                    Mathf.Approximately(
                        walkable.width,
                        CityStreetSurfacePlanner.SidewalkWidth) ||
                    Mathf.Approximately(
                        walkable.height,
                        CityStreetSurfacePlanner.SidewalkWidth) ||
                    layout.ElevationPlan.SignatureStairs.Any(stair =>
                        Mathf.Abs(
                            stair.Width -
                            Mathf.Min(
                                walkable.width,
                                walkable.height)) <= Tolerance),
                    Is.True);
            }
        }

        [Test]
        public void Create_AddsBoundedCrosswalksToEligibleApproaches()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                -28914);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);
            Dictionary<Vector2Int, int> streetDegrees =
                CountDegrees(layout, CityPathKind.Street);

            Assert.That(plan.CrosswalkNodes, Is.Not.Empty);
            Assert.That(
                plan.CrosswalkNodes.Count,
                Is.LessThanOrEqualTo(
                    CityStreetSurfacePlanner
                        .MaximumCrosswalkIntersections));
            int expectedApproachCount = 0;
            for (int index = 0;
                 index < plan.CrosswalkNodes.Count;
                 index++)
            {
                Vector2Int node = plan.CrosswalkNodes[index];
                Assert.That(
                    streetDegrees[node],
                    Is.GreaterThanOrEqualTo(3));
                Assert.That(TouchesParkPath(layout, node), Is.False);
                expectedApproachCount += streetDegrees[node];
            }

            Assert.That(
                plan.CrosswalkWalkableRectangles,
                Has.Count.EqualTo(expectedApproachCount));
            Assert.That(
                plan.Crosswalks,
                Has.Count.EqualTo(expectedApproachCount));
            Assert.That(
                plan.CrosswalkMarkings,
                Has.Count.EqualTo(
                    expectedApproachCount *
                    CityStreetSurfacePlanner.CrosswalkStripeCount));
            foreach (Rect crosswalk in
                     plan.CrosswalkWalkableRectangles)
            {
                Assert.That(
                    HasDimensions(
                        crosswalk,
                        CityStreetSurfacePlanner.CrosswalkDepth,
                        plan.CarriagewayWidth),
                    Is.True,
                    crosswalk.ToString());
                Assert.That(
                    layout.RoadEdges.Any(edge =>
                        layout.GetPathKind(edge) == CityPathKind.Street &&
                        Contains(layout.GetRoadRect(edge), crosswalk)),
                    Is.True,
                    crosswalk.ToString());
            }

            foreach (CityCrosswalkDescriptor crosswalk in plan.Crosswalks)
            {
                Assert.That(
                    plan.CrosswalkNodes,
                    Does.Contain(crosswalk.Node));
                Assert.That(
                    layout.GetPathKind(crosswalk.ApproachEdge),
                    Is.EqualTo(CityPathKind.Street));
                Assert.That(
                    crosswalk.ApproachEdge.Contains(crosswalk.Node),
                    Is.True);
                Assert.That(
                    plan.CrosswalkWalkableRectangles,
                    Does.Contain(crosswalk.WalkableBounds));
                Assert.That(
                    Vector3.Dot(
                        crosswalk.AlongDirection,
                        crosswalk.AcrossDirection),
                    Is.EqualTo(0f).Within(Tolerance));
            }

            foreach (Bounds stripe in plan.CrosswalkMarkings)
            {
                Rect stripeRect = CreateRect(stripe);
                Assert.That(
                    HasDimensions(
                        stripeRect,
                        0.36f,
                        plan.CarriagewayWidth),
                    Is.True,
                    stripe.ToString());
                Assert.That(
                    plan.CrosswalkWalkableRectangles.Any(crosswalk =>
                        Contains(crosswalk, stripeRect)),
                    Is.True,
                    stripe.ToString());
            }
        }

        [Test]
        public void Create_RemovesCenterDashesFromCrosswalks()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                66125);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);

            Assert.That(plan.CrosswalkWalkableRectangles, Is.Not.Empty);
            foreach (Bounds dash in plan.CenterMarkings)
            {
                Rect dashRect = CreateRect(dash);
                foreach (Rect crosswalk in
                         plan.CrosswalkWalkableRectangles)
                {
                    Assert.That(
                        HasPositiveOverlap(dashRect, crosswalk),
                        Is.False,
                        $"Dash {dashRect} overlaps crosswalk {crosswalk}.");
                }
            }
        }

        [Test]
        public void Create_PreservesParkPathsAndExcludesTheirNodes()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                17191);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);
            int expectedParkPathCount = layout.RoadEdges.Count(edge =>
                layout.GetPathKind(edge) == CityPathKind.ParkPath);

            Assert.That(
                plan.ParkPaths,
                Has.Count.EqualTo(expectedParkPathCount));
            for (int index = 0;
                 index < plan.CrosswalkNodes.Count;
                 index++)
            {
                Assert.That(
                    TouchesParkPath(layout, plan.CrosswalkNodes[index]),
                    Is.False);
            }
        }

        [Test]
        public void Create_UsesTheSameIntersectionOrderAsTrafficSignals()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                5051);
            CityStreetSurfacePlan streetPlan =
                CityStreetSurfacePlanner.Create(layout);
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(layout);
            Vector2Int[] signalNodes = nightPlan.TrafficSignals
                .Where(signal => signal.PairIndex == 0)
                .Select(signal => signal.IntersectionNode)
                .ToArray();

            CollectionAssert.AreEqual(
                streetPlan.CrosswalkNodes,
                signalNodes);
        }

        [Test]
        public void Create_RejectsNullAndSettingsRejectZeroCarriageway()
        {
            Assert.That(
                () => CityStreetSurfacePlanner.Create(null),
                Throws.ArgumentNullException);

            CityGenerationSettings settings = CreateDenseSettings();
            settings.RoadWidth =
                CityStreetSurfacePlanner.SidewalkWidth * 2f;
            Assert.That(
                () => settings.Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static Dictionary<Vector2Int, int> CountDegrees(
            CityLayout layout,
            CityPathKind kind)
        {
            Dictionary<Vector2Int, int> degrees = layout.Nodes
                .ToDictionary(node => node, _ => 0);
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != kind)
                {
                    continue;
                }

                degrees[edge.A] = degrees[edge.A] + 1;
                degrees[edge.B] = degrees[edge.B] + 1;
            }

            return degrees;
        }

        private static bool TouchesParkPath(
            CityLayout layout,
            Vector2Int node)
        {
            return layout.RoadEdges.Any(edge =>
                edge.Contains(node) &&
                layout.GetPathKind(edge) == CityPathKind.ParkPath);
        }

        private static Rect CreateRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static bool Contains(Rect container, Rect contained)
        {
            return contained.xMin >= container.xMin - Tolerance &&
                   contained.xMax <= container.xMax + Tolerance &&
                   contained.yMin >= container.yMin - Tolerance &&
                   contained.yMax <= container.yMax + Tolerance;
        }

        private static bool HasDimensions(
            Rect rectangle,
            float first,
            float second)
        {
            return Mathf.Abs(rectangle.width - first) <= Tolerance &&
                       Mathf.Abs(rectangle.height - second) <= Tolerance ||
                   Mathf.Abs(rectangle.width - second) <= Tolerance &&
                       Mathf.Abs(rectangle.height - first) <= Tolerance;
        }

        private static bool HasPositiveOverlap(Rect first, Rect second)
        {
            return Mathf.Min(first.xMax, second.xMax) -
                       Mathf.Max(first.xMin, second.xMin) > Tolerance &&
                   Mathf.Min(first.yMax, second.yMax) -
                       Mathf.Max(first.yMin, second.yMin) > Tolerance;
        }

        private static bool IsBusIntersectionSidewalk(
            CityLayout layout,
            Rect sidewalk)
        {
            float offset = CityBusIntersectionSelector
                .GetCornerCenterOffset(layout);
            IReadOnlyList<Vector2Int> nodes =
                CityBusIntersectionSelector.Select(layout);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector3 center = layout.GetNodeWorldPosition(nodes[index]);
                for (int xSign = -1; xSign <= 1; xSign += 2)
                {
                    for (int zSign = -1; zSign <= 1; zSign += 2)
                    {
                        Rect expected = new Rect(
                            center.x + (xSign * offset) - 0.5f,
                            center.z + (zSign * offset) - 0.5f,
                            CityStreetSurfacePlanner.SidewalkWidth,
                            CityStreetSurfacePlanner.SidewalkWidth);
                        if (Contains(expected, sidewalk))
                        {
                            return true;
                        }
                    }
                }

                Vector2Int[] directions =
                {
                    Vector2Int.left,
                    Vector2Int.right,
                    Vector2Int.down,
                    Vector2Int.up
                };
                float span = (offset * 2f) -
                             CityStreetSurfacePlanner.SidewalkWidth;
                for (int directionIndex = 0;
                     directionIndex < directions.Length;
                     directionIndex++)
                {
                    Vector2Int direction = directions[directionIndex];
                    if (layout.HasRoad(nodes[index], nodes[index] + direction))
                    {
                        continue;
                    }

                    Rect expected = direction.x != 0
                        ? new Rect(
                            center.x + (direction.x * offset) - 0.5f,
                            center.z - (span * 0.5f),
                            CityStreetSurfacePlanner.SidewalkWidth,
                            span)
                        : new Rect(
                            center.x - (span * 0.5f),
                            center.z + (direction.y * offset) - 0.5f,
                            span,
                            CityStreetSurfacePlanner.SidewalkWidth);
                    if (Contains(expected, sidewalk))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Approximately(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) <= Tolerance &&
                   Mathf.Abs(first.y - second.y) <= Tolerance &&
                   Mathf.Abs(first.width - second.width) <= Tolerance &&
                   Mathf.Abs(first.height - second.height) <= Tolerance;
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
    }
}
