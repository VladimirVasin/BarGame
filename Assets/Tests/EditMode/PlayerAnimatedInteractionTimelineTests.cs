using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerAnimatedInteractionTimelineTests
    {
        private const string ResourcePath =
            "Player/TestAnimatedInteractionAtlas";
        private const float Tolerance = 0.0001f;

        [Test]
        public void Definition_DefaultsMatchTheSixtyFourFrameContract()
        {
            var definition =
                new PlayerAnimatedInteractionDefinition(
                    ResourcePath);

            Assert.That(
                definition.TextureResourcePath,
                Is.EqualTo(ResourcePath));
            Assert.That(definition.EnterStartFrame, Is.Zero);
            Assert.That(definition.EnterFrameCount, Is.EqualTo(24));
            Assert.That(
                definition.EnterFramesPerSecond,
                Is.EqualTo(12f));
            Assert.That(
                definition.LoopStartFrame,
                Is.EqualTo(24));
            Assert.That(definition.LoopFrameCount, Is.EqualTo(16));
            Assert.That(
                definition.LoopFramesPerSecond,
                Is.EqualTo(8f));
            Assert.That(
                definition.LoopDurationSeconds,
                Is.EqualTo(2d).Within(Tolerance));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(0),
                Is.Zero);
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(15),
                Is.Zero);
            Assert.That(
                definition.ExitStartFrame,
                Is.EqualTo(40));
            Assert.That(definition.ExitFrameCount, Is.EqualTo(24));
            Assert.That(
                definition.ExitFramesPerSecond,
                Is.EqualTo(12f));
            Assert.That(
                definition.RenderAboveSceneDepth,
                Is.False);
            Assert.That(
                definition.TotalFrameCount,
                Is.EqualTo(64));

            var overlayDefinition =
                new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    renderAboveSceneDepth: true);
            Assert.That(
                overlayDefinition.RenderAboveSceneDepth,
                Is.True);
        }

        [Test]
        public void ControllerAtlasContract_UsesUnityBottomOriginRows()
        {
            Assert.That(
                PlayerAnimatedInteractionController.AtlasColumnCount,
                Is.EqualTo(8));
            Assert.That(
                PlayerAnimatedInteractionController.AtlasRowCount,
                Is.EqualTo(8));
            Assert.That(
                PlayerAnimatedInteractionController.AtlasFrameCount,
                Is.EqualTo(64));
            Assert.That(
                PlayerAnimatedInteractionController.FrameWidth,
                Is.EqualTo(128));
            Assert.That(
                PlayerAnimatedInteractionController.FrameHeight,
                Is.EqualTo(96));
            Assert.That(
                PlayerAnimatedInteractionController.PixelsPerUnit,
                Is.EqualTo(48f));
            Assert.That(
                PlayerAnimatedInteractionController.HipPivotXPixels,
                Is.EqualTo(64f));
            Assert.That(
                PlayerAnimatedInteractionController.HipPivotYPixels,
                Is.EqualTo(40f));
            Assert.That(
                PlayerAnimatedInteractionController
                    .AuthoredTextureFlipX,
                Is.True);

            AssertRect(
                PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(0),
                0f,
                0f);
            AssertRect(
                PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(7),
                896f,
                0f);
            AssertRect(
                PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(8),
                0f,
                96f);
            AssertRect(
                PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(63),
                896f,
                672f);
        }

        [Test]
        public void ControllerAtlasContract_RejectsInvalidFrameIndices()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerAnimatedInteractionController
                    .GetAtlasFrameRect(64));
        }

        [Test]
        public void ControllerOrientationContract_MapsAuthoredXToWorldAxis()
        {
            Vector3 cameraRight = Vector3.right;
            Vector3 cameraUp = Vector3.up;

            Assert.That(
                PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        Vector3.right,
                        cameraRight,
                        cameraUp),
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        Vector3.up,
                        cameraRight,
                        cameraUp),
                Is.EqualTo(-90f).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        (Vector3.right + Vector3.up).normalized,
                        cameraRight,
                        cameraUp),
                Is.EqualTo(-45f).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        Vector3.forward,
                        cameraRight,
                        cameraUp),
                Is.EqualTo(0f).Within(Tolerance),
                "An axis perpendicular to the camera plane has no " +
                "stable screen alignment.");
        }

        [Test]
        public void ControllerOrientationContract_UsesPerspectiveLineAtAnchor()
        {
            GameObject cameraObject =
                new GameObject("Perspective Roll Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = 60f;
                camera.aspect = 16f / 9f;
                camera.pixelRect =
                    new Rect(0f, 0f, 1600f, 900f);
                camera.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);

                Vector3 anchor = new Vector3(1f, 1f, 5f);
                Vector3 axis =
                    (Vector3.right + Vector3.forward)
                    .normalized;
                Vector3 screenStart =
                    camera.WorldToScreenPoint(anchor - axis);
                Vector3 screenEnd =
                    camera.WorldToScreenPoint(anchor + axis);
                Vector3 screenDelta =
                    screenEnd - screenStart;
                float expected =
                    -Mathf.Atan2(
                        screenDelta.y,
                        screenDelta.x) *
                    Mathf.Rad2Deg;

                float perspectiveRoll =
                    PlayerAnimatedInteractionController
                        .CalculateCameraPlaneTargetRollDegrees(
                            camera,
                            anchor,
                            axis);
                float basisRoll =
                    PlayerAnimatedInteractionController
                        .CalculateCameraPlaneTargetRollDegrees(
                            axis,
                            camera.transform.right,
                            camera.transform.up);

                Assert.That(
                    perspectiveRoll,
                    Is.EqualTo(expected).Within(Tolerance));
                Assert.That(
                    Mathf.Abs(
                        Mathf.DeltaAngle(
                            perspectiveRoll,
                            basisRoll)),
                    Is.GreaterThan(1f),
                    "An off-center perspective line must not collapse " +
                    "to the camera basis approximation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(
                    cameraObject);
            }
        }

        [Test]
        public void ControllerOrientationContract_FallsBackForHiddenLine()
        {
            GameObject cameraObject =
                new GameObject("Fallback Roll Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                camera.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);

                Assert.That(
                    PlayerAnimatedInteractionController
                        .CalculateCameraPlaneTargetRollDegrees(
                            camera,
                            new Vector3(0f, 0f, -5f),
                            Vector3.up),
                    Is.EqualTo(-90f).Within(Tolerance));
                Assert.That(
                    PlayerAnimatedInteractionController
                        .CalculateCameraPlaneTargetRollDegrees(
                            camera,
                            new Vector3(0f, 0f, 5f),
                            Vector3.forward),
                    Is.Zero.Within(Tolerance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(
                    cameraObject);
            }
        }

        [Test]
        public void ControllerBeginContract_PreservesOptionalAxisOverloads()
        {
            Type controllerType =
                typeof(PlayerAnimatedInteractionController);
            Type definitionType =
                typeof(PlayerAnimatedInteractionDefinition);

            Assert.That(
                controllerType.GetMethod(
                    nameof(
                        PlayerAnimatedInteractionController.Begin),
                    new[]
                    {
                        definitionType,
                        typeof(Vector3),
                        typeof(Vector3)
                    }),
                Is.Not.Null);
            Assert.That(
                controllerType.GetMethod(
                    nameof(
                        PlayerAnimatedInteractionController.Begin),
                    new[]
                    {
                        definitionType,
                        typeof(Vector3),
                        typeof(Vector3),
                        typeof(Vector3)
                    }),
                Is.Not.Null);
        }

        [Test]
        public void ControllerOrientationContract_BlendsRollByPhase()
        {
            const float targetRoll = -80f;

            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Idle,
                        0.5f,
                        targetRoll),
                Is.Zero);
            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Entering,
                        0f,
                        targetRoll),
                Is.Zero);
            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Entering,
                        0.25f,
                        targetRoll),
                Is.EqualTo(-12.5f).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Looping,
                        0.25f,
                        targetRoll),
                Is.EqualTo(targetRoll).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Exiting,
                        0.25f,
                        targetRoll),
                Is.EqualTo(-67.5f).Within(Tolerance));
            Assert.That(
                PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Exiting,
                        1f,
                        targetRoll),
                Is.Zero.Within(Tolerance));
        }

        [Test]
        public void ControllerOrientationContract_RejectsInvalidBasis()
        {
            Assert.Throws<ArgumentException>(
                () => PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        Vector3.right,
                        Vector3.zero,
                        Vector3.up));
            Assert.Throws<ArgumentException>(
                () => PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        Vector3.right,
                        Vector3.right,
                        Vector3.right));
            Assert.Throws<ArgumentException>(
                () => PlayerAnimatedInteractionController
                    .CalculateCameraPlaneTargetRollDegrees(
                        new Vector3(float.NaN, 0f, 0f),
                        Vector3.right,
                        Vector3.up));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerAnimatedInteractionController
                    .EvaluateCameraPlaneRollDegrees(
                        PlayerAnimatedInteractionPhase.Entering,
                        float.NaN,
                        20f));
        }

        [Test]
        public void Timeline_RequiresLoopBeforeExitAndCompletesAtIdle()
        {
            var timeline = CreateTimeline();

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle));
            Assert.That(timeline.FrameIndex, Is.EqualTo(-1));
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.RequestExit(), Is.False);

            Assert.That(timeline.Begin(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Entering));
            Assert.That(timeline.FrameIndex, Is.Zero);
            Assert.That(timeline.Begin(), Is.False);
            Assert.That(timeline.RequestExit(), Is.False);

            timeline.Advance(2f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.FrameIndex, Is.EqualTo(24));
            Assert.That(timeline.IsActive, Is.True);
            Assert.That(timeline.RequestExit(), Is.True);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(timeline.FrameIndex, Is.EqualTo(40));
            Assert.That(timeline.RequestExit(), Is.False);

            timeline.Advance(2f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle));
            Assert.That(timeline.FrameIndex, Is.EqualTo(-1));
            Assert.That(timeline.IsActive, Is.False);
        }

        [Test]
        public void Timeline_LoopWrapsWithoutAutomaticExit()
        {
            var timeline = CreateTimeline();
            timeline.Begin();
            timeline.Advance(2f);
            timeline.Advance(0.125f);

            Assert.That(timeline.FrameIndex, Is.EqualTo(25));

            timeline.Advance(1.875f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.FrameIndex, Is.EqualTo(24));
            Assert.That(timeline.IsActive, Is.True);

            timeline.Advance(1000f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.FrameIndex, Is.EqualTo(24));
        }

        [Test]
        public void Timeline_LoopAppliesExtraFrameHoldsAndWraps()
        {
            PlayerAnimatedInteractionTimeline timeline =
                CreateTimelineWithLoopHolds();
            timeline.Begin();
            timeline.Advance(0.5f);

            Assert.That(
                timeline.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(timeline.FrameIndex, Is.EqualTo(1));

            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(2));

            timeline.Advance(0.5f);
            Assert.That(
                timeline.FrameIndex,
                Is.EqualTo(2),
                "The first breathing extreme must remain held.");

            timeline.Advance(0.25f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(3));

            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(4));

            timeline.Advance(1f);
            Assert.That(
                timeline.FrameIndex,
                Is.EqualTo(4),
                "The resting extreme must keep its longer hold.");

            timeline.Advance(0.25f);
            Assert.That(
                timeline.FrameIndex,
                Is.EqualTo(1),
                "The completed breathing cycle must wrap to " +
                "the first loop frame.");
        }

        [Test]
        public void Timeline_LoopHoldsRemainFrameChunkIndependent()
        {
            PlayerAnimatedInteractionTimeline singleStep =
                CreateTimelineWithLoopHolds();
            PlayerAnimatedInteractionTimeline chunked =
                CreateTimelineWithLoopHolds();
            singleStep.Begin();
            chunked.Begin();

            singleStep.Advance(8.875f);
            for (int index = 0; index < 71; index++)
            {
                chunked.Advance(0.125f);
            }

            AssertSameState(singleStep, chunked);
        }

        [Test]
        public void Timeline_ProducesSameStateAcrossDeltaChunking()
        {
            PlayerAnimatedInteractionTimeline singleStep =
                CreateTimeline();
            PlayerAnimatedInteractionTimeline chunked =
                CreateTimeline();
            singleStep.Begin();
            chunked.Begin();

            singleStep.Advance(2.875f);
            for (int index = 0; index < 23; index++)
            {
                chunked.Advance(0.125f);
            }

            AssertSameState(singleStep, chunked);
            Assert.That(singleStep.RequestExit(), Is.True);
            Assert.That(chunked.RequestExit(), Is.True);

            singleStep.Advance(0.75f);
            for (int index = 0; index < 6; index++)
            {
                chunked.Advance(0.125f);
            }

            AssertSameState(singleStep, chunked);
        }

        [Test]
        public void DefinitionAndTimeline_RejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(
                () => new PlayerAnimatedInteractionDefinition(
                    " "));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    enterFramesPerSecond: float.NaN));
            Assert.Throws<ArgumentException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    loopFrameExtraHoldSeconds:
                        new float[15]));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    loopFrameExtraHoldSeconds:
                        CreateLoopHolds(-0.01f)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    loopFrameExtraHoldSeconds:
                        CreateLoopHolds(float.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    loopFrameExtraHoldSeconds:
                        CreateLoopHolds(float.PositiveInfinity)));

            PlayerAnimatedInteractionTimeline timeline =
                CreateTimeline();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Definition
                    .GetLoopFrameExtraHoldSeconds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Definition
                    .GetLoopFrameDurationSeconds(16));
        }

        [Test]
        public void Definition_CopiesLoopFrameHolds()
        {
            float[] holds = CreateLoopHolds(0.4f);
            var definition =
                new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    loopFrameExtraHoldSeconds: holds);

            holds[0] = 9f;

            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(0),
                Is.EqualTo(0.4f));
            Assert.That(
                definition.LoopDurationSeconds,
                Is.EqualTo(2.4d).Within(Tolerance));
        }

        private static PlayerAnimatedInteractionTimeline
            CreateTimeline()
        {
            return new PlayerAnimatedInteractionTimeline(
                new PlayerAnimatedInteractionDefinition(
                    ResourcePath));
        }

        private static PlayerAnimatedInteractionTimeline
            CreateTimelineWithLoopHolds()
        {
            return new PlayerAnimatedInteractionTimeline(
                new PlayerAnimatedInteractionDefinition(
                    ResourcePath,
                    enterFrameCount: 1,
                    enterFramesPerSecond: 2f,
                    loopFrameCount: 4,
                    loopFramesPerSecond: 2f,
                    exitFrameCount: 1,
                    exitFramesPerSecond: 2f,
                    loopFrameExtraHoldSeconds:
                        new[] { 0f, 0.25f, 0f, 0.75f }));
        }

        private static float[] CreateLoopHolds(
            float firstFrameHold)
        {
            var holds = new float[16];
            holds[0] = firstFrameHold;
            return holds;
        }

        private static void AssertRect(
            Rect actual,
            float expectedX,
            float expectedY)
        {
            Assert.That(actual.x, Is.EqualTo(expectedX));
            Assert.That(actual.y, Is.EqualTo(expectedY));
            Assert.That(
                actual.width,
                Is.EqualTo(
                    PlayerAnimatedInteractionController.FrameWidth));
            Assert.That(
                actual.height,
                Is.EqualTo(
                    PlayerAnimatedInteractionController.FrameHeight));
        }

        private static void AssertSameState(
            PlayerAnimatedInteractionTimeline expected,
            PlayerAnimatedInteractionTimeline actual)
        {
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(
                actual.FrameIndex,
                Is.EqualTo(expected.FrameIndex));
            Assert.That(
                actual.PhaseProgress,
                Is.EqualTo(expected.PhaseProgress)
                    .Within(Tolerance));
            Assert.That(
                actual.IsActive,
                Is.EqualTo(expected.IsActive));
        }
    }
}
