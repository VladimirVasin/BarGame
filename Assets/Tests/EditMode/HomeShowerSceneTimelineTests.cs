using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The shower's beats as a pure clock: the camera flies into the
    /// hero's head before he has arrived anywhere, a dock reached during
    /// the fly-in is remembered, the dock gets one rendered neutral
    /// frame, only the wash accepts E, the tap closes from wherever the
    /// water was, the drift dies with the straighten, he stands still for
    /// exactly three seconds of drips before the walk out, and the
    /// camera's return carries a hitch. The eyes hang under the water and
    /// lift for the drips.
    /// </summary>
    public sealed class HomeShowerSceneTimelineTests
    {
        private const float Step = 1f / 60f;

        private static void Advance(HomeShowerSceneTimeline timeline, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                timeline.Advance(Step);
            }
        }

        private static HomeShowerSceneTimeline ReachApproach()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach));
            return timeline;
        }

        private static HomeShowerSceneTimeline ReachWash()
        {
            HomeShowerSceneTimeline timeline = ReachApproach();
            timeline.NotifyDockReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle));
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            return timeline;
        }

        private static HomeShowerSceneTimeline ReachDripHold(out float washSeconds)
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            washSeconds = HomeShowerSceneTimeline.MinimumWashSeconds + 0.5f;
            timeline.Advance(washSeconds);
            Assert.That(timeline.RequestFinish(), Is.True);
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            return timeline;
        }

        [Test]
        public void TheCameraFliesInBeforeTheHeroArrivesAnywhere()
        {
            var timeline = new HomeShowerSceneTimeline();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.IsInsideHead, Is.False);
            timeline.Begin();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.DriftWeight, Is.Zero, "The walk moves the lens; no breathing on top of it.");
            Assert.That(timeline.IsInsideHead, Is.False);

            Advance(timeline, HomeShowerSceneTimeline.CameraInSeconds * 0.5f);
            Assert.That(timeline.CameraBlend, Is.GreaterThan(0.2f).And.LessThan(0.8f));
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));

            Advance(timeline, HomeShowerSceneTimeline.CameraInSeconds * 0.5f + Step);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeShowerScenePhase.Approach),
                "The lens lands in his head while he is still walking.");
            Assert.That(timeline.IsInsideHead, Is.True);

            Advance(timeline, 3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach), "The walk is open-ended.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
        }

        [Test]
        public void ADockReachedDuringTheFlyInIsRemembered()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Advance(timeline, 0.3f);
            timeline.NotifyDockReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn), "The fly-in is never cut short.");
            Assert.That(timeline.DockReached, Is.True);
            Advance(timeline, HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle), "No approach beat for a hero already there.");
        }

        [Test]
        public void TheDockGetsOneRenderedNeutralFrame()
        {
            HomeShowerSceneTimeline timeline = ReachApproach();
            timeline.NotifyDockReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle));
            Assert.That(timeline.PoseWeight, Is.Zero);
            timeline.Advance(1f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle), "No frame rendered, no wash.");
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            Assert.That(timeline.PoseWeight, Is.LessThan(0.05f));
            timeline.Advance(HomeShowerSceneTimeline.PoseRaiseSeconds);
            Assert.That(timeline.PoseWeight, Is.EqualTo(1f));
            timeline.Advance(HomeShowerSceneTimeline.WaterStartSeconds);
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            Assert.That(timeline.StopPromptVisible, Is.True);
        }

        [Test]
        public void StopIsAcceptedOnlyWhileWashing()
        {
            var timeline = new HomeShowerSceneTimeline();
            Assert.That(timeline.RequestFinish(), Is.False);
            timeline.Begin();
            Assert.That(timeline.RequestFinish(), Is.False, "CameraIn");
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "Approach");
            timeline.NotifyDockReached();
            Assert.That(timeline.RequestFinish(), Is.False, "Settle");
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.RequestFinish(), Is.True, "Wash");
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            Assert.That(timeline.RequestFinish(), Is.False, "WaterOff");
            Assert.That(timeline.StopPromptVisible, Is.False);
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "Straighten");
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "DripHold");
            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut));
            Assert.That(timeline.RequestFinish(), Is.False, "StepOut");
            timeline.NotifyWalkArrived();
            Assert.That(timeline.RequestFinish(), Is.False, "CameraOut");
            timeline.Advance(HomeShowerSceneTimeline.CameraOutSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.RequestFinish(), Is.False, "Completed");
        }

        [Test]
        public void TheTapClosesFromWhereverTheWaterWas()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(HomeShowerSceneTimeline.WaterStartSeconds * 0.5f);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.ConsumeValveCue(), Is.True);
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(timeline.ValveReach, Is.Zero);
            Assert.That(timeline.ValveTurn, Is.Zero);
            Assert.That(timeline.IsDripping, Is.True);
            Assert.That(timeline.DripSteadyRate, Is.Zero, "Nothing drips before the cut begins.");

            timeline.Advance(HomeShowerSceneTimeline.WaterCutStartSeconds);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f).Within(0.01f));
            timeline.Advance(HomeShowerSceneTimeline.ValveReachSeconds - HomeShowerSceneTimeline.WaterCutStartSeconds);
            Assert.That(timeline.ValveReach, Is.EqualTo(1f).Within(0.001f));
            Assert.That(timeline.WaterAmount, Is.LessThan(0.5f).And.GreaterThan(0f));
            Assert.That(timeline.DripSteadyRate, Is.GreaterThan(0f));

            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds - HomeShowerSceneTimeline.ValveReachSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.ValveReach, Is.EqualTo(1f));
            Assert.That(timeline.PoseWeight, Is.EqualTo(1f));
            Assert.That(timeline.DripSteadyRate, Is.EqualTo(HomeShowerDripModel.SteadyDropsPerSecond));
        }

        [Test]
        public void TheDriftDiesWithTheStraighten()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(1f);
            Assert.That(timeline.DriftWeight, Is.EqualTo(1f));
            timeline.RequestFinish();
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            Assert.That(timeline.DriftWeight, Is.EqualTo(1f));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.DriftWeight, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(timeline.PoseWeight, Is.GreaterThan(0f).And.LessThan(1f));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f));
            Assert.That(timeline.PoseWeight, Is.Zero);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The lens stays in his head while he stands.");
        }

        [Test]
        public void TheStillHoldIsExactlyThreeSecondsAndTheReturnCarriesAHitch()
        {
            HomeShowerSceneTimeline timeline = ReachDripHold(out _);
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f), "Exactly zero: the drift's zero branch is `<= 0`.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.DripSteadyRate, Is.Zero, "The hold runs the drip model's own schedule.");
            Assert.That(timeline.IsDripping, Is.True);
            Assert.That(timeline.IsInsideHead, Is.True);

            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds - 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));

            // A hitch of 0.3 s across the boundary is carried into the walk out.
            timeline.Advance(0.5f + 0.3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut));
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(timeline.IsDripping, Is.False);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The lens stays in his head for the walk out.");
            Assert.That(timeline.IsInsideHead, Is.True);
            timeline.Advance(2f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut), "The walk out is open-ended.");

            timeline.NotifyWalkArrived();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut));
            Assert.That(timeline.IsInsideHead, Is.False);
            timeline.Advance(0.3f);
            Assert.That(timeline.CameraBlend, Is.LessThan(1f).And.GreaterThan(0f));
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f));

            // The remainder of the return, plus one frame of float slack:
            // 0.3f + (1.4f - 0.3f) lands a few ulps short of 1.4f.
            timeline.Advance(HomeShowerSceneTimeline.CameraOutSeconds - 0.3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut).Or.EqualTo(HomeShowerScenePhase.Completed));
            Assert.That(timeline.CameraBlend, Is.LessThan(0.001f));
            timeline.Advance(Step);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.DriftWeight, Is.Zero);
            Assert.That(timeline.WaterAmount, Is.Zero);
        }

        [Test]
        public void TheLongWashFinishesOnItsOwn()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            Assert.That(timeline.ReachedMinimumWash, Is.False);
            timeline.Advance(HomeShowerSceneTimeline.MinimumWashSeconds);
            Assert.That(timeline.ReachedMinimumWash, Is.True);
            timeline.Advance(HomeShowerSceneTimeline.AutomaticWashSeconds - HomeShowerSceneTimeline.MinimumWashSeconds + 0.05f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(timeline.ConsumeValveCue(), Is.True);
            Assert.That(timeline.StopPromptVisible, Is.False, "The prompt must leave with the phase.");
        }

        [Test]
        public void AnEarlyStopForfeitsTheReward()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(2f);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.ReachedMinimumWash, Is.False);
            timeline.Advance(
                HomeShowerSceneTimeline.WaterOffSeconds +
                HomeShowerSceneTimeline.StraightenSeconds +
                HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut));
            timeline.NotifyWalkArrived();
            timeline.Advance(HomeShowerSceneTimeline.CameraOutSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.ReachedMinimumWash, Is.False);
        }

        [Test]
        public void TheDripClockRunsFromTheTapToTheEndOfTheHold()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(1f);
            Assert.That(timeline.IsDripping, Is.False);
            Assert.That(timeline.DripClock, Is.Zero);
            timeline.RequestFinish();
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            Assert.That(timeline.DripClock, Is.EqualTo(HomeShowerSceneTimeline.WaterOffSeconds).Within(0.001f));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds);
            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(
                timeline.DripClock,
                Is.EqualTo(HomeShowerSceneTimeline.WaterOffSeconds + HomeShowerSceneTimeline.StraightenSeconds + HomeShowerSceneTimeline.DripHoldSeconds).Within(0.01f));
            Assert.That(timeline.IsDripping, Is.False);
            float frozen = timeline.DripClock;
            timeline.Advance(0.5f);
            Assert.That(timeline.DripClock, Is.EqualTo(frozen), "The clock stops with the hold; the walk out is dry.");
        }

        [Test]
        public void TheSteamLagsTheWater()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(HomeShowerSceneTimeline.WaterStartSeconds);
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0f).And.LessThan(0.6f));
            timeline.Advance(4f);
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0.85f));
            timeline.RequestFinish();
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0.3f), "Steam hangs in the air after the tap.");
        }

        [Test]
        public void TheEyesHangWithTheHeadAndRestOnTheTrayForTheDrips()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WalkPitchDegrees), "Level-ish on the way in.");
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            timeline.NotifyDockReached();
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.ViewPitchDegrees, Is.LessThan(HomeShowerSceneTimeline.WalkPitchDegrees + 2f));
            timeline.Advance(HomeShowerSceneTimeline.PoseRaiseSeconds);
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WashPitchDegrees).Within(0.01f), "Hanging under the water with the head.");
            timeline.Advance(HomeShowerSceneTimeline.MinimumWashSeconds);
            timeline.RequestFinish();
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WashPitchDegrees));
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds);
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            float low = Mathf.Min(HomeShowerSceneTimeline.HoldPitchDegrees, HomeShowerSceneTimeline.WashPitchDegrees);
            float high = Mathf.Max(HomeShowerSceneTimeline.HoldPitchDegrees, HomeShowerSceneTimeline.WashPitchDegrees);
            Assert.That(timeline.ViewPitchDegrees, Is.GreaterThan(low).And.LessThan(high), "The straighten blends the gaze, never snaps it.");
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.HoldPitchDegrees), "Down at the tray for the drips.");
            Assert.That(HomeShowerSceneTimeline.HoldPitchDegrees, Is.GreaterThan(30f), "The tray and his feet must be in the frame while he stands.");
            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WalkPitchDegrees), "Level again for the walk out.");
            Assert.That(HomeShowerSceneTimeline.WashPitchDegrees, Is.EqualTo(HomeShowerWashPose.NeckPitchDegrees + HomeShowerWashPose.HeadPitchDegrees), "The lens hangs exactly as far as the head does.");
        }

        [Test]
        public void ResetPutsEverythingBack()
        {
            HomeShowerSceneTimeline timeline = ReachDripHold(out _);
            timeline.Reset();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.DriftWeight, Is.Zero);
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.SteamAmount, Is.Zero);
            Assert.That(timeline.DripClock, Is.Zero);
            Assert.That(timeline.DockReached, Is.False);
            Assert.That(timeline.ReachedMinimumWash, Is.False);
            Assert.That(timeline.IsInsideHead, Is.False);
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.Advance(5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
        }

        [Test]
        public void AdvanceRejectsNonFiniteTime()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Advance(float.PositiveInfinity));
            timeline.Advance(-1f);
            Assert.That(timeline.PhaseElapsed, Is.Zero, "Negative time is ignored, not rewound.");
        }
    }
}
