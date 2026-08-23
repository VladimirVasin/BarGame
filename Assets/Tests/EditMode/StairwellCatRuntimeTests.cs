using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatRuntimeTests
    {
        private readonly List<GameObject> gameObjects =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = gameObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (gameObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        gameObjects[index]);
                }
            }

            gameObjects.Clear();
        }

        [Test]
        public void Plan_PerchesOnMiddleRailAndKeepsApproachWalkable()
        {
            StairwellLayoutPlan stairwell =
                StairwellLayoutPlanner.Generate();
            StairwellCatPlan plan =
                StairwellCatPlan.Create(stairwell);
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);

            // The 3D origin is the rail-contact point: rail top, not
            // the old sprite pivot that floated 7 cm above it.
            Assert.That(
                plan.VisualLocalPosition,
                Is.EqualTo(
                    new Vector3(-1.70f, 2.76f, 2.32f)));
            Assert.That(
                plan.InteractionLocalPosition,
                Is.EqualTo(
                    new Vector3(-1.55f, 1.64f, 1.78f)));
            Assert.That(
                walkable.Contains(
                    plan.InteractionLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    plan.VisualLocalPosition,
                    plan.InteractionLocalPosition),
                Is.LessThan(1.65f));
            // The trigger volume compensates the visual drop so the
            // world-space interaction stays where the sprite's was.
            Assert.That(
                plan.TriggerLocalCenter,
                Is.EqualTo(
                    new Vector3(0f, -0.48f, -0.27f)));
            Assert.That(
                StairwellCatPlan.TriggerSize,
                Is.EqualTo(
                    new Vector3(0.72f, 1.30f, 1.05f)));
            Assert.That(
                plan.VisualLocalPosition.y +
                plan.TriggerLocalCenter.y,
                Is.EqualTo(2.28f).Within(0.0001f));
        }

        [Test]
        public void FeedingTimeline_IsOneShotAndFrameChunkIndependent()
        {
            var singleStep = new StairwellCatFeedingTimeline();
            var chunked = new StairwellCatFeedingTimeline();

            Assert.That(
                StairwellCatFeedingTimeline.FrameCount,
                Is.EqualTo(16));
            Assert.That(singleStep.FrameIndex, Is.EqualTo(-1));
            Assert.That(singleStep.IsActive, Is.False);
            Assert.That(singleStep.Begin(), Is.True);
            Assert.That(singleStep.Begin(), Is.False);
            Assert.That(chunked.Begin(), Is.True);

            singleStep.Advance(1.75f);
            for (int index = 0; index < 7; index++)
            {
                chunked.Advance(0.25f);
            }

            Assert.That(singleStep.IsActive, Is.True);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(10));
            Assert.That(
                chunked.FrameIndex,
                Is.EqualTo(singleStep.FrameIndex));
            Assert.That(
                chunked.ElapsedSeconds,
                Is.EqualTo(singleStep.ElapsedSeconds)
                    .Within(0.00001d));

            singleStep.Advance(0.75f);
            chunked.Advance(0.75f);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(15));
            Assert.That(chunked.FrameIndex, Is.EqualTo(15));

            singleStep.Advance(1f / 6f);
            chunked.Advance(1f / 6f);
            Assert.That(singleStep.IsActive, Is.False);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(-1));
            Assert.That(chunked.IsActive, Is.False);
            Assert.That(singleStep.Complete(), Is.False);
            Assert.That(singleStep.Cancel(), Is.False);
        }

        [Test]
        public void FeedingTimeline_RejectsInvalidDeltaAndSupportsCancel()
        {
            var timeline = new StairwellCatFeedingTimeline();

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.01f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.That(timeline.Begin(), Is.True);
            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(3));
            Assert.That(timeline.Cancel(), Is.True);
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.FrameIndex, Is.EqualTo(-1));
        }

        [Test]
        public void FeedingPlan_StagesEntryAndExitSafelyFacingCat()
        {
            StairwellLayoutPlan stairwell =
                StairwellLayoutPlanner.Generate();
            StairwellCatPlan cat =
                StairwellCatPlan.Create(stairwell);
            StairwellCatFeedingPlan feeding =
                StairwellCatFeedingPlan.Create(
                    stairwell,
                    cat);
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);
            var selector = new StairwellCameraShotSelector(
                StairwellFixedCameraController
                    .CreateDefaultShots(stairwell));

            Assert.That(
                feeding.EntryRootLocalPosition,
                Is.EqualTo(cat.InteractionLocalPosition));
            Assert.That(
                walkable.Contains(
                    feeding.EntryRootLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    feeding.ExitRootLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            AssertFinite(feeding.EntryRootLocalPosition);
            AssertFinite(feeding.ExitRootLocalPosition);
            Assert.That(
                feeding.ExitRootLocalPosition,
                Is.EqualTo(feeding.EntryRootLocalPosition));
            Assert.That(
                selector.Select(
                    feeding.EntryRootLocalPosition).Kind,
                Is.EqualTo(
                    StairwellCatFeedingPlan
                        .RequiredCameraShotKind));
            Assert.That(
                selector.Select(
                    feeding.ExitRootLocalPosition).Kind,
                Is.EqualTo(
                    StairwellCatFeedingPlan
                        .RequiredCameraShotKind));
            Assert.That(
                feeding.EntryHipLocalPosition,
                Is.EqualTo(feeding.ActionHipLocalPosition));
            Assert.That(
                feeding.ExitHipLocalPosition,
                Is.EqualTo(feeding.EntryHipLocalPosition));
            AssertFinite(feeding.EntryHipLocalPosition);
            AssertFinite(feeding.ExitHipLocalPosition);
            Assert.That(
                feeding.EntryHipLocalPosition.y,
                Is.EqualTo(
                    feeding.EntryRootLocalPosition.y +
                    PlayerCharacterDimensions.PelvisHeight +
                    StairwellCatFeedingPlan
                        .UprightVisualOffset)
                    .Within(0.0001f));
            Assert.That(
                feeding.EntryFacingLocalDirection.y,
                Is.Zero.Within(0.0001f));
            Assert.That(
                feeding.ExitFacingLocalDirection.y,
                Is.Zero.Within(0.0001f));
            Assert.That(
                feeding.EntryFacingLocalDirection.magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                feeding.ExitFacingLocalDirection.magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            AssertFinite(feeding.EntryFacingLocalDirection);
            AssertFinite(feeding.ExitFacingLocalDirection);
            Assert.That(
                Vector3.Dot(
                    feeding.EntryFacingLocalDirection,
                    cat.VisualLocalPosition -
                    feeding.EntryRootLocalPosition),
                Is.GreaterThan(0f));
            Assert.That(
                Vector3.Dot(
                    feeding.ExitFacingLocalDirection,
                    cat.VisualLocalPosition -
                    feeding.ExitRootLocalPosition),
                Is.GreaterThan(0f));
            Assert.That(
                Vector3.Angle(
                    feeding.EntryFacingLocalRotation *
                    Vector3.forward,
                    feeding.EntryFacingLocalDirection),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    feeding.ExitFacingLocalRotation *
                    Vector3.forward,
                    feeding.ExitFacingLocalDirection),
                Is.LessThan(0.001f));

            Assert.That(
                feeding.PlayerRootLocalPosition,
                Is.EqualTo(feeding.EntryRootLocalPosition));
            Assert.That(
                feeding.StandHipLocalPosition,
                Is.EqualTo(feeding.EntryHipLocalPosition));
            Assert.That(
                feeding.FacingLocalDirection,
                Is.EqualTo(feeding.EntryFacingLocalDirection));
            Assert.That(
                feeding.FacingLocalRotation,
                Is.EqualTo(feeding.EntryFacingLocalRotation));
            Assert.That(
                feeding.EntryLocalRotation,
                Is.EqualTo(feeding.EntryFacingLocalRotation));
            Assert.That(
                feeding.ExitLocalRotation,
                Is.EqualTo(feeding.ExitFacingLocalRotation));
        }

        [Test]
        public void IdleModel_IsInvariantToFrameChunking()
        {
            var oneChunk = new StairwellCatIdleModel(321);
            var manyChunks = new StairwellCatIdleModel(321);

            oneChunk.Advance(24.75f);
            for (int index = 0; index < 99; index++)
            {
                manyChunks.Advance(0.25f);
            }

            Assert.That(
                oneChunk.CurrentKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                manyChunks.CurrentKind,
                Is.EqualTo(oneChunk.CurrentKind));
            Assert.That(
                manyChunks.CurrentFrame,
                Is.EqualTo(oneChunk.CurrentFrame));
            Assert.That(
                manyChunks.ElapsedSeconds,
                Is.EqualTo(oneChunk.ElapsedSeconds)
                    .Within(0.00001d));
        }

        [Test]
        public void IdleModel_GroomRunsAllFramesAndEnds()
        {
            var model = new StairwellCatIdleModel(321);

            model.Advance(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds -
                0.01f);
            Assert.That(
                model.CurrentKind,
                Is.Not.EqualTo(StairwellCatIdleKind.Groom));

            model.Reset();
            model.Advance(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds);
            for (int frame = 0;
                 frame <
                 StairwellCatIdleModel.GroomFrameCount;
                 frame++)
            {
                Assert.That(
                    model.CurrentKind,
                    Is.EqualTo(StairwellCatIdleKind.Groom));
                Assert.That(
                    model.CurrentFrame,
                    Is.EqualTo(frame));
                model.Advance(
                    StairwellCatIdleModel
                        .GroomFrameSeconds);
            }

            Assert.That(
                model.CurrentKind,
                Is.Not.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                model.CurrentFrame,
                Is.InRange(0, 7));

            model.Advance(
                StairwellCatIdleModel
                    .GroomIntervalSeconds -
                StairwellCatIdleModel
                    .GroomDurationSeconds);
            Assert.That(
                model.CurrentKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(model.CurrentFrame, Is.Zero);
        }

        [Test]
        public void HeadYawModel_UsesAngularHysteresisRateLimitAndClamp()
        {
            var model = new StairwellCatHeadYawModel();

            // Below the enter threshold the head cannot be bothered.
            model.Update(
                StairwellCatHeadYawModel.DefaultEnterErrorDegrees -
                1f,
                0.5f);
            Assert.That(model.CurrentYawDegrees, Is.Zero);
            Assert.That(model.IsTurning, Is.False);

            // Above it the turn runs rate-limited, not teleported.
            model.Update(60f, 0.1f);
            Assert.That(model.IsTurning, Is.True);
            Assert.That(
                model.CurrentYawDegrees,
                Is.EqualTo(
                    StairwellCatHeadYawModel
                        .DefaultTurnDegreesPerSecond * 0.1f)
                    .Within(0.001f));

            // Given time it settles and stops turning.
            model.Update(60f, 1f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(60f));
            Assert.That(model.IsTurning, Is.False);

            // A residual error under the enter threshold is beneath
            // the cat's notice.
            model.Update(170f, 5f);
            Assert.That(model.CurrentYawDegrees, Is.EqualTo(60f));

            // Tracking never exceeds the clamp: the over-shoulder
            // turn belongs to the grin alone.
            model.Update(-170f, 5f);
            Assert.That(
                model.CurrentYawDegrees,
                Is.EqualTo(
                    -StairwellCatHeadYawModel
                        .DefaultMaxTrackYawDegrees));

            model.Reset();
            Assert.That(model.CurrentYawDegrees, Is.Zero);
            Assert.That(
                model.Update(float.NaN, 0.1f),
                Is.Zero);
        }

        [Test]
        public void GrinTimeline_SnapsOnHoldsAndSlowlyVanishes()
        {
            // The asymmetry is the design: fast on, slow off.
            Assert.That(
                StairwellCatGrinTimeline.AppearSeconds,
                Is.LessThan(
                    StairwellCatGrinTimeline.VanishSeconds));

            StairwellCatGrinTimeline timeline =
                StairwellCatGrinTimeline.CreateAppear(1f);

            StairwellCatGrinSample start = timeline.Evaluate(0f);
            Assert.That(start.Progress, Is.Zero.Within(0.0001f));
            Assert.That(
                start.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Appearing));

            StairwellCatGrinSample mid = timeline.Evaluate(0.2f);
            Assert.That(mid.Progress, Is.GreaterThan(0.5f));
            Assert.That(mid.Progress, Is.LessThan(1f));

            StairwellCatGrinSample held = timeline.Evaluate(
                StairwellCatGrinTimeline.AppearSeconds + 0.5f);
            Assert.That(held.Progress, Is.EqualTo(1f));
            Assert.That(
                held.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Held));

            StairwellCatGrinSample vanishing = timeline.Evaluate(
                StairwellCatGrinTimeline.AppearSeconds + 1f + 0.6f);
            Assert.That(
                vanishing.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Vanishing));
            Assert.That(vanishing.Progress, Is.InRange(0.01f, 0.99f));

            StairwellCatGrinSample done = timeline.Evaluate(
                StairwellCatGrinTimeline.AppearSeconds +
                1f +
                StairwellCatGrinTimeline.VanishSeconds +
                0.01f);
            Assert.That(done.Progress, Is.Zero);
            Assert.That(
                done.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Hidden));
            Assert.That(done.IsComplete, Is.True);

            // An infinite hold never vanishes on its own.
            StairwellCatGrinTimeline sustained =
                StairwellCatGrinTimeline.CreateAppear(
                    float.PositiveInfinity);
            StairwellCatGrinSample stillHeld =
                sustained.Evaluate(1000f);
            Assert.That(stillHeld.Progress, Is.EqualTo(1f));
            Assert.That(stillHeld.IsComplete, Is.False);
        }

        [Test]
        public void GrinTimeline_VanishScalesWithStartProgress()
        {
            StairwellCatGrinTimeline halfVanish =
                StairwellCatGrinTimeline.CreateVanish(0.5f);

            // Half a grin un-draws in half the time - an aborted
            // half-smile never crawls.
            StairwellCatGrinSample midway = halfVanish.Evaluate(
                StairwellCatGrinTimeline.VanishSeconds * 0.25f);
            Assert.That(
                midway.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Vanishing));
            Assert.That(midway.Progress, Is.EqualTo(0.25f).Within(0.0001f));

            StairwellCatGrinSample done = halfVanish.Evaluate(
                StairwellCatGrinTimeline.VanishSeconds * 0.5f);
            Assert.That(done.Progress, Is.Zero);
            Assert.That(done.IsComplete, Is.True);

            StairwellCatGrinSample immediate =
                StairwellCatGrinTimeline.CreateVanish(0f)
                    .Evaluate(0f);
            Assert.That(immediate.IsComplete, Is.True);
        }

        [Test]
        public void PoseRules_MapIdleFramesToPivotDeltas()
        {
            StairwellCatPose breathe =
                StairwellCatPoseRules.IdlePose(
                    StairwellCatIdleKind.Breathe,
                    3,
                    12f);
            Assert.That(breathe.ChestScale, Is.EqualTo(1.030f));
            Assert.That(breathe.HeadLiftMeters, Is.GreaterThan(0f));
            Assert.That(breathe.HeadYawDegrees, Is.EqualTo(12f));
            Assert.That(breathe.TailSwing01Degrees, Is.Zero);

            StairwellCatPose flick =
                StairwellCatPoseRules.IdlePose(
                    StairwellCatIdleKind.TailFlick,
                    StairwellCatIdleModel.TailFirstFrame,
                    0f);
            Assert.That(flick.TailSwing01Degrees, Is.Not.Zero);
            Assert.That(
                Mathf.Abs(flick.TailSwing03Degrees),
                Is.GreaterThan(
                    Mathf.Abs(flick.TailSwing01Degrees)));

            StairwellCatPose twitch =
                StairwellCatPoseRules.IdlePose(
                    StairwellCatIdleKind.EarTwitch,
                    StairwellCatIdleModel.EarFirstFrame,
                    0f);
            Assert.That(twitch.EarLeftTiltDegrees, Is.Not.Zero);
            Assert.That(twitch.EarRightTiltDegrees, Is.Zero);

            StairwellCatPose groom =
                StairwellCatPoseRules.IdlePose(
                    StairwellCatIdleKind.Groom,
                    4,
                    45f);
            Assert.That(groom.HeadPitchDegrees, Is.GreaterThan(20f));
            // A grooming cat does not track the player.
            Assert.That(groom.HeadYawDegrees, Is.Zero);
            Assert.That(groom.HeadRollDegrees, Is.Not.Zero);
        }

        [Test]
        public void PoseRules_FeedingDipsChewsAndLifts()
        {
            StairwellCatPose dip =
                StairwellCatPoseRules.FeedingPose(0);
            StairwellCatPose chew =
                StairwellCatPoseRules.FeedingPose(7);
            StairwellCatPose lift =
                StairwellCatPoseRules.FeedingPose(15);

            Assert.That(dip.HeadPitchDegrees, Is.GreaterThan(0f));
            Assert.That(
                dip.HeadPitchDegrees,
                Is.LessThan(chew.HeadPitchDegrees));
            Assert.That(
                chew.HeadPitchDegrees,
                Is.EqualTo(
                    StairwellCatPoseRules.FeedingPitchDegrees)
                    .Within(
                        StairwellCatPoseRules.FeedingChewDegrees));
            Assert.That(
                lift.HeadPitchDegrees,
                Is.LessThan(chew.HeadPitchDegrees));
            Assert.That(chew.EarLeftTiltDegrees, Is.Not.Zero);
        }

        [Test]
        public void PoseRules_ComposeGrinTurnsPastTrackingClamp()
        {
            StairwellCatPose basePose =
                StairwellCatPoseRules.IdlePose(
                    StairwellCatIdleKind.Breathe,
                    1,
                    40f);

            StairwellCatPose untouched =
                StairwellCatPoseRules.ComposeGrin(
                    basePose,
                    0f,
                    137f);
            Assert.That(
                untouched.HeadYawDegrees,
                Is.EqualTo(basePose.HeadYawDegrees));

            StairwellCatPose committed =
                StairwellCatPoseRules.ComposeGrin(
                    basePose,
                    1f,
                    137f);
            // The whole trickster beat: the grin turn reaches past
            // anything ordinary tracking is allowed.
            Assert.That(
                committed.HeadYawDegrees,
                Is.EqualTo(137f).Within(0.001f));
            Assert.That(
                Mathf.Abs(committed.HeadYawDegrees),
                Is.GreaterThan(
                    StairwellCatHeadYawModel
                        .DefaultMaxTrackYawDegrees));
            Assert.That(
                StairwellCatPoseRules.MaxGrinYawDegrees,
                Is.GreaterThan(137f));

            StairwellCatPose clamped =
                StairwellCatPoseRules.ComposeGrin(
                    basePose,
                    1f,
                    179f);
            Assert.That(
                clamped.HeadYawDegrees,
                Is.EqualTo(
                    StairwellCatPoseRules.MaxGrinYawDegrees)
                    .Within(0.001f));
        }

        [Test]
        public void Actor_AdoptsPivotsAndArticulatesFromIdleModel()
        {
            (StairwellCatActor actor,
             StairwellCatRigAnchors anchors,
             StairwellCatGrinController _,
             GameObject player) = CreateActor();

            Assert.That(actor.IsInitialized, Is.True);
            Assert.That(
                actor.Renderer,
                Is.SameAs(anchors.BodyRenderer));

            // The wheelchair-pattern adopt: ears and grin ride the
            // head, the tail is a chain, the torso rides the chest.
            Assert.That(
                anchors.EarLeftPivot.parent,
                Is.SameAs(anchors.HeadPivot));
            Assert.That(
                anchors.EarRightPivot.parent,
                Is.SameAs(anchors.HeadPivot));
            Assert.That(
                anchors.MuzzleAnchor.parent,
                Is.SameAs(anchors.HeadPivot));
            Assert.That(
                anchors.GrinRenderer.transform.parent,
                Is.SameAs(anchors.HeadPivot));
            Assert.That(
                anchors.TailPivots[1].parent,
                Is.SameAs(anchors.TailPivots[0]));
            Assert.That(
                anchors.TailPivots[2].parent,
                Is.SameAs(anchors.TailPivots[1]));

            // Breathe frame 3 swells the chest over its rest scale.
            actor.AdvancePresentation(1.4f);
            Assert.That(
                actor.CurrentIdleKind,
                Is.EqualTo(StairwellCatIdleKind.Breathe));
            Assert.That(actor.CurrentFrame, Is.EqualTo(3));
            Assert.That(
                anchors.ChestPivot.localScale.x,
                Is.EqualTo(1.030f).Within(0.0001f));

            // The player stands well to the side: the head turns,
            // but never past the tracking clamp. The cat faces the
            // negation of the model root's axes (see the actor).
            player.transform.position =
                anchors.ModelRoot.position -
                anchors.ModelRoot.right * 2f -
                anchors.ModelRoot.forward * 0.5f;
            for (int step = 0; step < 20; step++)
            {
                actor.AdvancePresentation(0.05f);
            }

            Assert.That(actor.HeadYawDegrees, Is.Not.Zero);
            Assert.That(
                Mathf.Abs(actor.HeadYawDegrees),
                Is.LessThanOrEqualTo(
                    StairwellCatHeadYawModel
                        .DefaultMaxTrackYawDegrees));
            Assert.That(
                Quaternion.Angle(
                    anchors.HeadPivot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(1f));

            // Groom pitches the head down and drops the tracking.
            // 2.4 s have elapsed so far (1.4 + 20 x 0.05); land in
            // the middle of the first groom window at 24.5 s.
            actor.AdvancePresentation(
                StairwellCatIdleModel.FirstGroomStartSeconds -
                2.4f +
                0.5f);
            Assert.That(
                actor.CurrentIdleKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                Quaternion.Angle(
                    anchors.HeadPivot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(5f));
        }

        [Test]
        public void Actor_FeedingKeepsContractAndPosesHeadDown()
        {
            (StairwellCatActor actor,
             StairwellCatRigAnchors anchors,
             StairwellCatGrinController _,
             GameObject _) = CreateActor();

            actor.AdvancePresentation(1.4f);
            int pausedIdleFrame = actor.CurrentFrame;
            StairwellCatIdleKind pausedIdleKind =
                actor.CurrentIdleKind;

            Assert.That(actor.TryPrepareFeeding(), Is.True);
            Assert.That(actor.IsFeedingPrepared, Is.True);
            Assert.That(actor.IsFeeding, Is.False);
            Assert.That(actor.BeginPreparedFeeding(), Is.True);
            Assert.That(actor.IsFeedingPrepared, Is.False);
            Assert.That(actor.BeginFeeding(), Is.False);
            Assert.That(actor.IsFeeding, Is.True);
            Assert.That(actor.CurrentFeedingFrame, Is.Zero);

            actor.AdvancePresentation(0.5f);
            Assert.That(actor.CurrentFeedingFrame, Is.EqualTo(3));
            // The idle model pauses while the cat eats.
            Assert.That(
                actor.CurrentFrame,
                Is.EqualTo(pausedIdleFrame));
            Assert.That(
                actor.CurrentIdleKind,
                Is.EqualTo(pausedIdleKind));
            // Head-down eating pose on the head pivot.
            Assert.That(
                Quaternion.Angle(
                    anchors.HeadPivot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(10f));

            Assert.That(actor.CancelFeeding(), Is.True);
            Assert.That(actor.CancelFeeding(), Is.False);
            Assert.That(actor.IsFeeding, Is.False);
            Assert.That(actor.CurrentFeedingFrame, Is.EqualTo(-1));

            Assert.That(actor.BeginFeeding(), Is.True);
            Assert.That(actor.CompleteFeeding(), Is.True);
            Assert.That(actor.CompleteFeeding(), Is.False);

            Assert.That(actor.TryPrepareFeeding(), Is.True);
            Assert.That(actor.CancelFeedingPreparation(), Is.True);
            Assert.That(actor.CancelFeedingPreparation(), Is.False);
            Assert.That(actor.BeginPreparedFeeding(), Is.False);

            Assert.That(actor.BeginFeeding(), Is.True);
            actor.AdvancePresentation(
                (float)StairwellCatFeedingTimeline
                    .DurationSeconds);
            Assert.That(actor.IsFeeding, Is.False);
        }

        [Test]
        public void Actor_GrinIsHiddenByDefaultAndTurnsHeadToCamera()
        {
            (StairwellCatActor actor,
             StairwellCatRigAnchors anchors,
             StairwellCatGrinController grin,
             GameObject _) = CreateActor();

            // Default: the grin does not exist.
            Assert.That(grin.IsGrinVisible, Is.False);
            Assert.That(grin.GrinProgress, Is.Zero);
            Assert.That(anchors.GrinRenderer.enabled, Is.False);
            Assert.That(
                grin.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Hidden));

            Assert.That(grin.BeginGrin(1f), Is.True);
            actor.AdvancePresentation(0.2f);
            Assert.That(grin.IsGrinVisible, Is.True);
            Assert.That(anchors.GrinRenderer.enabled, Is.True);
            Assert.That(grin.GrinProgress, Is.GreaterThan(0.5f));

            actor.AdvancePresentation(
                StairwellCatGrinTimeline.AppearSeconds);
            Assert.That(grin.GrinProgress, Is.EqualTo(1f));
            Assert.That(
                grin.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Held));

            // The camera sits behind the cat; the committed grin
            // swings the head past the ordinary tracking clamp.
            float headYaw = Quaternion.Angle(
                anchors.HeadPivot.localRotation,
                Quaternion.identity);
            Assert.That(
                headYaw,
                Is.GreaterThan(
                    StairwellCatHeadYawModel
                        .DefaultMaxTrackYawDegrees));

            // The finite hold un-draws on its own...
            actor.AdvancePresentation(
                1f + StairwellCatGrinTimeline.VanishSeconds + 0.1f);
            Assert.That(grin.IsGrinVisible, Is.False);
            Assert.That(anchors.GrinRenderer.enabled, Is.False);
            Assert.That(
                grin.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Hidden));

            // ...and an explicit EndGrin cuts a sustained one short.
            Assert.That(
                grin.BeginGrin(float.PositiveInfinity),
                Is.True);
            actor.AdvancePresentation(2f);
            Assert.That(grin.GrinProgress, Is.EqualTo(1f));
            Assert.That(grin.EndGrin(), Is.True);
            actor.AdvancePresentation(
                StairwellCatGrinTimeline.VanishSeconds + 0.1f);
            Assert.That(grin.IsGrinVisible, Is.False);
            Assert.That(grin.EndGrin(), Is.False);
        }

        [Test]
        public void GrinController_SetGrinProgressOverridesTimeline()
        {
            GameObject host = CreateGameObject("Grin Host");
            GameObject rendererObject =
                new GameObject("Grin Renderer");
            rendererObject.transform.SetParent(
                host.transform,
                false);
            rendererObject.AddComponent<MeshFilter>();
            MeshRenderer renderer =
                rendererObject.AddComponent<MeshRenderer>();
            StairwellCatGrinController controller =
                host.AddComponent<StairwellCatGrinController>();

            // Uninitialized, nothing works.
            Assert.That(controller.BeginGrin(), Is.False);

            controller.Initialize(renderer);
            Assert.That(renderer.enabled, Is.False);

            Assert.That(controller.BeginGrin(), Is.True);
            controller.Advance(10f);
            Assert.That(controller.IsGrinVisible, Is.False);

            controller.SetGrinProgress(0.5f);
            Assert.That(controller.IsGrinVisible, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(StairwellCatGrinPhase.Held));
            // The manual override cancelled the timeline.
            controller.Advance(10f);
            Assert.That(controller.GrinProgress, Is.EqualTo(0.5f));
            Assert.That(
                controller.HeadTurnWeight,
                Is.EqualTo(0.5f).Within(0.0001f));

            controller.SetGrinProgress(0f);
            Assert.That(controller.IsGrinVisible, Is.False);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(controller.BeginGrin(float.NaN), Is.False);
        }

        private (StairwellCatActor,
            StairwellCatRigAnchors,
            StairwellCatGrinController,
            GameObject) CreateActor()
        {
            GameObject cameraObject =
                CreateGameObject("Cat Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject player =
                CreateGameObject("Cat Test Player");
            GameObject cat = CreateGameObject("Cat Test Actor");

            StairwellCatRigAnchors anchors =
                StairwellCatTestRig.Create(cat);
            // The cat faces host -Z; put the camera behind the
            // cat's back, above, like the MiddleFlight shot, and
            // the player on the landing in front.
            cameraObject.transform.position =
                cat.transform.position +
                new Vector3(-1.5f, 2f, 1.6f);
            player.transform.position =
                cat.transform.position -
                anchors.ModelRoot.forward * 1.2f;

            StairwellCatGrinController grin =
                cat.AddComponent<StairwellCatGrinController>();
            grin.Initialize(anchors.GrinRenderer);
            StairwellCatActor actor =
                cat.AddComponent<StairwellCatActor>();
            actor.Initialize(
                camera,
                player.transform,
                anchors,
                grin);
            return (actor, anchors, grin, player);
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObjects.Add(gameObject);
            return gameObject;
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.That(IsFinite(value.x), Is.True);
            Assert.That(IsFinite(value.y), Is.True);
            Assert.That(IsFinite(value.z), Is.True);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
