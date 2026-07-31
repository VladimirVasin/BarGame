using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeRefrigeratorTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void OpenSequence_UsesContinuousExactPhaseEndpoints()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();

            AssertClosed(timeline);
            Assert.That(timeline.BeginOpen(), Is.True);
            Assert.That(timeline.BeginOpen(), Is.False);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.CameraApproach,
                camera: 0f,
                door: 0f,
                handle: 0f,
                hand: 0f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .CameraApproachDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Reach,
                camera: 1f,
                door: 0f,
                handle: 0f,
                hand: 0f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .ReachDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Unsealing,
                camera: 1f,
                door: 0f,
                handle: 0f,
                hand: 1f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .UnsealingDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Opening,
                camera: 1f,
                door: 0f,
                handle: 1f,
                hand: 1f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .OpeningDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Inspecting,
                camera: 1f,
                door: 1f,
                handle: 0f,
                hand: 0f,
                light: 1f);
            Assert.That(timeline.IsInspecting, Is.True);
            Assert.That(timeline.CanBeginClose, Is.True);
        }

        [Test]
        public void Inspecting_PersistsUntilExplicitCloseRequest()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();
            timeline.BeginOpen();
            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds);

            timeline.Advance(600f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    HomeRefrigeratorInteractionPhase.Inspecting));
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(600f).Within(Tolerance));
            Assert.That(timeline.IsActive, Is.True);
            Assert.That(timeline.BeginOpen(), Is.False);
            Assert.That(timeline.BeginClose(), Is.True);
            Assert.That(timeline.BeginClose(), Is.False);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Closing,
                camera: 1f,
                door: 1f,
                handle: 0f,
                hand: 0f,
                light: 1f);
        }

        [Test]
        public void CloseSequence_UsesContinuousExactPhaseEndpoints()
        {
            HomeRefrigeratorInteractionTimeline timeline =
                CreateInspectingTimeline();
            timeline.BeginClose();

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .ClosingDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Sealing,
                camera: 1f,
                door: 0f,
                handle: 1f,
                hand: 1f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .SealingDurationSeconds);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.CameraReturn,
                camera: 1f,
                door: 0f,
                handle: 0f,
                hand: 0f,
                light: 0f);

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .CameraReturnDurationSeconds);
            AssertClosed(timeline);
        }

        [Test]
        public void Advance_IsIndependentOfDeltaChunking()
        {
            var singleStep = new HomeRefrigeratorInteractionTimeline();
            var chunked = new HomeRefrigeratorInteractionTimeline();
            singleStep.BeginOpen();
            chunked.BeginOpen();

            const float openElapsed = 1.61f;
            singleStep.Advance(openElapsed);
            AdvanceInChunks(chunked, openElapsed, 161);
            AssertSameState(singleStep, chunked);

            singleStep.Advance(8f);
            AdvanceInChunks(chunked, 8f, 320);
            AssertSameState(singleStep, chunked);
            Assert.That(singleStep.BeginClose(), Is.True);
            Assert.That(chunked.BeginClose(), Is.True);

            const float closeElapsed = 1.43f;
            singleStep.Advance(closeElapsed);
            AdvanceInChunks(chunked, closeElapsed, 143);
            AssertSameState(singleStep, chunked);
        }

        [Test]
        public void AnimationChannels_AreBoundedAndUseAuthoredAccents()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();
            timeline.BeginOpen();
            float maximumDoorOpen = 0f;

            const int openSamples = 512;
            for (int index = 0; index < openSamples; index++)
            {
                AssertChannelsAreBounded(timeline.CurrentFrame);
                maximumDoorOpen = Math.Max(
                    maximumDoorOpen,
                    timeline.CurrentFrame.DoorOpen);
                timeline.Advance(
                    HomeRefrigeratorInteractionTimeline
                        .OpenSequenceDurationSeconds /
                    openSamples);
            }

            Assert.That(
                maximumDoorOpen,
                Is.GreaterThan(1f),
                "Opening should settle after a subtle overshoot.");
            Assert.That(
                maximumDoorOpen,
                Is.LessThanOrEqualTo(
                    1f +
                    HomeRefrigeratorInteractionTimeline
                        .DoorOpeningOvershoot +
                    Tolerance));

            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds);
            timeline.BeginClose();
            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .ClosingDurationSeconds +
                HomeRefrigeratorInteractionTimeline
                    .SealingDurationSeconds *
                0.5f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeRefrigeratorInteractionPhase.Sealing));
            Assert.That(
                timeline.CurrentFrame.DoorOpen,
                Is.EqualTo(
                    HomeRefrigeratorInteractionTimeline
                        .DoorGasketRebound)
                    .Within(Tolerance),
                "The gasket should rebound once before camera return.");
            AssertChannelsAreBounded(timeline.CurrentFrame);
        }

        [Test]
        public void LargeAdvance_StopsAtPersistentInspectionThenCompletesClose()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();
            timeline.BeginOpen();

            timeline.Advance(100f);

            Assert.That(timeline.IsInspecting, Is.True);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(
                    100f -
                    HomeRefrigeratorInteractionTimeline
                        .OpenSequenceDurationSeconds)
                    .Within(Tolerance));
            timeline.BeginClose();
            timeline.Advance(100f);
            AssertClosed(timeline);
        }

        [Test]
        public void Cancel_RestoresClosedPoseFromEveryActivePhase()
        {
            float[] elapsedTimes =
            {
                0f,
                HomeRefrigeratorInteractionTimeline
                    .CameraApproachDurationSeconds,
                HomeRefrigeratorInteractionTimeline
                    .CameraApproachDurationSeconds +
                HomeRefrigeratorInteractionTimeline
                    .ReachDurationSeconds,
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds - 0.01f,
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds
            };

            for (int index = 0; index < elapsedTimes.Length; index++)
            {
                var timeline =
                    new HomeRefrigeratorInteractionTimeline();
                timeline.BeginOpen();
                timeline.Advance(elapsedTimes[index]);

                Assert.That(timeline.Cancel(), Is.True);
                AssertClosed(timeline);
                Assert.That(timeline.Cancel(), Is.False);
                Assert.That(timeline.BeginOpen(), Is.True);
            }

            HomeRefrigeratorInteractionTimeline closing =
                CreateInspectingTimeline();
            closing.BeginClose();
            closing.Advance(
                HomeRefrigeratorInteractionTimeline
                    .ClosingDurationSeconds +
                0.01f);
            Assert.That(closing.Cancel(), Is.True);
            AssertClosed(closing);
        }

        [Test]
        public void Advance_RejectsInvalidUnscaledDeltaTime()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.001f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.PositiveInfinity));
        }

        private static HomeRefrigeratorInteractionTimeline
            CreateInspectingTimeline()
        {
            var timeline = new HomeRefrigeratorInteractionTimeline();
            timeline.BeginOpen();
            timeline.Advance(
                HomeRefrigeratorInteractionTimeline
                    .OpenSequenceDurationSeconds);
            return timeline;
        }

        private static void AssertClosed(
            HomeRefrigeratorInteractionTimeline timeline)
        {
            Assert.That(
                timeline.Phase,
                Is.EqualTo(HomeRefrigeratorInteractionPhase.Closed));
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.IsInspecting, Is.False);
            Assert.That(timeline.CanBeginOpen, Is.True);
            Assert.That(timeline.CanBeginClose, Is.False);
            Assert.That(timeline.PhaseElapsedSeconds, Is.Zero);
            Assert.That(timeline.PhaseDurationSeconds, Is.Zero);
            Assert.That(timeline.PhaseProgress, Is.Zero);
            AssertFrame(
                timeline,
                HomeRefrigeratorInteractionPhase.Closed,
                camera: 0f,
                door: 0f,
                handle: 0f,
                hand: 0f,
                light: 0f);
        }

        private static void AssertChannelsAreBounded(
            HomeRefrigeratorInteractionFrame frame)
        {
            Assert.That(frame.PhaseProgress, Is.InRange(0f, 1f));
            Assert.That(frame.CameraBlend, Is.InRange(0f, 1f));
            Assert.That(
                frame.DoorOpen,
                Is.InRange(
                    0f,
                    1f +
                    HomeRefrigeratorInteractionTimeline
                        .DoorOpeningOvershoot +
                    Tolerance));
            Assert.That(frame.HandleTurn, Is.InRange(0f, 1f));
            Assert.That(frame.HandReach, Is.InRange(0f, 1f));
            Assert.That(frame.LightIntensity, Is.InRange(0f, 1f));
        }

        private static void AssertFrame(
            HomeRefrigeratorInteractionTimeline timeline,
            HomeRefrigeratorInteractionPhase expectedPhase,
            float camera,
            float door,
            float handle,
            float hand,
            float light)
        {
            HomeRefrigeratorInteractionFrame frame =
                timeline.CurrentFrame;
            Assert.That(frame.Phase, Is.EqualTo(expectedPhase));
            Assert.That(
                frame.CameraBlend,
                Is.EqualTo(camera).Within(Tolerance));
            Assert.That(
                frame.DoorOpen,
                Is.EqualTo(door).Within(Tolerance));
            Assert.That(
                frame.HandleTurn,
                Is.EqualTo(handle).Within(Tolerance));
            Assert.That(
                frame.HandReach,
                Is.EqualTo(hand).Within(Tolerance));
            Assert.That(
                frame.LightIntensity,
                Is.EqualTo(light).Within(Tolerance));
        }

        private static void AdvanceInChunks(
            HomeRefrigeratorInteractionTimeline timeline,
            float elapsed,
            int chunkCount)
        {
            float chunk = elapsed / chunkCount;
            for (int index = 0; index < chunkCount; index++)
            {
                timeline.Advance(chunk);
            }
        }

        private static void AssertSameState(
            HomeRefrigeratorInteractionTimeline expected,
            HomeRefrigeratorInteractionTimeline actual)
        {
            HomeRefrigeratorInteractionFrame expectedFrame =
                expected.CurrentFrame;
            HomeRefrigeratorInteractionFrame actualFrame =
                actual.CurrentFrame;

            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(
                actual.PhaseElapsedSeconds,
                Is.EqualTo(expected.PhaseElapsedSeconds)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.PhaseProgress,
                Is.EqualTo(expectedFrame.PhaseProgress)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.CameraBlend,
                Is.EqualTo(expectedFrame.CameraBlend)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.DoorOpen,
                Is.EqualTo(expectedFrame.DoorOpen)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.HandleTurn,
                Is.EqualTo(expectedFrame.HandleTurn)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.HandReach,
                Is.EqualTo(expectedFrame.HandReach)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.LightIntensity,
                Is.EqualTo(expectedFrame.LightIntensity)
                    .Within(Tolerance));
        }
    }
}
