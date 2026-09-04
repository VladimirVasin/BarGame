using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
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
        public void Blink_LengthensWithTheDrinkAndSoberIsUnchanged()
        {
            int ClosedFrames(float intoxication)
            {
                var state = new PlayerFacialAnimationState();
                int longest = 0;
                int run = 0;
                for (int frame = 0; frame < 20 * 60; frame++)
                {
                    PlayerFacialExpression expression = state.Advance(1f / 60f, false, intoxication);
                    if (expression == PlayerFacialExpression.ClosedBlink)
                    {
                        run++;
                        longest = Mathf.Max(longest, run);
                    }
                    else
                    {
                        run = 0;
                    }
                }

                return longest;
            }

            int sober = ClosedFrames(0f);
            int drunk = ClosedFrames(1f);
            Assert.That(sober, Is.EqualTo(Mathf.RoundToInt(PlayerFacialAnimationState.ClosedBlinkDurationSeconds * 60f)).Within(1));
            Assert.That(drunk, Is.EqualTo(Mathf.RoundToInt(PlayerFacialAnimationState.DrunkClosedBlinkDurationSeconds * 60f)).Within(1));
            Assert.That(PlayerFacialAnimationState.BlinkSeconds(0f), Is.EqualTo(PlayerFacialAnimationState.BlinkDurationSeconds));
            Assert.That(PlayerFacialAnimationState.BlinkIntervalScale(0f), Is.EqualTo(1f));

            // The sober schedule with the new arguments is the old schedule.
            var oldStyle = new PlayerFacialAnimationState();
            var newStyle = new PlayerFacialAnimationState();
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                Assert.That(
                    newStyle.Advance(1f / 60f, true, 0f, PlayerFacialMood.None),
                    Is.EqualTo(oldStyle.Advance(1f / 60f, true)));
            }
        }

        [Test]
        public void RestingFace_FollowsTheLevel()
        {
            Assert.That(PlayerFacialAnimationState.RestingExpression(0f), Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(PlayerFacialAnimationState.RestingExpression(0.5f), Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(PlayerFacialAnimationState.RestingExpression(0.7f), Is.EqualTo(PlayerFacialExpression.Glazed));
            Assert.That(PlayerFacialAnimationState.RestingExpression(1f), Is.EqualTo(PlayerFacialExpression.Slack));

            var state = new PlayerFacialAnimationState();
            var seen = new System.Collections.Generic.HashSet<PlayerFacialExpression>();
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                seen.Add(state.Advance(1f / 60f, false, 0.5f));
            }

            Assert.That(seen, Has.Member(PlayerFacialExpression.Drowsy), "half drunk the lids droop in spells");
            Assert.That(seen, Has.Member(PlayerFacialExpression.Neutral), "and lift again between them");
            Assert.That(seen, Has.No.Member(PlayerFacialExpression.Glazed));

            seen.Clear();
            state.Reset();
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                seen.Add(state.Advance(1f / 60f, true, 1f));
            }

            Assert.That(seen, Has.Member(PlayerFacialExpression.Slack), "blind drunk the jaw hangs");
            Assert.That(seen, Has.Member(PlayerFacialExpression.Drowsy));
            Assert.That(seen, Has.Member(PlayerFacialExpression.ClosedBlink));
            Assert.That(seen, Has.No.Member(PlayerFacialExpression.Watchful), "the idle glances are the sober man's");
        }

        [Test]
        public void Mood_OverridesTheRestAndOutShutsTheEyes()
        {
            var state = new PlayerFacialAnimationState();
            var seen = new System.Collections.Generic.HashSet<PlayerFacialExpression>();
            for (int frame = 0; frame < 10 * 60; frame++)
            {
                seen.Add(state.Advance(1f / 60f, true, 1f, PlayerFacialMood.Grimace));
            }

            Assert.That(seen, Has.Member(PlayerFacialExpression.Grimace));
            Assert.That(seen, Has.Member(PlayerFacialExpression.ClosedBlink), "he still blinks through a grimace");
            Assert.That(seen, Has.No.Member(PlayerFacialExpression.Slack));
            Assert.That(seen, Has.No.Member(PlayerFacialExpression.Tense));

            seen.Clear();
            for (int frame = 0; frame < 10 * 60; frame++)
            {
                seen.Add(state.Advance(1f / 60f, true, 1f, PlayerFacialMood.Tense));
            }

            Assert.That(seen, Has.Member(PlayerFacialExpression.Tense));
            Assert.That(seen, Has.No.Member(PlayerFacialExpression.Grimace));

            seen.Clear();
            for (int frame = 0; frame < 10 * 60; frame++)
            {
                seen.Add(state.Advance(1f / 60f, true, 1f, PlayerFacialMood.Out));
            }

            Assert.That(seen, Is.EquivalentTo(new[] { PlayerFacialExpression.ClosedBlink }), "out cold the eyes stay shut");
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
