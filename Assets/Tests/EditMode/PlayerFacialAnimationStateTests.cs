using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class PlayerFacialAnimationStateTests
    {
        [Test]
        public void Advance_UsesDeterministicBlinkSequence()
        {
            var state = new PlayerFacialAnimationState();

            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialBlinkDelaySeconds - 0.01f),
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                state.Advance(0.02f),
                Is.EqualTo(PlayerFacialExpression.HalfBlink));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .HalfBlinkDurationSeconds),
                Is.EqualTo(PlayerFacialExpression.ClosedBlink));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .ClosedBlinkDurationSeconds),
                Is.EqualTo(PlayerFacialExpression.HalfBlink));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .HalfBlinkDurationSeconds),
                Is.EqualTo(PlayerFacialExpression.Neutral));
        }

        [Test]
        public void Advance_IsFrameRateIndependentAndResettable()
        {
            var singleStep = new PlayerFacialAnimationState();
            var manySteps = new PlayerFacialAnimationState();
            const float elapsedSeconds = 15.37f;

            PlayerFacialExpression expected =
                singleStep.Advance(elapsedSeconds);
            PlayerFacialExpression actual =
                PlayerFacialExpression.Neutral;
            for (int frame = 0; frame < 1537; frame++)
            {
                actual = manySteps.Advance(0.01f);
            }

            Assert.That(actual, Is.EqualTo(expected));

            manySteps.Reset();
            Assert.That(
                manySteps.CurrentExpression,
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                manySteps.Advance(
                    PlayerFacialAnimationState
                        .InitialBlinkDelaySeconds - 0.01f),
                Is.EqualTo(PlayerFacialExpression.Neutral));
        }

        [Test]
        public void Advance_UsesWatchfulAndTenseIdleExpressions()
        {
            var state = new PlayerFacialAnimationState();

            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialWatchfulDelaySeconds - 0.01f),
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                state.Advance(0.02f),
                Is.EqualTo(PlayerFacialExpression.Watchful));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .WatchfulDurationSeconds),
                Is.EqualTo(PlayerFacialExpression.Neutral));

            state.Reset();
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialTenseDelaySeconds + 0.01f),
                Is.EqualTo(PlayerFacialExpression.Tense));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .TenseDurationSeconds),
                Is.EqualTo(PlayerFacialExpression.Neutral));
        }

        [Test]
        public void Advance_OnlyPlaysExpressiveStatesDuringSustainedIdle()
        {
            var state = new PlayerFacialAnimationState();

            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialWatchfulDelaySeconds + 0.01f,
                    false),
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialWatchfulDelaySeconds + 0.01f,
                    true),
                Is.EqualTo(PlayerFacialExpression.Watchful));

            Assert.That(
                state.Advance(0.01f, false),
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialWatchfulDelaySeconds - 0.02f,
                    true),
                Is.EqualTo(PlayerFacialExpression.Neutral));
        }

        [Test]
        public void Advance_BlinksWhenIdleExpressionsAreDisabled()
        {
            var state = new PlayerFacialAnimationState();

            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialBlinkDelaySeconds + 0.01f,
                    false),
                Is.EqualTo(PlayerFacialExpression.HalfBlink));
        }

        [Test]
        public void Advance_IgnoresNegativeDeltaTime()
        {
            var state = new PlayerFacialAnimationState();

            Assert.That(
                state.Advance(-10f),
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                state.Advance(
                    PlayerFacialAnimationState
                        .InitialBlinkDelaySeconds + 0.01f),
                Is.EqualTo(PlayerFacialExpression.HalfBlink));
        }
    }
}
