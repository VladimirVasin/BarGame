using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarDrinkServiceTimelineTests
    {
        private const float Tolerance = 0.0002f;

        [Test]
        public void ConfirmedService_UsesContinuousExactPhaseEndpoints()
        {
            var timeline = new BarDrinkServiceTimeline();

            AssertClosed(timeline);
            Assert.That(timeline.BeginOpen(), Is.True);
            Assert.That(timeline.BeginOpen(), Is.False);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.CameraApproach,
                camera: 0f,
                arms: 0f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);

            timeline.Advance(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Browsing,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);
            Assert.That(timeline.CanConfirm, Is.True);
            Assert.That(timeline.Confirm(), Is.True);
            Assert.That(timeline.IsCommitted, Is.True);
            Assert.That(timeline.Confirm(), Is.False);
            Assert.That(timeline.Cancel(), Is.False);

            timeline.Advance(
                BarDrinkServiceTimeline.BottlePickupDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.VesselPlacement,
                camera: 1f,
                arms: 1f,
                bottleTravel: 1f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);

            timeline.Advance(
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Pouring,
                camera: 1f,
                arms: 1f,
                bottleTravel: 1f,
                bottleTilt: 0f,
                vessel: 1f,
                stream: 0f,
                fill: 0f,
                lift: 0f);

            timeline.Advance(
                BarDrinkServiceTimeline.PouringDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.BottleReturn,
                camera: 1f,
                arms: 1f,
                bottleTravel: 1f,
                bottleTilt: 0f,
                vessel: 1f,
                stream: 0f,
                fill: 1f,
                lift: 0f);

            timeline.Advance(
                BarDrinkServiceTimeline.BottleReturnDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Drinking,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 1f,
                stream: 0f,
                fill: 1f,
                lift: 1f);

            timeline.Advance(
                BarDrinkServiceTimeline.DrinkingDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.VesselReturn,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 1f,
                stream: 0f,
                fill: 0f,
                lift: 1f);
            Assert.That(timeline.IsCommitted, Is.True);
            Assert.That(timeline.Cancel(), Is.False);

            timeline.Advance(
                BarDrinkServiceTimeline.VesselReturnDurationSeconds);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Browsing,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.CanConfirm, Is.True);
            Assert.That(timeline.CanCancel, Is.True);

            Assert.That(timeline.Cancel(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            timeline.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds);
            AssertClosed(timeline);
        }

        [Test]
        public void Drinking_HoldsForExactlyThreeSecondsBeforeVesselReturn()
        {
            Assert.That(
                BarDrinkServiceTimeline.DrinkingDurationSeconds,
                Is.EqualTo(3f));
            BarDrinkServiceTimeline timeline =
                CreateCommittedTimelineAt(
                    BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                    BarDrinkServiceTimeline
                        .VesselPlacementDurationSeconds +
                    BarDrinkServiceTimeline.PouringDurationSeconds +
                    BarDrinkServiceTimeline.BottleReturnDurationSeconds);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.Drinking));
            Assert.That(
                timeline.PhaseDurationSeconds,
                Is.EqualTo(3f));
            Assert.That(timeline.CurrentFrame.DrinkLift, Is.EqualTo(1f));
            Assert.That(timeline.CurrentFrame.VesselFill, Is.EqualTo(1f));

            timeline.Advance(2.999f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.Drinking));
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(2.999f).Within(Tolerance));
            Assert.That(timeline.CurrentFrame.DrinkLift, Is.EqualTo(1f));
            Assert.That(
                timeline.CurrentFrame.VesselFill,
                Is.LessThan(0.001f));

            timeline.Advance(0.001f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselReturn));
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.Zero.Within(Tolerance));
            Assert.That(timeline.CurrentFrame.DrinkLift, Is.EqualTo(1f));
            Assert.That(timeline.CurrentFrame.VesselVisibility, Is.EqualTo(1f));
            Assert.That(timeline.CurrentFrame.VesselFill, Is.Zero);
        }

        [Test]
        public void BeerService_WaitsForPhysicalArrivalAndExplicitDrink()
        {
            BarDrinkServiceTimeline timeline = CreateBrowsingTimeline();

            Assert.That(timeline.Confirm(DrinkId.LightBeer), Is.True);
            Assert.That(timeline.IsBeerService, Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerWalkToTap));

            timeline.Advance(100f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerWalkToTap));
            Assert.That(timeline.ReportBeerServerAtTap(false), Is.False);
            Assert.That(timeline.ReportBeerServerAtTap(true), Is.True);

            timeline.Advance(
                BarDrinkServiceTimeline.BeerGlassPickupDurationSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerPouring));
            timeline.Advance(
                BarDrinkServiceTimeline.BeerPouringDurationSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerCarryToGuest));
            Assert.That(timeline.CurrentFrame.VesselFill, Is.EqualTo(1f));

            timeline.Advance(100f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerCarryToGuest));
            Assert.That(timeline.ReportBeerServerAtGuest(true), Is.True);
            timeline.Advance(
                BarDrinkServiceTimeline.BeerGlassPlacementDurationSeconds);

            Assert.That(timeline.IsAwaitingDrink, Is.True);
            Assert.That(timeline.CanBeginDrink, Is.True);
            Assert.That(timeline.CurrentFrame.VesselFill, Is.EqualTo(1f));
            timeline.Advance(100f);
            Assert.That(timeline.IsAwaitingDrink, Is.True);
            Assert.That(timeline.BeginDrink(), Is.True);
            Assert.That(timeline.BeginDrink(), Is.False);

            timeline.Advance(
                BarDrinkServiceTimeline.PlayerPickupDurationSeconds);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.PlayerDrinking));
            timeline.Advance(
                BarDrinkServiceTimeline.DrinkingDurationSeconds - 0.001f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.PlayerDrinking));
            timeline.Advance(0.001f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.PlayerVesselReturn));
            timeline.Advance(
                BarDrinkServiceTimeline.PlayerVesselReturnDurationSeconds);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter));
            Assert.That(timeline.HasEmptyVessel, Is.True);
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.IsBrowsing, Is.True);
            Assert.That(timeline.CurrentFrame.VesselVisibility, Is.EqualTo(1f));
            Assert.That(timeline.CurrentFrame.VesselFill, Is.Zero);
        }

        [Test]
        public void CompletedService_ReturnsToBrowsingAndCanConfirmAgain()
        {
            Assert.That(
                BarDrinkServiceTimeline.ConfirmedSequenceDurationSeconds,
                Is.EqualTo(
                    BarDrinkServiceTimeline
                        .ConfirmedPresentationDurationSeconds));
            BarDrinkServiceTimeline timeline = CreateBrowsingTimeline();
            Assert.That(timeline.Confirm(), Is.True);

            timeline.Advance(
                BarDrinkServiceTimeline
                    .ConfirmedPresentationDurationSeconds);

            AssertBrowsingAfterService(timeline);
            Assert.That(
                timeline.Phase,
                Is.Not.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(timeline.BeginOpen(), Is.False);

            Assert.That(timeline.Confirm(), Is.True);

            Assert.That(timeline.IsCommitted, Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup));
            Assert.That(timeline.Cancel(), Is.False);
        }

        [Test]
        public void Browsing_PersistsUntilConfirmOrCancel()
        {
            var timeline = new BarDrinkServiceTimeline();
            timeline.BeginOpen();

            timeline.Advance(100f);

            Assert.That(timeline.IsBrowsing, Is.True);
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.CanConfirm, Is.True);
            Assert.That(timeline.CanCancel, Is.True);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(
                    100f -
                    BarDrinkServiceTimeline
                        .CameraApproachDurationSeconds)
                    .Within(Tolerance));

            timeline.Advance(200f);

            Assert.That(timeline.IsBrowsing, Is.True);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(
                    300f -
                    BarDrinkServiceTimeline
                        .CameraApproachDurationSeconds)
                    .Within(Tolerance));
            Assert.That(timeline.Confirm(), Is.True);
            Assert.That(timeline.PhaseElapsedSeconds, Is.Zero);
        }

        [Test]
        public void CancelDuringApproach_ReturnsFromCurrentCameraPose()
        {
            var timeline = new BarDrinkServiceTimeline();
            timeline.BeginOpen();
            timeline.Advance(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds *
                0.61f);
            BarDrinkServiceFrame beforeCancel = timeline.CurrentFrame;

            Assert.That(timeline.Cancel(), Is.True);

            BarDrinkServiceFrame returnStart = timeline.CurrentFrame;
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.CanConfirm, Is.False);
            Assert.That(timeline.CanCancel, Is.False);
            Assert.That(timeline.Cancel(), Is.False);
            Assert.That(
                returnStart.CameraBlend,
                Is.EqualTo(beforeCancel.CameraBlend).Within(Tolerance));
            Assert.That(
                returnStart.ArmsVisibility,
                Is.EqualTo(beforeCancel.ArmsVisibility).Within(Tolerance));

            timeline.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds *
                0.5f);
            Assert.That(
                timeline.CurrentFrame.CameraBlend,
                Is.InRange(0f, beforeCancel.CameraBlend));
            Assert.That(
                timeline.CurrentFrame.ArmsVisibility,
                Is.InRange(0f, beforeCancel.ArmsVisibility));

            timeline.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds *
                0.5f);
            AssertClosed(timeline);
        }

        [Test]
        public void CancelFromBrowsing_UsesFullCameraReturn()
        {
            BarDrinkServiceTimeline timeline = CreateBrowsingTimeline();

            Assert.That(timeline.Cancel(), Is.True);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.CameraReturn,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);
            Assert.That(timeline.Confirm(), Is.False);

            timeline.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds);
            AssertClosed(timeline);
        }

        [Test]
        public void Confirm_IsIrreversibleButResetSupportsLifecycleCleanup()
        {
            BarDrinkServiceTimeline timeline = CreateBrowsingTimeline();
            Assert.That(timeline.Confirm(), Is.True);

            Assert.That(timeline.Cancel(), Is.False);
            timeline.Advance(
                BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds +
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.5f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.Pouring));
            Assert.That(timeline.IsCommitted, Is.True);
            Assert.That(timeline.Cancel(), Is.False);
            Assert.That(timeline.BeginOpen(), Is.False);

            timeline.Reset();

            AssertClosed(timeline);
            Assert.That(timeline.BeginOpen(), Is.True);
        }

        [Test]
        public void Advance_IsIndependentOfDeltaChunking()
        {
            var singleStep = new BarDrinkServiceTimeline();
            var chunked = new BarDrinkServiceTimeline();
            singleStep.BeginOpen();
            chunked.BeginOpen();

            const float browseElapsed = 3.17f;
            singleStep.Advance(browseElapsed);
            AdvanceInChunks(chunked, browseElapsed, 317);
            AssertSameState(singleStep, chunked);
            Assert.That(singleStep.Confirm(), Is.True);
            Assert.That(chunked.Confirm(), Is.True);

            float serviceElapsed =
                BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds +
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.43f;
            singleStep.Advance(serviceElapsed);
            AdvanceInChunks(chunked, serviceElapsed, 401);
            AssertSameState(singleStep, chunked);
            Assert.That(
                singleStep.Phase,
                Is.EqualTo(BarDrinkServicePhase.Pouring));

            singleStep.Advance(100f);
            AdvanceInChunks(chunked, 100f, 2000);
            AssertSameState(singleStep, chunked);
            AssertBrowsingAfterService(singleStep);
            AssertBrowsingAfterService(chunked);

            Assert.That(singleStep.Cancel(), Is.True);
            Assert.That(chunked.Cancel(), Is.True);
            singleStep.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds);
            AdvanceInChunks(
                chunked,
                BarDrinkServiceTimeline.CameraReturnDurationSeconds,
                70);
            AssertClosed(singleStep);
            AssertClosed(chunked);
        }

        [Test]
        public void LargeDelta_StopsAtBrowsingAfterConfirmedService()
        {
            var timeline = new BarDrinkServiceTimeline();
            timeline.BeginOpen();

            timeline.Advance(1000f);
            Assert.That(timeline.IsBrowsing, Is.True);

            Assert.That(timeline.Confirm(), Is.True);
            timeline.Advance(1000f);

            AssertBrowsingAfterService(timeline);
            float elapsedAfterService = timeline.PhaseElapsedSeconds;

            timeline.Advance(1000f);

            AssertBrowsingAfterService(timeline);
            Assert.That(
                timeline.PhaseElapsedSeconds,
                Is.EqualTo(elapsedAfterService + 1000f)
                    .Within(0.01f));
            Assert.That(timeline.Cancel(), Is.True);
            timeline.Advance(1000f);
            AssertClosed(timeline);
        }

        [Test]
        public void PresentationChannels_AreBoundedAndPhaseOwned()
        {
            var timeline = new BarDrinkServiceTimeline();
            timeline.BeginOpen();
            const float step = 0.0025f;
            float maximumTilt = 0f;
            float maximumStream = 0f;
            float maximumLift = 0f;
            float previousPourFill = 0f;
            float previousDrinkFill = 1f;
            float previousReturnLift = 1f;
            int safety = 0;

            while (!timeline.IsBrowsing && safety++ < 1000)
            {
                AssertChannelsAreBounded(timeline.CurrentFrame);
                timeline.Advance(step);
            }

            Assert.That(timeline.IsBrowsing, Is.True);
            timeline.Confirm();
            safety = 0;
            while (timeline.IsCommitted && safety++ < 5000)
            {
                BarDrinkServiceFrame frame = timeline.CurrentFrame;
                AssertChannelsAreBounded(frame);
                maximumTilt = Math.Max(maximumTilt, frame.BottleTilt);
                maximumStream = Math.Max(
                    maximumStream,
                    frame.StreamVisibility);
                maximumLift = Math.Max(maximumLift, frame.DrinkLift);

                if (frame.Phase != BarDrinkServicePhase.Pouring)
                {
                    Assert.That(frame.StreamVisibility, Is.Zero);
                }
                else
                {
                    Assert.That(
                        frame.VesselFill + Tolerance,
                        Is.GreaterThanOrEqualTo(previousPourFill));
                    previousPourFill = frame.VesselFill;
                }

                if (frame.Phase == BarDrinkServicePhase.Drinking)
                {
                    Assert.That(frame.DrinkLift, Is.EqualTo(1f));
                    Assert.That(
                        frame.VesselFill - Tolerance,
                        Is.LessThanOrEqualTo(previousDrinkFill));
                    previousDrinkFill = frame.VesselFill;
                }

                if (frame.Phase == BarDrinkServicePhase.VesselReturn)
                {
                    Assert.That(frame.VesselFill, Is.Zero);
                    Assert.That(
                        frame.DrinkLift - Tolerance,
                        Is.LessThanOrEqualTo(previousReturnLift));
                    previousReturnLift = frame.DrinkLift;
                }

                timeline.Advance(step);
            }

            Assert.That(safety, Is.LessThan(5000));
            AssertBrowsingAfterService(timeline);
            Assert.That(maximumTilt, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(maximumStream, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(maximumLift, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(previousPourFill, Is.EqualTo(1f).Within(0.01f));
            Assert.That(previousDrinkFill, Is.EqualTo(0f).Within(0.01f));
            Assert.That(previousReturnLift, Is.EqualTo(0f).Within(0.01f));

            Assert.That(timeline.Cancel(), Is.True);
            timeline.Advance(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds);
            AssertClosed(timeline);
        }

        [Test]
        public void Advance_RejectsInvalidUnscaledDeltaTime()
        {
            var timeline = new BarDrinkServiceTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.001f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.PositiveInfinity));
        }

        [Test]
        public void ZeroDeltaAndClosedAdvance_DoNotChangeState()
        {
            var timeline = new BarDrinkServiceTimeline();

            timeline.Advance(100f);
            AssertClosed(timeline);
            timeline.BeginOpen();
            BarDrinkServiceFrame before = timeline.CurrentFrame;

            timeline.Advance(0f);

            AssertSameFrame(before, timeline.CurrentFrame);
        }

        private static BarDrinkServiceTimeline CreateBrowsingTimeline()
        {
            var timeline = new BarDrinkServiceTimeline();
            timeline.BeginOpen();
            timeline.Advance(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            return timeline;
        }

        private static BarDrinkServiceTimeline CreateCommittedTimelineAt(
            float elapsedSeconds)
        {
            BarDrinkServiceTimeline timeline = CreateBrowsingTimeline();
            Assert.That(timeline.Confirm(), Is.True);
            timeline.Advance(elapsedSeconds);
            return timeline;
        }

        private static void AssertBrowsingAfterService(
            BarDrinkServiceTimeline timeline)
        {
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            Assert.That(timeline.IsActive, Is.True);
            Assert.That(timeline.IsBrowsing, Is.True);
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.CanBeginOpen, Is.False);
            Assert.That(timeline.CanConfirm, Is.True);
            Assert.That(timeline.CanCancel, Is.True);
            Assert.That(timeline.PhaseDurationSeconds, Is.Zero);
            Assert.That(timeline.PhaseProgress, Is.EqualTo(1f));
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Browsing,
                camera: 1f,
                arms: 1f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);
        }

        private static void AssertClosed(BarDrinkServiceTimeline timeline)
        {
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarDrinkServicePhase.Closed));
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.IsBrowsing, Is.False);
            Assert.That(timeline.IsCommitted, Is.False);
            Assert.That(timeline.CanBeginOpen, Is.True);
            Assert.That(timeline.CanConfirm, Is.False);
            Assert.That(timeline.CanCancel, Is.False);
            Assert.That(timeline.PhaseElapsedSeconds, Is.Zero);
            Assert.That(timeline.PhaseDurationSeconds, Is.Zero);
            Assert.That(timeline.PhaseProgress, Is.Zero);
            AssertFrame(
                timeline,
                BarDrinkServicePhase.Closed,
                camera: 0f,
                arms: 0f,
                bottleTravel: 0f,
                bottleTilt: 0f,
                vessel: 0f,
                stream: 0f,
                fill: 0f,
                lift: 0f);
        }

        private static void AssertChannelsAreBounded(
            BarDrinkServiceFrame frame)
        {
            Assert.That(frame.PhaseProgress, Is.InRange(0f, 1f));
            Assert.That(frame.CameraBlend, Is.InRange(0f, 1f));
            Assert.That(frame.ArmsVisibility, Is.InRange(0f, 1f));
            Assert.That(frame.BottleTravel, Is.InRange(0f, 1f));
            Assert.That(frame.BottleTilt, Is.InRange(0f, 1f));
            Assert.That(frame.VesselVisibility, Is.InRange(0f, 1f));
            Assert.That(frame.StreamVisibility, Is.InRange(0f, 1f));
            Assert.That(frame.VesselFill, Is.InRange(0f, 1f));
            Assert.That(frame.DrinkLift, Is.InRange(0f, 1f));
            Assert.That(frame.TapHandlePull, Is.InRange(0f, 1f));
            Assert.That(frame.ServiceVesselTravel, Is.InRange(0f, 1f));
        }

        private static void AssertFrame(
            BarDrinkServiceTimeline timeline,
            BarDrinkServicePhase phase,
            float camera,
            float arms,
            float bottleTravel,
            float bottleTilt,
            float vessel,
            float stream,
            float fill,
            float lift)
        {
            BarDrinkServiceFrame frame = timeline.CurrentFrame;
            Assert.That(frame.Phase, Is.EqualTo(phase));
            Assert.That(
                frame.PhaseElapsedSeconds,
                Is.EqualTo(timeline.PhaseElapsedSeconds)
                    .Within(Tolerance));
            Assert.That(
                frame.PhaseDurationSeconds,
                Is.EqualTo(timeline.PhaseDurationSeconds)
                    .Within(Tolerance));
            Assert.That(
                frame.PhaseProgress,
                Is.EqualTo(timeline.PhaseProgress).Within(Tolerance));
            Assert.That(
                frame.CameraBlend,
                Is.EqualTo(camera).Within(Tolerance));
            Assert.That(
                frame.ArmsVisibility,
                Is.EqualTo(arms).Within(Tolerance));
            Assert.That(
                frame.BottleTravel,
                Is.EqualTo(bottleTravel).Within(Tolerance));
            Assert.That(
                frame.BottleTilt,
                Is.EqualTo(bottleTilt).Within(Tolerance));
            Assert.That(
                frame.VesselVisibility,
                Is.EqualTo(vessel).Within(Tolerance));
            Assert.That(
                frame.StreamVisibility,
                Is.EqualTo(stream).Within(Tolerance));
            Assert.That(
                frame.VesselFill,
                Is.EqualTo(fill).Within(Tolerance));
            Assert.That(
                frame.DrinkLift,
                Is.EqualTo(lift).Within(Tolerance));
        }

        private static void AdvanceInChunks(
            BarDrinkServiceTimeline timeline,
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
            BarDrinkServiceTimeline expected,
            BarDrinkServiceTimeline actual)
        {
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(
                actual.IsCommitted,
                Is.EqualTo(expected.IsCommitted));
            Assert.That(
                actual.PhaseElapsedSeconds,
                Is.EqualTo(expected.PhaseElapsedSeconds)
                    .Within(Tolerance));
            AssertSameFrame(expected.CurrentFrame, actual.CurrentFrame);
        }

        private static void AssertSameFrame(
            BarDrinkServiceFrame expected,
            BarDrinkServiceFrame actual)
        {
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(
                actual.PhaseElapsedSeconds,
                Is.EqualTo(expected.PhaseElapsedSeconds)
                    .Within(Tolerance));
            Assert.That(
                actual.PhaseDurationSeconds,
                Is.EqualTo(expected.PhaseDurationSeconds)
                    .Within(Tolerance));
            Assert.That(
                actual.PhaseProgress,
                Is.EqualTo(expected.PhaseProgress).Within(Tolerance));
            Assert.That(
                actual.CameraBlend,
                Is.EqualTo(expected.CameraBlend).Within(Tolerance));
            Assert.That(
                actual.ArmsVisibility,
                Is.EqualTo(expected.ArmsVisibility).Within(Tolerance));
            Assert.That(
                actual.BottleTravel,
                Is.EqualTo(expected.BottleTravel).Within(Tolerance));
            Assert.That(
                actual.BottleTilt,
                Is.EqualTo(expected.BottleTilt).Within(Tolerance));
            Assert.That(
                actual.VesselVisibility,
                Is.EqualTo(expected.VesselVisibility).Within(Tolerance));
            Assert.That(
                actual.StreamVisibility,
                Is.EqualTo(expected.StreamVisibility).Within(Tolerance));
            Assert.That(
                actual.VesselFill,
                Is.EqualTo(expected.VesselFill).Within(Tolerance));
            Assert.That(
                actual.DrinkLift,
                Is.EqualTo(expected.DrinkLift).Within(Tolerance));
            Assert.That(
                actual.TapHandlePull,
                Is.EqualTo(expected.TapHandlePull).Within(Tolerance));
            Assert.That(
                actual.ServiceVesselTravel,
                Is.EqualTo(expected.ServiceVesselTravel).Within(Tolerance));
        }
    }
}
