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
                first.Openings,
                second.Openings);
        }

        [Test]
        public void CreatePlan_SegmentsTraceOnlyRoadUnionBoundary()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.LoopChance = 1f;
            settings.BarCount = 8;
            settings.MinimumBarRouteDistance = 0f;
            CityLayout layout =
                CityLayoutGenerator.Generate(settings, -4119);
            IReadOnlyList<Rect> roads =
                layout.CreateStreetRects();

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
                    layout.CreateStreetRects();
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

            Assert.That(plan.Openings, Is.Empty);
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
                settings.BarCount = 12;
                settings.MinimumBarRouteDistance = 0f;
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
        public void CreatePlan_LeavesFourClearParkGateOpenings()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);

            RoadFencePlan plan =
                RoadFencePlanner.CreatePlan(layout);

            Assert.That(layout.Park.IsEnabled, Is.True);
            Assert.That(layout.Park.Gates, Has.Count.EqualTo(4));
            Assert.That(plan.ParkGateOpenings, Has.Count.EqualTo(4));
            Assert.That(
                plan.Openings.Count,
                Is.EqualTo(
                    plan.EntranceOpenings.Count +
                    plan.ParkGateOpenings.Count +
                    plan.PlayerHomeOpenings.Count +
                    plan.PublicSpaceOpenings.Count +
                    plan.SupermarketOpenings.Count +
                    plan.OpenAreaAccessOpenings.Count));
            foreach (CityParkGateDescriptor gate
                     in layout.Park.Gates)
            {
                RoadFenceOpeningDescriptor opening =
                    plan.ParkGateOpenings.Single(
                        candidate =>
                            candidate.ParkGateId == gate.Id);

                Assert.That(
                    opening.Kind,
                    Is.EqualTo(RoadFenceOpeningKind.ParkGate));
                Assert.That(opening.BarId, Is.Empty);
                Assert.That(opening.Center, Is.EqualTo(gate.Center));
                Assert.That(
                    opening.OutwardNormal,
                    Is.EqualTo(gate.OutwardNormal));
                Assert.That(opening.Width, Is.EqualTo(gate.Width));
                AssertOpeningHasNoFence(plan, opening);
            }
        }

        [Test]
        public void CreatePlan_RemovesFenceAcrossEveryPublicStreetSide()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);
            int expectedOpeningCount =
                layout.DistrictPointsOfInterest.Sum(
                    point => point.Accesses.Count);

            Assert.That(
                plan.PublicSpaceOpenings,
                Has.Count.EqualTo(expectedOpeningCount));
            foreach (CityDistrictPointOfInterestDescriptor point
                     in layout.DistrictPointsOfInterest)
            {
                foreach (
                    CityDistrictPointOfInterestAccessDescriptor access
                    in point.Accesses)
                {
                    RoadFenceOpeningDescriptor opening =
                        plan.PublicSpaceOpenings.Single(
                            candidate => candidate.Id == access.Id);
                    float fullSideWidth =
                        access.StreetSideDirection.x != 0
                            ? point.PublicBounds.height
                            : point.PublicBounds.width;

                    Assert.That(
                        opening.Kind,
                        Is.EqualTo(
                            RoadFenceOpeningKind
                                .DistrictPointOfInterest));
                    Assert.That(
                        opening.DistrictPointOfInterestId,
                        Is.EqualTo(access.Id));
                    Assert.That(opening.Center, Is.EqualTo(access.Center));
                    Assert.That(
                        opening.OutwardNormal,
                        Is.EqualTo(access.OutwardNormal));
                    Assert.That(
                        opening.Width,
                        Is.EqualTo(fullSideWidth).Within(Tolerance));
                    AssertOpeningHasNoFenceOverlap(plan, opening);
                }
            }
        }

        [Test]
        public void CreatePlan_LeavesClearOpeningForPlayerHome()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan =
                RoadFencePlanner.CreatePlan(layout);

            Assert.That(layout.PlayerHome, Is.Not.Null);
            Assert.That(
                plan.PlayerHomeOpenings,
                Has.Count.EqualTo(1));
            RoadFenceOpeningDescriptor opening =
                plan.PlayerHomeOpenings[0];
            Vector3 frontage = new Vector3(
                layout.PlayerHome.FrontageDirection.x,
                0f,
                layout.PlayerHome.FrontageDirection.y);
            Vector3 expectedOutward = -frontage;
            Vector3 expectedCenter =
                layout.PlayerHome.ReturnPosition +
                (expectedOutward *
                 (layout.RoadWidth * 0.5f));

            Assert.That(
                opening.Kind,
                Is.EqualTo(
                    RoadFenceOpeningKind.PlayerHomeEntrance));
            Assert.That(
                opening.PlayerHomeId,
                Is.EqualTo("player-home"));
            Assert.That(opening.Center, Is.EqualTo(expectedCenter));
            Assert.That(
                opening.Width,
                Is.GreaterThanOrEqualTo(
                    PlayerHomeEntranceGeometry.WalkwayWidth));
            AssertOpeningHasNoFence(plan, opening);
        }

        [Test]
        public void Build_CreatesBoundedColliderFreeSpatialChunks()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan =
                RoadFencePlanner.CreatePlan(layout);
            var parent = new GameObject("Fence Test Parent");

            try
            {
                GameObject fenceRoot =
                    RoadFenceWorldBuilder.Build(
                        parent.transform,
                        plan);

                Assert.That(
                    fenceRoot.transform.childCount,
                    Is.GreaterThan(1));
                for (int index = 0;
                     index < fenceRoot.transform.childCount;
                     index++)
                {
                    Transform chunk =
                        fenceRoot.transform.GetChild(index);
                    Renderer[] renderers =
                        chunk.GetComponentsInChildren<Renderer>(
                            true);
                    Assert.That(
                        renderers.Length,
                        Is.InRange(1, 2),
                        chunk.name);
                    foreach (Renderer renderer in renderers)
                    {
                        Assert.That(
                            renderer.bounds.size.x,
                            Is.LessThanOrEqualTo(49f),
                            $"{chunk.name} is too wide.");
                        Assert.That(
                            renderer.bounds.size.z,
                            Is.LessThanOrEqualTo(49f),
                            $"{chunk.name} is too deep.");
                    }
                }

                Assert.That(
                    fenceRoot.GetComponentsInChildren<Collider>(
                        true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
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
                    $"Fence overlaps opening {opening.Id}.");
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
                $"No fence borders the first side of {opening.Id}.");
            Assert.That(
                hasFenceAfter,
                Is.True,
                $"No fence borders the second side of {opening.Id}.");
        }

        private static void AssertOpeningHasNoFenceOverlap(
            RoadFencePlan plan,
            RoadFenceOpeningDescriptor opening)
        {
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
                    $"Fence overlaps opening {opening.Id}.");
            }
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

                bool liesInsideOpening =
                    plan.Openings.Any(opening =>
                        opening.IsHorizontal == horizontal &&
                        Mathf.Abs(
                            opening.FixedCoordinate -
                            fixedCoordinate) <= Tolerance &&
                        (opening.OutwardNormal -
                         outwardNormal).sqrMagnitude <=
                        Tolerance * Tolerance &&
                        coordinate >=
                        opening.MinimumCoordinate - Tolerance &&
                        coordinate <=
                        opening.MaximumCoordinate + Tolerance);
                if (liesInsideOpening)
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
            settings.MinimumBarRouteDistance = 0f;
            settings.LoopChance = 1f;
            return settings;
        }
    }
}
