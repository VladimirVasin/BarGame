using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeOpeningTimelineTests
    {
        [Test]
        public void
            Timeline_Holds0559ForFiveSecondsThenEnablesSilentMenu()
        {
            var timeline = new HomeOpeningTimeline();

            Assert.That(timeline.Begin(), Is.True);
            Assert.That(timeline.Begin(), Is.False);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.ClockHold));
            Assert.That(
                timeline.DisplayHour,
                Is.EqualTo(HomeOpeningTimeline.LockedHour));
            Assert.That(
                timeline.DisplayMinute,
                Is.EqualTo(HomeOpeningTimeline.LockedMinute));
            Assert.That(timeline.ShouldRing, Is.False);
            Assert.That(timeline.MenuVisible, Is.False);
            Assert.That(
                HomeOpeningTimeline.LockedClockSeconds,
                Is.EqualTo(5f));
            Assert.That(timeline.DisplayVisible, Is.True);
            Assert.That(timeline.RequestWake(), Is.False);

            timeline.Advance(
                HomeOpeningTimeline.LockedClockSeconds -
                0.001f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.ClockHold));
            Assert.That(timeline.DisplayHour, Is.EqualTo(5));
            Assert.That(timeline.DisplayMinute, Is.EqualTo(59));
            Assert.That(timeline.ShouldRing, Is.False);
            Assert.That(timeline.MenuVisible, Is.False);

            timeline.Advance(0.001f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(timeline.MenuVisible, Is.True);
            Assert.That(timeline.ShouldRing, Is.False);
            Assert.That(
                timeline.DisplayHour,
                Is.EqualTo(HomeOpeningTimeline.LockedHour));
            Assert.That(
                timeline.DisplayMinute,
                Is.EqualTo(HomeOpeningTimeline.LockedMinute));
            Assert.That(timeline.DisplayVisible, Is.True);

            var crossing = new HomeOpeningTimeline();
            crossing.Begin();
            crossing.Advance(
                HomeOpeningTimeline.LockedClockSeconds +
                0.75f);
            Assert.That(
                crossing.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(
                crossing.PhaseElapsedSeconds,
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                crossing.DisplayElapsedSeconds,
                Is.EqualTo(5.75f).Within(0.0001f));
            Assert.That(crossing.DisplayHour, Is.EqualTo(5));
            Assert.That(crossing.DisplayMinute, Is.EqualTo(59));
        }

        [Test]
        public void
            LockedDisplay_FlickersThroughMenuThenStaysSolidAtWake()
        {
            HomeOpeningTimeline beforeFirst =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds -
                    0.001f);
            Assert.That(beforeFirst.DisplayVisible, Is.True);

            HomeOpeningTimeline firstStart =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds);
            Assert.That(firstStart.DisplayVisible, Is.False);

            HomeOpeningTimeline beforeFirstEnd =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkOffSeconds -
                    0.001f);
            Assert.That(beforeFirstEnd.DisplayVisible, Is.False);

            HomeOpeningTimeline firstEnd =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkOffSeconds);
            Assert.That(firstEnd.DisplayVisible, Is.True);

            HomeOpeningTimeline beforeSecond =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkPeriodSeconds -
                    0.001f);
            Assert.That(beforeSecond.DisplayVisible, Is.True);

            HomeOpeningTimeline secondStart =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkPeriodSeconds);
            Assert.That(secondStart.DisplayVisible, Is.False);

            HomeOpeningTimeline secondEnd =
                CreateTimelineAt(
                    HomeOpeningTimeline
                        .DisplayBlinkInitialDelaySeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkPeriodSeconds,
                    HomeOpeningTimeline
                        .DisplayBlinkOffSeconds);
            Assert.That(secondEnd.DisplayVisible, Is.True);

            Assert.That(
                HomeOpeningTimeline
                    .DisplayBlinkPeriodSeconds,
                Is.EqualTo(3f),
                "Display flickers must be separated by a long interval.");
            Assert.That(
                HomeOpeningTimeline
                    .DisplayBlinkOffSeconds,
                Is.EqualTo(0.16f),
                "Each display flicker itself must remain brief.");

            HomeOpeningTimeline timeline =
                CreateTimelineAt(
                    HomeOpeningTimeline.LockedClockSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(timeline.DisplayVisible, Is.True);
            Assert.That(timeline.DisplayHour, Is.EqualTo(5));
            Assert.That(timeline.DisplayMinute, Is.EqualTo(59));

            float thirdBlinkStart =
                HomeOpeningTimeline
                    .DisplayBlinkInitialDelaySeconds +
                HomeOpeningTimeline
                    .DisplayBlinkPeriodSeconds *
                2f;
            timeline.Advance(
                thirdBlinkStart -
                timeline.DisplayElapsedSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(timeline.MenuVisible, Is.True);
            Assert.That(timeline.DisplayVisible, Is.False);
            Assert.That(timeline.DisplayHour, Is.EqualTo(5));
            Assert.That(timeline.DisplayMinute, Is.EqualTo(59));

            timeline.Advance(
                HomeOpeningTimeline.DisplayBlinkOffSeconds);
            Assert.That(timeline.DisplayVisible, Is.True);
            Assert.That(timeline.RequestWake(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(timeline.ShouldRing, Is.True);
            Assert.That(timeline.WakeAnimationReady, Is.False);
            Assert.That(timeline.DisplayVisible, Is.True);
            Assert.That(timeline.DisplayHour, Is.EqualTo(6));
            Assert.That(timeline.DisplayMinute, Is.Zero);
        }

        [Test]
        public void WakeRequest_IsSingleAndRequiresExplicitCompletion()
        {
            var timeline = new HomeOpeningTimeline();
            timeline.Begin();

            Assert.That(timeline.RequestWake(), Is.False);
            timeline.Advance(
                HomeOpeningTimeline.LockedClockSeconds);

            Assert.That(timeline.RequestWake(), Is.True);
            Assert.That(timeline.RequestWake(), Is.False);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(timeline.ShouldRing, Is.True);
            Assert.That(timeline.MenuVisible, Is.False);
            Assert.That(timeline.DisplayHour, Is.EqualTo(6));
            Assert.That(timeline.DisplayMinute, Is.Zero);
            Assert.That(
                HomeOpeningTimeline.WakeAlarmSeconds,
                Is.EqualTo(3f));
            Assert.That(timeline.WakeAnimationReady, Is.False);
            Assert.That(timeline.BeginWakeAnimation(), Is.False);
            Assert.That(
                timeline.WakeCameraTransitionProgress,
                Is.Zero);
            Assert.That(
                timeline.WakeCameraTransitionBlend,
                Is.Zero);
            Assert.That(
                timeline.WakeCameraTransitionComplete,
                Is.False);

            timeline.Advance(
                HomeOpeningTimeline.WakeAlarmSeconds -
                0.001f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(timeline.ShouldRing, Is.True);
            Assert.That(timeline.WakeAnimationReady, Is.False);
            Assert.That(timeline.BeginWakeAnimation(), Is.False);

            timeline.Advance(0.001f);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(3f).Within(0.0001f));
            Assert.That(timeline.ShouldRing, Is.True);
            Assert.That(timeline.WakeAnimationReady, Is.True);
            Assert.That(timeline.BeginWakeAnimation(), Is.True);
            Assert.That(timeline.BeginWakeAnimation(), Is.False);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(timeline.ShouldRing, Is.False);
            Assert.That(timeline.WakeAnimationReady, Is.False);
            Assert.That(timeline.PhaseElapsedSeconds, Is.Zero);

            timeline.Advance(
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds /
                4f);
            Assert.That(
                timeline.WakeCameraTransitionProgress,
                Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(
                timeline.WakeCameraTransitionBlend,
                Is.EqualTo(0.103515625f).Within(0.000001f));
            Assert.That(
                timeline.WakeCameraTransitionComplete,
                Is.False);

            timeline.Advance(
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds /
                4f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(
                timeline.WakeCameraTransitionProgress,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                timeline.WakeCameraTransitionBlend,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                timeline.WakeCameraTransitionComplete,
                Is.False);

            timeline.Advance(
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds /
                2f);
            Assert.That(
                timeline.WakeCameraTransitionProgress,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                timeline.WakeCameraTransitionBlend,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                timeline.WakeCameraTransitionComplete,
                Is.True);
            Assert.That(timeline.CompleteWake(), Is.True);
            Assert.That(timeline.CompleteWake(), Is.False);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.Complete));
            Assert.That(timeline.IsActive, Is.False);
        }

        [Test]
        public void Timeline_IsFrameRateIndependentAndRejectsBadDelta()
        {
            var singleStep = new HomeOpeningTimeline();
            var manySteps = new HomeOpeningTimeline();
            singleStep.Begin();
            manySteps.Begin();

            const int stepCount = 256;
            const float elapsed =
                HomeOpeningTimeline.LockedClockSeconds;
            singleStep.Advance(elapsed);
            for (int index = 0; index < stepCount; index++)
            {
                manySteps.Advance(elapsed / stepCount);
            }

            Assert.That(manySteps.Phase, Is.EqualTo(singleStep.Phase));
            Assert.That(
                singleStep.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(
                manySteps.PhaseElapsedSeconds,
                Is.EqualTo(singleStep.PhaseElapsedSeconds)
                    .Within(0.0001f));
            Assert.That(
                manySteps.DisplayElapsedSeconds,
                Is.EqualTo(singleStep.DisplayElapsedSeconds)
                    .Within(0.0001f));
            Assert.That(
                manySteps.DisplayHour,
                Is.EqualTo(singleStep.DisplayHour));
            Assert.That(
                manySteps.DisplayMinute,
                Is.EqualTo(singleStep.DisplayMinute));
            Assert.That(singleStep.MenuVisible, Is.True);
            Assert.That(singleStep.ShouldRing, Is.False);
            Assert.That(singleStep.RequestWake(), Is.True);
            Assert.That(manySteps.RequestWake(), Is.True);
            singleStep.Advance(
                HomeOpeningTimeline.WakeAlarmSeconds);
            for (int index = 0; index < stepCount; index++)
            {
                manySteps.Advance(
                    HomeOpeningTimeline.WakeAlarmSeconds /
                    stepCount);
            }

            Assert.That(singleStep.WakeAnimationReady, Is.True);
            Assert.That(manySteps.WakeAnimationReady, Is.True);
            Assert.That(
                manySteps.PhaseElapsedSeconds,
                Is.EqualTo(singleStep.PhaseElapsedSeconds)
                    .Within(0.0001f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => singleStep.Advance(-0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => singleStep.Advance(float.NaN));
        }

        [Test]
        public void Cancel_CompletesAnyActiveOpeningOnlyOnce()
        {
            var timeline = new HomeOpeningTimeline();

            Assert.That(timeline.Cancel(), Is.False);
            timeline.Begin();
            timeline.Advance(
                HomeOpeningTimeline
                    .DisplayBlinkInitialDelaySeconds);
            Assert.That(timeline.DisplayVisible, Is.False);
            Assert.That(timeline.Cancel(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeOpeningPhase.Complete));
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.DisplayVisible, Is.True);
            Assert.That(timeline.DisplayHour, Is.EqualTo(5));
            Assert.That(timeline.DisplayMinute, Is.EqualTo(59));
            Assert.That(timeline.Cancel(), Is.False);

            var alarmRinging = new HomeOpeningTimeline();
            alarmRinging.Begin();
            alarmRinging.Advance(
                HomeOpeningTimeline.LockedClockSeconds);
            Assert.That(alarmRinging.RequestWake(), Is.True);
            Assert.That(alarmRinging.Cancel(), Is.True);
            Assert.That(alarmRinging.DisplayHour, Is.EqualTo(6));
            Assert.That(alarmRinging.DisplayMinute, Is.Zero);

            var waking = new HomeOpeningTimeline();
            waking.Begin();
            waking.Advance(
                HomeOpeningTimeline.LockedClockSeconds);
            Assert.That(waking.RequestWake(), Is.True);
            waking.Advance(
                HomeOpeningTimeline.WakeAlarmSeconds);
            Assert.That(waking.BeginWakeAnimation(), Is.True);
            Assert.That(waking.Cancel(), Is.True);
            Assert.That(waking.DisplayHour, Is.EqualTo(6));
            Assert.That(waking.DisplayMinute, Is.Zero);
        }

        private static HomeOpeningTimeline CreateTimelineAt(
            params float[] advances)
        {
            var timeline = new HomeOpeningTimeline();
            timeline.Begin();
            for (int index = 0;
                 index < advances.Length;
                 index++)
            {
                timeline.Advance(advances[index]);
            }

            return timeline;
        }
    }
}
