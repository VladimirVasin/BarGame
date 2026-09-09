using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class WorldItemInspectionTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void InspectionAndReturn_UseContinuousExactEndpoints()
        {
            var timeline =
                new WorldItemInspectionTimeline();

            AssertBrowsing(timeline);
            Assert.That(timeline.BeginInspection(), Is.True);
            Assert.That(timeline.BeginInspection(), Is.False);
            AssertFrame(
                timeline,
                WorldItemInspectionPhase.FlyingIn,
                phaseProgress: 0f,
                moveProgress: 0f,
                backdropAlpha: 0f,
                rotationDegrees: 0f);

            timeline.Advance(
                WorldItemInspectionTimeline
                    .FlyingInDurationSeconds);
            AssertFrame(
                timeline,
                WorldItemInspectionPhase.Inspecting,
                phaseProgress: 1f,
                moveProgress: 1f,
                backdropAlpha: 1f,
                rotationDegrees:
                    WorldItemInspectionTimeline
                        .FlyingInDurationSeconds *
                    WorldItemInspectionTimeline
                        .RotationDegreesPerSecond);
            Assert.That(timeline.IsInspecting, Is.True);
            Assert.That(timeline.BeginReturn(), Is.True);
            Assert.That(timeline.BeginReturn(), Is.False);
            AssertFrame(
                timeline,
                WorldItemInspectionPhase.FlyingOut,
                phaseProgress: 0f,
                moveProgress: 1f,
                backdropAlpha: 1f,
                rotationDegrees:
                    WorldItemInspectionTimeline
                        .FlyingInDurationSeconds *
                    WorldItemInspectionTimeline
                        .RotationDegreesPerSecond);

            timeline.Advance(
                WorldItemInspectionTimeline
                    .FlyingOutDurationSeconds);
            AssertBrowsing(timeline);
        }

        [Test]
        public void Inspecting_PersistsAndRotationWrapsDeterministically()
        {
            var timeline =
                new WorldItemInspectionTimeline();
            timeline.BeginInspection();
            float revolutionSeconds =
                360f /
                WorldItemInspectionTimeline
                    .RotationDegreesPerSecond;
            const float inspectionSeconds = 2.75f;

            timeline.Advance(
                WorldItemInspectionTimeline
                    .FlyingInDurationSeconds +
                revolutionSeconds +
                inspectionSeconds);

            Assert.That(timeline.IsInspecting, Is.True);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(
                        revolutionSeconds + inspectionSeconds)
                    .Within(Tolerance));
            float expectedRotation =
                (WorldItemInspectionTimeline
                     .FlyingInDurationSeconds +
                 inspectionSeconds) *
                WorldItemInspectionTimeline
                    .RotationDegreesPerSecond;
            Assert.That(
                timeline.CurrentFrame.RotationDegrees,
                Is.EqualTo(expectedRotation).Within(Tolerance));
        }

        [Test]
        public void BeginReturnDuringFlyingIn_PreservesBlendAndScalesDuration()
        {
            var timeline =
                new WorldItemInspectionTimeline();
            timeline.BeginInspection();
            timeline.Advance(
                WorldItemInspectionTimeline
                    .FlyingInDurationSeconds *
                0.5f);
            WorldItemInspectionFrame beforeReturn =
                timeline.CurrentFrame;

            Assert.That(timeline.BeginReturn(), Is.True);

            WorldItemInspectionFrame returnStart =
                timeline.CurrentFrame;
            Assert.That(
                returnStart.Phase,
                Is.EqualTo(
                    WorldItemInspectionPhase.FlyingOut));
            Assert.That(returnStart.PhaseProgress, Is.Zero);
            Assert.That(
                returnStart.MoveProgress,
                Is.EqualTo(beforeReturn.MoveProgress).Within(Tolerance));
            Assert.That(
                returnStart.BackdropAlpha,
                Is.EqualTo(beforeReturn.BackdropAlpha).Within(Tolerance));
            Assert.That(
                returnStart.RotationDegrees,
                Is.EqualTo(beforeReturn.RotationDegrees).Within(Tolerance));
            float expectedReturnDuration =
                WorldItemInspectionTimeline
                    .FlyingOutDurationSeconds *
                beforeReturn.MoveProgress;
            Assert.That(
                returnStart.PhaseDurationSeconds,
                Is.EqualTo(expectedReturnDuration).Within(Tolerance));

            timeline.Advance(expectedReturnDuration);

            AssertBrowsing(timeline);
        }

        [Test]
        public void Advance_IsIndependentOfDeltaChunking()
        {
            var singleStep =
                new WorldItemInspectionTimeline();
            var chunked =
                new WorldItemInspectionTimeline();
            singleStep.BeginInspection();
            chunked.BeginInspection();

            const float inspectionElapsed = 7.31f;
            singleStep.Advance(inspectionElapsed);
            AdvanceInChunks(chunked, inspectionElapsed, 0.017f);
            AssertSameState(singleStep, chunked);

            Assert.That(singleStep.BeginReturn(), Is.True);
            Assert.That(chunked.BeginReturn(), Is.True);
            const float returnElapsed = 0.21f;
            singleStep.Advance(returnElapsed);
            AdvanceInChunks(chunked, returnElapsed, 0.013f);
            AssertSameState(singleStep, chunked);

            singleStep.Advance(10f);
            AdvanceInChunks(chunked, 10f, 0.031f);
            AssertSameState(singleStep, chunked);
            AssertBrowsing(singleStep);
        }

        [Test]
        public void Cancel_RestoresBrowsingFromEveryActivePhase()
        {
            var flyingIn =
                new WorldItemInspectionTimeline();
            flyingIn.BeginInspection();
            flyingIn.Advance(0.1f);
            Assert.That(flyingIn.Cancel(), Is.True);
            AssertBrowsing(flyingIn);
            Assert.That(flyingIn.Cancel(), Is.False);

            var inspecting = CreateInspectingTimeline();
            inspecting.Advance(4f);
            Assert.That(inspecting.Cancel(), Is.True);
            AssertBrowsing(inspecting);

            var flyingOut = CreateInspectingTimeline();
            flyingOut.BeginReturn();
            flyingOut.Advance(0.1f);
            Assert.That(flyingOut.Cancel(), Is.True);
            AssertBrowsing(flyingOut);
        }

        [Test]
        public void ManualRotation_HoldsOffTheIdleTurnAndThenResumes()
        {
            var timeline = CreateInspectingTimeline();
            float idleDegrees = timeline.CurrentFrame.RotationDegrees;

            Assert.That(timeline.AddManualRotation(90f), Is.True);
            Assert.That(timeline.IsManuallyTurned, Is.True);
            Assert.That(
                timeline.CurrentFrame.RotationDegrees,
                Is.EqualTo(idleDegrees + 90f).Within(Tolerance));

            // While the hand is on it the idle turn adds nothing.
            float hold =
                WorldItemInspectionTimeline.ManualRotationHoldSeconds;
            timeline.Advance(hold * 0.5f);
            Assert.That(
                timeline.CurrentFrame.RotationDegrees,
                Is.EqualTo(idleDegrees + 90f).Within(Tolerance));

            // Past the hold the drift picks up again, from where he left it.
            timeline.Advance(hold * 0.5f + 1f);
            Assert.That(
                timeline.CurrentFrame.RotationDegrees,
                Is.EqualTo(
                    idleDegrees +
                    90f +
                    WorldItemInspectionTimeline.RotationDegreesPerSecond)
                    .Within(Tolerance));
            Assert.That(timeline.IsManuallyTurned, Is.False);
        }

        [Test]
        public void ManualRotation_WrapsAndIsRefusedWhileBrowsing()
        {
            var browsing = new WorldItemInspectionTimeline();
            Assert.That(browsing.AddManualRotation(45f), Is.False);
            Assert.That(browsing.ManualRotationDegrees, Is.EqualTo(0f));

            var timeline = CreateInspectingTimeline();
            timeline.AddManualRotation(-30f);
            Assert.That(
                timeline.ManualRotationDegrees,
                Is.EqualTo(330f).Within(Tolerance));
            timeline.AddManualRotation(400f);
            Assert.That(
                timeline.ManualRotationDegrees,
                Is.EqualTo(10f).Within(Tolerance));
            Assert.That(
                timeline.CurrentFrame.RotationDegrees,
                Is.InRange(0f, 360f));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.AddManualRotation(float.NaN));

            // A cancelled look forgets the hand that turned it.
            timeline.Cancel();
            Assert.That(timeline.ManualRotationDegrees, Is.EqualTo(0f));
        }

        [Test]
        public void Advance_RejectsInvalidUnscaledDeltaTime()
        {
            var timeline =
                new WorldItemInspectionTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.001f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.PositiveInfinity));
        }

        private static WorldItemInspectionTimeline
            CreateInspectingTimeline()
        {
            var timeline =
                new WorldItemInspectionTimeline();
            timeline.BeginInspection();
            timeline.Advance(
                WorldItemInspectionTimeline
                    .FlyingInDurationSeconds);
            return timeline;
        }

        private static void AssertBrowsing(
            WorldItemInspectionTimeline timeline)
        {
            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    WorldItemInspectionPhase.Browsing));
            Assert.That(timeline.IsBrowsing, Is.True);
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.IsInspecting, Is.False);
            Assert.That(timeline.CanBeginInspection, Is.True);
            Assert.That(timeline.CanBeginReturn, Is.False);
            Assert.That(timeline.PhaseElapsedSeconds, Is.Zero);
            Assert.That(timeline.PhaseDurationSeconds, Is.Zero);
            Assert.That(timeline.PhaseProgress, Is.Zero);
            AssertFrame(
                timeline,
                WorldItemInspectionPhase.Browsing,
                phaseProgress: 0f,
                moveProgress: 0f,
                backdropAlpha: 0f,
                rotationDegrees: 0f);
        }

        private static void AssertFrame(
            WorldItemInspectionTimeline timeline,
            WorldItemInspectionPhase expectedPhase,
            float phaseProgress,
            float moveProgress,
            float backdropAlpha,
            float rotationDegrees)
        {
            WorldItemInspectionFrame frame =
                timeline.CurrentFrame;
            Assert.That(frame.Phase, Is.EqualTo(expectedPhase));
            Assert.That(
                frame.PhaseProgress,
                Is.EqualTo(phaseProgress).Within(Tolerance));
            Assert.That(
                frame.MoveProgress,
                Is.EqualTo(moveProgress).Within(Tolerance));
            Assert.That(
                frame.ItemBlend,
                Is.EqualTo(moveProgress).Within(Tolerance));
            Assert.That(
                frame.BackdropAlpha,
                Is.EqualTo(backdropAlpha).Within(Tolerance));
            Assert.That(
                frame.RotationDegrees,
                Is.EqualTo(rotationDegrees).Within(Tolerance));
        }

        private static void AdvanceInChunks(
            WorldItemInspectionTimeline timeline,
            float elapsedSeconds,
            float maximumChunkSeconds)
        {
            float remaining = elapsedSeconds;
            while (remaining > 0f)
            {
                float chunk = Math.Min(remaining, maximumChunkSeconds);
                timeline.Advance(chunk);
                remaining -= chunk;
            }
        }

        private static void AssertSameState(
            WorldItemInspectionTimeline expected,
            WorldItemInspectionTimeline actual)
        {
            WorldItemInspectionFrame expectedFrame =
                expected.CurrentFrame;
            WorldItemInspectionFrame actualFrame =
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
                actualFrame.MoveProgress,
                Is.EqualTo(expectedFrame.MoveProgress)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.BackdropAlpha,
                Is.EqualTo(expectedFrame.BackdropAlpha)
                    .Within(Tolerance));
            Assert.That(
                actualFrame.RotationDegrees,
                Is.EqualTo(expectedFrame.RotationDegrees)
                    .Within(Tolerance * 10f));
        }
    }
}
