using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBalconySmokingInteractionPlayModeTests
    {
        private const float TimeoutSeconds = 15f;
        private const float GuidedMoveSpeed = 2.6f;
        private const float GuidedStartOffset = 0.42f;
        private float previousTimeScale;
        private HomeInteriorRoot home;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.ResetDrinkingState();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            Scene homeScene =
                SceneManager.GetSceneByName(
                    SceneIds.HomeInterior);
            if (homeScene.IsValid() && homeScene.isLoaded)
            {
                Scene cleanup =
                    SceneManager.CreateScene(
                        "Home Smoking Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(homeScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            inputFixture?.TearDown();
            inputFixture = null;

            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.ResetDrinkingState();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Smoking_ClickableExitQueuesAtCalmFrameAndRestores()
        {
            yield return LoadHome();
            Assert.That(home.SmokingPlan, Is.Not.Null);
            Assert.That(home.Smoking, Is.Not.Null);
            Assert.That(home.SmokingMusic, Is.Not.Null);
            Assert.That(home.Smoking.IsInitialized, Is.True);
            Assert.That(
                home.Smoking.Definition
                    .VisualCrossfadeDurationSeconds,
                Is.Zero,
                "Smoking must use a hard 0/1 rig-to-atlas handoff.");

            Vector3 entryPosition =
                home.transform.TransformPoint(
                    home.SmokingPlan.EntryRootPosition);
            Quaternion entryRotation =
                home.transform.rotation *
                home.SmokingPlan.EntryRotation;
            Vector3 guidedStart =
                home.transform.TransformPoint(
                    home.SmokingPlan.EntryRootPosition +
                    Vector3.back * GuidedStartOffset);
            home.Player.Motor.Teleport(guidedStart);
            home.Player.GameObject.transform.rotation =
                home.transform.rotation *
                Quaternion.LookRotation(Vector3.left, Vector3.up);
            Physics.SyncTransforms();
            yield return WaitForActiveSmoking();
            Assert.That(
                home.Smoking.CanInteract(
                    home.Player.Interactor),
                Is.True);

            Vector3 balconyCameraPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion balconyCameraRotation =
                home.CameraFollow.FixedBaseRotation;

            Time.timeScale = 0.25f;
            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(home.Smoking.OwnsInteraction, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.Player.Interactor.InputEnabled, Is.False);
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.Empty);
            AssertGuidedApproachPresentation();
            AssertBoundedGuidedStep(
                guidedStart,
                home.Player.GameObject.transform.position,
                entryPosition,
                Time.deltaTime);

            inputFixture.Release(
                keyboard.eKey,
                queueEventOnly: true);
            InputSystem.Update();
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            inputFixture.Press(
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            Assert.That(keyboard.wKey.isPressed, Is.True);
            Assert.That(keyboard.dKey.isPressed, Is.True);

            bool madeGuidedProgress = false;
            int positioningFrames = 0;
            float positioningDeadline =
                Time.realtimeSinceStartup + 3f;
            while (home.AnimatedInteraction.Phase ==
                       PlayerAnimatedInteractionPhase.Positioning &&
                   Time.realtimeSinceStartup < positioningDeadline)
            {
                AssertGuidedApproachPresentation();
                Vector3 previousPosition =
                    home.Player.GameObject.transform.position;
                float previousDistance =
                    PlanarDistance(previousPosition, entryPosition);
                yield return null;
                Vector3 currentPosition =
                    home.Player.GameObject.transform.position;
                AssertBoundedGuidedStep(
                    previousPosition,
                    currentPosition,
                    entryPosition,
                    Time.deltaTime);
                AssertOnGuidedSegment(
                    guidedStart,
                    entryPosition,
                    currentPosition);
                float currentDistance =
                    PlanarDistance(currentPosition, entryPosition);
                Assert.That(
                    currentDistance,
                    Is.LessThanOrEqualTo(previousDistance + 0.001f),
                    "WASD input must not redirect the scripted smoking " +
                    "approach away from its entry point.");
                madeGuidedProgress |=
                    currentDistance + 0.0001f < previousDistance;
                positioningFrames++;
            }

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            inputFixture.Release(
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            Time.timeScale = 1f;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Entering));
            Assert.That(positioningFrames, Is.GreaterThan(1));
            Assert.That(madeGuidedProgress, Is.True);
            AssertExactPose(
                home.Player.GameObject.transform,
                entryPosition,
                entryRotation);
            Assert.That(
                home.Player.Visual.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.BackRight),
                "The rig handoff view must match the smoking atlas's " +
                "exact BackRight endpoint.");
            AssertAtlasOnlyPresentation();
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.EqualTo(
                    HomeBalconySmokingInteraction
                        .StopSmokingPromptKey));
            Assert.That(
                Vector3.Angle(
                    home.Player.GameObject.transform.forward,
                    home.transform.TransformDirection(
                        Vector3.right)),
                Is.LessThan(0.01f));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.flipX,
                Is.False,
                "Smoking must use the atlas's city-facing profile without " +
                "the bed animation's shared mirror.");
            Assert.That(
                home.AnimatedInteraction.CameraPlaneAlignmentEnabled,
                Is.False,
                "The balcony shot and smoking atlas must share world-up " +
                "alignment so the exact endpoint cannot jump or drift.");
            Assert.That(
                home.Player.Visual.VisualRoot
                    .GetComponent<BillboardSprite>()
                    .CameraPlaneAlignmentEnabled,
                Is.False,
                "The ordinary balcony rig must already be world-up before " +
                "the hard atlas handoff.");
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.Smoking.Timeline.CameraDriftBlend,
                Is.EqualTo(0f),
                "Camera drift must begin at zero instead of cutting into " +
                "the captured Balcony shot.");

            Time.timeScale = 12f;
            yield return WaitForAtlasPhaseCompletion(
                PlayerAnimatedInteractionPhase.Looping);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.flipX,
                Is.False,
                "The smoking-specific direction must remain unchanged " +
                "after the enter handoff.");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(
                Vector3.Angle(
                    home.AnimatedInteraction.AnimationVisualRoot.up,
                    Vector3.up),
                Is.LessThan(0.1f),
                "The smoking silhouette must remain vertical in world " +
                "space throughout its loop.");
            float footOffset =
                (PlayerSpriteRig.FeetPivotPixels -
                 PlayerAnimatedInteractionController.HipPivotYPixels) /
                PlayerAnimatedInteractionController.PixelsPerUnit;
            Vector3 actualFoot =
                home.AnimatedInteraction.AnimationVisualRoot.TransformPoint(
                    Vector3.up * footOffset);
            Vector3 expectedFoot = home.transform.TransformPoint(
                home.SmokingPlan.DockRootPosition +
                Vector3.up *
                HomeBalconySmokingPlan.UprightVisualOffset);
            Assert.That(
                Vector3.Distance(actualFoot, expectedFoot),
                Is.LessThan(0.01f),
                "The upright smoking billboard must keep the authored feet " +
                "on the balcony dock instead of moving them through the " +
                "pitched camera plane.");
            Assert.That(
                Vector3.Dot(
                    home.AnimatedInteraction
                        .AnimationVisualRoot.right,
                    camera.transform.right),
                Is.LessThan(0f),
                "The camera-facing billboard reverses its local X, so the " +
                "unflipped atlas is the city-facing handedness.");
            Assert.That(
                Vector3.Angle(camera.transform.up, Vector3.up),
                Is.GreaterThan(10f),
                "The close camera must remain materially pitched so this " +
                "test distinguishes world-up from camera-plane alignment.");
            Vector3 actionHipWorld =
                home.transform.TransformPoint(
                    home.SmokingPlan.ActionHipPosition);
            Vector3 cityFacingWorld =
                actionHipWorld +
                home.transform.TransformDirection(Vector3.right);
            Vector3 actionHipViewport =
                camera.WorldToViewportPoint(actionHipWorld);
            Assert.That(actionHipViewport.z, Is.GreaterThan(0f));
            Assert.That(
                actionHipViewport.x,
                Is.InRange(0.28f, 0.43f),
                "The cityward close-camera framing must leave the hero " +
                "safely visible on the left side across supported desktop " +
                "aspects.");
            Vector3 cityFacingViewport =
                camera.WorldToViewportPoint(cityFacingWorld);
            Assert.That(
                cityFacingViewport.z,
                Is.GreaterThan(0f));
            Assert.That(
                cityFacingViewport.x,
                Is.InRange(0f, 1f),
                "The one-metre cityward probe must remain inside the " +
                "close shot instead of panning beyond the frame.");
            Assert.That(
                cityFacingViewport.x,
                Is.GreaterThan(actionHipViewport.x),
                "Home-local +X must project screen-right in the close " +
                "smoking shot for the unmirrored source profile.");
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(
                        HomeBalconySmokingPlan.CameraFieldOfView)
                    .Within(0.01f));
            Vector3 staticSmokingCameraPosition =
                home.transform.TransformPoint(
                    HomeBalconySmokingPlan.CameraPosition);
            Vector3 smokingLookAt =
                home.transform.TransformPoint(
                    home.SmokingPlan.CameraLookAt);
            Quaternion staticSmokingCameraRotation =
                Quaternion.LookRotation(
                    (smokingLookAt - staticSmokingCameraPosition)
                        .normalized,
                    home.transform.up);
            Assert.That(
                Vector3.Dot(
                    staticSmokingCameraRotation * Vector3.forward,
                    home.transform.TransformDirection(Vector3.right)),
                Is.GreaterThan(0.19f),
                "The authored close-camera heading must carry a visible " +
                "cityward +X component while keeping its position fixed.");
            HomeBalconySmokingCameraDriftSample drift =
                home.Smoking.Timeline.CameraDrift;
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    staticSmokingCameraPosition +
                    staticSmokingCameraRotation *
                    drift.LocalPosition),
                Is.LessThan(0.0001f),
                "Smoking drift must be layered in the close camera's " +
                "local space on top of the authored target position.");
            Assert.That(
                PreciseQuaternionAngleDegrees(
                    home.CameraFollow.FixedBaseRotation,
                    staticSmokingCameraRotation *
                        Quaternion.Euler(drift.LocalEulerAngles)),
                Is.LessThan(0.001f),
                "Smoking drift must be layered over the authored target " +
                "rotation without changing its base path.");
            AssertDriftWithinAuthoredBounds(drift);

            Vector3 firstDriftingPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion firstDriftingRotation =
                home.CameraFollow.FixedBaseRotation;
            float driftDeadline =
                Time.realtimeSinceStartup + 0.75f;
            while (Time.realtimeSinceStartup < driftDeadline &&
                    (Vector3.Distance(
                        home.CameraFollow.FixedBasePosition,
                        firstDriftingPosition) < 0.00025f ||
                    PreciseQuaternionAngleDegrees(
                        home.CameraFollow.FixedBaseRotation,
                        firstDriftingRotation) < 0.002f))
            {
                yield return null;
            }

            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    firstDriftingPosition),
                Is.GreaterThanOrEqualTo(0.00025f),
                "The close smoking camera must continue drifting during " +
                "the persistent loop.");
            Assert.That(
                PreciseQuaternionAngleDegrees(
                    home.CameraFollow.FixedBaseRotation,
                    firstDriftingRotation),
                Is.GreaterThanOrEqualTo(0.002f),
                "The close smoking camera must keep a subtle rotational " +
                "sway instead of becoming static.");
            Vector3 localCameraOffset =
                Quaternion.Inverse(staticSmokingCameraRotation) *
                (home.CameraFollow.FixedBasePosition -
                 staticSmokingCameraPosition);
            Assert.That(
                Mathf.Abs(localCameraOffset.x),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .LateralAmplitudeMeters +
                    0.0001f));
            Assert.That(
                Mathf.Abs(localCameraOffset.y),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .VerticalAmplitudeMeters +
                    0.0001f));
            Assert.That(
                Mathf.Abs(localCameraOffset.z),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .DepthAmplitudeMeters +
                    0.0001f));
            Assert.That(
                home.SmokingMusic.NormalizedGain,
                Is.EqualTo(1f).Within(0.001f));

            yield return WaitForUnsafeLoopFrame();
            Assert.That(
                home.InteractionPrompt.IsClickable,
                Is.True);
            Assert.That(
                home.InteractionPrompt.TryInvokePrompt(),
                Is.True);
            Assert.That(home.Smoking.ExitQueued, Is.True);
            Assert.That(
                home.InteractionPrompt.PromptKey,
                Is.Empty);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.SmokingMusic.NormalizedGain,
                Is.EqualTo(1f).Within(0.001f));

            Time.timeScale = 12f;
            yield return WaitForAnimatedPhase(
                PlayerAnimatedInteractionPhase.Exiting);
            AssertAtlasOnlyPresentation();
            Time.timeScale = 12f;
            yield return WaitForAtlasPhaseCompletion(
                PlayerAnimatedInteractionPhase.Idle);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(home.Smoking.OwnsInteraction, Is.False);
            Assert.That(home.Smoking.ExitQueued, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(
                home.Player.Visual.BodyRenderer.enabled,
                Is.True);
            Assert.That(
                home.Player.Visual.BodyRenderer.color.a,
                Is.EqualTo(1f).Within(0.001f));
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                SpriteRenderer renderer =
                    home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
                Assert.That(
                    renderer.color.a,
                    Is.EqualTo(1f).Within(0.001f));
            }
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.False);
            Assert.That(home.Player.Shadow.enabled, Is.True);
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            Assert.That(
                home.SmokingMusic.NormalizedGain,
                Is.EqualTo(0f));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(70f).Within(0.01f));
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    balconyCameraPosition),
                Is.LessThan(0.001f),
                "Completing smoking must restore the exact captured " +
                "Balcony camera position after the drift fades out.");
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    balconyCameraRotation),
                Is.LessThan(0.01f),
                "Completing smoking must restore the exact captured " +
                "Balcony camera rotation after the drift fades out.");
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));
            AssertExactPose(
                home.Player.GameObject.transform,
                home.transform.TransformPoint(
                    home.SmokingPlan.ExitRootPosition),
                home.transform.rotation *
                    home.SmokingPlan.ExitRotation);
            Assert.That(
                home.Player.Visual.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.BackRight));
        }

        private IEnumerator LoadHome()
        {
            AsyncOperation load =
                SceneManager.LoadSceneAsync(
                    SceneIds.HomeInterior,
                    LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home =
                    Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
        }

        private IEnumerator WaitForAnimatedPhase(
            PlayerAnimatedInteractionPhase expected)
        {
            float deadline =
                Time.realtimeSinceStartup + 5f;
            while (home.AnimatedInteraction.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expected));
        }

        private IEnumerator WaitForActiveSmoking()
        {
            float deadline =
                Time.realtimeSinceStartup + 2f;
            while (!ReferenceEquals(
                       home.Player.Interactor.ActiveInteractable,
                       home.Smoking) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.Player.Interactor.ActiveInteractable,
                Is.SameAs(home.Smoking));
        }

        private IEnumerator WaitForAtlasPhaseCompletion(
            PlayerAnimatedInteractionPhase expected)
        {
            PlayerAnimatedInteractionPhase activePhase =
                expected == PlayerAnimatedInteractionPhase.Looping
                    ? PlayerAnimatedInteractionPhase.Entering
                    : PlayerAnimatedInteractionPhase.Exiting;
            float deadline =
                Time.realtimeSinceStartup + 5f;
            while (home.AnimatedInteraction.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    home.AnimatedInteraction.Phase,
                    Is.EqualTo(activePhase));
                AssertAtlasOnlyPresentation();
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expected));
            if (expected != PlayerAnimatedInteractionPhase.Idle)
            {
                AssertAtlasOnlyPresentation();
            }
        }

        private IEnumerator WaitForUnsafeLoopFrame()
        {
            float deadline =
                Time.realtimeSinceStartup + 5f;
            int localFrame = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                localFrame =
                    home.AnimatedInteraction.FrameIndex -
                    home.Smoking.Definition.LoopStartFrame;
                if (localFrame >= 4 && localFrame <= 20)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(localFrame, Is.InRange(4, 20));
            Assert.That(
                HomeBalconySmokingTimeline
                    .IsSafeExitLoopFrame(localFrame),
                Is.False);
        }

        private void AssertGuidedApproachPresentation()
        {
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.False);
            Assert.That(home.Player.Shadow.enabled, Is.True);
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                SpriteRenderer renderer =
                    home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
                Assert.That(
                    renderer.color.a,
                    Is.EqualTo(1f).Within(0.001f));
            }
        }

        private void AssertAtlasOnlyPresentation()
        {
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.color.a,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.Player.Shadow.enabled,
                Is.False);
            Assert.That(
                home.Player.ContactShadow.enabled,
                Is.False);
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                SpriteRenderer renderer =
                    home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.False);
                Assert.That(
                    renderer.color.a,
                    Is.EqualTo(0f).Within(0.001f));
            }
        }

        private static void AssertBoundedGuidedStep(
            Vector3 previous,
            Vector3 current,
            Vector3 target,
            float deltaTime)
        {
            float step = PlanarDistance(previous, current);
            Assert.That(
                step,
                Is.LessThanOrEqualTo(
                    GuidedMoveSpeed * Mathf.Max(0f, deltaTime) +
                    0.005f),
                "The authored approach must advance by a bounded walk " +
                "step instead of teleporting.");
            Assert.That(
                PlanarDistance(current, target),
                Is.LessThanOrEqualTo(
                    PlanarDistance(previous, target) + 0.001f));
        }

        private static void AssertOnGuidedSegment(
            Vector3 start,
            Vector3 end,
            Vector3 current)
        {
            start.y = 0f;
            end.y = 0f;
            current.y = 0f;
            Vector3 segment = end - start;
            float progress = Vector3.Dot(
                current - start,
                segment) / segment.sqrMagnitude;
            Vector3 closest = start +
                segment * Mathf.Clamp01(progress);
            Assert.That(progress, Is.InRange(-0.01f, 1.01f));
            Assert.That(
                Vector3.Distance(current, closest),
                Is.LessThan(0.01f),
                "Movement input must not steer the player away from the " +
                "authored entry segment.");
        }

        private static void AssertExactPose(
            Transform root,
            Vector3 expectedPosition,
            Quaternion expectedRotation)
        {
            Assert.That(
                Vector3.Distance(root.position, expectedPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(root.rotation, expectedRotation),
                Is.LessThan(0.001f));
        }

        private static float PlanarDistance(
            Vector3 first,
            Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private static float PreciseQuaternionAngleDegrees(
            Quaternion first,
            Quaternion second)
        {
            Quaternion delta =
                Quaternion.Inverse(first) * second;
            float sinHalfAngle = new Vector3(
                delta.x,
                delta.y,
                delta.z).magnitude;
            float cosHalfAngle = Mathf.Abs(delta.w);
            return 2f * Mathf.Atan2(
                       sinHalfAngle,
                       cosHalfAngle) *
                   Mathf.Rad2Deg;
        }

        private static void AssertDriftWithinAuthoredBounds(
            HomeBalconySmokingCameraDriftSample drift)
        {
            Assert.That(
                Mathf.Abs(drift.LocalPosition.x),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .LateralAmplitudeMeters +
                    0.000001f));
            Assert.That(
                Mathf.Abs(drift.LocalPosition.y),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .VerticalAmplitudeMeters +
                    0.000001f));
            Assert.That(
                Mathf.Abs(drift.LocalPosition.z),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .DepthAmplitudeMeters +
                    0.000001f));
            Assert.That(
                Mathf.Abs(drift.LocalEulerAngles.x),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .PitchAmplitudeDegrees +
                    0.000001f));
            Assert.That(
                Mathf.Abs(drift.LocalEulerAngles.y),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .YawAmplitudeDegrees +
                    0.000001f));
            Assert.That(
                Mathf.Abs(drift.LocalEulerAngles.z),
                Is.LessThanOrEqualTo(
                    HomeBalconySmokingCameraDrift
                        .RollAmplitudeDegrees +
                    0.000001f));
        }
    }
}
