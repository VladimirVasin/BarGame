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
        public void Create_WithSameLayoutAndSeed_ProducesIdenticalPlan()
        {
            CityLayout layout = CreateDefaultLayout(9017);

            CityPedestrianPlan first = CityPedestrianPlanner.Create(
                layout,
                4411);
            CityPedestrianPlan second = CityPedestrianPlanner.Create(
                layout,
                4411);

            Assert.That(second.StableSeed, Is.EqualTo(first.StableSeed));
            Assert.That(second.DesiredCount, Is.EqualTo(first.DesiredCount));
            Assert.That(second.AgentRadius, Is.EqualTo(first.AgentRadius));
            CollectionAssert.AreEqual(
                first.Definitions,
                second.Definitions);
        }

        [Test]
        public void Create_UsesOnlySafeOffsetStreetSegments()
        {
            CityLayout layout = CreateDefaultLayout(-5107);
            RoadWalkableArea streetArea =
                new RoadWalkableArea(layout.CreateStreetRects());

            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                2203);

            Assert.That(
                plan.Count,
                Is.EqualTo(CityPedestrianPlanner.TargetPedestrianCount));
            Assert.That(
                plan.Definitions.Select(definition => definition.Id),
                Is.Unique);
            foreach (CityPedestrianDefinition definition
                     in plan.Definitions)
            {
                Assert.That(definition.RouteEdges, Has.Count.EqualTo(1));
                Assert.That(definition.Waypoints, Has.Count.EqualTo(2));
                RoadEdge edge = definition.RouteEdges[0];
                Assert.That(
                    layout.GetPathKind(edge),
                    Is.EqualTo(CityPathKind.Street));

                Vector3 edgeStart = layout.GetNodeWorldPosition(edge.A);
                Vector3 edgeEnd = layout.GetNodeWorldPosition(edge.B);
                Vector3 tangent = (edgeEnd - edgeStart).normalized;
                Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
                Vector3 first = definition.Waypoints[0];
                Vector3 second = definition.Waypoints[1];
                float intersectionClearance =
                    CityPedestrianPlanner.GetIntersectionClearance(layout);

                Assert.That(
                    Vector3.Dot(first - edgeStart, tangent),
                    Is.EqualTo(intersectionClearance)
                        .Within(PositionTolerance));
                Assert.That(
                    Vector3.Dot(edgeEnd - second, tangent),
                    Is.EqualTo(intersectionClearance)
                        .Within(PositionTolerance));
                Assert.That(
                    intersectionClearance - plan.AgentRadius,
                    Is.GreaterThan(layout.RoadWidth * 0.5f),
                    "Route endpoints must leave the pedestrian body " +
                    "outside the intersection footprint.");
                Assert.That(
                    Mathf.Abs(Vector3.Dot(first - edgeStart, left)),
                    Is.EqualTo(CityPedestrianPlanner.RoadCenterOffset)
                        .Within(PositionTolerance));
                Assert.That(
                    definition.RouteLength,
                    Is.EqualTo(Vector3.Distance(first, second))
                        .Within(PositionTolerance));
                Assert.That(
                    definition.RouteLength,
                    Is.GreaterThanOrEqualTo(
                        CityPedestrianPlanner.MinimumRouteLength));

                for (int sample = 0; sample <= 16; sample++)
                {
                    Vector3 position = Vector3.Lerp(
                        first,
                        second,
                        sample / 16f);
                    Assert.That(
                        streetArea.Contains(position, plan.AgentRadius),
                        Is.True,
                        $"'{definition.Id}' leaves its safe street area.");
                }

                Assert.That(
                    definition.Speed,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumSpeed,
                        CityPedestrianPlanner.MaximumSpeed));
                Assert.That(
                    definition.AnimationSpeed,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumAnimationSpeed,
                        CityPedestrianPlanner.MaximumAnimationSpeed));
                Assert.That(
                    definition.AnimationPhase01,
                    Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(
                    definition.PaletteVariant,
                    Is.InRange(
                        0,
                        CityPedestrianPlanner.PaletteVariantCount - 1));
            }
        }

        [Test]
        public void Create_PrioritizesEdgesAtCityFunctions()
        {
            CityLayout layout = CreateDefaultLayout(17091);
            var functionalFrontages = new HashSet<RoadEdge>();
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.IsBar &&
                    !lot.IsPlayerHome &&
                    !lot.IsSupermarket)
                {
                    continue;
                }

                Assert.That(
                    layout.TryGetFrontageEdge(lot, out RoadEdge edge),
                    Is.True);
                AddStreetEdge(layout, functionalFrontages, edge);
            }

            foreach (CityDistrictPointOfInterestDescriptor point
                     in layout.DistrictPointsOfInterest)
            {
                foreach (CityDistrictPointOfInterestAccessDescriptor access
                         in point.Accesses)
                {
                    AddStreetEdge(
                        layout,
                        functionalFrontages,
                        access.FrontageEdge);
                }
            }

            foreach (CityOpenAreaAccessDescriptor access
                     in layout.OpenAreaAccesses)
            {
                AddStreetEdge(
                    layout,
                    functionalFrontages,
                    access.FrontageEdge);
            }

            foreach (CityParkGateDescriptor gate in layout.Park.Gates)
            {
                AddStreetEdge(
                    layout,
                    functionalFrontages,
                    FindNearestStreetEdge(layout, gate.Center));
            }

            int desiredCount = Mathf.Min(4, functionalFrontages.Count);

            CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                layout,
                7701,
                desiredCount);

            Assert.That(desiredCount, Is.GreaterThan(0));
            Assert.That(plan.Count, Is.EqualTo(desiredCount));
            foreach (CityPedestrianDefinition definition
                     in plan.Definitions)
            {
                Assert.That(
                    functionalFrontages.Contains(
                        definition.RouteEdges[0]),
                    Is.True,
                    $"'{definition.Id}' ignored higher-priority functions.");
            }
        }

        [Test]
        public void Create_WithDifferentPopulationSeed_VariesStableData()
        {
            CityLayout layout = CreateDefaultLayout(1297);

            CityPedestrianPlan first = CityPedestrianPlanner.Create(
                layout,
                1001);
            CityPedestrianPlan second = CityPedestrianPlanner.Create(
                layout,
                2002);

            Assert.That(second.StableSeed, Is.Not.EqualTo(first.StableSeed));
            CollectionAssert.AreNotEqual(
                first.Definitions,
                second.Definitions);
        }

        [Test]
        public void Create_OnSmallOrNarrowCity_ReturnsFewerOrZeroSafely()
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

            CityPedestrianPlan fewer = CityPedestrianPlanner.Create(
                smallLayout,
                12,
                CityPedestrianPlanner.MaximumPedestrianCount);
            CityPedestrianPlan noneRequested =
                CityPedestrianPlanner.Create(smallLayout, 12, 0);

            Assert.That(fewer.Count, Is.GreaterThan(0));
            Assert.That(
                fewer.Count,
                Is.LessThan(CityPedestrianPlanner.MaximumPedestrianCount));
            Assert.That(noneRequested.Count, Is.Zero);
            Assert.That(noneRequested.DesiredCount, Is.Zero);

            CityGenerationSettings narrowSettings = smallSettings.Copy();
            narrowSettings.RoadWidth = 0.6f;
            CityLayout narrowLayout = CityLayoutGenerator.Generate(
                narrowSettings,
                8123);
            CityPedestrianPlan narrow = CityPedestrianPlanner.Create(
                narrowLayout,
                12);

            Assert.That(narrow.Count, Is.Zero);
        }

        [Test]
        public void Create_WithNullLayout_Throws()
        {
            Assert.That(
                () => CityPedestrianPlanner.Create(null, 42),
                Throws.ArgumentNullException);
        }

        private static CityLayout CreateDefaultLayout(int seed)
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                seed);
        }

        private static void AddStreetEdge(
            CityLayout layout,
            ISet<RoadEdge> edges,
            RoadEdge edge)
        {
            if (layout.HasRoad(edge) &&
                layout.GetPathKind(edge) == CityPathKind.Street)
            {
                edges.Add(edge);
            }
        }

        private static RoadEdge FindNearestStreetEdge(
            CityLayout layout,
            Vector3 position)
        {
            RoadEdge nearest = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = end - start;
                float denominator = delta.sqrMagnitude;
                float amount = denominator <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector3.Dot(position - start, delta) /
                        denominator);
                float distance =
                    (position - Vector3.Lerp(start, end, amount))
                    .sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = edge;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
