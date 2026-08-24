using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellLayoutTests
    {
        [Test]
        public void Generate_CreatesConnectedThreeLevelRoute()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();

            Assert.DoesNotThrow(
                () => StairwellLayoutValidator
                    .ValidateOrThrow(plan));
            Assert.That(plan.StreetElevation, Is.Zero);
            Assert.That(plan.MiddleElevation, Is.EqualTo(1.6f));
            Assert.That(
                plan.ApartmentElevation,
                Is.EqualTo(3.2f));
            Assert.That(
                plan.LowerFlight.TopElevation,
                Is.EqualTo(plan.MiddleElevation)
                    .Within(0.001f));
            Assert.That(
                plan.ApartmentFlight.TopElevation,
                Is.EqualTo(plan.ApartmentElevation)
                    .Within(0.001f));
            Assert.That(
                plan.UpperFlight.BaseElevation,
                Is.EqualTo(plan.ApartmentElevation)
                    .Within(0.001f));
        }

        [Test]
        public void Generate_UsesControllerSafeStairsAndSpawns()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            var walkable =
                new RoadWalkableArea(
                    plan.WalkableRectangles);

            Assert.That(
                plan.LowerFlight.StepCount,
                Is.EqualTo(
                    StairwellLayoutPlanner.StepsPerFlight));
            Assert.That(
                plan.ApartmentFlight.StepCount,
                Is.EqualTo(
                    StairwellLayoutPlanner.StepsPerFlight));
            Assert.That(
                plan.UpperFlight.StepCount,
                Is.EqualTo(
                    StairwellLayoutPlanner.StepsPerFlight));
            Assert.That(
                plan.LowerFlight.StepRise,
                Is.LessThanOrEqualTo(
                    StairwellLayoutValidator.MaximumStepRise));
            Assert.That(
                plan.LowerFlight.Width,
                Is.GreaterThanOrEqualTo(
                    StairwellLayoutValidator.MinimumFlightWidth));
            Assert.That(
                walkable.Contains(
                    plan.StreetSpawn,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    plan.ApartmentSpawn,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
        }

        [Test]
        public void Generate_KeepsRadiusSafeRouteContinuousAtFlightSeams()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            var walkable =
                new RoadWalkableArea(
                    plan.WalkableRectangles);
            Vector2[] route =
            {
                new Vector2(-1.45f, -3.78f),
                new Vector2(-1.45f, 1.20f),
                new Vector2(1.45f, 1.20f),
                new Vector2(1.45f, -3.52f),
                new Vector2(3.18f, -3.52f)
            };

            for (int segment = 1;
                 segment < route.Length;
                 segment++)
            {
                Vector2 from = route[segment - 1];
                Vector2 to = route[segment];
                int samples = Mathf.CeilToInt(
                    Vector2.Distance(from, to) / 0.04f);
                for (int sample = 0;
                     sample <= samples;
                     sample++)
                {
                    Vector2 point = Vector2.Lerp(
                        from,
                        to,
                        sample / (float)samples);
                    Assert.That(
                        walkable.Contains(
                            new Vector3(
                                point.x,
                                0f,
                                point.y),
                            StairwellLayoutValidator.PlayerRadius),
                        Is.True,
                        $"Route mask gap at segment {segment}, " +
                        $"sample {sample}: {point}.");
                }
            }
        }

        [Test]
        public void UpperBlocker_SealsWholeUpperFlight()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            Bounds blocker = plan.UpperBlockerBounds;

            Assert.That(
                blocker.min.x,
                Is.LessThanOrEqualTo(
                    plan.UpperFlightBounds.xMin));
            Assert.That(
                blocker.max.x,
                Is.GreaterThanOrEqualTo(
                    plan.UpperFlightBounds.xMax));
            Assert.That(
                blocker.min.y,
                Is.LessThanOrEqualTo(
                    plan.ApartmentElevation));
            Assert.That(
                blocker.max.y,
                Is.GreaterThanOrEqualTo(
                    plan.ApartmentElevation + 1.7f));
            Assert.That(
                blocker.center.z,
                Is.InRange(
                    plan.UpperFlightBounds.yMin,
                    plan.UpperFlightBounds.yMax));
        }

        [Test]
        public void UpperBlocker_LeavesHeadroomOverTheLowerFlight()
        {
            // The blocker seals the upper flight from the apartment floor
            // plane upward, and the lower flight climbs to the middle
            // landing directly beneath it. With a 1.6 m storey and a 1.7 m
            // hero those two are one centimetre from meeting: the blocker
            // used to graze the head of anyone walking down the top
            // treads, and because planar speed is read back from achieved
            // movement, every graze cost him all of it — the descent
            // crawled instead of stopping outright, which is why it read
            // as a flaky test rather than as a wall.
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            Bounds blocker = plan.UpperBlockerBounds;
            StairwellFlightPlan lower = plan.LowerFlight;

            // The hero's crown, plus the controller skin that collides
            // slightly ahead of it, at the highest point of the lower
            // flight that reaches under the blocker. His capsule is a
            // radius wide, so he arrives that far before his feet do.
            const float heroCrown = 1.7f + 0.04f;
            const float capsuleRadius = 0.32f;
            float reachZ = blocker.max.z + capsuleRadius;
            float travelled = Mathf.Clamp(
                reachZ - lower.Start.y,
                0f,
                lower.RunLength);
            float surface = lower.BaseElevation +
                (travelled / lower.RunLength) *
                (lower.TopElevation - lower.BaseElevation);

            Assert.That(
                surface + heroCrown,
                Is.LessThan(blocker.min.y),
                "The upper-flight debris hangs into the head clearance " +
                "of a hero descending the lower flight.");
        }

        [Test]
        public void WorldBuilder_CreatesWalkableRampsAndPhysicalBlocker()
        {
            var parent =
                new GameObject("Stairwell Builder Test");
            try
            {
                StairwellLayoutPlan plan =
                    StairwellLayoutPlanner.Generate();
                StairwellWorldResult world =
                    StairwellWorldBuilder.Build(
                        parent.transform,
                        plan);

                Assert.That(
                    world.StairColliders,
                    Has.Count.EqualTo(3));
                Assert.That(
                    world.StairColliders,
                    Has.All.Property("enabled").True);
                Assert.That(
                    world.UpperBlocker,
                    Is.Not.Null);
                Assert.That(
                    world.UpperBlocker.enabled,
                    Is.True);
                Assert.That(
                    world.UpperBlocker.isTrigger,
                    Is.False);
                Assert.That(world.StreetDoor, Is.Not.Null);
                Assert.That(world.ApartmentDoor, Is.Not.Null);
                Renderer blockerRenderer =
                    world.UpperBlocker.GetComponent<Renderer>();
                Assert.That(blockerRenderer, Is.Not.Null);
                Assert.That(blockerRenderer.enabled, Is.False);
                Assert.That(
                    world.Root.Find(
                        "Stairwell Dressing/" +
                        "Impassable Upper Debris"),
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void GetSpawn_UsesArrivalSide()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();

            Assert.That(
                plan.GetSpawn(
                    StairwellArrivalKind.StreetDoor),
                Is.EqualTo(plan.StreetSpawn));
            Assert.That(
                plan.GetSpawn(
                    StairwellArrivalKind.ApartmentDoor),
                Is.EqualTo(plan.ApartmentSpawn));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => plan.GetSpawn(
                    (StairwellArrivalKind)99));
        }

        [Test]
        public void FixedCameraSelector_UsesHeightBandsWithHysteresis()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            var selector = new StairwellCameraShotSelector(
                StairwellFixedCameraController
                    .CreateDefaultShots(plan));

            AssertShot(
                selector,
                plan.StreetSpawn,
                StairwellCameraShotKind.GroundFlight);
            AssertShot(
                selector,
                new Vector3(0f, 1.54f, 0f),
                StairwellCameraShotKind.GroundFlight);
            AssertShot(
                selector,
                new Vector3(0f, 1.58f, 0f),
                StairwellCameraShotKind.MiddleFlight);
            AssertShot(
                selector,
                new Vector3(0f, 3.12f, 0f),
                StairwellCameraShotKind.MiddleFlight);
            AssertShot(
                selector,
                new Vector3(0f, 3.16f, 0f),
                StairwellCameraShotKind.ApartmentLanding);
            AssertShot(
                selector,
                new Vector3(0f, 3.04f, 0f),
                StairwellCameraShotKind.ApartmentLanding);
            AssertShot(
                selector,
                new Vector3(0f, 3.00f, 0f),
                StairwellCameraShotKind.MiddleFlight);
            AssertShot(
                selector,
                new Vector3(0f, 1.46f, 0f),
                StairwellCameraShotKind.MiddleFlight);
            AssertShot(
                selector,
                new Vector3(0f, 1.42f, 0f),
                StairwellCameraShotKind.GroundFlight);
        }

        private static void AssertShot(
            StairwellCameraShotSelector selector,
            Vector3 position,
            StairwellCameraShotKind expected)
        {
            Assert.That(
                selector.Select(position).Kind,
                Is.EqualTo(expected));
        }
    }
}
