using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBedInteractionPlayModeTests
    {
        private const float TimeoutSeconds = 15f;
        private const float FastTimeScale = 20f;

        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private HomeInteriorRoot home;
        private float previousTimeScale;

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
            GameSessionState.ResetEconomyState();
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
                Scene cleanupScene =
                    SceneManager.CreateScene(
                        "Home Bed Interaction Test Cleanup");
                SceneManager.SetActiveScene(cleanupScene);
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
            GameSessionState.ResetEconomyState();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            Bed_EStartsPersistentSleepAndSecondE_RestoresMovement()
        {
            yield return LoadHome();
            Assert.That(home.Bed, Is.Not.Null);
            Assert.That(home.AnimatedInteraction, Is.Not.Null);
            Assert.That(
                home.Bed.Definition.RenderAboveSceneDepth,
                Is.True);
            Assert.That(
                home.Bed.Definition.LoopFrameCount,
                Is.EqualTo(
                    HomeBedInteraction.SleepLoopFrameCount));
            Assert.That(
                home.Bed.Definition.LoopFramesPerSecond,
                Is.EqualTo(
                    HomeBedInteraction.SleepLoopFramesPerSecond));
            Assert.That(
                home.Bed.Definition
                    .GetLoopFrameExtraHoldSeconds(
                        HomeBedInteraction
                            .FullInhaleLoopFrameOffset),
                Is.EqualTo(
                    HomeBedInteraction
                        .FullInhaleExtraHoldSeconds));
            Assert.That(
                home.Bed.Definition
                    .GetLoopFrameExtraHoldSeconds(
                        HomeBedInteraction
                            .FullExhaleLoopFrameOffset),
                Is.EqualTo(
                    HomeBedInteraction
                        .FullExhaleExtraHoldSeconds));
            Assert.That(
                home.Bed.Definition.LoopDurationSeconds,
                Is.EqualTo(5d).Within(0.0001d));
            Assert.That(
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.SleepPromptKey));
            Transform surfaceClutter =
                home.Room.Find(
                    HomeBedInteraction.SurfaceClutterName);
            Assert.That(surfaceClutter, Is.Not.Null);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.True);

            home.Player.Motor.Teleport(
                home.BedInteractionPlan
                    .ApproachRootPosition);
            Physics.SyncTransforms();
            yield return WaitForActiveBed(home);

            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Entering));
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.False);
            Assert.That(home.Player.Shadow.enabled, Is.False);
            Assert.That(home.Player.ContactShadow.enabled, Is.False);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.False);
            AssertRigRendererState(home, false);
            Vector3 lockedPosition =
                home.Player.GameObject.transform.position;

            inputFixture.Release(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                keyboard.dKey,
                queueEventOnly: true);
            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
                Assert.That(
                    home.Player.Motor.PlanarVelocity,
                    Is.EqualTo(Vector3.zero));
                AssertPlanarPosition(
                    home.Player.GameObject.transform.position,
                    lockedPosition);
            }

            inputFixture.Release(
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            yield return null;
            Time.timeScale = FastTimeScale;
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Looping);

            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.WakePromptKey));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer
                    .sharedMaterial,
                Is.SameAs(
                    PlayerAnimatedInteractionResources
                        .OverlayMaterial));
            AssertBedAxisAlignment(home);
            Assert.That(home.Player.Shadow.enabled, Is.False);
            Assert.That(home.Player.ContactShadow.enabled, Is.False);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.False);
            AssertRigRendererState(home, false);
            yield return WaitForActiveBed(home);

            int initialLoopFrame =
                home.AnimatedInteraction.FrameIndex;
            bool frameChanged = false;
            float persistenceDeadline = Time.time + 6f;
            while (Time.time < persistenceDeadline)
            {
                yield return null;
                Assert.That(
                    home.AnimatedInteraction.Phase,
                    Is.EqualTo(
                        PlayerAnimatedInteractionPhase.Looping),
                    "Sleep must remain in its loop until another E press.");
                Assert.That(
                    home.AnimatedInteraction.FrameIndex,
                    Is.InRange(24, 39));
                frameChanged |=
                    home.AnimatedInteraction.FrameIndex !=
                    initialLoopFrame;
                AssertPlanarPosition(
                    home.Player.GameObject.transform.position,
                    lockedPosition);
            }

            Assert.That(
                frameChanged,
                Is.True,
                "The persistent sleep loop must continue animating.");

            Time.timeScale = 1f;
            yield return null;
            yield return WaitForActiveBed(home);
            Assert.That(
                keyboard.eKey.isPressed,
                Is.False,
                "The first interaction key press must be released.");
            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.eKey,
                queueEventOnly: true);
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Exiting);
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.False);

            inputFixture.Release(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;
            Time.timeScale = FastTimeScale;
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Idle);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.IsActive,
                Is.False);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.False);
            Assert.That(
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.SleepPromptKey));
            Assert.That(
                home.Player.Shadow.enabled,
                Is.True);
            Assert.That(
                home.Player.ContactShadow.enabled,
                Is.True);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.True);
            AssertRigRendererState(home, true);

            Vector3 wakePosition =
                home.Player.GameObject.transform.position;
            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            yield return null;
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            yield return null;
            float movementDeadline =
                Time.realtimeSinceStartup + 2f;
            while (PlanarDistance(
                       home.Player.GameObject.transform.position,
                       wakePosition) <
                   0.04f &&
                   Time.realtimeSinceStartup <
                   movementDeadline)
            {
                keyboard.MakeCurrent();
                inputFixture.Press(
                    keyboard.dKey,
                    queueEventOnly: true);
                yield return null;
            }

            Assert.That(
                PlanarDistance(
                    home.Player.GameObject.transform.position,
                    wakePosition),
                Is.GreaterThanOrEqualTo(0.04f),
                "Movement input must work again after waking. " +
                $"key={keyboard.dKey.isPressed}, " +
                $"motorEnabled={home.Player.Motor.enabled}, " +
                $"inputEnabled={home.Player.Motor.InputEnabled}, " +
                $"speedMultiplier={home.Player.Motor.SpeedMultiplier}, " +
                $"timeScale={Time.timeScale}, " +
                $"deltaTime={Time.deltaTime}, " +
                $"start={wakePosition}, " +
                $"end={home.Player.GameObject.transform.position}, " +
                $"spawn={home.Layout.PlayerSpawn}, " +
                $"approach={home.BedInteractionPlan.ApproachRootPosition}");
            inputFixture.Release(
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            yield return null;

            home.AnimatedInteraction.enabled = false;
            Assert.That(
                home.Bed.CanInteract(home.Player.Interactor),
                Is.False,
                "A disabled controller must not advertise the bed.");
            home.AnimatedInteraction.enabled = true;
            surfaceClutter.gameObject.SetActive(false);
            home.Player.Motor.Teleport(
                home.BedInteractionPlan
                    .ApproachRootPosition);
            Physics.SyncTransforms();
            yield return WaitForActiveBed(home);

            home.Bed.Interact(home.Player.Interactor);
            yield return null;
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Entering));
            Time.timeScale = FastTimeScale;
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Looping);
            Time.timeScale = 1f;
            yield return null;

            home.Bed.enabled = false;
            yield return null;
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle),
                "Disabling the owning bed must cancel persistent sleep.");
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.False);
            Assert.That(home.Player.Shadow.enabled, Is.True);
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(home, true);
            Assert.That(
                surfaceClutter.gameObject.activeSelf,
                Is.False,
                "Cancellation must restore the clutter state captured " +
                "for the current interaction.");
            Assert.That(
                home.Bed.CanInteract(home.Player.Interactor),
                Is.False);
        }

        [UnityTest]
        public IEnumerator
            Bed_ProgrammaticSleepStartsInLoopAndWakeRestoresPlayer()
        {
            yield return LoadHome();

            Transform surfaceClutter =
                home.Room.Find(
                    HomeBedInteraction.SurfaceClutterName);
            Assert.That(surfaceClutter, Is.Not.Null);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.True);

            Assert.That(home.Bed.BeginSleeping(), Is.True);

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.AnimatedInteraction.FrameIndex,
                Is.EqualTo(
                    home.Bed.Definition.LoopStartFrame));
            Assert.That(
                home.Player.GameObject.transform.position,
                Is.EqualTo(
                    home.BedInteractionPlan
                        .ApproachRootPosition));
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.WakePromptKey));
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.True);
            Assert.That(home.Player.Shadow.enabled, Is.False);
            Assert.That(home.Player.ContactShadow.enabled, Is.False);
            AssertRigRendererState(home, false);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.False);
            Assert.That(
                home.Bed.BeginSleeping(),
                Is.False,
                "The bed must not replace an interaction it already owns.");

            Assert.That(home.Bed.RequestWake(), Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                home.AnimatedInteraction.ExitDurationMultiplier,
                Is.EqualTo(1f),
                "An ordinary bed wake must retain its base duration.");
            Assert.That(
                home.AnimatedInteraction.ExitDurationSeconds,
                Is.EqualTo(
                    home.Bed.Definition.ExitFrameCount /
                    (double)home.Bed.Definition.ExitFramesPerSecond)
                    .Within(0.0001d));
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.False);
            Assert.That(home.Bed.RequestWake(), Is.False);

            Time.timeScale = FastTimeScale;
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Idle);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.AnimationRenderer.enabled,
                Is.False);
            Assert.That(
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.SleepPromptKey));
            Assert.That(home.Player.Shadow.enabled, Is.True);
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(home, true);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.True);
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

            home = null;
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

        private static IEnumerator WaitForActiveBed(
            HomeInteriorRoot home)
        {
            float deadline =
                Time.realtimeSinceStartup + 2f;
            while (!ReferenceEquals(
                       home.Player.Interactor
                           .ActiveInteractable,
                       home.Bed) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.Player.Interactor.ActiveInteractable,
                Is.SameAs(home.Bed));
        }

        private static IEnumerator WaitForPhase(
            HomeInteriorRoot home,
            PlayerAnimatedInteractionPhase expected)
        {
            float deadline =
                Time.realtimeSinceStartup + 3f;
            while (home.AnimatedInteraction.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expected));
        }

        private static void AssertPlanarPosition(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                PlanarDistance(actual, expected),
                Is.LessThan(0.001f));
        }

        private static void AssertRigRendererState(
            HomeInteriorRoot home,
            bool expected)
        {
            for (int index = 0;
                 index < home.Player.Visual.Renderers.Count;
                 index++)
            {
                Assert.That(
                    home.Player.Visual.Renderers[index].enabled,
                    Is.EqualTo(expected),
                    $"Unexpected rig renderer state at index {index}.");
            }
        }

        private static void AssertBedAxisAlignment(
            HomeInteriorRoot home)
        {
            PlayerAnimatedInteractionController controller =
                home.AnimatedInteraction;
            SpriteRenderer renderer =
                controller.AnimationRenderer;
            Transform visualRoot =
                controller.AnimationVisualRoot;
            Assert.That(controller.HasActionRightAxis, Is.True);
            Assert.That(
                Vector3.Angle(
                    controller.ActionRightAxis,
                    home.BedInteractionPlan.HeadToFootAxis),
                Is.LessThan(0.01f));
            Assert.That(visualRoot, Is.Not.Null);
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(
                Vector3.Angle(
                    visualRoot.forward,
                    -camera.transform.forward),
                Is.LessThan(0.1f));

            Vector3 authoredTextureRight =
                visualRoot.right *
                (renderer.flipX ? -1f : 1f);
            Vector2 projectedBedAxis =
                ScreenDirection(
                    camera,
                    home.BedInteractionPlan.ActionHipPosition,
                    home.BedInteractionPlan.ActionHipPosition +
                    home.BedInteractionPlan.HeadToFootAxis);
            Vector2 authoredScreenRight =
                ScreenDirection(
                    camera,
                    visualRoot.position,
                    visualRoot.position +
                    authoredTextureRight);
            Assert.That(
                Vector2.Angle(
                    authoredScreenRight,
                    projectedBedAxis),
                Is.LessThan(0.1f),
                "Authored texture-right must follow the bed from head " +
                "to feet after camera-plane projection and flipX.");

            Transform pillow =
                home.Room.Find("Home Pillow");
            Assert.That(pillow, Is.Not.Null);
            Vector3 pillowSideAtHipHeight =
                pillow.position;
            pillowSideAtHipHeight.y =
                home.BedInteractionPlan.ActionHipPosition.y;
            Vector2 projectedPillowDirection =
                ScreenDirection(
                    camera,
                    home.BedInteractionPlan.ActionHipPosition,
                    pillowSideAtHipHeight);
            Vector2 authoredScreenLeft =
                ScreenDirection(
                    camera,
                    visualRoot.position,
                    visualRoot.position -
                    authoredTextureRight);
            Assert.That(
                Vector2.Angle(
                    authoredScreenLeft,
                    projectedPillowDirection),
                Is.LessThan(0.1f),
                "Authored texture-left/head must remain on the xMin " +
                "pillow side.");
        }

        private static Vector2 ScreenDirection(
            Camera camera,
            Vector3 worldStart,
            Vector3 worldEnd)
        {
            Vector3 screenStart =
                camera.WorldToScreenPoint(worldStart);
            Vector3 screenEnd =
                camera.WorldToScreenPoint(worldEnd);
            Assert.That(screenStart.z, Is.GreaterThan(0f));
            Assert.That(screenEnd.z, Is.GreaterThan(0f));
            Vector2 direction = new Vector2(
                screenEnd.x - screenStart.x,
                screenEnd.y - screenStart.y);
            Assert.That(
                direction.sqrMagnitude,
                Is.GreaterThan(0.0001f));
            return direction.normalized;
        }

        private static float PlanarDistance(
            Vector3 first,
            Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }
}
