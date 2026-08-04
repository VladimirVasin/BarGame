using System.Collections;
using System.Reflection;
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
        private const float GuidedMoveSpeed = 2.6f;
        private const float GuidedStartOffset = 0.22f;

        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private HomeInteriorRoot home;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SetSceneTransitionStateForTest(false);
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
            SetSceneTransitionStateForTest(false);
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
                home.Bed.Definition.EnterClipName,
                Is.EqualTo("BedEnter"));
            Assert.That(
                home.Bed.Definition.LoopClipName,
                Is.EqualTo("BedSleepLoop"));
            Assert.That(
                home.Bed.Definition.ExitClipName,
                Is.EqualTo("BedExit"));
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

            Vector3 entryPosition =
                home.BedInteractionPlan.EntryRootPosition;
            Quaternion entryRotation =
                home.BedInteractionPlan.EntryRotation;
            Vector3 guidedStart =
                entryPosition + Vector3.right * GuidedStartOffset;
            home.Player.Motor.Teleport(guidedStart);
            home.Player.GameObject.transform.rotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);
            Physics.SyncTransforms();
            yield return WaitForActiveBed(home);

            Time.timeScale = 0.25f;
            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(
                home.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.False);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.False);
            AssertGuidedApproachPresentation(home);
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
                keyboard.dKey,
                queueEventOnly: true);
            InputSystem.Update();
            Assert.That(keyboard.dKey.isPressed, Is.True);

            bool madeGuidedProgress = false;
            bool sawSettledRenderFrame = false;
            int positioningFrames = 0;
            float positioningDeadline =
                Time.realtimeSinceStartup + 3f;
            while (home.AnimatedInteraction.Phase ==
                       PlayerAnimatedInteractionPhase.Positioning &&
                   Time.realtimeSinceStartup < positioningDeadline)
            {
                AssertGuidedApproachPresentation(home);
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
                    "WASD input must not redirect the scripted bed " +
                    "approach away from its entry point.");
                madeGuidedProgress |=
                    currentDistance + 0.0001f < previousDistance;
                if (home.AnimatedInteraction.Phase ==
                        PlayerAnimatedInteractionPhase.Positioning &&
                    home.Player.Visual.InteractionHandoffLocked &&
                    currentDistance < 0.001f &&
                    Quaternion.Angle(
                        home.Player.GameObject.transform.rotation,
                        entryRotation) < 0.01f)
                {
                    Assert.That(
                        home.Player.Visual.InteractionHandoffLocked,
                        Is.True,
                        "The exact entry pose must be held by the shared " +
                        "handoff lock for one rendered frame before 3D " +
                        "Entering begins.");
                    AssertGuidedApproachPresentation(home);
                    sawSettledRenderFrame = true;
                }

                positioningFrames++;
            }

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
            Assert.That(
                sawSettledRenderFrame,
                Is.True,
                "Positioning must expose one settled ordinary-rig frame " +
                "before the contextual clip begins.");
            AssertExactPose(
                home.Player.GameObject.transform,
                entryPosition,
                entryRotation);
            AssertContinuous3DPresentation(home, "BedEnter");
            Vector3 lockedPosition = entryPosition;

            Time.timeScale = FastTimeScale;
            yield return WaitForPhaseCompletion(
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
            AssertContinuous3DPresentation(home, "BedSleepLoop");
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.False);
            AssertRigRendererState(home, true);
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
            AssertContinuous3DPresentation(home, "BedExit");
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
            yield return WaitForPhaseCompletion(
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
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.SleepPromptKey));
            Assert.That(
                home.Player.ContactShadow.enabled,
                Is.True);
            Assert.That(surfaceClutter.gameObject.activeSelf, Is.True);
            AssertRigRendererState(home, true);
            Assert.That(
                ((IPlayerClipPresentation)home.Player.Visual)
                    .IsClipActive,
                Is.False);
            AssertExactPose(
                home.Player.GameObject.transform,
                home.BedInteractionPlan.ExitRootPosition,
                home.BedInteractionPlan.ExitRotation);

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
                    .EntryRootPosition);
            home.Player.GameObject.transform.rotation =
                home.BedInteractionPlan.EntryRotation;
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
            Bed_SceneTransitionCancelsPositioningWithoutFurtherMovement()
        {
            yield return LoadHome();

            Vector3 entryPosition =
                home.BedInteractionPlan.EntryRootPosition;
            Vector3 startPosition =
                entryPosition + Vector3.right * 0.42f;
            home.Player.Motor.Teleport(startPosition);
            home.Player.GameObject.transform.rotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);
            Physics.SyncTransforms();
            yield return WaitForActiveBed(home);

            home.Bed.Interact(home.Player.Interactor);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            yield return null;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(home.Player.Motor.InteractionPoseMoveActive, Is.True);
            Vector3 positionAtTransition =
                home.Player.GameObject.transform.position;

            SetSceneTransitionStateForTest(true);
            yield return null;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(home.AnimatedInteraction.IsActive, Is.False);
            Assert.That(home.Player.Motor.InteractionPoseMoveActive, Is.False);
            Assert.That(home.Player.Motor.InteractionPoseMoveStalled, Is.False);
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(
                PlanarDistance(
                    home.Player.GameObject.transform.position,
                    positionAtTransition),
                Is.LessThan(0.0001f));

            yield return null;
            Assert.That(
                PlanarDistance(
                    home.Player.GameObject.transform.position,
                    positionAtTransition),
                Is.LessThan(0.0001f),
                "A cancelled Positioning move must not resume while the " +
                "scene transition flag remains active.");
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            SetSceneTransitionStateForTest(false);
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
                        .EntryRootPosition));
            Assert.That(
                Quaternion.Angle(
                    home.Player.GameObject.transform.rotation,
                    home.BedInteractionPlan.EntryRotation),
                Is.LessThan(0.001f));
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
                ((IPlayerClipPresentation)home.Player.Visual)
                    .ActiveClipName,
                Is.EqualTo("BedSleepLoop"));
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(home, true);
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
                home.Bed.PromptKey,
                Is.EqualTo(HomeBedInteraction.SleepPromptKey));
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

        private static void SetSceneTransitionStateForTest(bool value)
        {
            PropertyInfo property = typeof(SceneTransitionService).GetProperty(
                nameof(SceneTransitionService.IsTransitioning),
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(property, Is.Not.Null);
            MethodInfo setter = property.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null);
            setter.Invoke(null, new object[] { value });
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

        private static IEnumerator WaitForPhaseCompletion(
            HomeInteriorRoot home,
            PlayerAnimatedInteractionPhase expected)
        {
            PlayerAnimatedInteractionPhase activePhase =
                expected == PlayerAnimatedInteractionPhase.Looping
                    ? PlayerAnimatedInteractionPhase.Entering
                    : PlayerAnimatedInteractionPhase.Exiting;
            float deadline =
                Time.realtimeSinceStartup + 3f;
            while (home.AnimatedInteraction.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    home.AnimatedInteraction.Phase,
                    Is.EqualTo(activePhase));
                AssertContinuous3DPresentation(
                    home,
                    activePhase ==
                        PlayerAnimatedInteractionPhase.Entering
                        ? "BedEnter"
                        : "BedExit");
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(expected));
            if (expected != PlayerAnimatedInteractionPhase.Idle)
            {
                AssertContinuous3DPresentation(
                    home,
                    "BedSleepLoop");
            }
        }

        private static void AssertGuidedApproachPresentation(
            HomeInteriorRoot home)
        {
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Positioning));
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(home, true);
            Assert.That(
                ((IPlayerClipPresentation)home.Player.Visual)
                    .IsClipActive,
                Is.False);
        }

        private static void AssertContinuous3DPresentation(
            HomeInteriorRoot home,
            string expectedClip)
        {
            Assert.That(home.Player.ContactShadow.enabled, Is.True);
            AssertRigRendererState(home, true);
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
