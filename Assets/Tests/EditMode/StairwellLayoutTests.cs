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

        [TestCase(false)]
        [TestCase(true)]
        public void FixedCameraSelector_UsesHeightBandsWithHysteresis(bool corrupted)
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate(corrupted);
            var selector = new StairwellCameraShotSelector(
                StairwellFixedCameraController
                    .CreateDefaultShots(plan));

            if (corrupted)
            {
                AssertShot(selector, plan.ApartmentSpawn, StairwellCameraShotKind.ApartmentLanding);
                AssertShot(selector, new Vector3(-1.45f, 4.30f, -.5f), StairwellCameraShotKind.ApartmentLanding);
                AssertShot(selector, new Vector3(-1.45f, 4.34f, -.4f), StairwellCameraShotKind.UpperFlight);
                AssertShot(selector, new Vector3(-1.45f, 4.84f, 1.02f), StairwellCameraShotKind.UpperFlight);
                AssertShot(selector, new Vector3(-1.45f, 4.84f, 1.10f), StairwellCameraShotKind.UpperLanding);
                AssertShot(selector, new Vector3(-1.45f, 4.84f, .90f), StairwellCameraShotKind.UpperLanding);
                AssertShot(selector, new Vector3(-1.45f, 4.80f, .72f), StairwellCameraShotKind.UpperFlight);
                AssertShot(selector, new Vector3(-1.45f, 4.20f, -.74f), StairwellCameraShotKind.UpperFlight);
                AssertShot(selector, new Vector3(-1.45f, 4.16f, -.83f), StairwellCameraShotKind.ApartmentLanding);
                AssertShot(selector, plan.ApartmentDoorActionRoot, StairwellCameraShotKind.ApartmentLanding);
                return;
            }

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

        [TestCase(16f / 9f)]
        [TestCase(4f / 3f)]
        public void CorruptedFixedCameras_FrameDoorRouteAndUpperLanding(float aspect)
        {
            StairwellLayoutPlan plan = StairwellLayoutPlanner.Generate(true);
            var selector = new StairwellCameraShotSelector(
                StairwellFixedCameraController.CreateDefaultShots(plan));
            var cameraObject = new GameObject("Corrupted Stairwell Framing Check");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.aspect = aspect;
            try
            {
                foreach (Vector3 root in new[]
                {
                    plan.ApartmentSpawn, plan.ApartmentDoorActionRoot,
                    new Vector3(-2f, 3.24f, -4.2f),
                    new Vector3(-1.45f, 4.30f, -.5f),
                    new Vector3(-1.45f, 4.34f, -.4f),
                    new Vector3(-1.45f, 4.84f, 1.02f),
                    new Vector3(-1.90f, 4.84f, 1.10f),
                    new Vector3(-1f, 4.84f, 1.87f),
                    new Vector3(-1.45f, 4.80f, .72f),
                    new Vector3(-1.45f, 4.16f, -.83f),
                    plan.ApartmentDoorActionRoot
                })
                {
                    StairwellCameraShot shot = selector.Select(root);
                    Vector2 offset = shot.Focus.Resolve(shot.Position, shot.Rotation,
                        shot.FieldOfView, aspect, root);
                    camera.transform.SetPositionAndRotation(shot.Position,
                        FixedCameraFocus.Compose(shot.Rotation, offset));
                    camera.fieldOfView = shot.FieldOfView;
                    foreach (Vector3 point in new[]
                    {
                        root + Vector3.up * FixedCameraFocus.BodyLowerHeight,
                        root + Vector3.up * FixedCameraFocus.BodyUpperHeight,
                        root + Vector3.up * FixedCameraFocus.BodyAimHeight + Vector3.right * .32f,
                        root + Vector3.up * FixedCameraFocus.BodyAimHeight - Vector3.right * .32f
                    })
                    {
                        Vector3 projected = camera.WorldToViewportPoint(point);
                        Assert.That(projected.z, Is.GreaterThan(0f), $"{shot.Kind} at {root}");
                        Assert.That(projected.x, Is.InRange(.03f, .97f), $"{shot.Kind} at {root}");
                        Assert.That(projected.y, Is.InRange(.03f, .97f), $"{shot.Kind} at {root}");
                    }
                }
            }
            finally { Object.DestroyImmediate(cameraObject); }
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
