using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

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
            Assert.That(home.Smoking.CigaretteProp, Is.Not.Null);
            Assert.That(
                home.Smoking.CigaretteProp.activeSelf,
                Is.False);
            AssertCigaretteGeometry();
            Transform ashtray = AssertPermanentAshtray();
            Player3DCharacterPresentation playerPresentation =
                (Player3DCharacterPresentation)home.Player.Visual;
            HomeBalconySmokingExhaleEffect exhale =
                home.Smoking.ExhaleEffect;
            Assert.That(exhale, Is.Not.Null);
            Assert.That(exhale.IsInitialized, Is.True);
            Assert.That(
                exhale.MouthAnchor,
                Is.SameAs(
                    playerPresentation.Registry.Anchors.Mouth));
            Assert.That(exhale.Particles, Is.Not.Null);
            Assert.That(exhale.SmokeRenderer, Is.Not.Null);
            Assert.That(
                exhale.Particles.transform.parent,
                Is.SameAs(exhale.transform),
                "The world-space plume must not inherit the FBX mouth " +
                "bone's 100x hierarchy scale.");
            Assert.That(
                exhale.Particles.main.simulationSpace,
                Is.EqualTo(ParticleSystemSimulationSpace.World));
            Assert.That(
                exhale.Particles.main.maxParticles,
                Is.EqualTo(
                    HomeBalconySmokingExhaleEffect.MaximumParticles));
            Assert.That(
                exhale.LoopDurationSeconds,
                Is.EqualTo(9.5f).Within(0.0001f));
            Assert.That(
                exhale.BurstTimeSeconds,
                Is.EqualTo(5.8666667f).Within(0.0001f));
            Assert.That(
                exhale.SmokeRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.AtmosphereMaterial));
            Assert.That(exhale.IsEmissionCycleActive, Is.False);
            Assert.That(exhale.Particles.particleCount, Is.Zero);
            Assert.That(
                home.Smoking.Definition.EnterClipName,
                Is.EqualTo("SmokeEnter"));
            Assert.That(
                home.Smoking.Definition.LoopClipName,
                Is.EqualTo("SmokeLoop"));
            Assert.That(
                home.Smoking.Definition.ExitClipName,
                Is.EqualTo("SmokeExit"));

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
            AssertContinuous3DPresentation("SmokeEnter");
            Assert.That(
                home.Smoking.CigaretteProp.activeSelf,
                Is.False,
                "The cigarette must remain hidden until the entering " +
                "hand reaches its authored extraction frame.");
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
                home.Smoking.Timeline.CameraDriftBlend,
                Is.EqualTo(0f),
                "Camera drift must begin at zero instead of cutting into " +
                "the captured Balcony shot.");

            Time.timeScale = 4f;
            yield return WaitForCigaretteVisibility(
                true,
                PlayerAnimatedInteractionPhase.Entering);
            int revealLocalFrame =
                home.AnimatedInteraction.FrameIndex -
                home.Smoking.Definition.EnterStartFrame;
            Assert.That(
                revealLocalFrame,
                Is.InRange(
                    HomeBalconySmokingInteraction
                        .CigaretteRevealEnterLocalFrame,
                    HomeBalconySmokingInteraction
                        .CigaretteRevealEnterLocalFrame + 1));

            Time.timeScale = 12f;
            yield return WaitForPhaseCompletion(
                PlayerAnimatedInteractionPhase.Looping);
            Time.timeScale = 1f;
            yield return null;

            AssertContinuous3DPresentation("SmokeLoop");
            Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.True);
            Assert.That(exhale.IsEmissionCycleActive, Is.True);
            Assert.That(exhale.Particles.particleCount, Is.Zero);
            AssertCigaretteGeometry();
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(
                Vector3.Angle(
                    home.Player.GameObject.transform.up,
                    Vector3.up),
                Is.LessThan(0.1f),
                "The continuous smoking rig must remain upright in world " +
                "space throughout its loop.");
            Assert.That(
                Vector3.Angle(camera.transform.up, Vector3.up),
                Is.GreaterThan(10f),
                "The close camera must remain materially pitched so this " +
                "test distinguishes world-up from camera-plane alignment.");
            AssertAnimatedCharacterFacesCity(
                playerPresentation.Registry);
            Vector3 actionHipWorld =
                playerPresentation.Registry.Anchors.Pelvis.position;
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

            Time.timeScale = 4f;
            yield return WaitForExhaleBurst();
            float firstExhaleTime = Time.time;
            yield return WaitForExhaleSmokeToClear();
            yield return WaitForExhaleBurst();
            float secondExhaleTime = Time.time;
            Assert.That(
                secondExhaleTime - firstExhaleTime,
                Is.EqualTo(exhale.LoopDurationSeconds).Within(0.50f),
                "The mouth plume must repeat once per complete smoking " +
                "loop.");
            yield return WaitForExhaleSmokeToClear();
            yield return WaitForUnsafeLoopFrame();
            Time.timeScale = 1f;
            int unsafeLoopFrame =
                home.AnimatedInteraction.FrameIndex -
                home.Smoking.Definition.LoopStartFrame;
            Assert.That(unsafeLoopFrame, Is.InRange(4, 20));
            Assert.That(
                HomeBalconySmokingTimeline
                    .IsSafeExitLoopFrame(unsafeLoopFrame),
                Is.False);
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
            Assert.That(exhale.IsEmissionCycleActive, Is.False);
            Assert.That(exhale.Particles.isEmitting, Is.False);
            Assert.That(
                exhale.Particles.particleCount,
                Is.GreaterThan(0),
                "Stopping the emitter must let the already exhaled " +
                "world-space plume dissipate naturally during exit.");
            AssertContinuous3DPresentation("SmokeExit");
            Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.True);
            Time.timeScale = 4f;
            yield return WaitForCigaretteVisibility(
                false,
                PlayerAnimatedInteractionPhase.Exiting);
            int hideLocalFrame =
                home.AnimatedInteraction.FrameIndex -
                home.Smoking.Definition.ExitStartFrame;
            Assert.That(
                hideLocalFrame,
                Is.InRange(
                    HomeBalconySmokingInteraction
                        .CigaretteHideExitLocalFrame,
                    HomeBalconySmokingInteraction
                        .CigaretteHideExitLocalFrame + 1));
            AssertExitFlickOverAshtray(ashtray);
            Time.timeScale = 12f;
            yield return WaitForPhaseCompletion(
                PlayerAnimatedInteractionPhase.Idle);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(home.Smoking.OwnsInteraction, Is.False);
            Assert.That(home.Smoking.ExitQueued, Is.False);
            Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.False);
            Assert.That(ashtray.gameObject.activeInHierarchy, Is.True);
            Assert.That(AssertPermanentAshtray(), Is.SameAs(ashtray));
            Assert.That(exhale.IsEmissionCycleActive, Is.False);
            Assert.That(exhale.Particles.particleCount, Is.Zero);
            Assert.That(exhale.Particles.IsAlive(true), Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer =
                    home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
            }
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            Assert.That(
                ((IPlayerClipPresentation)home.Player.Visual)
                    .IsClipActive,
                Is.False);
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

        private IEnumerator WaitForPhaseCompletion(
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
                AssertContinuous3DPresentation(
                    activePhase ==
                        PlayerAnimatedInteractionPhase.Entering
                        ? "SmokeEnter"
                        : "SmokeExit");
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expected));
            if (expected != PlayerAnimatedInteractionPhase.Idle)
            {
                AssertContinuous3DPresentation("SmokeLoop");
            }
        }

        private IEnumerator WaitForExhaleBurst()
        {
            HomeBalconySmokingExhaleEffect exhale =
                home.Smoking.ExhaleEffect;
            float deadline =
                Time.realtimeSinceStartup + 4f;
            while (exhale.Particles.particleCount == 0 &&
                   home.AnimatedInteraction.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
            Assert.That(exhale.IsEmissionCycleActive, Is.True);
            Assert.That(
                exhale.Particles.particleCount,
                Is.GreaterThanOrEqualTo(
                    HomeBalconySmokingExhaleEffect
                        .BurstParticleCount),
                "Each 9.5-second smoking loop must emit one dense, " +
                "visible mouth plume.");
            AssertExhaleParticleMotion(exhale);
        }

        private IEnumerator WaitForExhaleSmokeToClear()
        {
            HomeBalconySmokingExhaleEffect exhale =
                home.Smoking.ExhaleEffect;
            float deadline =
                Time.realtimeSinceStartup + 2f;
            while (exhale.Particles.particleCount > 0 &&
                   home.AnimatedInteraction.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                exhale.Particles.particleCount,
                Is.Zero,
                "The plume must dissipate before the next smoking loop " +
                "burst.");
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

        private void AssertExhaleParticleMotion(
            HomeBalconySmokingExhaleEffect exhale)
        {
            Transform mouth = exhale.MouthAnchor;
            Vector3 expectedEmitterPosition =
                mouth.position +
                mouth.up.normalized *
                HomeBalconySmokingExhaleEffect.MouthForwardOffset;
            Assert.That(
                Vector3.Distance(
                    exhale.Particles.transform.position,
                    expectedEmitterPosition),
                Is.LessThan(0.035f));
            Assert.That(
                Vector3.Dot(
                    exhale.Particles.transform.forward,
                    mouth.up.normalized),
                Is.GreaterThan(0.99f));

            var samples = new ParticleSystem.Particle[
                HomeBalconySmokingExhaleEffect.MaximumParticles];
            int count = exhale.Particles.GetParticles(samples);
            Assert.That(count, Is.GreaterThan(0));
            Vector3 averageVelocity = Vector3.zero;
            for (int index = 0; index < count; index++)
            {
                averageVelocity += samples[index].velocity;
            }

            averageVelocity /= count;
            Vector3 cityDirection =
                home.transform.TransformDirection(Vector3.right);
            Assert.That(
                Vector3.Dot(
                    averageVelocity,
                    cityDirection.normalized),
                Is.GreaterThan(0.10f),
                "The exhaled smoke must travel outward toward the city.");
        }

        private IEnumerator WaitForCigaretteVisibility(
            bool expectedVisible,
            PlayerAnimatedInteractionPhase expectedPhase)
        {
            float deadline =
                Time.realtimeSinceStartup + 3f;
            while (home.Smoking.CigaretteProp.activeSelf !=
                       expectedVisible &&
                   home.AnimatedInteraction.Phase == expectedPhase &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expectedPhase),
                "The cigarette visibility marker must be presented " +
                "inside its authored animation phase.");
            Assert.That(
                home.Smoking.CigaretteProp.activeSelf,
                Is.EqualTo(expectedVisible));
        }

        private void AssertCigaretteGeometry()
        {
            Transform prop = home.Smoking.CigaretteProp.transform;
            Player3DCharacterPresentation presentation =
                (Player3DCharacterPresentation)home.Player.Visual;
            Assert.That(
                prop.parent,
                Is.SameAs(
                    presentation.Registry.Anchors.RightCigarette));
            Assert.That(prop.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(prop.localRotation, Is.EqualTo(Quaternion.identity));

            Transform paper = prop.Find("Paper");
            Transform ember = prop.Find("Ember");
            Assert.That(paper, Is.Not.Null);
            Assert.That(ember, Is.Not.Null);
            Assert.That(
                paper.localPosition,
                Is.EqualTo(
                    HomeBalconySmokingInteraction
                        .CigarettePaperLocalPosition));
            Assert.That(
                paper.localScale,
                Is.EqualTo(
                    HomeBalconySmokingInteraction
                        .CigarettePaperLocalScale));
            Assert.That(
                ember.localPosition,
                Is.EqualTo(
                    HomeBalconySmokingInteraction
                        .CigaretteEmberLocalPosition));
            Assert.That(
                ember.localScale,
                Is.EqualTo(
                    HomeBalconySmokingInteraction
                        .CigaretteEmberLocalScale));

            Assert.That(
                GetLocalMeshSize(paper),
                Is.EqualTo(new Vector3(0.0065f, 0.070f, 0.0065f))
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                GetLocalMeshSize(ember),
                Is.EqualTo(new Vector3(0.007f, 0.004f, 0.007f))
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            AssertWorldMeshSize(
                paper,
                new Vector3(0.0065f, 0.070f, 0.0065f));
            AssertWorldMeshSize(
                ember,
                new Vector3(0.007f, 0.004f, 0.007f));
            Assert.That(paper.localPosition.y, Is.GreaterThan(0f));
            Assert.That(ember.localPosition.y, Is.GreaterThan(0f));
            float paperTip =
                paper.localPosition.y + paper.localScale.y;
            float emberBase =
                ember.localPosition.y - ember.localScale.y;
            Assert.That(emberBase, Is.EqualTo(paperTip).Within(0.0001f));
            Assert.That(
                ember.localPosition.y,
                Is.GreaterThan(paper.localPosition.y));
        }

        private Transform AssertPermanentAshtray()
        {
            Transform ashtray =
                home.Balcony.Find("Home Balcony Ashtray");
            Assert.That(ashtray, Is.Not.Null);
            Assert.That(ashtray.gameObject.activeSelf, Is.True);
            Assert.That(ashtray.gameObject.activeInHierarchy, Is.True);
            Assert.That(
                ashtray.localPosition,
                Is.EqualTo(home.SmokingPlan.AshtrayPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                ashtray.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The permanent rail ashtray must remain visual-only.");

            Renderer[] renderers =
                ashtray.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(3));
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(
                    renderers[index].sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial),
                    "The ashtray must reuse the shared primitive material.");
            }

            Transform body =
                ashtray.Find("Home Balcony Ashtray Body");
            Transform railCap =
                home.Balcony.Find("Home Balcony Outer Rail Cap");
            Assert.That(body, Is.Not.Null);
            Assert.That(railCap, Is.Not.Null);
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            Renderer railRenderer = railCap.GetComponent<Renderer>();
            Assert.That(bodyRenderer, Is.Not.Null);
            Assert.That(railRenderer, Is.Not.Null);
            Assert.That(
                bodyRenderer.bounds.min.y,
                Is.EqualTo(railRenderer.bounds.max.y).Within(0.001f),
                "The ashtray base must rest directly on the outer rail cap.");
            return ashtray;
        }

        private void AssertExitFlickOverAshtray(Transform ashtray)
        {
            Transform ember =
                home.Smoking.CigaretteProp.transform.Find("Ember");
            Transform basin =
                ashtray.Find("Home Balcony Ashtray Basin");
            Assert.That(ember, Is.Not.Null);
            Assert.That(basin, Is.Not.Null);

            Bounds dishBounds = basin.GetComponent<Renderer>().bounds;
            Vector3 emberPosition = ember.position;
            Assert.That(
                emberPosition.x,
                Is.InRange(dishBounds.min.x, dishBounds.max.x),
                "The exit flick's ember must remain over the ashtray width.");
            Assert.That(
                emberPosition.z,
                Is.InRange(dishBounds.min.z, dishBounds.max.z),
                "The exit flick's ember must remain over the ashtray depth.");
            float heightAboveBasin =
                emberPosition.y -
                basin.GetComponent<Renderer>().bounds.max.y;
            Assert.That(
                heightAboveBasin,
                Is.InRange(0.04f, 0.24f),
                "The ashtray must sit directly below the authored exit " +
                "flick, not elsewhere on the rail.");
        }

        private void AssertAnimatedCharacterFacesCity(
            Player3DAssetRegistry registry)
        {
            Renderer head = FindPlayerRenderer(registry, "GEO_Head");
            Renderer nose = FindPlayerRenderer(registry, "GEO_Nose");
            Vector3 headToNose = Vector3.ProjectOnPlane(
                nose.bounds.center - head.bounds.center,
                home.transform.up);
            Assert.That(
                headToNose.sqrMagnitude,
                Is.GreaterThan(0.0001f));
            Vector3 cityFacing =
                home.transform.TransformDirection(Vector3.right);
            Assert.That(
                Vector3.Dot(
                    headToNose.normalized,
                    cityFacing.normalized),
                Is.GreaterThan(0.95f),
                "The sampled smoking rig's visible face must point " +
                "toward the Home-local +X city direction, not merely " +
                "leave the gameplay root facing that way.");
        }

        private static Renderer FindPlayerRenderer(
            Player3DAssetRegistry registry,
            string meshName)
        {
            for (int index = 0;
                 index < registry.MeshBindings.Count;
                 index++)
            {
                Player3DMeshBinding binding =
                    registry.MeshBindings[index];
                if (binding.MeshName == meshName)
                {
                    return binding.Renderer;
                }
            }

            Assert.Fail($"Missing Player 3D renderer '{meshName}'.");
            return null;
        }

        private static Vector3 GetLocalMeshSize(Transform part)
        {
            MeshFilter filter = part.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Vector3 scale = part.localScale;
            scale = new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));
            return Vector3.Scale(
                filter.sharedMesh.bounds.size,
                scale);
        }

        private static void AssertWorldMeshSize(
            Transform part,
            Vector3 expected)
        {
            MeshFilter filter = part.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Vector3 meshSize = filter.sharedMesh.bounds.size;
            Vector3 actual = new Vector3(
                part.TransformVector(Vector3.right * meshSize.x).magnitude,
                part.TransformVector(Vector3.up * meshSize.y).magnitude,
                part.TransformVector(Vector3.forward * meshSize.z).magnitude);
            Assert.That(
                actual.x,
                Is.EqualTo(expected.x).Within(0.0001f),
                "Cigarette world-space diameter was multiplied by the " +
                "imported socket scale.");
            Assert.That(
                actual.y,
                Is.EqualTo(expected.y).Within(0.0001f),
                "Cigarette world-space length was multiplied by the " +
                "imported socket scale.");
            Assert.That(
                actual.z,
                Is.EqualTo(expected.z).Within(0.0001f),
                "Cigarette world-space diameter was multiplied by the " +
                "imported socket scale.");
        }

        private void AssertGuidedApproachPresentation()
        {
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.False);
            Assert.That(
                home.Smoking.ExhaleEffect.IsEmissionCycleActive,
                Is.False);
            Assert.That(
                home.Smoking.ExhaleEffect.Particles.particleCount,
                Is.Zero);
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer =
                    home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
            }
            Assert.That(
                ((IPlayerClipPresentation)home.Player.Visual)
                    .IsClipActive,
                Is.False);
        }

        private void AssertContinuous3DPresentation(
            string expectedClip)
        {
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer = home.Player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
            }

            IPlayerClipPresentation clips =
                (IPlayerClipPresentation)home.Player.Visual;
            Assert.That(clips.IsClipActive, Is.True);
            Assert.That(clips.ActiveClipName, Is.EqualTo(expectedClip));
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
