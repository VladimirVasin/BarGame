using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RoadFencePlannerTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void CreatePlan_WithSameLayout_ProducesIdenticalPlan()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);

            RoadFencePlan first =
                RoadFencePlanner.CreatePlan(layout);
            RoadFencePlan second =
                RoadFencePlanner.CreatePlan(layout);

            CollectionAssert.AreEqual(
                first.Segments,
                second.Segments);
            CollectionAssert.AreEqual(
                first.EntranceOpenings,
                second.EntranceOpenings);
        }

        [Test]
        public void CreatePlan_SegmentsTraceOnlyRoadUnionBoundary()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.LoopChance = 1f;
            settings.BarCount = 8;
            CityLayout layout =
                CityLayoutGenerator.Generate(settings, -4119);
            IReadOnlyList<Rect> roads =
                layout.CreateRoadRects();

            RoadFencePlan plan =
                RoadFencePlanner.CreatePlan(layout);

            Assert.That(plan.Segments, Is.Not.Empty);
            foreach (RoadFenceSegmentDescriptor segment
                     in plan.Segments)
            {
                Assert.That(
                    segment.Length,
                    Is.GreaterThan(Tolerance));
                Assert.That(
                    IsCardinal(segment.OutwardNormal),
                    Is.True);

                Vector3 inward =
                    segment.Center -
                    (segment.OutwardNormal * 0.02f);
                Vector3 outward =
                    segment.Center +
                    (segment.OutwardNormal * 0.02f);
                Assert.That(
                    IsInsideAnyRoad(roads, inward),
                    Is.True,
                    $"Fence interior sample failed at {segment.Center}.");
                Assert.That(
                    IsInsideAnyRoad(roads, outward),
                    Is.False,
                    $"Fence exterior sample failed at {segment.Center}.");
            }
        }

        [Test]
        public void CreatePlan_SparseRoadsCoverEveryExposedBoundary()
        {
            int[] seeds =
            {
                -99123,
                -4119,
                0,
                101,
                202,
                8844,
                71923
            };
            for (int seedIndex = 0;
                 seedIndex < seeds.Length;
                 seedIndex++)
            {
                CityGenerationSettings settings =
                    CityGenerationSettings.Default;
                settings.BarCount = 0;
                settings.LoopChance = 0f;
                CityLayout layout =
                    CityLayoutGenerator.Generate(
                        settings,
                        seeds[seedIndex]);
                IReadOnlyList<Rect> roads =
                    layout.CreateRoadRects();
                RoadFencePlan plan =
                    RoadFencePlanner.CreatePlan(layout);

                for (int roadIndex = 0;
                     roadIndex < roads.Count;
                     roadIndex++)
                {
                    Rect road = roads[roadIndex];
                    AssertSideCoverage(
                        plan,
                        roads,
                        true,
                        road.yMin,
                        road.xMin,
                        road.xMax,
                        Vector3.back,
                        seeds[seedIndex]);
                    AssertSideCoverage(
                        plan,
                        roads,
                        true,
                        road.yMax,
                        road.xMin,
                        road.xMax,
                        Vector3.forward,
                        seeds[seedIndex]);
                    AssertSideCoverage(
                        plan,
                        roads,
                        false,
                        road.xMin,
                        road.yMin,
                        road.yMax,
                        Vector3.left,
                        seeds[seedIndex]);
                    AssertSideCoverage(
                        plan,
                        roads,
                        false,
                        road.xMax,
                        road.yMin,
                        road.yMax,
                        Vector3.right,
                        seeds[seedIndex]);
                }
            }
        }

        [Test]
        public void CreatePlan_DenseSingleBlockHasOnlyRingPerimeter()
        {
            CityGenerationSettings settings =
                CreateSingleBlockSettings(0);
            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 4125);

            RoadFencePlan plan =
                RoadFencePlanner.CreatePlan(layout);

            float outerWidth =
                layout.NodeSpacing.x + layout.RoadWidth;
            float outerDepth =
                layout.NodeSpacing.y + layout.RoadWidth;
            float innerWidth =
                layout.NodeSpacing.x - layout.RoadWidth;
            float innerDepth =
                layout.NodeSpacing.y - layout.RoadWidth;
            float expectedLength = 2f *
                (outerWidth + outerDepth + innerWidth + innerDepth);

            Assert.That(plan.EntranceOpenings, Is.Empty);
            Assert.That(plan.Segments, Has.Count.EqualTo(8));
            Assert.That(
                plan.Segments.Sum(segment => segment.Length),
                Is.EqualTo(expectedLength).Within(Tolerance));
        }

        [Test]
        public void CreatePlan_SingleBarCutsOneFullEntranceOpening()
        {
            CityGenerationSettings noBarSettings =
                CreateSingleBlockSettings(0);
            CityGenerationSettings barSettings =
                CreateSingleBlockSettings(1);
            CityLayout noBarLayout =
                CityLayoutGenerator.Generate(noBarSettings, 7751);
            CityLayout barLayout =
                CityLayoutGenerator.Generate(barSettings, 7751);

            RoadFencePlan closed =
                RoadFencePlanner.CreatePlan(noBarLayout);
            RoadFencePlan opened =
                RoadFencePlanner.CreatePlan(barLayout);

            Assert.That(opened.EntranceOpenings, Has.Count.EqualTo(1));
            Assert.That(opened.Segments, Has.Count.EqualTo(9));
            Assert.That(
                opened.Segments.Sum(segment => segment.Length),
                Is.EqualTo(
                    closed.Segments.Sum(segment => segment.Length) -
                    BarEntranceGeometry.FenceOpeningWidth)
                .Within(Tolerance));
        }

        [Test]
        public void CreatePlan_LeavesClearOpeningForEveryBar()
        {
            int[] seeds = { -47, 0, 8844, 91275 };
            for (int seedIndex = 0;
                 seedIndex < seeds.Length;
                 seedIndex++)
            {
                CityGenerationSettings settings =
                    CityGenerationSettings.Default;
                settings.BarCount =
                    settings.BlocksX * settings.BlocksZ;
                CityLayout layout =
                    CityLayoutGenerator.Generate(
                        settings,
                        seeds[seedIndex]);
                RoadFencePlan plan =
                    RoadFencePlanner.CreatePlan(layout);
                BuildingLot[] bars = layout.BuildingLots
                    .Where(lot => lot.IsBar)
                    .ToArray();

                Assert.That(
                    plan.EntranceOpenings,
                    Has.Count.EqualTo(bars.Length));
                foreach (BuildingLot bar in bars)
                {
                    RoadFenceOpeningDescriptor opening =
                        plan.EntranceOpenings.Single(
                            candidate =>
                                candidate.BarId == bar.BarId);
                    Vector3 frontage = new Vector3(
                        bar.FrontageDirection.x,
                        0f,
                        bar.FrontageDirection.y);
                    Vector3 expectedOutward = -frontage;
                    Vector3 expectedCenter =
                        bar.ReturnPosition +
                        (expectedOutward *
                         (layout.RoadWidth * 0.5f));

                    Assert.That(
                        opening.Center,
                        Is.EqualTo(expectedCenter));
                    Assert.That(
                        opening.OutwardNormal,
                        Is.EqualTo(expectedOutward));
                    Assert.That(
                        opening.Width,
                        Is.GreaterThanOrEqualTo(
                            BarEntranceGeometry.WalkwayWidth));
                    AssertOpeningHasNoFence(plan, opening);
                }
            }
        }

        [Test]
        public void CreatePlan_WithNullLayout_Throws()
        {
            Assert.That(
                () => RoadFencePlanner.CreatePlan(null),
                Throws.ArgumentNullException);
        }

        private static void AssertOpeningHasNoFence(
            RoadFencePlan plan,
            RoadFenceOpeningDescriptor opening)
        {
            bool hasFenceBefore = false;
            bool hasFenceAfter = false;

            foreach (RoadFenceSegmentDescriptor segment
                     in plan.Segments)
            {
                if (segment.IsHorizontal != opening.IsHorizontal ||
                    Mathf.Abs(
                        segment.FixedCoordinate -
                        opening.FixedCoordinate) > Tolerance ||
                    (segment.OutwardNormal -
                     opening.OutwardNormal).sqrMagnitude >
                    Tolerance * Tolerance)
                {
                    continue;
                }

                float overlap = Mathf.Min(
                                    segment.MaximumCoordinate,
                                    opening.MaximumCoordinate) -
                                Mathf.Max(
                                    segment.MinimumCoordinate,
                                    opening.MinimumCoordinate);
                Assert.That(
                    overlap,
                    Is.LessThanOrEqualTo(Tolerance),
                    $"Fence overlaps entrance {opening.BarId}.");
                hasFenceBefore |=
                    segment.MaximumCoordinate <=
                    opening.MinimumCoordinate + Tolerance;
                hasFenceAfter |=
                    segment.MinimumCoordinate >=
                    opening.MaximumCoordinate - Tolerance;
            }

            Assert.That(
                hasFenceBefore,
                Is.True,
                $"No fence borders the first side of {opening.BarId}.");
            Assert.That(
                hasFenceAfter,
                Is.True,
                $"No fence borders the second side of {opening.BarId}.");
        }

        private static void AssertSideCoverage(
            RoadFencePlan plan,
            IReadOnlyList<Rect> roads,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            Vector3 outwardNormal,
            int seed)
        {
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maximum - minimum) / 0.35f));
            for (int sample = 0;
                 sample < sampleCount;
                 sample++)
            {
                float coordinate = Mathf.Lerp(
                    minimum,
                    maximum,
                    (sample + 0.5f) / sampleCount);
                Vector3 boundaryPoint = horizontal
                    ? new Vector3(
                        coordinate,
                        0f,
                        fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate,
                        0f,
                        coordinate);
                if (!IsInsideAnyRoad(
                        roads,
                        boundaryPoint -
                        (outwardNormal * 0.02f)) ||
                    IsInsideAnyRoad(
                        roads,
                        boundaryPoint +
                        (outwardNormal * 0.02f)))
                {
                    continue;
                }

                bool covered = plan.Segments.Any(segment =>
                    segment.IsHorizontal == horizontal &&
                    Mathf.Abs(
                        segment.FixedCoordinate -
                        fixedCoordinate) <= Tolerance &&
                    (segment.OutwardNormal -
                     outwardNormal).sqrMagnitude <=
                    Tolerance * Tolerance &&
                    coordinate >=
                    segment.MinimumCoordinate - Tolerance &&
                    coordinate <=
                    segment.MaximumCoordinate + Tolerance);
                Assert.That(
                    covered,
                    Is.True,
                    $"Missing fence at {boundaryPoint} " +
                    $"for sparse seed {seed}.");
            }
        }

        private static bool IsInsideAnyRoad(
            IReadOnlyList<Rect> roads,
            Vector3 point)
        {
            for (int index = 0; index < roads.Count; index++)
            {
                Rect road = roads[index];
                if (point.x > road.xMin &&
                    point.x < road.xMax &&
                    point.z > road.yMin &&
                    point.z < road.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCardinal(Vector3 direction)
        {
            bool horizontal =
                Mathf.Abs(Mathf.Abs(direction.x) - 1f) <=
                Tolerance &&
                Mathf.Abs(direction.z) <= Tolerance;
            bool vertical =
                Mathf.Abs(Mathf.Abs(direction.z) - 1f) <=
                Tolerance &&
                Mathf.Abs(direction.x) <= Tolerance;
            return Mathf.Abs(direction.y) <= Tolerance &&
                   (horizontal || vertical);
        }

        private static CityGenerationSettings
            CreateSingleBlockSettings(int barCount)
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlocksX = 1;
            settings.BlocksZ = 1;
            settings.BarCount = barCount;
            settings.LoopChance = 1f;
            return settings;
        }
    }
}
