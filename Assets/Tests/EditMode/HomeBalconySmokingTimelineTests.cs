using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBalconySmokingTimelineTests
    {
        [Test]
        public void Entering_HoldsThenFinishesCameraAndMusicFades()
        {
            var timeline =
                new HomeBalconySmokingTimeline();

            Assert.That(timeline.Begin(), Is.True);
            Assert.That(timeline.CameraBlend, Is.EqualTo(0f));
            Assert.That(timeline.CameraDriftBlend, Is.EqualTo(0f));
            Assert.That(timeline.MusicGain, Is.EqualTo(0f));

            timeline.Advance(
                HomeBalconySmokingTimeline.CameraHoldSeconds);
            Assert.That(timeline.CameraBlend, Is.EqualTo(0f));
            Assert.That(timeline.CameraDriftBlend, Is.EqualTo(0f));
            Assert.That(timeline.MusicGain, Is.GreaterThan(0f));

            timeline.Advance(
                HomeBalconySmokingTimeline.CameraArrivalSeconds -
                HomeBalconySmokingTimeline.CameraHoldSeconds);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.CameraDriftBlend, Is.EqualTo(1f));
            Assert.That(timeline.MusicGain, Is.EqualTo(1f));
            Assert.That(timeline.EnterLooping(), Is.True);
            Assert.That(timeline.CameraBlend, Is.EqualTo(1f));
            Assert.That(timeline.CameraDriftBlend, Is.EqualTo(1f));
            Assert.That(timeline.MusicGain, Is.EqualTo(1f));
        }

        [Test]
        public void Exiting_ReversesCameraDriftAndMusicOverTwoSeconds()
        {
            var timeline =
                new HomeBalconySmokingTimeline();
            timeline.Begin();
            timeline.EnterLooping();
            Assert.That(timeline.BeginExit(), Is.True);

            timeline.Advance(
                HomeBalconySmokingTimeline.ExitDurationSeconds *
                0.5f);
            Assert.That(
                timeline.CameraBlend,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                timeline.CameraDriftBlend,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                timeline.MusicGain,
                Is.EqualTo(0.5f).Within(0.0001f));

            timeline.Advance(
                HomeBalconySmokingTimeline.ExitDurationSeconds *
                0.5f);
            Assert.That(timeline.CameraBlend, Is.EqualTo(0f));
            Assert.That(timeline.CameraDriftBlend, Is.EqualTo(0f));
            Assert.That(timeline.MusicGain, Is.EqualTo(0f));
        }

        [Test]
        public void CameraDrift_UsesContinuousClockAcrossPhaseChanges()
        {
            var timeline =
                new HomeBalconySmokingTimeline();

            timeline.Begin();
            timeline.Advance(
                HomeBalconySmokingTimeline.EnterDurationSeconds);
            HomeBalconySmokingCameraDriftSample entering =
                timeline.CameraDrift;
            Assert.That(timeline.EnterLooping(), Is.True);
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalPosition,
                    entering.LocalPosition),
                Is.LessThan(0.000001f));
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalEulerAngles,
                    entering.LocalEulerAngles),
                Is.LessThan(0.000001f));
            timeline.Advance(1.75f);
            HomeBalconySmokingCameraDriftSample looping =
                timeline.CameraDrift;
            Assert.That(timeline.BeginExit(), Is.True);
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalPosition,
                    looping.LocalPosition),
                Is.LessThan(0.000001f));
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalEulerAngles,
                    looping.LocalEulerAngles),
                Is.LessThan(0.000001f));
            timeline.Advance(0.25f);

            Assert.That(
                timeline.PresentationElapsedSeconds,
                Is.EqualTo(6d).Within(0.0001d));
            HomeBalconySmokingCameraDriftSample expected =
                HomeBalconySmokingCameraDrift.Evaluate(
                    6d,
                    timeline.CameraBlend);
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalPosition,
                    expected.LocalPosition),
                Is.LessThan(0.000001f));
            Assert.That(
                Vector3.Distance(
                    timeline.CameraDrift.LocalEulerAngles,
                    expected.LocalEulerAngles),
                Is.LessThan(0.000001f));

            timeline.Reset();
            Assert.That(timeline.PresentationElapsedSeconds, Is.Zero);
        }

        [Test]
        public void CameraDrift_IsDeterministicBoundedAndFrameSmooth()
        {
            HomeBalconySmokingCameraDriftSample first =
                HomeBalconySmokingCameraDrift.Evaluate(7.25d, 1f);
            HomeBalconySmokingCameraDriftSample repeated =
                HomeBalconySmokingCameraDrift.Evaluate(7.25d, 1f);
            Assert.That(
                repeated.LocalPosition,
                Is.EqualTo(first.LocalPosition));
            Assert.That(
                repeated.LocalEulerAngles,
                Is.EqualTo(first.LocalEulerAngles));

            HomeBalconySmokingCameraDriftSample muted =
                HomeBalconySmokingCameraDrift.Evaluate(7.25d, 0f);
            Assert.That(muted.LocalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                muted.LocalEulerAngles,
                Is.EqualTo(Vector3.zero));

            HomeBalconySmokingCameraDriftSample previous =
                HomeBalconySmokingCameraDrift.Evaluate(0d, 1f);
            float largestPosition = previous.LocalPosition.magnitude;
            float largestRotation =
                previous.LocalEulerAngles.magnitude;
            for (int frame = 1; frame <= 30 * 60; frame++)
            {
                HomeBalconySmokingCameraDriftSample current =
                    HomeBalconySmokingCameraDrift.Evaluate(
                        frame / 60d,
                        1f);
                Assert.That(
                    Mathf.Abs(current.LocalPosition.x),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .LateralAmplitudeMeters +
                        0.000001f));
                Assert.That(
                    Mathf.Abs(current.LocalPosition.y),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .VerticalAmplitudeMeters +
                        0.000001f));
                Assert.That(
                    Mathf.Abs(current.LocalPosition.z),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .DepthAmplitudeMeters +
                        0.000001f));
                Assert.That(
                    Mathf.Abs(current.LocalEulerAngles.x),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .PitchAmplitudeDegrees +
                        0.000001f));
                Assert.That(
                    Mathf.Abs(current.LocalEulerAngles.y),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .YawAmplitudeDegrees +
                        0.000001f));
                Assert.That(
                    Mathf.Abs(current.LocalEulerAngles.z),
                    Is.LessThanOrEqualTo(
                        HomeBalconySmokingCameraDrift
                            .RollAmplitudeDegrees +
                        0.000001f));
                Assert.That(
                    Vector3.Distance(
                        current.LocalPosition,
                        previous.LocalPosition),
                    Is.LessThan(0.0004f),
                    "Low-frequency drift must not introduce positional " +
                    "jitter between 60 Hz samples.");
                Assert.That(
                    Vector3.Distance(
                        current.LocalEulerAngles,
                        previous.LocalEulerAngles),
                    Is.LessThan(0.005f),
                    "Low-frequency drift must not introduce rotational " +
                    "jitter between 60 Hz samples.");
                largestPosition = Mathf.Max(
                    largestPosition,
                    current.LocalPosition.magnitude);
                largestRotation = Mathf.Max(
                    largestRotation,
                    current.LocalEulerAngles.magnitude);
                previous = current;
            }

            Assert.That(largestPosition, Is.GreaterThan(0.01f));
            Assert.That(largestRotation, Is.GreaterThan(0.10f));
        }

        [Test]
        public void SafeExitFrames_OnlyCoverRestAndExhaleBridges()
        {
            for (int frame = 0;
                 frame < HomeBalconySmokingPlan.LoopFrameCount;
                 frame++)
            {
                bool expected = frame <= 3 || frame >= 21;
                Assert.That(
                    HomeBalconySmokingTimeline
                        .IsSafeExitLoopFrame(frame),
                    Is.EqualTo(expected),
                    $"Unexpected safety result for loop frame {frame}.");
            }

            Assert.That(
                HomeBalconySmokingTimeline
                    .IsSafeExitLoopFrame(-1),
                Is.False);
            Assert.That(
                HomeBalconySmokingTimeline
                    .IsSafeExitLoopFrame(24),
                Is.False);
        }

        [Test]
        public void Advance_RejectsInvalidDeltaTime()
        {
            var timeline =
                new HomeBalconySmokingTimeline();
            timeline.Begin();

            Assert.That(
                () => timeline.Advance(-0.01f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => timeline.Advance(float.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => HomeBalconySmokingCameraDrift.Evaluate(
                    double.PositiveInfinity,
                    1f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => HomeBalconySmokingCameraDrift.Evaluate(
                    0d,
                    float.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
