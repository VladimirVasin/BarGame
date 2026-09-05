using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class IntoxicationRulesTests
    {
        [Test]
        public void Perception_IsContinuousExponentialWithExactEndpoints()
        {
            Assert.That(IntoxicationPerceptionRules.Evaluate(0f).Intensity,
                Is.Zero);
            Assert.That(IntoxicationPerceptionRules.Evaluate(100f).Intensity,
                Is.EqualTo(1f));
            Assert.That(IntoxicationPerceptionRules.Evaluate(0f).WorldTimeScale,
                Is.EqualTo(1f));
            Assert.That(IntoxicationPerceptionRules.Evaluate(100f).WorldTimeScale,
                Is.EqualTo(0.88f));
            float previous = 0f;
            float previousIncrement = 0f;
            for (int step = 1; step <= 200; step++)
            {
                float current = IntoxicationPerceptionRules.Evaluate(step * 0.5f)
                    .Intensity;
                Assert.That(current, Is.GreaterThan(previous));
                Assert.That(current - previous, Is.GreaterThan(previousIncrement));
                previousIncrement = current - previous;
                previous = current;
            }

            Assert.That(IntoxicationPerceptionRules.Evaluate(80f).Intensity,
                Is.EqualTo(0.3999f).Within(0.001f));
            Assert.That(IntoxicationPerceptionRules.Evaluate(90f).Intensity,
                Is.EqualTo(0.6336f).Within(0.001f));
            Assert.That(IntoxicationPerceptionRules.Evaluate(float.NaN).Intensity,
                Is.Zero);
        }

        [TestCase(-1, 0, IntoxicationStage.Sober)]
        [TestCase(0, 0, IntoxicationStage.Sober)]
        [TestCase(1, 1, IntoxicationStage.LightBuzz)]
        [TestCase(20, 20, IntoxicationStage.LightBuzz)]
        [TestCase(21, 21, IntoxicationStage.Tipsy)]
        [TestCase(40, 40, IntoxicationStage.Tipsy)]
        [TestCase(41, 41, IntoxicationStage.Drunk)]
        [TestCase(60, 60, IntoxicationStage.Drunk)]
        [TestCase(61, 61, IntoxicationStage.Unsteady)]
        [TestCase(80, 80, IntoxicationStage.Unsteady)]
        [TestCase(81, 81, IntoxicationStage.VeryDrunk)]
        [TestCase(100, 100, IntoxicationStage.VeryDrunk)]
        [TestCase(101, 100, IntoxicationStage.VeryDrunk)]
        public void Evaluate_UsesTwentyPointBands(
            int level,
            int expectedLevel,
            IntoxicationStage expectedStage)
        {
            IntoxicationProfile profile =
                IntoxicationStageRules.Evaluate(level);

            Assert.That(profile.Level, Is.EqualTo(expectedLevel));
            Assert.That(profile.Stage, Is.EqualTo(expectedStage));
            Assert.That(profile.StageNameKey, Is.Not.Empty);
        }

        [Test]
        public void Evaluate_EffectsGrowMonotonicallyAcrossBands()
        {
            IntoxicationProfile previous =
                IntoxicationStageRules.Evaluate(0);
            int[] levels = { 20, 40, 60, 80, 100 };

            for (int index = 0; index < levels.Length; index++)
            {
                IntoxicationProfile current =
                    IntoxicationStageRules.Evaluate(levels[index]);

                Assert.That(
                    current.SpeedMultiplier,
                    Is.LessThanOrEqualTo(previous.SpeedMultiplier));
                Assert.That(
                    current.PuppetSwayDegrees,
                    Is.GreaterThanOrEqualTo(previous.PuppetSwayDegrees));
                Assert.That(
                    current.CameraRollDegrees,
                    Is.GreaterThanOrEqualTo(previous.CameraRollDegrees));
                Assert.That(
                    current.VignetteStrength,
                    Is.GreaterThanOrEqualTo(previous.VignetteStrength));
                Assert.That(
                    current.GhostPixels,
                    Is.GreaterThanOrEqualTo(previous.GhostPixels));
                Assert.That(
                    current.WarpStrength,
                    Is.GreaterThanOrEqualTo(previous.WarpStrength));
                Assert.That(
                    current.ExposurePulse,
                    Is.GreaterThanOrEqualTo(previous.ExposurePulse));
                Assert.That(
                    current.ChromaticAberration,
                    Is.GreaterThanOrEqualTo(
                        previous.ChromaticAberration));
                Assert.That(
                    current.LensDistortion,
                    Is.LessThanOrEqualTo(previous.LensDistortion));
                Assert.That(
                    current.DollyZoomStrength,
                    Is.GreaterThanOrEqualTo(previous.DollyZoomStrength));
                Assert.That(
                    current.VertigoStrength,
                    Is.GreaterThanOrEqualTo(previous.VertigoStrength));
                Assert.That(
                    current.MutterSlurAmount,
                    Is.GreaterThanOrEqualTo(previous.MutterSlurAmount));
                Assert.That(
                    current.MutterScatterAmount,
                    Is.GreaterThanOrEqualTo(previous.MutterScatterAmount));

                previous = current;
            }

            Assert.That(
                IntoxicationStageRules.Evaluate(100).SpeedMultiplier,
                Is.LessThan(
                    IntoxicationStageRules.Evaluate(60).SpeedMultiplier));
            Assert.That(
                IntoxicationStageRules.Evaluate(100).PuppetSwayDegrees,
                Is.GreaterThan(
                    IntoxicationStageRules.Evaluate(60).PuppetSwayDegrees));

            IntoxicationProfile sober =
                IntoxicationStageRules.Evaluate(0);
            Assert.That(sober.ChromaticAberration, Is.Zero);
            Assert.That(sober.LensDistortion, Is.Zero);
            IntoxicationProfile blackout =
                IntoxicationStageRules.Evaluate(100);
            Assert.That(
                blackout.ChromaticAberration,
                Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(
                blackout.LensDistortion,
                Is.EqualTo(-0.14f).Within(0.001f));

            // The dolly zoom is silent through the balance threshold and
            // reaches its full swing only at the top of the last stage.
            Assert.That(sober.DollyZoomStrength, Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(60).DollyZoomStrength,
                Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(61).DollyZoomStrength,
                Is.GreaterThan(0f));
            Assert.That(
                IntoxicationStageRules.Evaluate(80).DollyZoomStrength,
                Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(
                blackout.DollyZoomStrength,
                Is.EqualTo(1f).Within(0.001f));

            // The vertigo whirlpool shares that gate exactly: still water
            // through the threshold, a third of the wind-up at 80.
            Assert.That(sober.VertigoStrength, Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(60).VertigoStrength,
                Is.Zero);
            Assert.That(
                IntoxicationStageRules.Evaluate(61).VertigoStrength,
                Is.GreaterThan(0f));
            Assert.That(
                IntoxicationStageRules.Evaluate(80).VertigoStrength,
                Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(
                blackout.VertigoStrength,
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void BalanceThreshold_EnablesFallsOnlyAboveSixty()
        {
            IntoxicationProfile threshold =
                IntoxicationStageRules.Evaluate(60);
            IntoxicationProfile firstEnabled =
                IntoxicationStageRules.Evaluate(61);
            IntoxicationProfile maximum =
                IntoxicationStageRules.Evaluate(100);

            Assert.That(threshold.BalanceEnabled, Is.False);
            Assert.That(firstEnabled.BalanceEnabled, Is.True);
            Assert.That(maximum.BalanceEnabled, Is.True);
            Assert.That(
                maximum.BalanceDifficulty,
                Is.GreaterThan(firstEnabled.BalanceDifficulty));
        }

        [Test]
        public void EpisodeSeed_IsDeterministicAndChangesWithSequence()
        {
            const int citySeed = 887733;
            const int sequence = 4;

            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, sequence),
                Is.EqualTo(
                    PlayerBalanceRules.EpisodeSeed(citySeed, sequence)));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, sequence),
                Is.Not.EqualTo(
                    PlayerBalanceRules.EpisodeSeed(
                        citySeed,
                        sequence + 1)));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, sequence),
                Is.Not.EqualTo(
                    PlayerBalanceRules.EpisodeSeed(
                        citySeed + 1,
                        sequence)));
        }

        [Test]
        public void Recovery_LowerLevelsLosePointsFaster()
        {
            float nearSober =
                IntoxicationStageRules.GetRecoverySecondsPerPoint(1);
            float middle =
                IntoxicationStageRules.GetRecoverySecondsPerPoint(50);
            float maximum =
                IntoxicationStageRules.GetRecoverySecondsPerPoint(100);

            Assert.That(nearSober, Is.LessThan(middle));
            Assert.That(middle, Is.LessThan(maximum));
            Assert.That(
                maximum,
                Is.EqualTo(
                    IntoxicationStageRules
                        .RecoverySecondsPerPointAtMaximumLevel));
        }

    }
}
