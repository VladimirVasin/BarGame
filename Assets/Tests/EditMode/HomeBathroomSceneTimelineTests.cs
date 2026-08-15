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
        public void Toilet_RunsPrivacyCutAndFlushesOnce()
        {
            var timeline = new HomeToiletSceneTimeline();
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.Idle));
            timeline.Begin();
            Assert.That(timeline.CameraBlend, Is.Zero);

            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraAwaySeconds + 0.05f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.Privacy));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.ConsumeFlushCue(), Is.False);

            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.FlushTimeSeconds + 0.05f);
            Assert.That(timeline.ConsumeFlushCue(), Is.True);
            Assert.That(
                timeline.ConsumeFlushCue(),
                Is.False,
                "The flush must land exactly once.");
            Assert.That(timeline.WaterAmount, Is.GreaterThan(0f));

            Advance(timeline.Advance, 2f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.CameraReturn));
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraReturnSeconds + 0.05f);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.WaterAmount, Is.Zero);
        }

        [Test]
        public void Toilet_StopRequestShortensPrivacyAfterMinimum()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraAwaySeconds + 0.05f);
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.MinimumPrivacySeconds +
                0.05f);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.CameraReturn));
            Assert.That(timeline.ReachedMinimumPrivacy, Is.True);
            Assert.That(
                timeline.ConsumeFlushCue(),
                Is.True,
                "An early finish still flushes before the camera " +
                "turns back.");
        }

        [Test]
        public void Toilet_InterruptsBeforeMinimumWithoutReward()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraAwaySeconds + 0.05f);
            Advance(timeline.Advance, 0.5f);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.CameraReturn));
            Assert.That(timeline.ReachedMinimumPrivacy, Is.False);
            Assert.That(
                timeline.RequestFinish(),
                Is.False,
                "A second stop during the wind-down is refused.");
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraReturnSeconds + 0.05f);
            Assert.That(timeline.IsCompleted, Is.True);
        }

        [Test]
        public void Toilet_AbortDuringCameraAwayKeepsBlendAndFlush()
        {
            var timeline = new HomeToiletSceneTimeline();
            timeline.Begin();
            Advance(timeline.Advance, 1.2f);
            float blendBefore = timeline.CameraBlend;
            Assert.That(
                blendBefore,
                Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeToiletScenePhase.CameraReturn));
            Assert.That(
                timeline.CameraBlend,
                Is.EqualTo(blendBefore).Within(0.001f),
                "An abort mid-retreat must not snap the camera.");
            Advance(
                timeline.Advance,
                HomeToiletSceneTimeline.CameraReturnSeconds + 0.05f);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(
                timeline.ConsumeFlushCue(),
                Is.False,
                "Nothing happened yet — the abort must not flush.");
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
        public void Brushing_RaisesArmScrubsAndRinses()
        {
            var timeline = new HomeTeethBrushingTimeline();
            timeline.Begin();
            Assert.That(timeline.ArmWeight, Is.Zero);

            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.ArmRaiseStartSeconds +
                (HomeTeethBrushingTimeline.ArmRaiseSeconds * 0.5f));
            Assert.That(
                timeline.ArmWeight,
                Is.GreaterThan(0f).And.LessThan(1f));

            // Cross into Brushing with only a small remainder so the
            // foam has not started yet.
            Advance(
                timeline.Advance,
                (HomeTeethBrushingTimeline.CameraToMirrorSeconds -
                 HomeTeethBrushingTimeline.ArmRaiseStartSeconds -
                 (HomeTeethBrushingTimeline.ArmRaiseSeconds * 0.5f)) +
                0.1f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeTeethBrushingPhase.Brushing));
            Assert.That(timeline.ArmWeight, Is.EqualTo(1f));
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.FoamVisible, Is.False);

            int scrubs = 0;
            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                timeline.Advance(Step);
                elapsed += Step;
                if (timeline.ConsumeScrubCue())
                {
                    scrubs++;
                }
            }

            Assert.That(scrubs, Is.GreaterThanOrEqualTo(3));
            Assert.That(
                timeline.FoamVisible,
                Is.True,
                "Foam must appear after a couple of seconds.");

            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.MinimumBrushSeconds);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeTeethBrushingPhase.Rinse));

            bool firstPour = false;
            bool secondPour = false;
            elapsed = 0f;
            float dipPeak = 0f;
            while (elapsed < HomeTeethBrushingTimeline.RinseSeconds)
            {
                timeline.Advance(Step);
                elapsed += Step;
                dipPeak = Mathf.Max(dipPeak, timeline.RinseDip);
                if (timeline.ConsumePourCue())
                {
                    if (!firstPour)
                    {
                        firstPour = true;
                    }
                    else
                    {
                        secondPour = true;
                    }
                }
            }

            Assert.That(firstPour, Is.True);
            Assert.That(secondPour, Is.True);
            Assert.That(dipPeak, Is.EqualTo(1f).Within(0.01f));
            Assert.That(timeline.ArmWeight, Is.Zero);

            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.CameraReturnSeconds +
                0.1f);
            Assert.That(timeline.IsCompleted, Is.True);
        }

        [Test]
        public void Brushing_InterruptsBeforeMinimumWithoutReward()
        {
            var timeline = new HomeTeethBrushingTimeline();
            timeline.Begin();
            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.CameraToMirrorSeconds +
                1f);
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeTeethBrushingPhase.Rinse),
                "A stop mid-brushing still passes the rinse beat.");
            Assert.That(timeline.ReachedMinimumBrush, Is.False);
            Advance(
                timeline.Advance,
                HomeTeethBrushingTimeline.RinseSeconds +
                HomeTeethBrushingTimeline.CameraReturnSeconds +
                0.2f);
            Assert.That(timeline.IsCompleted, Is.True);
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
            Assert.That(timeline.ReachedMinimumBrush, Is.False);
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
