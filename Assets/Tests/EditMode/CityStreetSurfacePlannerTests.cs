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
                    Is.EqualTo(layout.RoadWidth).Within(Tolerance));
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
                        Contains(corridor, walkable)),
                    Is.True,
                    $"Sidewalk {walkable} left every street corridor.");
                Assert.That(
                    Mathf.Approximately(
                        walkable.width,
                        CityStreetSurfacePlanner.SidewalkWidth) ||
                    Mathf.Approximately(
                        walkable.height,
                        CityStreetSurfacePlanner.SidewalkWidth),
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
