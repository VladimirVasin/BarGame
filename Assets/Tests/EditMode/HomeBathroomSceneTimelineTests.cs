using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBathroomSceneTimelineTests
    {
        private const float Step = 1f / 60f;

        private static void Advance(
            System.Action<float> advance,
            float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                advance(Step);
            }
        }

        [Test]
        public void Toilet_RunsSixSecondsThenTwoSecondShakeAndFlushesOnce()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            Assert.That(timeline.CameraBlend, Is.Zero);
            timeline.Advance(HomeToiletSceneTimeline.EnterSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletScenePhase.Urinating));
            Assert.That(timeline.RemainingAmount, Is.EqualTo(1f));
            timeline.Advance(3f);
            Assert.That(timeline.RemainingAmount, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(timeline.UrineFlow, Is.EqualTo(1f));
            Assert.That(HomeToiletSceneTimeline.EvaluateUrineFlow(4.8f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(HomeToiletSceneTimeline.EvaluateUrineFlow(5.4f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HomeToiletSceneTimeline.AverageUrineFlow(4.8f, 6f), Is.EqualTo(0.5f).Within(0.0001f));
            timeline.Advance(3f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletScenePhase.Shaking));
            Assert.That(timeline.RemainingAmount, Is.Zero);
            Assert.That(timeline.UrineFlow, Is.Zero);
            Assert.That(timeline.ConsumeFlushCue(), Is.False);
            timeline.Advance(2f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletScenePhase.Exiting));
            Assert.That(timeline.ConsumeFlushCue(), Is.True);
            Assert.That(timeline.ConsumeFlushCue(), Is.False);
            timeline.Advance(HomeToiletSceneTimeline.ExitSeconds);
            Assert.That(timeline.CanCommit, Is.True);
        }

        [Test]
        public void Toilet_StopDuringEmissionNeverCommitsOrFlushes()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            timeline.Advance(HomeToiletSceneTimeline.EnterSeconds + 4f);
            Assert.That(timeline.RequestFinish(), Is.True);
            timeline.Advance(HomeToiletSceneTimeline.ExitSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.CanCommit, Is.False);
            Assert.That(timeline.ConsumeFlushCue(), Is.False);
        }

        [Test]
        public void Toilet_HitchCarriesAllPhaseTimeExactlyOnce()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            timeline.Advance(30f);
            Assert.That(timeline.TotalUrinatingSeconds, Is.EqualTo(6f));
            Assert.That(timeline.TotalShakingSeconds, Is.EqualTo(2f));
            Assert.That(timeline.CanCommit, Is.True);
            Assert.That(timeline.ConsumeFlushCue(), Is.True);
            Assert.That(timeline.ConsumeFlushCue(), Is.False);
        }

        [Test]
        public void Toilet_AbortDuringEntryKeepsCameraBlend()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            timeline.Advance(HomeToiletSceneTimeline.EnterSeconds * 0.4f);
            float before = timeline.CameraBlend;
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.CameraBlend, Is.EqualTo(before).Within(0.0001f));
            Assert.That(timeline.RequestFinish(), Is.False);
            timeline.Advance(HomeToiletSceneTimeline.ExitSeconds);
            Assert.That(timeline.CanCommit, Is.False);
            Assert.That(timeline.ConsumeFlushCue(), Is.False);
        }

        [Test]
        public void Shower_DrawsCurtainRunsWaterAndOpensAgain()
        {
            var timeline = new HomeShowerSceneTimeline();
            Assert.That(
                timeline.CurtainScale,
                Is.EqualTo(
                    HomeShowerSceneTimeline.GatheredCurtainScale));
            timeline.Begin();

            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.CurtainCloseSeconds * 0.5f);
            float midClose = timeline.CurtainScale;
            Assert.That(
                midClose,
                Is.GreaterThan(
                    HomeShowerSceneTimeline.GatheredCurtainScale)
                    .And.LessThan(1f));

            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.CurtainCloseSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeShowerScenePhase.WaterStart));
            Assert.That(timeline.CurtainScale, Is.EqualTo(1f));

            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.WaterStartSeconds + 0.05f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeShowerScenePhase.Hold));
            Assert.That(timeline.WaterAmount, Is.EqualTo(1f));

            // Camera arrives during the hold.
            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.CameraArrivalSeconds);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));

            // An early stop is honoured too, but forfeits the reward.
            var early = new HomeShowerSceneTimeline();
            early.Begin();
            Advance(
                early.Advance,
                HomeShowerSceneTimeline.CurtainCloseSeconds +
                HomeShowerSceneTimeline.WaterStartSeconds + 0.1f);
            Assert.That(early.RequestFinish(), Is.True);
            Assert.That(
                early.Phase,
                Is.EqualTo(HomeShowerScenePhase.WaterStop));
            Assert.That(early.ReachedMinimumHold, Is.False);
            Advance(early.Advance, 4f);
            Assert.That(early.IsCompleted, Is.True);
            Assert.That(
                early.CurtainScale,
                Is.EqualTo(
                    HomeShowerSceneTimeline.GatheredCurtainScale));

            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.MinimumHoldSeconds);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeShowerScenePhase.WaterStop));
            Assert.That(timeline.ReachedMinimumHold, Is.True);

            Advance(timeline.Advance, 4f);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(
                timeline.CurtainScale,
                Is.EqualTo(
                    HomeShowerSceneTimeline.GatheredCurtainScale));
            Assert.That(timeline.WaterAmount, Is.Zero);
        }

        [Test]
        public void Shower_AutoFinishesAfterTheLongHold()
        {
            var timeline = new HomeShowerSceneTimeline();
            timeline.Begin();
            Advance(
                timeline.Advance,
                HomeShowerSceneTimeline.CurtainCloseSeconds +
                HomeShowerSceneTimeline.WaterStartSeconds +
                HomeShowerSceneTimeline.AutomaticHoldSeconds +
                HomeShowerSceneTimeline.WaterStopSeconds +
                HomeShowerSceneTimeline.CurtainOpenSeconds +
                0.3f);
            Assert.That(timeline.IsCompleted, Is.True);
        }

        [Test]
        public void Brushing_RequiresMouseTravelAtTeethThenShowsTeethAndSpits()
        {
            var timeline = new HomeTeethBrushingTimeline();
            var progress = new HomeTeethBrushingProgress();
            timeline.Begin();
            Assert.That(timeline.ArmWeight, Is.Zero);
            timeline.Advance(
                HomeTeethBrushingTimeline.ArmRaiseStartSeconds +
                HomeTeethBrushingTimeline.ArmRaiseSeconds * 0.5f);
            Assert.That(timeline.ArmWeight, Is.GreaterThan(0f).And.LessThan(1f));
            timeline.Advance(HomeTeethBrushingTimeline.CameraToMirrorSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.Brushing));
            Assert.That(timeline.ArmWeight, Is.EqualTo(1f));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));

            // Waiting, body motion alone and tiny mouse jitter earn nothing.
            timeline.Advance(60f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.Brushing));
            Assert.That(progress.Credit(0.1f, true, 60f), Is.Zero);
            progress.Move(Vector2.right);
            Assert.That(progress.Offset, Is.EqualTo(Vector2.zero));
            Assert.That(progress.Credit(0.1f, true, Step), Is.Zero);
            progress.Move(Vector2.right * 100f);
            Assert.That(progress.Credit(0.1f, false, Step), Is.Zero);
            Assert.That(progress.Credit(0.1f, true, Step), Is.Zero,
                "Travel without contact cannot be banked for a later frame.");
            progress.Move(Vector2.left * 100f);
            Assert.That(progress.Credit(0f, true, Step), Is.Zero,
                "Mouse input alone cannot clean when the actual brush is stationary.");
            Assert.That(progress.Amount, Is.Zero);

            // Alternating strokes must physically move the brush at the teeth;
            // each frame is capped so violent mouse motion cannot finish early.
            int strokes = 0;
            while (!progress.Complete && strokes < 600)
            {
                Vector2 before = progress.Offset;
                progress.Move(Vector2.right * (strokes % 2 == 0 ? 100f : -100f));
                float travel = Vector2.Distance(before, progress.Offset);
                float credited = progress.Credit(travel, true, Step);
                Assert.That(credited,
                    Is.LessThanOrEqualTo(HomeTeethBrushingProgress.MaximumCreditSpeed * Step + 0.000001f));
                timeline.Advance(Step);
                strokes++;
            }
            Assert.That(progress.Complete, Is.True);
            Assert.That(progress.Amount, Is.EqualTo(1f));
            Assert.That(strokes * Step, Is.GreaterThanOrEqualTo(7.99f));
            Assert.That(timeline.CanCommit, Is.False);
            timeline.CompleteBrushing();
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.ShowTeeth));
            Assert.That(timeline.Cleaned, Is.True);
            Assert.That(timeline.RequestFinish(), Is.False,
                "The completed brush action owns the short teeth/spit finale.");
            timeline.Advance(HomeTeethBrushingTimeline.ShowTeethSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.Spit));
            Assert.That(timeline.ArmWeight, Is.Zero);
            Assert.That(timeline.EmissionSeconds, Is.Zero,
                "Spitting follows the tooth display, never overlaps it.");
            timeline.Advance(HomeTeethBrushingTimeline.SpitStartSeconds);
            Assert.That(timeline.SpitBend, Is.EqualTo(1f).Within(0.001f));
            Assert.That(timeline.EmissionSeconds, Is.Zero);
            timeline.Advance(HomeTeethBrushingTimeline.SpitSeconds -
                HomeTeethBrushingTimeline.SpitStartSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.CameraReturn));
            Assert.That(timeline.EmissionSeconds,
                Is.EqualTo(HomeTeethBrushingTimeline.SpitEndSeconds -
                    HomeTeethBrushingTimeline.SpitStartSeconds).Within(0.0001f));
            Assert.That(timeline.CanCommit, Is.False);
            timeline.Advance(HomeTeethBrushingTimeline.CameraReturnSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.CanCommit, Is.True);
            Assert.That(timeline.CameraBlend, Is.Zero);
        }

        [Test]
        public void Brushing_CancelsPartialCleaningWithoutTeethSpitOrReward()
        {
            var timeline = new HomeTeethBrushingTimeline();
            var progress = new HomeTeethBrushingProgress();
            timeline.Begin();
            timeline.Advance(HomeTeethBrushingTimeline.CameraToMirrorSeconds);
            progress.Move(Vector2.right * 100f);
            progress.Credit(progress.Offset.magnitude, true, 0.2f);
            Assert.That(progress.Amount, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.CameraReturn));
            Assert.That(timeline.RequestFinish(), Is.False);
            timeline.CompleteBrushing();
            Assert.That(timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.CameraReturn),
                "A stale completion after cancellation cannot start the finale.");
            timeline.Advance(HomeTeethBrushingTimeline.CameraReturnSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.WasCancelled, Is.True);
            Assert.That(timeline.Cleaned, Is.False);
            Assert.That(timeline.CanCommit, Is.False);
            Assert.That(timeline.EmissionSeconds, Is.Zero);
        }

        [Test]
        public void Brushing_AbortDuringCameraPushKeepsBlend()
        {
            var timeline = new HomeTeethBrushingTimeline();
            timeline.Begin();
            Advance(timeline.Advance, 1.6f);
            float blendBefore = timeline.CameraBlend;
            Assert.That(
                blendBefore,
                Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeTeethBrushingPhase.CameraReturn));
            Assert.That(
                timeline.CameraBlend,
                Is.EqualTo(blendBefore).Within(0.001f),
                "An abort mid-push must not snap the camera.");
            Assert.That(timeline.Cleaned, Is.False);
            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.CameraReturnSeconds +
                0.1f);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.ArmWeight, Is.Zero);
        }

        [Test]
        public void TeethBrushingRelief_IsGatedOncePerGameDay()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.UpdateNeeds(0, 40);

            Assert.That(
                GameSessionState.TryCommitTeethBrushingRelief(5),
                Is.True);
            int stressAfterFirst = GameSessionState.StressLevel;
            Assert.That(stressAfterFirst, Is.EqualTo(35));
            Assert.That(
                GameSessionState.LastTeethBrushingDayIndex,
                Is.EqualTo(GameSessionState.GameDayIndex));

            Assert.That(
                GameSessionState.TryCommitTeethBrushingRelief(5),
                Is.False,
                "A second brushing the same game day must commit " +
                "nothing.");
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(stressAfterFirst));

            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.LastTeethBrushingDayIndex,
                Is.EqualTo(-1),
                "A new game must re-arm the daily gate.");
        }

        [Test]
        public void BathroomStressRelief_CommitsUngated()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.UpdateNeeds(0, 30);

            GameSessionState.CommitBathroomStressRelief("shower", 12);
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(18));
            GameSessionState.CommitBathroomStressRelief("toilet", 6);
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(12));
            GameSessionState.BeginNewGame();
        }
    }
}
