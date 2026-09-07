using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The shower's deterministic staging: rendered curtain gestures precede
    /// each crossing, the camera lands before the hero enters, and follows him
    /// out along the same path before the curtain closes. Water keeps its clocks.
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

        private static void FinishGesture(HomeShowerSceneTimeline timeline)
        {
            Assert.That(timeline.IsCurtainGesture, Is.True);
            timeline.NotifyGestureFrameRendered();
            timeline.Advance(HomeShowerCurtainPose.DurationSeconds);
            Assert.That(timeline.GestureNormalized, Is.EqualTo(1f));
            if (timeline.Phase == HomeShowerScenePhase.OpenCurtain)
            {
                Advance(timeline, HomeShowerSceneTimeline.CameraApproachSeconds + HomeShowerSceneTimeline.CameraInSeconds);
                Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
                timeline.NotifyCameraFrameRendered();
            }
            timeline.NotifyGestureFrameRendered();
            timeline.Advance(0f);
        }

        private static HomeShowerSceneTimeline ReachCameraIn()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach));
            timeline.NotifyEntryReached();
            FinishGesture(timeline);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepIn));
            timeline.NotifyWalkArrived();
            FinishGesture(timeline);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            return timeline;
        }

        private static HomeShowerSceneTimeline ReachWaterOn()
        {
            HomeShowerSceneTimeline timeline = ReachCameraIn();
            timeline.NotifyDockReached();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle));
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOn));
            return timeline;
        }

        private static void FinishWaterOn(HomeShowerSceneTimeline timeline)
        {
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOn));
            foreach (float endpoint in new[] { HomeShowerSceneTimeline.WaterOnReachEndSeconds,
                HomeShowerSceneTimeline.WaterOnTurnEndSeconds, HomeShowerSceneTimeline.ColdWaterOnReachEndSeconds,
                HomeShowerSceneTimeline.ColdWaterOnTurnEndSeconds })
            {
                timeline.Advance(Mathf.Max(0f, endpoint - timeline.PhaseElapsed));
                timeline.NotifyValveFrameRendered();
                timeline.ConsumeValveCue();
            }
            timeline.Advance(HomeShowerSceneTimeline.WaterOnSeconds - timeline.PhaseElapsed);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            timeline.ConsumeValveCue();
        }

        private static void FinishWaterOff(HomeShowerSceneTimeline timeline)
        {
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            foreach (float endpoint in new[] { HomeShowerSceneTimeline.ValveReachSeconds,
                HomeShowerSceneTimeline.ValveReachSeconds + HomeShowerSceneTimeline.ValveTurnSeconds,
                HomeShowerSceneTimeline.WaterCutStartSeconds,
                HomeShowerSceneTimeline.WaterCutStartSeconds + HomeShowerSceneTimeline.ValveTurnSeconds })
            {
                timeline.Advance(Mathf.Max(0f, endpoint - timeline.PhaseElapsed));
                timeline.NotifyValveFrameRendered();
            }
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds - timeline.PhaseElapsed);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
        }

        private static HomeShowerSceneTimeline ReachWash()
        {
            HomeShowerSceneTimeline timeline = ReachWaterOn();
            FinishWaterOn(timeline);
            return timeline;
        }

        private static void FinishExit(HomeShowerSceneTimeline timeline)
        {
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachExit));
            timeline.NotifyWalkArrived();
            FinishGesture(timeline);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut));
            timeline.NotifyWalkArrived();
            FinishCameraReturn(timeline);
            timeline.NotifyWalkArrived();
            FinishGesture(timeline);
            Assert.That(timeline.IsCompleted, Is.True);
        }

        private static void FinishCameraReturn(HomeShowerSceneTimeline timeline)
        {
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut));
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.Zero, "The lens waits for a rendered offscreen appearance handoff.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.ExitAppearanceReady, Is.False);
            timeline.NotifyExitAppearanceReady();
            Assert.That(timeline.ExitAppearanceReady, Is.True);
            timeline.Advance(HomeShowerSceneTimeline.CameraOutSeconds);
            Assert.That(timeline.CameraBlend, Is.Zero.Within(0.0001f));
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut));
            Assert.That(timeline.CameraReturned, Is.False);
            timeline.NotifyCameraFrameRendered();
            Assert.That(timeline.CameraReturned, Is.True);
            timeline.Advance(0f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachCloseCurtain));
        }

        private static HomeShowerSceneTimeline ReachDripHold(out float washSeconds)
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            washSeconds = HomeShowerWashingProgress.MinimumActiveSeconds + 0.5f;
            timeline.Advance(washSeconds);
            timeline.NotifyWashingCompleted();
            Assert.That(timeline.RequestFinish(), Is.True);
            FinishWaterOff(timeline);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            return timeline;
        }

        [Test]
        public void TheCameraStartsWithTheApproachAndArrivesBeforeTheHeroEnters()
        {
            var timeline = new HomeShowerSceneTimeline();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f), "The idle tap is closed.");
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.IsInsideHead, Is.False);
            timeline.Begin();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.DriftWeight, Is.Zero, "The walk moves the lens; no breathing on top of it.");
            Assert.That(timeline.IsInsideHead, Is.False);

            Advance(timeline, HomeShowerSceneTimeline.CameraApproachSeconds + Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach), "The walk is open-ended.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(HomeShowerCameraPath.CurtainApproachEnd));
            timeline.NotifyCameraFrameRendered();
            Assert.That(timeline.CameraArrived, Is.False, "An early presentation cannot acknowledge the future eye position.");
            timeline.NotifyEntryReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.OpenCurtain));
            timeline.NotifyGestureFrameRendered();
            timeline.Advance(HomeShowerCurtainPose.DurationSeconds * 0.5f);
            Assert.That(timeline.GestureNormalized, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(timeline.CameraBlend, Is.EqualTo(HomeShowerCameraPath.CurtainApproachEnd),
                "The lens waits before the cloth while the hand is still opening it.");
            timeline.Advance(HomeShowerCurtainPose.DurationSeconds * (HomeShowerCurtainPose.ReleaseStart - 0.5f));
            Assert.That(timeline.CameraBlend, Is.EqualTo(HomeShowerCameraPath.CurtainApproachEnd));
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds * 0.5f);
            Assert.That(timeline.CameraBlend, Is.LessThan(1f), "The opening cannot collapse the newly independent flight into one frame.");
            Assert.That(timeline.CameraArrived, Is.False);
            Advance(timeline, HomeShowerCurtainPose.DurationSeconds + HomeShowerSceneTimeline.CameraInSeconds);
            timeline.NotifyGestureFrameRendered();
            timeline.Advance(0f);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.OpenCurtain), "The hero waits until the landed camera has actually rendered.");
            Assert.That(timeline.CameraArrived, Is.False);
            timeline.NotifyCameraFrameRendered();
            timeline.Advance(0f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepIn));
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.CameraArrived, Is.True);
            Assert.That(timeline.IsInsideHead, Is.False);
            timeline.NotifyWalkArrived();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CloseCurtain));
            FinishGesture(timeline);
            timeline.Advance(3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The camera waits at the future eyes while the hero reaches the dock.");
            Assert.That(timeline.IsInsideHead, Is.False);
        }

        [Test]
        public void TheLandedCameraWaitsForTheGroundedDock()
        {
            HomeShowerSceneTimeline timeline = ReachCameraIn();
            timeline.NotifyDockReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            Assert.That(timeline.CameraArrived, Is.True);
            Assert.That(timeline.DockReached, Is.True);
            Advance(timeline, HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle), "No approach beat for a hero already there.");
        }

        [Test]
        public void AHeroAlreadyInsideSkipsTheEntryCurtainAndStillWaitsForTheDock()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin(alreadyInside: true);
            timeline.NotifyEntryReached();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds + Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.CameraArrived, Is.False);
            timeline.NotifyCameraFrameRendered();
            timeline.Advance(0f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn), "The camera arrives before the grounded hero.");
            timeline.NotifyDockReached();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle));
        }

        [Test]
        public void EveryCurtainGestureHoldsBothRenderedEndpointsEvenThroughAHitch()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            timeline.NotifyEntryReached();
            foreach (HomeShowerScenePhase gesture in new[]
            {
                HomeShowerScenePhase.OpenCurtain, HomeShowerScenePhase.CloseCurtain,
                HomeShowerScenePhase.OpenExitCurtain, HomeShowerScenePhase.CloseExitCurtain
            })
            {
                Assert.That(timeline.Phase, Is.EqualTo(gesture));
                timeline.Advance(20f);
                Assert.That(timeline.GestureNormalized, Is.Zero, "The neutral entry must be seen.");
                Assert.That(timeline.PhaseElapsed, Is.Zero);
                timeline.NotifyGestureFrameRendered();
                timeline.Advance(20f);
                Assert.That(timeline.Phase, Is.EqualTo(gesture), "A hitch cannot skip the terminal pose.");
                Assert.That(timeline.GestureNormalized, Is.EqualTo(1f));
                if (gesture == HomeShowerScenePhase.OpenCurtain)
                {
                    timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
                    timeline.NotifyCameraFrameRendered();
                }
                timeline.NotifyGestureFrameRendered();
                timeline.Advance(0f);
                if (gesture == HomeShowerScenePhase.OpenCurtain || gesture == HomeShowerScenePhase.OpenExitCurtain)
                {
                    timeline.NotifyWalkArrived();
                    if (gesture == HomeShowerScenePhase.OpenExitCurtain)
                    {
                        FinishCameraReturn(timeline);
                        timeline.NotifyWalkArrived();
                    }
                }
                else if (gesture == HomeShowerScenePhase.CloseCurtain)
                {
                    timeline.NotifyDockReached();
                    timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
                    timeline.NotifySettleFrameRendered();
                    timeline.Advance(0f);
                    FinishWaterOn(timeline);
                    timeline.RequestFinish();
                    FinishWaterOff(timeline);
                    timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds + HomeShowerSceneTimeline.DripHoldSeconds + Step);
                    timeline.NotifyWalkArrived();
                }
            }
            Assert.That(timeline.IsCompleted, Is.True);
        }

        [Test]
        public void TheDockRendersNeutralBeforeTheHandOpensTheValve()
        {
            HomeShowerSceneTimeline timeline = ReachCameraIn();
            timeline.NotifyDockReached();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle));
            Assert.That(timeline.PoseWeight, Is.Zero);
            timeline.Advance(1f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Settle), "No frame rendered, no wash.");
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOn));
            Assert.That(timeline.PoseWeight, Is.LessThan(0.05f));
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.Zero);
            timeline.NotifyValveFrameRendered();
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.WaterOnReachEndSeconds));
            Assert.That(timeline.PoseWeight, Is.EqualTo(1f));
            Assert.That(timeline.ValveReach, Is.EqualTo(1f));
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.Zero, "A hitch cannot turn the valve before the reached hand has rendered.");
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.NotifyValveFrameRendered();
            timeline.Advance(HomeShowerSceneTimeline.ValveTurnSeconds * 0.5f);
            Assert.That(timeline.ValveTurn, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(timeline.WaterAmount, Is.EqualTo((1f - timeline.ValveTurn) * 0.5f).Within(0.001f));
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f), "Cold stays closed throughout the right-hand turn.");
            Assert.That(timeline.ConsumeValveCue(), Is.True, "The sound belongs to the actual valve turn.");
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.WaterOnTurnEndSeconds));
            Assert.That(timeline.ValveTurn, Is.Zero);
            Assert.That(timeline.ValveReach, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f));
            timeline.Advance(20f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOn), "The open endpoint also has to render before the hand releases.");
            timeline.NotifyValveFrameRendered();
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.ColdWaterOnReachEndSeconds));
            Assert.That(timeline.WorkingValveIsCold, Is.True);
            Assert.That(timeline.ValveReach, Is.Zero);
            Assert.That(timeline.ColdValveReach, Is.EqualTo(1f));
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f));
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.NotifyValveFrameRendered();
            timeline.Advance(HomeShowerSceneTimeline.ValveTurnSeconds * 0.5f);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(timeline.ConsumeValveCue(), Is.True, "The left cold-wheel turn has its own cue.");
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.ColdWaterOnTurnEndSeconds));
            Assert.That(timeline.ColdValveTurn, Is.Zero);
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            timeline.NotifyValveFrameRendered();
            timeline.Advance(HomeShowerSceneTimeline.WaterOnSeconds - timeline.PhaseElapsed);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            Assert.That(timeline.ValveReach, Is.Zero, "The free right palm returns to its wall brace.");
            Assert.That(timeline.ColdValveReach, Is.Zero, "The left palm also returns to the wall after its turn.");
            Assert.That(timeline.ConsumeValveCue(), Is.False, "One opening emits one valve cue.");
            Assert.That(timeline.StopPromptVisible, Is.True);
        }

        [Test]
        public void StopIsAcceptedOnlyWhileWashing()
        {
            var timeline = new HomeShowerSceneTimeline();
            Assert.That(timeline.RequestFinish(), Is.False);
            timeline.Begin();
            Assert.That(timeline.RequestFinish(), Is.False, "Approach");
            timeline.NotifyEntryReached();
            Assert.That(timeline.RequestFinish(), Is.False, "OpenCurtain");
            FinishGesture(timeline);
            Assert.That(timeline.RequestFinish(), Is.False, "StepIn");
            timeline.NotifyWalkArrived();
            Assert.That(timeline.RequestFinish(), Is.False, "CloseCurtain");
            FinishGesture(timeline);
            Assert.That(timeline.RequestFinish(), Is.False, "CameraIn");
            timeline.NotifyDockReached();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "Settle");
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.RequestFinish(), Is.False, "WaterOn");
            FinishWaterOn(timeline);
            Assert.That(timeline.RequestFinish(), Is.True, "Wash");
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            Assert.That(timeline.RequestFinish(), Is.False, "WaterOff");
            Assert.That(timeline.StopPromptVisible, Is.False);
            FinishWaterOff(timeline);
            Assert.That(timeline.RequestFinish(), Is.False, "Straighten");
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "DripHold");
            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(timeline.RequestFinish(), Is.False, "ApproachExit");
            timeline.NotifyWalkArrived();
            Assert.That(timeline.RequestFinish(), Is.False, "OpenExitCurtain");
            FinishGesture(timeline);
            Assert.That(timeline.RequestFinish(), Is.False, "StepOut");
            timeline.NotifyWalkArrived();
            Assert.That(timeline.RequestFinish(), Is.False, "CameraOut");
            FinishCameraReturn(timeline);
            Assert.That(timeline.RequestFinish(), Is.False, "ApproachCloseCurtain");
            timeline.NotifyWalkArrived();
            Assert.That(timeline.RequestFinish(), Is.False, "CloseExitCurtain");
            FinishGesture(timeline);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.RequestFinish(), Is.False, "Completed");
        }

        [Test]
        public void TheWaterStaysOnUntilTheClosingHandTurnsTheValve()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            Assert.That(timeline.ValveReach, Is.Zero);
            Assert.That(timeline.ValveTurn, Is.Zero);
            Assert.That(timeline.IsDripping, Is.True);
            Assert.That(timeline.DripSteadyRate, Is.Zero, "Nothing drips before the cut begins.");

            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.ValveReachSeconds));
            Assert.That(timeline.WorkingValveIsCold, Is.True);
            Assert.That(timeline.ColdValveReach, Is.EqualTo(1f));
            Assert.That(timeline.ValveReach, Is.Zero);
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f), "Reaching for a valve cannot turn off its water.");
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.NotifyValveFrameRendered();
            float halfTurn = HomeShowerSceneTimeline.ValveTurnSeconds * 0.5f;
            timeline.Advance(halfTurn);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(timeline.ValveTurn, Is.Zero, "Hot stays open while the left hand closes cold.");
            Assert.That(timeline.ConsumeValveCue(), Is.True);
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.ValveReachSeconds + HomeShowerSceneTimeline.ValveTurnSeconds));
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f));
            timeline.NotifyValveFrameRendered();
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.WaterCutStartSeconds));
            Assert.That(timeline.WorkingValveIsCold, Is.False);
            Assert.That(timeline.ColdValveReach, Is.Zero);
            Assert.That(timeline.ValveReach, Is.EqualTo(1f));
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.5f));
            timeline.NotifyValveFrameRendered();
            timeline.Advance(halfTurn);
            Assert.That(timeline.WaterAmount, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(timeline.ConsumeValveCue(), Is.True);
            Assert.That(timeline.DripSteadyRate, Is.GreaterThan(0f));
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.WaterCutStartSeconds + HomeShowerSceneTimeline.ValveTurnSeconds));
            Assert.That(timeline.WaterAmount, Is.Zero);
            timeline.NotifyValveFrameRendered();
            timeline.Advance(HomeShowerSceneTimeline.WaterOffSeconds - timeline.PhaseElapsed);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.ValveReach, Is.Zero);
            Assert.That(timeline.ColdValveReach, Is.Zero);
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f));
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
            FinishWaterOff(timeline);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Straighten));
            Assert.That(timeline.DriftWeight, Is.EqualTo(1f));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.DriftWeight, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(timeline.PoseWeight, Is.GreaterThan(0f).And.LessThan(1f));
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f));
            Assert.That(timeline.PoseWeight, Is.Zero);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The detached lens stays inside the stall while he stands.");
        }

        [Test]
        public void TheCameraWaitsForTheHeroToExitAndItsReturnMustRenderBeforeTheCurtainCloses()
        {
            HomeShowerSceneTimeline timeline = ReachDripHold(out _);
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f), "Exactly zero: the drift's zero branch is `<= 0`.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.DripSteadyRate, Is.Zero, "The hold runs the drip model's own schedule.");
            Assert.That(timeline.IsDripping, Is.True);
            Assert.That(timeline.IsInsideHead, Is.False, "The camera detaches while he straightens.");

            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds - 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));

            // Even a hitch cannot start the return before the hero crosses the curtain.
            timeline.Advance(0.5f + 0.3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachExit));
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(timeline.IsDripping, Is.False);
            Assert.That(timeline.IsInsideHead, Is.False);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.DriftWeight, Is.EqualTo(0f));

            Assert.That(timeline.WaterAmount, Is.Zero);
            timeline.Advance(20f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachExit), "The return to the curtain waits for the motor.");
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            timeline.NotifyWalkArrived();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.OpenExitCurtain));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The camera watches the opening from inside.");
            FinishGesture(timeline);
            timeline.NotifyCameraFrameRendered(); // An early acknowledgement must not open the return gate.
            timeline.NotifyExitAppearanceReady(); // Likewise, a handoff outside CameraOut cannot release its clock.
            timeline.Advance(20f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.StepOut));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            timeline.NotifyWalkArrived();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.CameraReturned, Is.False);
            timeline.NotifyCameraFrameRendered();
            timeline.Advance(20f);
            Assert.That(timeline.PhaseElapsed, Is.Zero);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f), "The hero must first leave the frame and restore his clothes.");
            timeline.NotifyExitAppearanceReady();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            Assert.That(timeline.CameraBlend, Is.EqualTo(HomeShowerCameraPath.CurtainApproachEnd).Within(0.0001f),
                "The inner leg uses the same duration in reverse.");
            timeline.Advance(HomeShowerSceneTimeline.CameraApproachSeconds * 0.5f);
            Assert.That(timeline.CameraBlend, Is.EqualTo(HomeShowerCameraPath.CurtainApproachEnd * 0.5f).Within(0.0001f));
            timeline.Advance(20f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraOut), "Even a hitch must present the exact original camera position.");
            Assert.That(timeline.PhaseElapsed, Is.EqualTo(HomeShowerSceneTimeline.CameraOutSeconds));
            Assert.That(timeline.CameraBlend, Is.Zero);
            Assert.That(timeline.CameraReturned, Is.False);
            timeline.NotifyCameraFrameRendered();
            timeline.Advance(0f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachCloseCurtain));
            Assert.That(timeline.CameraReturned, Is.True);
            timeline.Advance(20f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachCloseCurtain),
                "After the lens returns, the motor must turn toward the original closing gesture before it begins.");
            Assert.That(timeline.CameraBlend, Is.Zero);
            timeline.NotifyWalkArrived();
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CloseExitCurtain));
            FinishGesture(timeline);
            Assert.That(timeline.IsCompleted, Is.True);
        }

        [Test]
        public void WaitingUnderWaterNeitherCompletesCleaningNorClosesTheTap()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            Assert.That(timeline.ReachedMinimumWash, Is.False);
            timeline.Advance(120f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            Assert.That(timeline.ReachedMinimumWash, Is.False, "Time alone cannot earn interactive washing relief.");
            Assert.That(timeline.ConsumeValveCue(), Is.False);
            Assert.That(timeline.StopPromptVisible, Is.True);
            timeline.NotifyWashingCompleted();
            Assert.That(timeline.ReachedMinimumWash, Is.True);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash), "The owner must first put the held soap back.");
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            Assert.That(timeline.PhaseElapsed, Is.Zero);
            Assert.That(timeline.ConsumeValveCue(), Is.False, "The closing cue waits for the hand to turn the valve.");
            Assert.That(timeline.StopPromptVisible, Is.False, "The prompt must leave with the phase.");
        }

        [Test]
        public void AnEarlyStopForfeitsTheReward()
        {
            HomeShowerSceneTimeline timeline = ReachWash();
            timeline.Advance(2f);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.ReachedMinimumWash, Is.False);
            FinishWaterOff(timeline);
            timeline.Advance(
                HomeShowerSceneTimeline.StraightenSeconds +
                HomeShowerSceneTimeline.DripHoldSeconds);
            FinishExit(timeline);
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
            FinishWaterOff(timeline);
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
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0f).And.LessThan(timeline.WaterAmount));
            timeline.Advance(4f);
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0.85f));
            timeline.RequestFinish();
            FinishWaterOff(timeline);
            Assert.That(timeline.WaterAmount, Is.Zero);
            Assert.That(timeline.SteamAmount, Is.GreaterThan(0.3f), "Steam hangs in the air after the tap.");
        }

        [Test]
        public void TheEyesHangWithTheHeadAndRestOnTheTrayForTheDrips()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WalkPitchDegrees), "Level-ish on the way in.");
            timeline.NotifyEntryReached();
            FinishGesture(timeline);
            timeline.NotifyWalkArrived();
            FinishGesture(timeline);
            timeline.NotifyDockReached();
            timeline.Advance(HomeShowerSceneTimeline.CameraInSeconds);
            timeline.NotifySettleFrameRendered();
            timeline.Advance(Step);
            Assert.That(timeline.ViewPitchDegrees, Is.LessThan(HomeShowerSceneTimeline.WalkPitchDegrees + 2f));
            timeline.Advance(HomeShowerSceneTimeline.PoseRaiseSeconds);
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WashPitchDegrees).Within(0.01f), "Hanging under the water with the head.");
            FinishWaterOn(timeline);
            timeline.RequestFinish();
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WashPitchDegrees));
            FinishWaterOff(timeline);
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            float low = Mathf.Min(HomeShowerSceneTimeline.HoldPitchDegrees, HomeShowerSceneTimeline.WashPitchDegrees);
            float high = Mathf.Max(HomeShowerSceneTimeline.HoldPitchDegrees, HomeShowerSceneTimeline.WashPitchDegrees);
            Assert.That(timeline.ViewPitchDegrees, Is.GreaterThan(low).And.LessThan(high), "The straighten blends the gaze, never snaps it.");
            timeline.Advance(HomeShowerSceneTimeline.StraightenSeconds * 0.5f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.HoldPitchDegrees), "Down at the tray for the drips.");
            Assert.That(HomeShowerSceneTimeline.HoldPitchDegrees, Is.GreaterThan(30f), "The tray and his feet must be in the frame while he stands.");
            timeline.Advance(HomeShowerSceneTimeline.DripHoldSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeShowerScenePhase.ApproachExit));
            Assert.That(timeline.ViewPitchDegrees, Is.EqualTo(HomeShowerSceneTimeline.WalkPitchDegrees), "The walking actor no longer controls the parked lens.");
            FinishExit(timeline);
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
            Assert.That(timeline.ValveTurn, Is.EqualTo(1f), "Cleanup leaves the real valve closed for the next visit.");
            Assert.That(timeline.ColdValveTurn, Is.EqualTo(1f));
            Assert.That(timeline.SteamAmount, Is.Zero);
            Assert.That(timeline.DripClock, Is.Zero);
            Assert.That(timeline.DockReached, Is.False);
            Assert.That(timeline.CameraReturned, Is.False);
            Assert.That(timeline.ExitAppearanceReady, Is.False);
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
