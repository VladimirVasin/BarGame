using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class YardWheelchairMotionTests
    {
        [Test]
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

            // The ring the rider follows is the ring worn into the ground.
            foreach (CityOpenAreaDecorationDescriptor segment in
                     decorations.Descriptors)
            {
                if (!segment.StableId.StartsWith(
                        YardWheelchairPlan.RingIdPrefix))
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    new Vector2(plan.Center.x, plan.Center.z),
                    new Vector2(
                        segment.Bounds.center.x,
                        segment.Bounds.center.z));
                Assert.That(
                    distance,
                    Is.EqualTo(plan.Radius).Within(0.05f),
                    segment.StableId);
            }
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
