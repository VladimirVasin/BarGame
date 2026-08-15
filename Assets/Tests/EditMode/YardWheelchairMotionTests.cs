using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class YardWheelchairMotionTests
    {
        [Test]
        [Category("CityTraversal")]
        public void Plan_ReadsTheAuthoredCircuitFromTheYardDressing()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityOpenAreaDecorationPlan decorations =
                CityOpenAreaDecorationPlanner.Create(layout);

            YardWheelchairPlan plan =
                YardWheelchairPlan.Create(decorations);

            Assert.That(plan.IsPresent, Is.True);
            // The circuit fits the gap between the two buildings, so the
            // radius follows the ground rather than a fixed number.
            Assert.That(plan.Radius, Is.InRange(3.5f, 6.5f));

            CityOpenAreaDecorationDescriptor trunk =
                decorations.Descriptors.First(descriptor =>
                    descriptor.StableId ==
                    YardWheelchairPlan.TreeTrunkId);
            Assert.That(
                plan.Center.x,
                Is.EqualTo(trunk.Bounds.center.x).Within(0.001f));
            Assert.That(
                plan.Center.z,
                Is.EqualTo(trunk.Bounds.center.z).Within(0.001f));
            Assert.That(
                plan.GroundY,
                Is.EqualTo(trunk.Bounds.min.y).Within(0.001f));

            // Nothing is drawn for the lap any more: the circuit is the
            // yard site contract's authored ring, ridden on bare ground.
            Assert.That(decorations.HomeYardSite.HasValue, Is.True);
            HomeYardSitePlan site = decorations.HomeYardSite.Value;
            Assert.That(
                plan.Radius,
                Is.EqualTo(site.RingRadius).Within(0.001f));
            Assert.That(
                Vector3.Distance(plan.Center, site.RingCenter),
                Is.LessThan(0.001f));
            Assert.That(
                decorations.Descriptors.Any(descriptor =>
                    descriptor.StableId.StartsWith("home-yard-ring-")),
                Is.False,
                "The worn ring geometry must stay removed.");

            // With the elevation plan the lap carries the real ground:
            // every probed angle must match the sampled terrace datum,
            // so the chair can never hover over a lower terrace.
            YardWheelchairPlan elevated = YardWheelchairPlan.Create(
                decorations,
                layout.ElevationPlan);
            Assert.That(elevated.IsPresent, Is.True);
            for (int probe = 0; probe < 8; probe++)
            {
                float angle = Mathf.PI * 2f * probe / 8f;
                var point = new Vector2(
                    site.RingCenter.x +
                    Mathf.Cos(angle) * site.RingRadius,
                    site.RingCenter.z +
                    Mathf.Sin(angle) * site.RingRadius);
                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        point,
                        out float groundTop,
                        out _),
                    Is.True,
                    $"probe {probe}");
                Assert.That(
                    elevated.SampleGroundHeight(angle),
                    Is.EqualTo(groundTop).Within(0.001f),
                    $"probe {probe}");
            }
        }

        [Test]
        public void Sample_FollowsTheSampledGroundProfile()
        {
            var heights = new float[8];
            for (int index = 0; index < heights.Length; index++)
            {
                heights[index] = 2f + ((index & 1) == 0 ? 0f : 0.4f);
            }

            var plan = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                2f,
                true,
                heights);

            bool leftThePlane = false;
            for (int step = 0; step <= 48; step++)
            {
                float distance = plan.LapLength * step / 48f;
                YardWheelchairPose pose =
                    YardWheelchairMotion.Sample(plan, distance);
                float angle = -distance / 6f;
                Assert.That(
                    pose.Position.y,
                    Is.EqualTo(plan.SampleGroundHeight(angle))
                        .Within(0.0001f),
                    $"step {step}");
                Assert.That(
                    pose.Position.y,
                    Is.InRange(2f, 2.4f),
                    $"step {step}");
                leftThePlane |= pose.Position.y > 2.1f;
            }

            Assert.That(
                leftThePlane,
                Is.True,
                "The lap must actually ride the profile, not the plane.");
        }

        [Test]
        public void Provider_BindsTheStagedPrefabWithoutPublishingIt()
        {
            YardWheelchairProvider provider =
                YardWheelchairProvider.Load();

            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.StagedPrefab, Is.Not.Null);

            var wheelchairRegistry = provider.StagedPrefab
                .GetComponentInChildren<CityWheelchairNpcAssetRegistry>(
                    true);
            Assert.That(wheelchairRegistry, Is.Not.Null);
            Assert.That(
                wheelchairRegistry.LeftWheelPivot,
                Is.Not.Null);
            Assert.That(
                wheelchairRegistry.RightWheelPivot,
                Is.Not.Null);
            Assert.That(
                wheelchairRegistry.LeftCasterPivot,
                Is.Not.Null);
            Assert.That(
                wheelchairRegistry.RightCasterPivot,
                Is.Not.Null);
            Assert.That(wheelchairRegistry.BellowsPivot, Is.Not.Null);
            Assert.That(wheelchairRegistry.PipeBankPivot, Is.Not.Null);

            CityPedestrianAssetRegistry pedestrianRegistry =
                wheelchairRegistry.PedestrianRegistry;
            Assert.That(pedestrianRegistry, Is.Not.Null);
            Assert.That(
                pedestrianRegistry.DesignId,
                Is.EqualTo(YardWheelchairProvider.DesignId));
            Assert.That(
                pedestrianRegistry.IdleClip,
                Is.Not.Null);
            Assert.That(
                pedestrianRegistry.WalkClip,
                Is.Not.Null);
            Assert.That(pedestrianRegistry.SitClip, Is.Null);
            Assert.That(pedestrianRegistry.Animator, Is.Not.Null);
            Assert.That(
                pedestrianRegistry.Animator.runtimeAnimatorController,
                Is.Null);

            // The staged NPC stays out of the ambient pool: it must not be
            // reachable by path, and it must not be in the catalog.
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/PipebackRoller3D"),
                Is.Null);
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    YardWheelchairProvider.DesignId,
                    out _),
                Is.False);
        }

        [Test]
        public void Sample_HoldsTheCircuitAndDriftsAcrossIt()
        {
            var plan = new YardWheelchairPlan(
                new Vector3(10f, 0f, -4f),
                6f,
                1.5f,
                true);

            for (int step = 0; step <= 32; step++)
            {
                float distance = plan.LapLength * step / 32f;
                YardWheelchairPose pose =
                    YardWheelchairMotion.Sample(plan, distance);

                float radius = Vector2.Distance(
                    new Vector2(plan.Center.x, plan.Center.z),
                    new Vector2(pose.Position.x, pose.Position.z));
                Assert.That(
                    radius,
                    Is.EqualTo(plan.Radius)
                        .Within(YardWheelchairMotion.RadiusWander + 0.001f),
                    $"step {step}");
                Assert.That(
                    pose.Position.y,
                    Is.EqualTo(plan.GroundY).Within(0.001f));

                // The chassis never points where it is going: that gap is
                // the drift.
                float slip = Mathf.Abs(pose.SlipDegrees);
                Assert.That(
                    slip,
                    Is.GreaterThan(
                        YardWheelchairMotion.BaseSlipDegrees -
                        YardWheelchairMotion.SlipBreathDegrees - 0.001f),
                    $"step {step}");
                Assert.That(
                    slip,
                    Is.LessThan(
                        YardWheelchairMotion.BaseSlipDegrees +
                        YardWheelchairMotion.SlipBreathDegrees + 0.001f));
                Assert.That(pose.Speed, Is.GreaterThan(0.5f));
            }
        }

        [Test]
        public void Sample_DriftsIntoTheCircleAndFlipsWithDirection()
        {
            var clockwise = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                0f,
                true);
            var counterClockwise = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                0f,
                false);

            YardWheelchairPose first =
                YardWheelchairMotion.Sample(clockwise, 3f);
            YardWheelchairPose second =
                YardWheelchairMotion.Sample(counterClockwise, 3f);

            Assert.That(
                Mathf.Sign(first.SlipDegrees),
                Is.Not.EqualTo(Mathf.Sign(second.SlipDegrees)));
            Assert.That(
                first.SlipDegrees,
                Is.EqualTo(-second.SlipDegrees).Within(0.001f));
        }

        [Test]
        public void Advance_ReturnsToTheStartAfterOneLap()
        {
            var plan = new YardWheelchairPlan(
                new Vector3(-3f, 0f, 12f),
                6f,
                0.75f,
                true);
            YardWheelchairPose start =
                YardWheelchairMotion.Sample(plan, 0f);

            float distance = 0f;
            float elapsed = 0f;
            const float step = 1f / 60f;
            while (elapsed < 240f)
            {
                float next = YardWheelchairMotion.Advance(
                    plan,
                    distance,
                    step);
                elapsed += step;
                // Advance wraps at the lap, so a drop means a full circuit.
                if (next < distance)
                {
                    distance = next;
                    break;
                }

                distance = next;
            }

            Assert.That(
                elapsed,
                Is.LessThan(240f),
                "The rider must complete a lap in a sane time.");
            YardWheelchairPose wrapped =
                YardWheelchairMotion.Sample(plan, distance);
            Assert.That(
                Vector3.Distance(wrapped.Position, start.Position),
                Is.LessThan(0.4f));
        }

        [Test]
        public void Advance_IgnoresNonPositiveSteps()
        {
            var plan = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                0f,
                true);

            Assert.That(
                YardWheelchairMotion.Advance(plan, 4f, 0f),
                Is.EqualTo(4f));
            Assert.That(
                YardWheelchairMotion.Advance(plan, 4f, float.NaN),
                Is.EqualTo(4f));
        }

        [Test]
        public void Sample_PulsesLikeHandPushes()
        {
            var plan = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                0f,
                true);

            float minSpeed = float.PositiveInfinity;
            float maxSpeed = 0f;
            for (int step = 0; step <= 200; step++)
            {
                float distance = 10f +
                                 YardWheelchairMotion.PushDistance *
                                 step / 200f;
                YardWheelchairPose pose =
                    YardWheelchairMotion.Sample(plan, distance);
                minSpeed = Mathf.Min(minSpeed, pose.Speed);
                maxSpeed = Mathf.Max(maxSpeed, pose.Speed);
                Assert.That(
                    pose.PushPhase,
                    Is.InRange(0f, 1f),
                    $"step {step}");
            }

            // One cycle must read as a push: a real surge, a real sag,
            // but the chair never actually stops.
            Assert.That(maxSpeed / minSpeed, Is.GreaterThan(1.8f));
            Assert.That(minSpeed, Is.GreaterThan(0.5f));

            // The rhythm lives on the ground, so it repeats exactly
            // one push-distance later.
            Assert.That(
                YardWheelchairMotion.Sample(plan, 10f).PushPhase,
                Is.EqualTo(
                    YardWheelchairMotion.Sample(
                        plan,
                        10f + YardWheelchairMotion.PushDistance)
                        .PushPhase)
                    .Within(0.001f));
        }

        [Test]
        public void Presentation_TurnsRealWheelGeometry()
        {
            YardWheelchairProvider provider =
                YardWheelchairProvider.Load();
            GameObject instance =
                Object.Instantiate(provider.StagedPrefab);
            try
            {
                var registry = instance
                    .GetComponentInChildren<
                        CityWheelchairNpcAssetRegistry>(true);
                var presentation = instance.AddComponent<
                    YardWheelchairPresentation>();
                presentation.Initialize(registry);

                // The static chair meshes must now hang under their
                // authored pivots, so a pivot turn moves real geometry.
                AssertAdopted(
                    instance.transform,
                    "ACC_WheelTyre.L",
                    registry.LeftWheelPivot);
                AssertAdopted(
                    instance.transform,
                    "ACC_PushRim.R",
                    registry.RightWheelPivot);
                AssertAdopted(
                    instance.transform,
                    "ACC_CasterTyre.L",
                    registry.LeftCasterPivot);
                AssertAdopted(
                    instance.transform,
                    "ACC_CasterHub.R",
                    registry.RightCasterPivot);

                var plan = new YardWheelchairPlan(
                    Vector3.zero,
                    6f,
                    0f,
                    true);
                Quaternion rest =
                    registry.LeftWheelPivot.localRotation;
                YardWheelchairPose pose =
                    YardWheelchairMotion.Sample(plan, 5f);
                presentation.Advance(1f / 60f, pose, 5f, true);
                Assert.That(
                    Quaternion.Angle(
                        rest,
                        registry.LeftWheelPivot.localRotation),
                    Is.GreaterThan(1f),
                    "Covered distance must turn the wheel pivot.");

                // The roll sign is a regression contract: positive
                // covered ground must apply the flipped spin, or the
                // tyres visibly roll backwards again.
                Assert.That(
                    Quaternion.Angle(
                        registry.LeftWheelPivot.localRotation,
                        rest * Quaternion.Euler(
                            YardWheelchairPresentation.RollSign *
                            pose.LeftWheelSpin,
                            0f,
                            0f)),
                    Is.LessThan(0.1f));
                presentation.Shutdown();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertAdopted(
            Transform root,
            string partName,
            Transform pivot)
        {
            Transform part = root
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == partName);
            Assert.That(part, Is.Not.Null, partName);
            Assert.That(
                part.parent,
                Is.SameAs(pivot),
                $"'{partName}' must hang under '{pivot.name}'.");
        }

        [Test]
        public void Sample_TurnsWheelsFromDistanceWithADifferential()
        {
            var plan = new YardWheelchairPlan(
                Vector3.zero,
                6f,
                0f,
                true);

            YardWheelchairPose pose =
                YardWheelchairMotion.Sample(plan, 12f);

            Assert.That(pose.LeftWheelSpin, Is.GreaterThan(0f));
            Assert.That(pose.RightWheelSpin, Is.GreaterThan(0f));
            // Riding clockwise, the left wheel is the outer one.
            Assert.That(
                pose.LeftWheelSpin,
                Is.GreaterThan(pose.RightWheelSpin));

            YardWheelchairPose later =
                YardWheelchairMotion.Sample(plan, 24f);
            Assert.That(
                later.LeftWheelSpin,
                Is.GreaterThan(pose.LeftWheelSpin));
        }
    }
}
