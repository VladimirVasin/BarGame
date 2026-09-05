using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerAnimatedInteractionTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Definition_DefaultsDescribeRequired3DClipsAndLogicalFrames()
        {
            var definition =
                new PlayerAnimatedInteractionDefinition(
                    " Enter ",
                    "Loop",
                    " Exit ");

            Assert.That(definition.EnterClipName, Is.EqualTo("Enter"));
            Assert.That(definition.LoopClipName, Is.EqualTo("Loop"));
            Assert.That(definition.ExitClipName, Is.EqualTo("Exit"));
            Assert.That(definition.EnterStartFrame, Is.Zero);
            Assert.That(definition.EnterFrameCount, Is.EqualTo(24));
            Assert.That(definition.EnterFramesPerSecond, Is.EqualTo(12f));
            Assert.That(definition.LoopStartFrame, Is.EqualTo(24));
            Assert.That(definition.LoopFrameCount, Is.EqualTo(16));
            Assert.That(definition.LoopFramesPerSecond, Is.EqualTo(8f));
            Assert.That(
                definition.LoopDurationSeconds,
                Is.EqualTo(2d).Within(Tolerance));
            Assert.That(definition.ExitStartFrame, Is.EqualTo(40));
            Assert.That(definition.ExitFrameCount, Is.EqualTo(24));
            Assert.That(definition.ExitFramesPerSecond, Is.EqualTo(12f));
            Assert.That(definition.TotalFrameCount, Is.EqualTo(64));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(0),
                Is.Zero);
            Assert.That(
                definition.GetLoopFrameDurationSeconds(15),
                Is.EqualTo(0.125d).Within(Tolerance));
        }

        [TestCase(null, "Loop", "Exit")]
        [TestCase("", "Loop", "Exit")]
        [TestCase("Enter", " ", "Exit")]
        [TestCase("Enter", "Loop", "\t")]
        public void Definition_RequiresEvery3DClipName(
            string enter,
            string loop,
            string exit)
        {
            Assert.That(
                () => new PlayerAnimatedInteractionDefinition(
                    enter,
                    loop,
                    exit),
                Throws.ArgumentException);
        }

        [Test]
        public void Definition_RejectsInvalidTimingAndLoopHolds()
        {
            Assert.That(
                () => CreateDefinition(enterFrameCount: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateDefinition(loopFramesPerSecond: 0f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateDefinition(exitFramesPerSecond: float.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateDefinition(
                    loopFrameCount: 2,
                    loopFrameExtraHoldSeconds: new[] { 0f }),
                Throws.ArgumentException);
            Assert.That(
                () => CreateDefinition(
                    loopFrameCount: 2,
                    loopFrameExtraHoldSeconds:
                        new[] { 0f, -0.1f }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateDefinition(
                    loopFrameCount: 2,
                    loopFrameExtraHoldSeconds:
                        new[] { 0f, float.PositiveInfinity }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Definition_CopiesLoopHoldsAndValidatesLookup()
        {
            float[] holds = { 0.25f, 0.5f };
            PlayerAnimatedInteractionDefinition definition =
                CreateDefinition(
                    loopFrameCount: 2,
                    loopFramesPerSecond: 2f,
                    loopFrameExtraHoldSeconds: holds);
            holds[0] = 99f;

            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(0),
                Is.EqualTo(0.25f));
            Assert.That(
                definition.LoopDurationSeconds,
                Is.EqualTo(1.75d).Within(Tolerance));
            Assert.That(
                () => definition.GetLoopFrameDurationSeconds(-1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => definition.GetLoopFrameExtraHoldSeconds(2),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Pose_NormalizesRotationAndRejectsNonFiniteValues()
        {
            var pose = new PlayerAnimatedInteractionPose(
                new Vector3(1f, 2f, 3f),
                new Quaternion(0f, 0f, 0f, 2f),
                new Vector3(4f, 5f, 6f));

            Assert.That(pose.RootRotation.w, Is.EqualTo(1f));
            Assert.That(
                () => new PlayerAnimatedInteractionPose(
                    new Vector3(float.NaN, 0f, 0f),
                    Quaternion.identity,
                    Vector3.zero),
                Throws.ArgumentException);
            Assert.That(
                () => new PlayerAnimatedInteractionPose(
                    Vector3.zero,
                    new Quaternion(0f, 0f, 0f, 0f),
                    Vector3.zero),
                Throws.ArgumentException);
            Assert.That(
                () => new PlayerAnimatedInteractionPose(
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(0f, float.PositiveInfinity, 0f)),
                Throws.ArgumentException);
        }

        [Test]
        public void Timeline_AdvancesEnterLoopExitAndHoldsTerminalPose()
        {
            PlayerAnimatedInteractionDefinition definition =
                CreateDefinition(
                    enterFrameCount: 2,
                    enterFramesPerSecond: 2f,
                    loopFrameCount: 2,
                    loopFramesPerSecond: 2f,
                    exitFrameCount: 2,
                    exitFramesPerSecond: 2f);
            var timeline =
                new PlayerAnimatedInteractionTimeline(definition);

            Assert.That(timeline.Begin(), Is.True);
            Assert.That(timeline.Begin(), Is.False);
            Assert.That(timeline.Phase, Is.EqualTo(
                PlayerAnimatedInteractionPhase.Entering));
            Assert.That(timeline.FrameIndex, Is.EqualTo(0));
            Assert.That(timeline.ClipProgress, Is.Zero);

            timeline.Advance(0.51f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(1));
            Assert.That(timeline.PhaseProgress, Is.EqualTo(0.51f)
                .Within(Tolerance));

            timeline.Advance(0.49f);
            Assert.That(timeline.Phase, Is.EqualTo(
                PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.FrameIndex, Is.EqualTo(2));
            Assert.That(timeline.ClipProgress, Is.Zero.Within(Tolerance));

            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(3));
            Assert.That(timeline.ClipProgress, Is.EqualTo(0.5f)
                .Within(Tolerance));
            Assert.That(timeline.RequestExit(), Is.True);
            Assert.That(timeline.RequestExit(), Is.False);
            Assert.That(timeline.FrameIndex, Is.EqualTo(4));
            Assert.That(timeline.ClipProgress, Is.Zero);

            timeline.Advance(1f);
            Assert.That(timeline.IsActive, Is.True);
            Assert.That(timeline.Phase, Is.EqualTo(
                PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(timeline.FrameIndex, Is.EqualTo(5));
            Assert.That(timeline.ClipProgress, Is.EqualTo(1f));

            timeline.Advance(0.001f);
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.Phase, Is.EqualTo(
                PlayerAnimatedInteractionPhase.Idle));
            Assert.That(timeline.FrameIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Timeline_LargeExitHitchStillPresentsEndpointUntilNextAdvance()
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition(
                    exitFrameCount: 4,
                    exitFramesPerSecond: 4f));
            Assert.That(timeline.BeginLooping(), Is.True);
            Assert.That(timeline.RequestExit(), Is.True);

            timeline.Advance(20f);

            Assert.That(timeline.IsActive, Is.True);
            Assert.That(timeline.FrameIndex, Is.EqualTo(
                timeline.Definition.ExitStartFrame + 3));
            Assert.That(timeline.PhaseProgress, Is.EqualTo(1f));
            timeline.Advance(0f);
            Assert.That(timeline.IsActive, Is.True);
            timeline.Advance(0.01f);
            Assert.That(timeline.IsActive, Is.False);
        }

        [TestCase(0.6f)]
        [TestCase(20f)]
        public void Timeline_QueuedExitFinishesLoopAtExactSeamAndResetClearsRequest(float crossingStep)
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition(loopFrameCount: 2, loopFramesPerSecond: 2f));
            Assert.That(timeline.RequestExitAtLoopBoundary(), Is.False);
            timeline.BeginLooping();
            timeline.Advance(0.45f);
            Assert.That(timeline.RequestExitAtLoopBoundary(), Is.True);
            Assert.That(timeline.RequestExitAtLoopBoundary(), Is.False);
            Assert.That(timeline.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.ClipProgress, Is.EqualTo(0.45f).Within(Tolerance));

            timeline.Advance(crossingStep);

            Assert.That(timeline.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(timeline.ClipProgress, Is.Zero);
            Assert.That(timeline.FrameIndex, Is.EqualTo(timeline.Definition.ExitStartFrame));
            timeline.Reset();
            timeline.BeginLooping();
            timeline.Advance(2.5f);
            Assert.That(timeline.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
        }

        [Test]
        public void Timeline_LoopHoldsFreezeClipProgressAndLogicalFrame()
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition(
                    loopFrameCount: 2,
                    loopFramesPerSecond: 2f,
                    loopFrameExtraHoldSeconds:
                        new[] { 1f, 0f }));
            timeline.BeginLooping();

            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(
                timeline.Definition.LoopStartFrame));
            Assert.That(timeline.ClipProgress, Is.EqualTo(0.5f)
                .Within(Tolerance));

            timeline.Advance(0.8f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(
                timeline.Definition.LoopStartFrame));
            Assert.That(timeline.ClipProgress, Is.EqualTo(0.5f)
                .Within(Tolerance));

            timeline.Advance(0.3f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(
                timeline.Definition.LoopStartFrame + 1));
            Assert.That(timeline.ClipProgress, Is.EqualTo(0.6f)
                .Within(Tolerance));
        }

        [Test]
        public void Timeline_ExitMultiplierChangesDurationAndResetRestoresDefault()
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition(
                    exitFrameCount: 4,
                    exitFramesPerSecond: 2f));
            timeline.BeginLooping();

            Assert.That(timeline.RequestExit(1.75f), Is.True);
            Assert.That(timeline.ExitDurationMultiplier, Is.EqualTo(1.75f));
            Assert.That(
                timeline.ExitDurationSeconds,
                Is.EqualTo(3.5d).Within(Tolerance));

            timeline.Reset();
            Assert.That(timeline.ExitDurationMultiplier, Is.EqualTo(1f));
            Assert.That(
                timeline.ExitDurationSeconds,
                Is.EqualTo(2d).Within(Tolerance));
        }

        [Test]
        public void Timeline_EquivalentChunksProduceEquivalentLoopSample()
        {
            PlayerAnimatedInteractionDefinition definition =
                CreateDefinition(
                    enterFrameCount: 2,
                    enterFramesPerSecond: 4f,
                    loopFrameCount: 3,
                    loopFramesPerSecond: 4f,
                    loopFrameExtraHoldSeconds:
                        new[] { 0f, 0.25f, 0f });
            var oneChunk =
                new PlayerAnimatedInteractionTimeline(definition);
            var manyChunks =
                new PlayerAnimatedInteractionTimeline(definition);
            oneChunk.Begin();
            manyChunks.Begin();

            oneChunk.Advance(1.15f);
            for (int index = 0; index < 23; index++)
            {
                manyChunks.Advance(0.05f);
            }

            Assert.That(manyChunks.Phase, Is.EqualTo(oneChunk.Phase));
            Assert.That(manyChunks.FrameIndex, Is.EqualTo(
                oneChunk.FrameIndex));
            Assert.That(manyChunks.ClipProgress, Is.EqualTo(
                oneChunk.ClipProgress).Within(Tolerance));
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Timeline_RejectsInvalidDeltaTime(float deltaTime)
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition());
            timeline.Begin();

            Assert.That(
                () => timeline.Advance(deltaTime),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Timeline_RejectsInvalidExitMultiplier(float multiplier)
        {
            var timeline = new PlayerAnimatedInteractionTimeline(
                CreateDefinition());
            timeline.BeginLooping();

            Assert.That(
                () => timeline.RequestExit(multiplier),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static PlayerAnimatedInteractionDefinition CreateDefinition(
            int enterFrameCount = 1,
            float enterFramesPerSecond = 1f,
            int loopFrameCount = 1,
            float loopFramesPerSecond = 1f,
            int exitFrameCount = 1,
            float exitFramesPerSecond = 1f,
            float[] loopFrameExtraHoldSeconds = null)
        {
            return new PlayerAnimatedInteractionDefinition(
                "Enter",
                "Loop",
                "Exit",
                enterFrameCount,
                enterFramesPerSecond,
                loopFrameCount,
                loopFramesPerSecond,
                exitFrameCount,
                exitFramesPerSecond,
                loopFrameExtraHoldSeconds);
        }

        [Test]
        public void PelvisTransition_StandsStillForItsHoldAndArrivesAtItsSettle()
        {
            var start = Vector3.zero;
            var waypoint = new Vector3(0f, 0f, 1f);
            var end = new Vector3(0f, 0f, 2f);
            var transition = new PlayerAnimatedInteractionPelvisTransition(
                waypoint,
                enterArrivalProgress: 0.5f,
                enterDepartureProgress: 0.6f,
                exitArrivalProgress: 0.5f,
                exitDepartureProgress: 0.6f,
                enterHoldProgress: 0.3f,
                enterSettleProgress: 0.8f,
                exitHoldProgress: 0.2f,
                exitSettleProgress: 0.9f);

            Assert.That(
                transition.EvaluateEntering(start, end, 0f),
                Is.EqualTo(start));
            Assert.That(
                transition.EvaluateEntering(start, end, 0.3f),
                Is.EqualTo(start),
                "The hold is inclusive of its own moment: a body that has " +
                "already twitched by the frame it is allowed to move is a " +
                "body that moved early.");
            Assert.That(
                transition.EvaluateEntering(start, end, 0.4f).z,
                Is.InRange(0.01f, 0.99f),
                "And it is genuinely under way in between.");
            Assert.That(
                transition.EvaluateEntering(start, end, 0.55f),
                Is.EqualTo(waypoint));
            Assert.That(
                transition.EvaluateEntering(start, end, 0.8f),
                Is.EqualTo(end),
                "Arrived at the settle, not on the closing frame.");
            Assert.That(
                transition.EvaluateEntering(start, end, 1f),
                Is.EqualTo(end),
                "And still there afterwards, while the clip finishes in " +
                "place.");

            Assert.That(
                transition.EvaluateExiting(end, start, 0.2f),
                Is.EqualTo(end));
            Assert.That(
                transition.EvaluateExiting(end, start, 0.9f),
                Is.EqualTo(start));
        }

        [Test]
        public void PelvisTransition_WithoutHoldsIsTheOlderTwoMarkerShape()
        {
            var start = Vector3.zero;
            var waypoint = new Vector3(0f, 0f, 1f);
            var end = new Vector3(0f, 0f, 2f);
            var transition = new PlayerAnimatedInteractionPelvisTransition(
                waypoint,
                enterArrivalProgress: 0.5f,
                enterDepartureProgress: 0.6f,
                exitArrivalProgress: 0.5f,
                exitDepartureProgress: 0.6f);

            // Every seat with no door to wait for still moves from the first
            // frame and lands on the last, and none of them said so.
            Assert.That(
                transition.EvaluateEntering(start, end, 0.05f).z,
                Is.GreaterThan(0f),
                "A bench sitter must not have acquired a hold he never " +
                "asked for.");
            Assert.That(
                transition.EvaluateEntering(start, end, 1f),
                Is.EqualTo(end));
        }

        [Test]
        public void PelvisTransition_RefusesAnOutOfOrderProgressLadder()
        {
            var waypoint = new Vector3(0f, 0f, 1f);

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionPelvisTransition(
                    waypoint,
                    enterArrivalProgress: 0.3f,
                    enterDepartureProgress: 0.6f,
                    exitArrivalProgress: 0.5f,
                    exitDepartureProgress: 0.6f,
                    enterHoldProgress: 0.4f),
                "A hold that outlasts its own arrival is a divide by a " +
                "negative span, not a slower walk.");

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionPelvisTransition(
                    waypoint,
                    enterArrivalProgress: 0.5f,
                    enterDepartureProgress: 0.6f,
                    exitArrivalProgress: 0.5f,
                    exitDepartureProgress: 0.6f,
                    enterSettleProgress: 0.55f));
        }
    }
}
