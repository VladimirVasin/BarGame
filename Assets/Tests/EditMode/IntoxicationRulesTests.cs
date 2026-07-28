using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class IntoxicationRulesTests
    {
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
        }

        [Test]
        public void BalanceRules_EnableOnlyAboveSixtyAndGetHarder()
        {
            IntoxicationProfile threshold =
                IntoxicationStageRules.Evaluate(60);
            IntoxicationProfile firstEnabled =
                IntoxicationStageRules.Evaluate(61);
            IntoxicationProfile maximum =
                IntoxicationStageRules.Evaluate(100);
            BalanceChallengeSettings low =
                BalanceChallengeSettings.FromDifficulty(
                    firstEnabled.BalanceDifficulty);
            BalanceChallengeSettings high =
                BalanceChallengeSettings.FromDifficulty(
                    maximum.BalanceDifficulty);

            Assert.That(threshold.BalanceEnabled, Is.False);
            Assert.That(firstEnabled.BalanceEnabled, Is.True);
            Assert.That(maximum.BalanceEnabled, Is.True);
            Assert.That(high.WarningDuration, Is.LessThan(low.WarningDuration));
            Assert.That(high.Duration, Is.GreaterThan(low.Duration));
            Assert.That(
                high.SafeSectorDegrees,
                Is.LessThan(low.SafeSectorDegrees));
            Assert.That(
                high.PointerFrequency,
                Is.GreaterThan(low.PointerFrequency));
            Assert.That(
                BalanceChallengeRules.GetMinimumInterval(100),
                Is.LessThan(BalanceChallengeRules.GetMinimumInterval(61)));
            Assert.That(
                BalanceChallengeRules.GetMaximumInterval(100),
                Is.LessThan(BalanceChallengeRules.GetMaximumInterval(61)));
        }

        [Test]
        public void BalanceRules_IntervalsAndSeeds_AreDeterministic()
        {
            const int citySeed = 887733;
            const int sequence = 4;
            float first = BalanceChallengeRules.GetNextInterval(
                84,
                citySeed,
                sequence);
            float repeated = BalanceChallengeRules.GetNextInterval(
                84,
                citySeed,
                sequence);

            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(
                first,
                Is.InRange(
                    BalanceChallengeRules.GetMinimumInterval(84),
                    BalanceChallengeRules.GetMaximumInterval(84)));
            Assert.That(
                BalanceChallengeRules.GetChallengeSeed(
                    citySeed,
                    sequence),
                Is.Not.EqualTo(
                    BalanceChallengeRules.GetChallengeSeed(
                        citySeed,
                        sequence + 1)));
        }

        [Test]
        public void BalanceModel_FixedStepIsIndependentOfFrameChunking()
        {
            BalanceChallengeSettings settings =
                BalanceChallengeSettings.FromDifficulty(0.65f);
            var sixtyFps = new BalanceChallengeModel(settings, 12345);
            var thirtyFps = new BalanceChallengeModel(settings, 12345);

            for (int index = 0; index < 60; index++)
            {
                sixtyFps.Advance(1f / 60f, 0.35f);
            }

            for (int index = 0; index < 30; index++)
            {
                thirtyFps.Advance(1f / 30f, 0.35f);
            }

            Assert.That(
                sixtyFps.Position,
                Is.EqualTo(thirtyFps.Position).Within(0.0001f));
            Assert.That(
                sixtyFps.Velocity,
                Is.EqualTo(thirtyFps.Velocity).Within(0.0001f));
            Assert.That(
                sixtyFps.Risk,
                Is.EqualTo(thirtyFps.Risk).Within(0.0001f));
            Assert.That(
                sixtyFps.Elapsed,
                Is.EqualTo(thirtyFps.Elapsed).Within(0.0001f));
        }

        [Test]
        public void BalanceModel_LeftAndRightInputPushInOppositeDirections()
        {
            BalanceChallengeSettings settings =
                BalanceChallengeSettings.FromDifficulty(0.5f);
            var left = new BalanceChallengeModel(settings, 5566);
            var right = new BalanceChallengeModel(settings, 5566);

            for (int index = 0; index < 60; index++)
            {
                left.Advance(1f / 60f, -1f);
                right.Advance(1f / 60f, 1f);
            }

            Assert.That(
                right.Position - left.Position,
                Is.GreaterThan(0.35f));
        }

        [Test]
        public void BalanceModel_MaximumDifficultyCanBeStabilized()
        {
            BalanceChallengeSettings settings =
                BalanceChallengeSettings.FromDifficulty(1f);
            var model = new BalanceChallengeModel(settings, 91827);

            for (int index = 0;
                 index < 1000 && !model.IsComplete;
                 index++)
            {
                float input = model.Position > 0f
                    ? -1f
                    : model.Position < 0f
                        ? 1f
                        : 0f;
                model.Advance(
                    BalanceChallengeModel.FixedStep,
                    input);
            }

            Assert.That(model.IsComplete, Is.True);
            Assert.That(model.Succeeded, Is.True);
        }

        [Test]
        public void PlayerPose_PositiveBalanceAndFallLeanScreenRight()
        {
            PlayerIntoxicationPose balance =
                PlayerIntoxicationPoseEvaluator.Evaluate(
                    0f,
                    0f,
                    1f,
                    1f,
                    0f);
            PlayerIntoxicationPose fall =
                PlayerIntoxicationPoseEvaluator.Evaluate(
                    0f,
                    0f,
                    0f,
                    1f,
                    1f);

            Assert.That(balance.BodyOffsetX, Is.GreaterThan(0f));
            Assert.That(balance.BodyRoll, Is.LessThan(0f));
            Assert.That(fall.BodyOffsetX, Is.GreaterThan(0f));
            Assert.That(fall.BodyRoll, Is.LessThan(-80f));
        }

        [Test]
        public void PlayerPose_FullyDownPoseDoesNotKeepDrunkenSway()
        {
            PlayerIntoxicationPose first =
                PlayerIntoxicationPoseEvaluator.Evaluate(
                    1f,
                    0f,
                    0f,
                    -1f,
                    1f);
            PlayerIntoxicationPose later =
                PlayerIntoxicationPoseEvaluator.Evaluate(
                    1f,
                    4.3f,
                    0f,
                    -1f,
                    1f);

            Assert.That(later.BodyOffsetX, Is.EqualTo(first.BodyOffsetX));
            Assert.That(later.BodyLift, Is.EqualTo(first.BodyLift));
            Assert.That(later.BodyRoll, Is.EqualTo(first.BodyRoll));
            Assert.That(later.ArmSpread, Is.EqualTo(first.ArmSpread));
            Assert.That(later.KneeBend, Is.EqualTo(first.KneeBend));
        }
    }
}
