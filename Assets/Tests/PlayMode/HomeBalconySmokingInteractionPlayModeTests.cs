using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBalconySmokingInteractionPlayModeTests
    {
        private const float TimeoutSeconds = 15f;
        private float previousTimeScale;
        private HomeInteriorRoot home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
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

            home.Player.Motor.Teleport(
                home.transform.TransformPoint(
                    home.SmokingPlan.DockRootPosition));
            Physics.SyncTransforms();
            yield return null;
            Assert.That(
                home.Smoking.CanInteract(
                    home.Player.Interactor),
                Is.True);
            Assert.That(
                home.Smoking.BeginInteraction(),
                Is.True);

            Vector3 balconyCameraPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion balconyCameraRotation =
                home.CameraFollow.FixedBaseRotation;

            Assert.That(home.Smoking.OwnsInteraction, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Entering));
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.Player.Interactor.InputEnabled, Is.False);
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
                "Smoking must use a world-up billboard rather than lean " +
                "with the pitched close-camera plane.");
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                home.Smoking.Timeline.CameraDriftBlend,
                Is.EqualTo(0f),
                "Camera drift must begin at zero instead of cutting into " +
                "the captured Balcony shot.");

            yield return WaitForVisualCrossfadeOverlap(
                PlayerAnimatedInteractionPhase.Entering);

            Time.timeScale = 12f;
            yield return WaitForAnimatedPhase(
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
            Time.timeScale = 1f;
            yield return WaitForVisualCrossfadeOverlap(
                PlayerAnimatedInteractionPhase.Exiting);
            Time.timeScale = 12f;
            yield return WaitForAnimatedPhase(
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
            Assert.That(
                PlanarDistance(
                    home.Player.GameObject.transform.position,
                    home.transform.TransformPoint(
                        home.SmokingPlan.DockRootPosition)),
                Is.LessThan(0.001f));
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

        private IEnumerator WaitForVisualCrossfadeOverlap(
            PlayerAnimatedInteractionPhase expectedPhase)
        {
            float deadline =
                Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline &&
                   home.AnimatedInteraction.Phase == expectedPhase)
            {
                float rigOpacity =
                    home.AnimatedInteraction.RigVisualOpacity;
                float animationOpacity =
                    home.AnimatedInteraction
                        .AnimationVisualOpacity;
                if (rigOpacity > 0.15f &&
                    rigOpacity < 0.85f &&
                    animationOpacity > 0.15f &&
                    animationOpacity < 0.85f)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expectedPhase));
            Assert.That(
                home.AnimatedInteraction.RigVisualOpacity,
                Is.InRange(0.15f, 0.85f));
            Assert.That(
                home.AnimatedInteraction.AnimationVisualOpacity,
                Is.InRange(0.15f, 0.85f));
            Assert.That(
                home.Player.Visual.BodyRenderer.enabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.True);
            Assert.That(
                home.Player.Visual.BodyRenderer.color.a,
                Is.InRange(0.15f, 0.85f));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.color.a,
                Is.InRange(0.15f, 0.85f));
            Assert.That(
                home.Player.Shadow.enabled,
                Is.False,
                "The non-fadeable dynamic shadow stays hidden until the " +
                "interaction completes.");
            Assert.That(
                home.Player.ContactShadow.enabled,
                Is.False,
                "The non-fadeable contact shadow stays hidden until the " +
                "interaction completes.");
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
