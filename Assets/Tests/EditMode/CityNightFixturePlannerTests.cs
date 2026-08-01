using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityNightFixturePlannerTests
    {
        private const float PositionTolerance = 0.0001f;

        [Test]
        public void CreatePlan_WithSameLayout_ProducesIdenticalDescriptors()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                71923);

            CityNightFixturePlan first =
                CityNightFixturePlanner.CreatePlan(layout);
            CityNightFixturePlan second =
                CityNightFixturePlanner.CreatePlan(layout);

            CollectionAssert.AreEqual(first.StreetLamps, second.StreetLamps);
            CollectionAssert.AreEqual(
                first.TrafficSignals,
                second.TrafficSignals);
        }

        [Test]
        public void CreatePlan_PlacesAtMostTwoLampsPerEdgeOutsidePublicSpaces()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                -4119);

            CityNightFixturePlan plan =
                CityNightFixturePlanner.CreatePlan(layout);

            Assert.That(
                plan.StreetLamps.Count,
                Is.LessThanOrEqualTo(layout.RoadEdges.Count * 2));

            foreach (RoadEdge edge in layout.RoadEdges)
            {
                StreetLampDescriptor[] lamps = plan.StreetLamps
                    .Where(lamp => lamp.Edge == edge)
                    .OrderBy(lamp => lamp.EdgeT)
                    .ToArray();
                Assert.That(lamps.Length, Is.InRange(0, 2), edge.ToString());
                for (int index = 0; index < lamps.Length; index++)
                {
                    Assert.That(
                        lamps[index].EdgeT ==
                            CityNightFixturePlanner.FirstLampEdgeT ||
                        lamps[index].EdgeT ==
                            CityNightFixturePlanner.SecondLampEdgeT,
                        Is.True,
                        edge.ToString());
                    AssertLampFacesRoad(layout, lamps[index]);
                    AssertOutsidePublicSpaceReservations(
                        layout,
                        lamps[index].Position,
                        1f);
                }
            }
        }

        [Test]
        public void CreatePlan_LimitsSignalsToEligibleIntersections()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CreateDenseSettings(),
                5051);
            Dictionary<Vector2Int, int> degrees = CountDegrees(layout);

            CityNightFixturePlan plan =
                CityNightFixturePlanner.CreatePlan(layout);

            int eligibleCount = degrees.Count(pair => pair.Value >= 3);
            int maximumIntersectionCount = Mathf.Min(
                eligibleCount,
                CityNightFixturePlanner.MaximumSignalIntersections);
            IGrouping<Vector2Int, TrafficSignalDescriptor>[] signalGroups =
                plan.TrafficSignals
                    .GroupBy(signal => signal.IntersectionNode)
                    .ToArray();

            Assert.That(
                signalGroups,
                Has.Length.LessThanOrEqualTo(maximumIntersectionCount));
            Assert.That(
                plan.TrafficSignals.Count,
                Is.EqualTo(signalGroups.Length * 2));

            foreach (IGrouping<Vector2Int, TrafficSignalDescriptor> group
                     in signalGroups)
            {
                Assert.That(degrees[group.Key], Is.GreaterThanOrEqualTo(3));
                TrafficSignalDescriptor[] signals = group
                    .OrderBy(signal => signal.PairIndex)
                    .ToArray();
                Assert.That(signals, Has.Length.EqualTo(2));
                Assert.That(signals[0].PairIndex, Is.EqualTo(0));
                Assert.That(signals[1].PairIndex, Is.EqualTo(1));
                Assert.That(
                    signals[1].BlinkPhase01,
                    Is.EqualTo(signals[0].BlinkPhase01));
                Assert.That(
                    signals[0].BlinkPhase01,
                    Is.InRange(0f, 1f));

                Vector3 nodePosition =
                    layout.GetNodeWorldPosition(group.Key);
                Vector3 pairedOffset =
                    (signals[0].Position - nodePosition) +
                    (signals[1].Position - nodePosition);
                Assert.That(
                    pairedOffset.sqrMagnitude,
                    Is.LessThan(PositionTolerance));
                AssertSignalFacesIntersection(nodePosition, signals[0]);
                AssertSignalFacesIntersection(nodePosition, signals[1]);
                AssertOutsidePublicSpaceReservations(
                    layout,
                    signals[0].Position,
                    1f);
                AssertOutsidePublicSpaceReservations(
                    layout,
                    signals[1].Position,
                    1f);
            }
        }

        [Test]
        public void CreatePlan_UsesSeedForStableFixtureVariation()
        {
            CityGenerationSettings settings = CreateDenseSettings();
            CityLayout firstLayout =
                CityLayoutGenerator.Generate(settings, 1001);
            CityLayout secondLayout =
                CityLayoutGenerator.Generate(settings, 2002);

            CollectionAssert.AreEqual(
                firstLayout.RoadEdges,
                secondLayout.RoadEdges,
                "LoopChance=1 should keep the graph identical.");

            CityNightFixturePlan first =
                CityNightFixturePlanner.CreatePlan(firstLayout);
            CityNightFixturePlan second =
                CityNightFixturePlanner.CreatePlan(secondLayout);

            bool sameLamps = first.StreetLamps.SequenceEqual(
                second.StreetLamps);
            bool sameSignals = first.TrafficSignals.SequenceEqual(
                second.TrafficSignals);
            Assert.That(sameLamps && sameSignals, Is.False);
        }

        [Test]
        public void CreatePlan_WithNullLayout_Throws()
        {
            Assert.That(
                () => CityNightFixturePlanner.CreatePlan(null),
                Throws.ArgumentNullException);
        }

        private static void AssertLampFacesRoad(
            CityLayout layout,
            StreetLampDescriptor lamp)
        {
            Vector3 start = layout.GetNodeWorldPosition(lamp.Edge.A);
            Vector3 end = layout.GetNodeWorldPosition(lamp.Edge.B);
            Vector3 centerline = Vector3.Lerp(start, end, lamp.EdgeT);
            Vector3 toRoad = (centerline - lamp.Position).normalized;
            float expectedOffset = (layout.RoadWidth * 0.5f) + 0.75f;

            Assert.That(
                Vector3.Distance(centerline, lamp.Position),
                Is.EqualTo(expectedOffset).Within(PositionTolerance));
            Assert.That(
                lamp.Forward.sqrMagnitude,
                Is.EqualTo(1f).Within(PositionTolerance));
            Assert.That(
                Vector3.Dot(lamp.Forward, toRoad),
                Is.EqualTo(1f).Within(PositionTolerance));
        }

        private static void AssertSignalFacesIntersection(
            Vector3 nodePosition,
            TrafficSignalDescriptor signal)
        {
            Vector3 toNode = (nodePosition - signal.Position).normalized;
            Assert.That(
                signal.Forward.sqrMagnitude,
                Is.EqualTo(1f).Within(PositionTolerance));
            Assert.That(
                Vector3.Dot(signal.Forward, toNode),
                Is.EqualTo(1f).Within(PositionTolerance));
        }

        private static void AssertOutsidePublicSpaceReservations(
            CityLayout layout,
            Vector3 position,
            float clearance)
        {
            Vector2 point = new Vector2(position.x, position.z);
            foreach (CityDistrictPointOfInterestDescriptor pointOfInterest
                     in layout.DistrictPointsOfInterest)
            {
                Assert.That(
                    ContainsExpanded(
                        pointOfInterest.PublicBounds,
                        point,
                        clearance),
                    Is.False,
                    $"Fixture at {position} overlaps '{pointOfInterest.Id}'.");
                foreach (
                    CityDistrictPointOfInterestAccessDescriptor access
                    in pointOfInterest.Accesses)
                {
                    Assert.That(
                        ContainsExpanded(
                            access.ApproachBounds,
                            point,
                            clearance),
                        Is.False,
                        $"Fixture at {position} blocks '{access.Id}'.");
                }
            }
        }

        private static bool ContainsExpanded(
            Rect bounds,
            Vector2 point,
            float expansion)
        {
            return point.x >= bounds.xMin - expansion &&
                   point.x <= bounds.xMax + expansion &&
                   point.y >= bounds.yMin - expansion &&
                   point.y <= bounds.yMax + expansion;
        }

        private static Dictionary<Vector2Int, int> CountDegrees(
            CityLayout layout)
        {
            Dictionary<Vector2Int, int> degrees = layout.Nodes.ToDictionary(
                node => node,
                _ => 0);
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                degrees[edge.A] = degrees[edge.A] + 1;
                degrees[edge.B] = degrees[edge.B] + 1;
            }

            return degrees;
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
