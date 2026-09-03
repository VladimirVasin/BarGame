using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerFootPlacementRulesTests
    {
        private const float MinimumPlant = 0.2f;
        private const float Tolerance = 0.00001f;

        [Test]
        public void FootPlantAmounts_LeftAndRightAlternate()
        {
            // Walk: the left heel lands at cycle 0, the right at 0.5, and at
            // the quarter cycle neither boot is down.
            PlayerFootPlacementRules.FootPlantAmounts(
                0f,
                false,
                MinimumPlant,
                out float left,
                out float right);
            Assert.That(left, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(right, Is.EqualTo(MinimumPlant).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.5f,
                false,
                MinimumPlant,
                out left,
                out right);
            Assert.That(left, Is.EqualTo(MinimumPlant).Within(Tolerance));
            Assert.That(right, Is.EqualTo(1f).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.25f,
                false,
                MinimumPlant,
                out left,
                out right);
            Assert.That(left, Is.EqualTo(MinimumPlant).Within(Tolerance));
            Assert.That(right, Is.EqualTo(MinimumPlant).Within(Tolerance));

            // Run keeps the same order with a flight near 0.375 where
            // neither boot touches the ground.
            PlayerFootPlacementRules.FootPlantAmounts(
                0f,
                true,
                MinimumPlant,
                out left,
                out right);
            Assert.That(left, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(right, Is.EqualTo(MinimumPlant).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.375f,
                true,
                MinimumPlant,
                out left,
                out right);
            Assert.That(left, Is.EqualTo(MinimumPlant).Within(Tolerance));
            Assert.That(right, Is.EqualTo(MinimumPlant).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.5f,
                true,
                MinimumPlant,
                out left,
                out right);
            Assert.That(left, Is.EqualTo(MinimumPlant).Within(Tolerance));
            Assert.That(right, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void FootPlantAmounts_WrapsTheCycleAndClampsTheMinimum()
        {
            PlayerFootPlacementRules.FootPlantAmounts(
                1f,
                false,
                MinimumPlant,
                out float wrappedLeft,
                out float wrappedRight);
            Assert.That(wrappedLeft, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                wrappedRight,
                Is.EqualTo(MinimumPlant).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                -0.5f,
                true,
                MinimumPlant,
                out float negativeLeft,
                out float negativeRight);
            Assert.That(
                negativeLeft,
                Is.EqualTo(MinimumPlant).Within(Tolerance));
            Assert.That(negativeRight, Is.EqualTo(1f).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.25f,
                false,
                1.5f,
                out float clampedLeft,
                out float clampedRight);
            Assert.That(clampedLeft, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(clampedRight, Is.EqualTo(1f).Within(Tolerance));

            PlayerFootPlacementRules.FootPlantAmounts(
                0.25f,
                false,
                -1f,
                out float floorLeft,
                out float floorRight);
            Assert.That(floorLeft, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(floorRight, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void CombinedPlant_MatchesLegacyScalarCurve()
        {
            const int samples = 64;
            for (int index = 0; index < samples; index++)
            {
                float cycle = index / (float)samples;

                PlayerFootPlacementRules.FootPlantAmounts(
                    cycle,
                    false,
                    MinimumPlant,
                    out float walkLeft,
                    out float walkRight);
                float legacyWalk = Mathf.Lerp(
                    MinimumPlant,
                    1f,
                    Mathf.Abs(Mathf.Cos(cycle * Mathf.PI * 2f)));
                Assert.That(
                    PlayerFootPlacementRules.CombinedPlant(walkLeft, walkRight),
                    Is.EqualTo(legacyWalk).Within(Tolerance),
                    $"Walk cycle {cycle}");

                PlayerFootPlacementRules.FootPlantAmounts(
                    cycle,
                    true,
                    MinimumPlant,
                    out float runLeft,
                    out float runRight);
                float halfCycle = Mathf.Repeat(cycle, 0.5f) * 2f;
                float legacyRunPlanted = halfCycle <= 0.75f
                    ? 1f - (halfCycle / 0.75f)
                    : (halfCycle - 0.75f) / 0.25f;
                float legacyRun = Mathf.Lerp(
                    MinimumPlant,
                    1f,
                    legacyRunPlanted);
                Assert.That(
                    PlayerFootPlacementRules.CombinedPlant(runLeft, runRight),
                    Is.EqualTo(legacyRun).Within(Tolerance),
                    $"Run cycle {cycle}");
            }
        }

        [Test]
        public void PelvisDrop_TakesMinimumAndClamps()
        {
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.5f, 0.2f, 0f, false),
                Is.EqualTo(-0.35f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(0.05f, 0.2f, 0f, false),
                Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(0.3f, 0.4f, 0f, false),
                Is.EqualTo(0.12f).Within(Tolerance));

            // Which foot is lower does not matter.
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(0.2f, -0.5f, 0f, false),
                Is.EqualTo(-0.35f).Within(Tolerance));

            // The bounds are parameters.
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(
                    -0.5f,
                    0.2f,
                    0f,
                    false,
                    -0.1f,
                    0.05f),
                Is.EqualTo(-0.1f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(
                    0.3f,
                    0.4f,
                    0f,
                    false,
                    -0.1f,
                    0.05f),
                Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.DefaultPelvisMinimumDrop,
                Is.EqualTo(-0.35f));
            Assert.That(
                PlayerFootPlacementRules.DefaultPelvisMaximumLift,
                Is.EqualTo(0.12f));
        }

        [Test]
        public void PelvisDrop_ReleasesNegativeWithRunBlend()
        {
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.1f, -0.1f, 1f, true),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.1f, -0.1f, 1f, false),
                Is.EqualTo(-0.1f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.1f, -0.1f, 0.5f, true),
                Is.EqualTo(-0.05f).Within(Tolerance));

            // A lift is never released: both boots found higher ground and
            // the authored flight has nothing to protect there.
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(0.05f, 0.08f, 1f, true),
                Is.EqualTo(0.05f).Within(Tolerance));

            // The blend is clamped before it releases anything.
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.1f, -0.1f, 3f, true),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.PelvisDrop(-0.1f, -0.1f, -1f, true),
                Is.EqualTo(-0.1f).Within(Tolerance));
        }

        [Test]
        public void Classify_FlatRampEdge()
        {
            const float toeDistance = 0.14f;
            const float rampLimit =
                PlayerFootPlacementRules.DefaultRampLimitDegrees;

            Assert.That(
                PlayerFootPlacementRules.Classify(
                    0f,
                    0.001f,
                    toeDistance,
                    rampLimit),
                Is.EqualTo(FootSurfaceKind.Flat));
            Assert.That(
                PlayerFootPlacementRules.Classify(
                    0f,
                    0.03f,
                    toeDistance,
                    rampLimit),
                Is.EqualTo(FootSurfaceKind.Ramp));
            Assert.That(
                PlayerFootPlacementRules.Classify(
                    0f,
                    0.06f,
                    toeDistance,
                    rampLimit),
                Is.EqualTo(FootSurfaceKind.Edge));

            // The direction of the rise does not matter.
            Assert.That(
                PlayerFootPlacementRules.Classify(
                    0.03f,
                    0f,
                    toeDistance,
                    rampLimit),
                Is.EqualTo(FootSurfaceKind.Ramp));
            Assert.That(
                PlayerFootPlacementRules.Classify(
                    0.06f,
                    0f,
                    toeDistance,
                    rampLimit),
                Is.EqualTo(FootSurfaceKind.Edge));

            // A steeper limit admits the same rise as a ramp; a zero limit
            // makes every non-flat hit an edge.
            Assert.That(
                PlayerFootPlacementRules.Classify(0f, 0.06f, toeDistance, 45f),
                Is.EqualTo(FootSurfaceKind.Ramp));
            Assert.That(
                PlayerFootPlacementRules.Classify(0f, 0.01f, toeDistance, 0f),
                Is.EqualTo(FootSurfaceKind.Edge));
        }

        [Test]
        public void SupportHeight_EdgeUsesHigherHitOnlyWhilePlanted()
        {
            const float heel = 0f;
            const float toe = 0.16f;

            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Edge,
                    heel,
                    toe,
                    1f),
                Is.EqualTo(toe));
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Edge,
                    heel,
                    toe,
                    0.2f),
                Is.EqualTo(heel));
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Edge,
                    heel,
                    toe,
                    0.5f),
                Is.EqualTo(heel));

            // Heel above toe: the heel is already the higher hit, so a
            // swinging boot and a planted one agree.
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Edge,
                    toe,
                    heel,
                    1f),
                Is.EqualTo(toe));
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Edge,
                    toe,
                    heel,
                    0f),
                Is.EqualTo(toe));

            // Every other kind follows the heel regardless of plant.
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Ramp,
                    heel,
                    toe,
                    1f),
                Is.EqualTo(heel));
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.Flat,
                    0.05f,
                    0.051f,
                    1f),
                Is.EqualTo(0.05f));
            Assert.That(
                PlayerFootPlacementRules.SupportHeight(
                    FootSurfaceKind.None,
                    0.3f,
                    0.9f,
                    1f),
                Is.EqualTo(0.3f));
        }

        [Test]
        public void ClampReach_KeepsNinetyEightPercent()
        {
            var hip = new Vector3(0f, 1f, 0f);
            const float legLength = 0.9f;
            const float expectedReach = legLength * 0.98f;

            Assert.That(
                PlayerFootPlacementRules.DefaultReachFraction,
                Is.EqualTo(0.98f));

            Vector3 clamped = PlayerFootPlacementRules.ClampReach(
                hip,
                Vector3.zero,
                legLength);
            Assert.That(
                Vector3.Distance(hip, clamped),
                Is.EqualTo(expectedReach).Within(Tolerance));
            Assert.That(clamped.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(clamped.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                clamped.y,
                Is.EqualTo(1f - expectedReach).Within(Tolerance));

            // A target inside the reach is returned untouched.
            var inside = new Vector3(0.1f, 0.4f, 0.2f);
            Assert.That(
                PlayerFootPlacementRules.ClampReach(hip, inside, legLength),
                Is.EqualTo(inside));

            // An off-axis target is pulled in along its own ray.
            Vector3 far = hip + new Vector3(0.6f, -0.8f, 0f) * 3f;
            Vector3 clampedFar = PlayerFootPlacementRules.ClampReach(
                hip,
                far,
                legLength);
            Assert.That(
                Vector3.Distance(hip, clampedFar),
                Is.EqualTo(expectedReach).Within(Tolerance));
            Assert.That(
                Vector3.Dot(
                    (clampedFar - hip).normalized,
                    (far - hip).normalized),
                Is.EqualTo(1f).Within(Tolerance));

            // An explicit fraction overrides the default.
            Assert.That(
                Vector3.Distance(
                    hip,
                    PlayerFootPlacementRules.ClampReach(
                        hip,
                        Vector3.zero,
                        legLength,
                        0.5f)),
                Is.EqualTo(0.45f).Within(Tolerance));
        }

        [Test]
        public void IkWeight_ReleasesUnplantedFootWithRun()
        {
            // Walking: the layer's own blend is all that shows.
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 0f, 0f),
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.IkWeight(0.4f, 0f, 1f),
                Is.EqualTo(0.4f).Within(Tolerance));

            // Run flight: a boot off the ground is released entirely, a
            // planted one is not.
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 1f, 0f),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 1f, 1f),
                Is.EqualTo(1f).Within(Tolerance));

            // Half blends release half.
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 0.5f, 0f),
                Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 1f, 0.5f),
                Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.IkWeight(0.4f, 1f, 0.5f),
                Is.EqualTo(0.2f).Within(Tolerance));

            // Every input is clamped to the unit range.
            Assert.That(
                PlayerFootPlacementRules.IkWeight(2f, 0f, 0f),
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.IkWeight(1f, 2f, -1f),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void MaximumTargetStep_IsUnboundedWhileSwinging()
        {
            const float deltaTime = 1f / 60f;

            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(0.2f, deltaTime),
                Is.EqualTo(float.PositiveInfinity));
            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(0.49f, deltaTime),
                Is.EqualTo(float.PositiveInfinity));

            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(1f, deltaTime),
                Is.EqualTo(0.01f).Within(0.000001f));
            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(0.5f, deltaTime),
                Is.EqualTo(0.01f).Within(0.000001f));
            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(1f, 0.1f, 0.3f),
                Is.EqualTo(0.03f).Within(0.000001f));
            Assert.That(
                PlayerFootPlacementRules
                    .DefaultPlantedTargetRateMetresPerSecond,
                Is.EqualTo(0.6f));

            // A negative clock or rate never moves the target backwards.
            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(1f, -0.1f),
                Is.EqualTo(0f));
            Assert.That(
                PlayerFootPlacementRules.MaximumTargetStep(1f, 0.1f, -1f),
                Is.EqualTo(0f));
        }

        [Test]
        public void ClipLift_IsNeverNegative()
        {
            Assert.That(
                PlayerFootPlacementRules.ClipLift(0.05f, 0f),
                Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.ClipLift(1f, 0.9f),
                Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(PlayerFootPlacementRules.ClipLift(0.1f, 0.1f), Is.Zero);
            Assert.That(PlayerFootPlacementRules.ClipLift(-0.01f, 0f), Is.Zero);
            Assert.That(PlayerFootPlacementRules.ClipLift(0.8f, 0.9f), Is.Zero);
        }

        [Test]
        public void TargetSoleHeight_AddsClearanceAndLift()
        {
            Assert.That(
                PlayerFootPlacementRules.TargetSoleHeight(0.16f, 0.01f, 0.05f),
                Is.EqualTo(0.22f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.TargetSoleHeight(-0.3f, 0f, 0f),
                Is.EqualTo(-0.3f).Within(Tolerance));
            Assert.That(
                PlayerFootPlacementRules.TargetSoleHeight(0f, 0.004f, 0f),
                Is.EqualTo(0.004f).Within(Tolerance));

            // A lift the clip holds is preserved over whatever the probe found.
            float lift = PlayerFootPlacementRules.ClipLift(0.07f, 0.02f);
            Assert.That(
                PlayerFootPlacementRules.TargetSoleHeight(0.5f, 0f, lift),
                Is.EqualTo(0.55f).Within(Tolerance));
        }
    }
}
