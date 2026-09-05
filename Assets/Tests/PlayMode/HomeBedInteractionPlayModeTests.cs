using System.Collections;
using System.Reflection;
using System.Collections.Generic;
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
        private Mesh bodyProbeMesh;
        private readonly List<Vector3> bodyProbeVertices =
            new List<Vector3>();
        private readonly HashSet<Renderer> bodyProbeRenderers =
            new HashSet<Renderer>();

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
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            GameSessionState.UpdateFatigue(
                GameSessionState.DefaultFatigue);
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
            if (bodyProbeMesh != null)
            {
                Object.DestroyImmediate(bodyProbeMesh);
                bodyProbeMesh = null;
            }
            GameSessionState.ClearRoute();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            GameSessionState.UpdateFatigue(
                GameSessionState.DefaultFatigue);
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
                    Is.InRange(
                        home.Bed.Definition.LoopStartFrame,
                        home.Bed.Definition.LoopStartFrame +
                            home.Bed.Definition.LoopFrameCount - 1),
                    "Sleep must keep cycling inside its own loop frames.");
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
                keyboard.wKey,
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
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;
            }

            Assert.That(
                PlanarDistance(
                    home.Player.GameObject.transform.position,
                    wakePosition),
                Is.GreaterThanOrEqualTo(0.04f),
                "Movement input must work again after waking. " +
                $"key={keyboard.wKey.isPressed}, " +
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
                keyboard.wKey,
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
            yield return null;
            AssertSleepingHeadToFootOrientation(home);
            AssertBodyRestsOnMattress(
                home,
                "BedSleepLoop",
                true,
                SleepBeddingGive);
            AssertHeadRestsOnPillow(home);

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

            int seatedExitFrame =
                home.Bed.Definition.ExitStartFrame +
                Mathf.CeilToInt(
                    HomeBedInteractionPlan.ExitSeatArrivalProgress *
                    home.Bed.Definition.ExitFrameCount);
            yield return WaitForAnimationFrame(
                home,
                seatedExitFrame);
            yield return null;
            AssertSeatedOnDoorSideEdge(home);

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

        [UnityTest]
        public IEnumerator
            Bed_SleepAndWakeNeverPushTheHeroThroughTheMattress()
        {
            yield return LoadHome();

            Assert.That(home.Bed.BeginSleeping(), Is.True);
            yield return null;

            // The loop is the pose the player stares at, so it is held to
            // resting on the mattress rather than merely clearing it.
            for (int frame = 0; frame < 12; frame++)
            {
                AssertBodyRestsOnMattress(
                    home,
                    "BedSleepLoop",
                    true,
                    SleepBeddingGive);
                AssertHeadRestsOnPillow(home);
                yield return null;
            }

            Assert.That(home.Bed.RequestWake(), Is.True);

            // Follow the entire exit, including both legs leaving the bed.
            // Only actual posed vertices over the mattress count: a boot
            // correctly resting outside its edge must not fail an AABB test.
            yield return ObserveBedTransition(
                PlayerAnimatedInteractionPhase.Exiting,
                PlayerAnimatedInteractionPhase.Idle,
                false);
        }

        [UnityTest]
        public IEnumerator
            Bed_MattressDentsUnderTheSleeperAndSlowlyRecovers()
        {
            yield return LoadHome();
            Assert.That(home.BedSurface, Is.Not.Null);

            float restTop =
                HomeInteriorWorldBuilder.BedMattressSurfaceHeight;
            float sink =
                HomeInteriorWorldBuilder.BedSleeperSinkDepth;
            Vector3 hip = home.BedInteractionPlan.ActionHipPosition;
            HomeBedDeformableSurface mattressSurface =
                FindBedSurface("Home Bed Mattress");
            HomeBedDeformableSurface pillowSurface =
                FindBedSurface("Home Pillow");
            Vector3[] pillowRestVertices = pillowSurface.Mesh.vertices;
            AssertPillowHasVolume(pillowSurface, pillowRestVertices);
            AreaCaptureFixture.CaptureHomeBedFrame(home, "00-rest");

            Assert.That(
                home.BedSurface.GetSurfaceHeight(hip),
                Is.EqualTo(restTop).Within(0.002f),
                "Before anyone lies down the surface must be flat.");

            home.Player.Motor.Teleport(
                home.BedInteractionPlan.EntryRootPosition);
            home.Player.GameObject.transform.rotation =
                home.BedInteractionPlan.EntryRotation;
            Physics.SyncTransforms();
            yield return WaitForActiveBed(home);
            home.Bed.Interact(home.Player.Interactor);
            yield return WaitForPhase(
                home, PlayerAnimatedInteractionPhase.Entering);
            Time.timeScale = 2f;
            yield return ObserveBedTransition(
                PlayerAnimatedInteractionPhase.Entering,
                PlayerAnimatedInteractionPhase.Looping);

            // Observe one complete breathing cycle after the real lie-down;
            // this also lets the soft surfaces reach their loaded shape.
            float sleepDeadline = Time.time +
                (float)home.Bed.Definition.LoopDurationSeconds;
            while (Time.time < sleepDeadline)
            {
                yield return null;
                AssertBodyRestsOnMattress(
                    home, "BedSleepLoop", true, SleepBeddingGive);
                AssertHeadRestsOnPillow(home);
                AssertPillowKeepsItsBaseAndThickness(
                    pillowSurface, pillowRestVertices);
            }
            AreaCaptureFixture.CaptureHomeBedFrame(home, "20-sleep");

            Player3DCharacterPresentation presentation =
                home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);

            // V2's deepest supine part is a boot. The spine lifts the torso
            // above it, so each footprint must meet its own visible underside.
            Assert.That(
                presentation.Registry.TryGetPart(
                    Player3DAnatomicalPart.Torso,
                    out Player3DAnatomicalPartBinding torso),
                Is.True);
            Assert.That(
                home.BedSurface.GetSurfaceHeight(
                    torso.Renderer.bounds.center),
                Is.EqualTo(Mathf.Max(restTop - sink,
                    torso.Renderer.bounds.min.y)).Within(0.005f),
                "Under the sleeper's torso the mattress must meet its " +
                "actual underside, independently of the deeper boot dent.");
            Assert.That(
                home.BedSurface.GetSurfaceHeight(hip),
                Is.LessThan(restTop - 0.02f),
                "and the dent must still be clearly visible under his " +
                "hips at the torso's skirt.");

            // The model is not the picture: read the actual mesh, so a
            // broken write path cannot hide behind green model asserts.
            Vector3[] meshVertices =
                mattressSurface.Mesh.vertices;
            float lowestTopVertex = float.PositiveInfinity;
            for (int index = 0;
                 index < mattressSurface.TopVertexCount;
                 index++)
            {
                lowestTopVertex = Mathf.Min(
                    lowestTopVertex,
                    meshVertices[index].y);
            }

            Assert.That(
                lowestTopVertex,
                Is.EqualTo(
                    (HomeInteriorWorldBuilder.BedMattressThickness *
                     0.5f) - sink)
                    .Within(0.006f),
                "The rendered mesh itself must carry the dent, not just " +
                "the model behind it.");
            Vector3 headSupportPoint = AssertHeadRestsOnPillow(home);
            Assert.That(
                pillowSurface.SampleRestWorldHeight(headSupportPoint) -
                    home.BedSurface.GetSurfaceHeight(headSupportPoint),
                Is.GreaterThan(0.01f),
                "The pillow must be dented under the sleeping head.");
            Assert.That(
                MaximumVertexDisplacement(
                    pillowRestVertices, pillowSurface.Mesh.vertices),
                Is.GreaterThan(0.01f),
                "The visible pillow mesh must compress under the head.");

            Assert.That(home.Bed.RequestWake(), Is.True);
            yield return ObserveBedTransition(
                PlayerAnimatedInteractionPhase.Exiting,
                PlayerAnimatedInteractionPhase.Idle);
            AreaCaptureFixture.CaptureHomeBedFrame(home, "40-standing");

            // The dent refills on its own slow spring after he is up.
            float recoveryDeadline = Time.time + 2.5f;
            while (Time.time < recoveryDeadline)
            {
                yield return null;
                AssertPillowKeepsItsBaseAndThickness(
                    pillowSurface, pillowRestVertices);
            }

            Assert.That(
                home.BedSurface.GetSurfaceHeight(hip),
                Is.EqualTo(restTop).Within(0.002f),
                "The dent must have refilled within a couple of " +
                "seconds of the hero standing up.");

            Vector3[] restedVertices = mattressSurface.Mesh.vertices;
            float lowestRestedTopVertex = float.PositiveInfinity;
            for (int index = 0;
                 index < mattressSurface.TopVertexCount;
                 index++)
            {
                lowestRestedTopVertex = Mathf.Min(
                    lowestRestedTopVertex,
                    restedVertices[index].y);
            }

            Assert.That(
                lowestRestedTopVertex,
                Is.EqualTo(
                    HomeInteriorWorldBuilder.BedMattressThickness *
                    0.5f)
                    .Within(0.003f),
                "and the rendered mesh must be flat again once the " +
                "dent has refilled.");
            Assert.That(
                MaximumVertexDisplacement(
                    pillowRestVertices, pillowSurface.Mesh.vertices),
                Is.LessThan(0.003f),
                "The pillow must recover its authored convex shape after waking.");
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            AssertExactPose(
                home.Player.GameObject.transform,
                home.BedInteractionPlan.ExitRootPosition,
                home.BedInteractionPlan.ExitRotation);
            AreaCaptureFixture.CaptureHomeBedFrame(home, "41-recovered");
            Time.timeScale = 1f;
            yield return AreaCaptureFixture.CaptureHomeBedOpening(home);
        }

        [UnityTest]
        public IEnumerator
            Bed_CancelledSleepLetsTheDentRecover()
        {
            yield return LoadHome();
            Assert.That(home.BedSurface, Is.Not.Null);
            Vector3 hip = home.BedInteractionPlan.ActionHipPosition;
            float restTop =
                HomeInteriorWorldBuilder.BedMattressSurfaceHeight;

            Assert.That(home.Bed.BeginSleeping(), Is.True);
            yield return null;
            Assert.That(
                home.BedSurface.GetSurfaceHeight(hip),
                Is.LessThan(restTop - 0.03f));

            home.AnimatedInteraction.CancelActiveInteraction();
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));

            Time.timeScale = FastTimeScale;
            float recoveryDeadline = Time.time + 2.5f;
            while (Time.time < recoveryDeadline)
            {
                yield return null;
            }

            Assert.That(
                home.BedSurface.GetSurfaceHeight(hip),
                Is.EqualTo(restTop).Within(0.002f),
                "An abandoned sleep must not leave a permanent dent.");
        }

        [UnityTest]
        public IEnumerator
            Bed_FatigueResetsOnlyAfterCompletedWake()
        {
            yield return LoadHome();

            GameSessionState.UpdateFatigue(67);
            Assert.That(home.Bed.BeginSleeping(), Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(67));

            Assert.That(home.Bed.RequestWake(), Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(67),
                "Requesting wake must not reset fatigue before the exit " +
                "animation completes.");

            Time.timeScale = FastTimeScale;
            yield return WaitForPhase(
                home,
                PlayerAnimatedInteractionPhase.Idle);
            Time.timeScale = 1f;
            yield return null;

            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(GameSessionState.DefaultFatigue));

            GameSessionState.UpdateFatigue(83);
            Assert.That(home.Bed.BeginSleeping(), Is.True);
            Assert.That(home.Bed.RequestWake(), Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));

            home.Bed.enabled = false;
            yield return null;

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(83),
                "Cancelling an exiting bed interaction must not count as " +
                "a completed sleep.");
        }

        private HomeBedDeformableSurface FindBedSurface(string objectName)
        {
            Transform surfaceObject = home.Room.Find(objectName);
            Assert.That(surfaceObject, Is.Not.Null, objectName);
            HomeBedDeformableSurface surface =
                surfaceObject.GetComponent<HomeBedDeformableSurface>();
            Assert.That(surface, Is.Not.Null, objectName);
            return surface;
        }

        private IEnumerator ObserveBedTransition(
            PlayerAnimatedInteractionPhase phase,
            PlayerAnimatedInteractionPhase completedPhase,
            bool capture = true)
        {
            bool entering = phase == PlayerAnimatedInteractionPhase.Entering;
            int startFrame = entering
                ? home.Bed.Definition.EnterStartFrame
                : home.Bed.Definition.ExitStartFrame;
            int frameCount = entering
                ? home.Bed.Definition.EnterFrameCount
                : home.Bed.Definition.ExitFrameCount;
            float[] checkpoints = entering
                ? new[] { 0f, 0.22f, 0.30f, 0.375f, 0.45f, 0.545f, 0.62f, 0.78f, 0.98f }
                : new[] { 0f, 0.30f, 0.375f, 0.445f, 0.515f, 0.58f, 0.71f, 0.84f, 0.98f };
            int nextCheckpoint = 0;
            int sampledFrames = 0;
            HomeBedDeformableSurface pillow = FindBedSurface("Home Pillow");
            var pillowRest = new Vector3[pillow.VertexCount];
            pillow.CopyBaseVertices(pillowRest);
            var presentation = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            string clip = entering ? "BedEnter" : "BedExit";
            bool fixedMotion = capture && AreaCaptureFixture.CaptureHomeBedEnabled;
            float previousCaptureStep = Time.captureDeltaTime;
            if (fixedMotion)
                Time.captureDeltaTime = 1f / (24f * Mathf.Max(0.001f, Time.timeScale));
            double motionStartedAt = Time.timeAsDouble;
            // Saving two camera views must not advance the clip past its
            // short support changes. The longer wall timeout is capture-only.
            float deadline = Time.realtimeSinceStartup + (fixedMotion ? 90f : TimeoutSeconds);
            try
            {
                if (capture) AreaCaptureFixture.CaptureHomeBedMotionFrame(home, clip, 0f);
                while (home.AnimatedInteraction.Phase == phase &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    if (home.AnimatedInteraction.Phase != phase)
                    {
                        break;
                    }

                    float progress = Mathf.Clamp01(
                        (home.AnimatedInteraction.FrameIndex - startFrame) /
                        (float)frameCount);
                    if (capture) AreaCaptureFixture.CaptureHomeBedMotionFrame(
                        home, clip, (float)(Time.timeAsDouble - motionStartedAt));
                    AssertContinuous3DPresentation(home, clip);
                    Assert.That(
                        home.Room.InverseTransformPoint(
                            presentation.Registry.Anchors.Pelvis.position).x,
                        Is.EqualTo(home.BedInteractionPlan.ActionHipPosition.x).Within(0.015f),
                        $"{clip} must keep the pelvis opposite the middle of the " +
                        "bed instead of sliding toward the pillow or foot end.");
                    AssertPosedVerticesClearMattress($"{clip} at {progress:P0}");
                    AssertPillowKeepsItsBaseAndThickness(pillow, pillowRest);
                    if (capture && nextCheckpoint < checkpoints.Length &&
                        progress >= checkpoints[nextCheckpoint])
                    {
                        AreaCaptureFixture.CaptureHomeBedFrame(
                            home,
                            $"{(entering ? 10 : 30)}-{clip}-{nextCheckpoint:00}");
                        nextCheckpoint++;
                    }
                    sampledFrames++;
                }

                Assert.That(home.AnimatedInteraction.Phase, Is.EqualTo(completedPhase));
                Assert.That(sampledFrames, Is.GreaterThan(4),
                    "The complete transition must expose intermediate posed frames.");
                if (fixedMotion)
                    Assert.That(sampledFrames, Is.GreaterThanOrEqualTo(frameCount),
                        "Motion review must include every authored frame, not sparse key poses.");
                if (capture) AreaCaptureFixture.CaptureHomeBedMotionFrame(
                    home, clip, (float)(Time.timeAsDouble - motionStartedAt));
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureStep;
                if (capture) AreaCaptureFixture.CompleteHomeBedMotion(clip);
            }
            yield return null;
        }

        private void AssertPosedVerticesClearMattress(
            string context,
            bool requireContact = false,
            float give = WakeBeddingGive)
        {
            var presentation = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            bodyProbeRenderers.Clear();
            HomeBedDeformableSurface mattress = FindBedSurface("Home Bed Mattress");
            // Soft goods can give locally. Use the deepest permitted support
            // plane; the live contact/indent assertions check the held pose.
            float supportY = HomeInteriorWorldBuilder.BedMattressSurfaceHeight -
                HomeInteriorWorldBuilder.BedSleeperSinkDepth;
            float minimumY = supportY - give;
            float lowestBody = float.PositiveInfinity;
            string lowestBodyMesh = "none";
            foreach (Player3DMeshBinding binding in presentation.Registry.MeshBindings)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    !bodyProbeRenderers.Add(renderer))
                {
                    continue;
                }

                if (!ReadPosedVertices(renderer))
                {
                    continue;
                }

                float lowest = float.PositiveInfinity;
                float worldMinimum = float.PositiveInfinity;
                Vector3 lowestPoint = Vector3.zero;
                foreach (Vector3 localVertex in bodyProbeVertices)
                {
                    Vector3 vertex = renderer.transform.TransformPoint(localVertex);
                    worldMinimum = Mathf.Min(worldMinimum, vertex.y);
                    Vector2 mattressPoint = mattress.WorldToLocalPlanar(vertex);
                    const float edgeInset = 0.025f;
                    if (Mathf.Abs(mattressPoint.x) < mattress.SizeX * 0.5f - edgeInset &&
                        Mathf.Abs(mattressPoint.y) < mattress.SizeZ * 0.5f - edgeInset)
                    {
                        if (vertex.y < lowest)
                        {
                            lowest = vertex.y;
                            lowestPoint = vertex;
                        }
                    }
                }
                Assert.That(lowest, Is.GreaterThan(minimumY),
                    $"{context}: posed {binding.MeshName} reaches {lowest:F3} m " +
                    $"over the mattress at {lowestPoint:F3}, below its compressed " +
                    $"support limit at {minimumY:F3} m. Whole-mesh posed minimum=" +
                    $"{worldMinimum:F3}, renderer AABB minimum={renderer.bounds.min.y:F3}.");
                if (lowest < lowestBody)
                {
                    lowestBody = lowest;
                    lowestBodyMesh = binding.MeshName;
                }
            }
            if (requireContact)
            {
                Assert.That(lowestBody, Is.LessThan(supportY + 0.05f),
                    $"{context}: the lowest posed mesh over the mattress is " +
                    $"{lowestBodyMesh} at {lowestBody:F3} m, above the allowed " +
                    $"contact gap over its compressed support at {supportY:F3} m.");
            }
        }

        private bool ReadPosedVertices(Renderer renderer)
        {
            bodyProbeVertices.Clear();
            if (renderer is SkinnedMeshRenderer skinned)
            {
                bodyProbeMesh ??= new Mesh { name = "Home bed posed-body test probe" };
                bodyProbeMesh.Clear(false);
                skinned.BakeMesh(bodyProbeMesh, true);
                bodyProbeMesh.GetVertices(bodyProbeVertices);
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null)
                {
                    return false;
                }
                filter.sharedMesh.GetVertices(bodyProbeVertices);
            }
            return bodyProbeVertices.Count > 0;
        }

        private static void AssertPillowHasVolume(
            HomeBedDeformableSurface pillow,
            Vector3[] rest)
        {
            float low = float.PositiveInfinity;
            float high = float.NegativeInfinity;
            for (int index = 0; index < pillow.TopVertexCount; index++)
            {
                low = Mathf.Min(low, rest[index].y);
                high = Mathf.Max(high, rest[index].y);
            }
            Assert.That(high - low, Is.GreaterThan(0.06f),
                "An unloaded pillow must have a visibly rounded crown and shoulders.");
            Assert.That(pillow.Thickness, Is.GreaterThanOrEqualTo(0.12f));
        }

        private void AssertPillowKeepsItsBaseAndThickness(
            HomeBedDeformableSurface pillow,
            Vector3[] rest)
        {
            Vector3[] current = pillow.Mesh.vertices;
            float maximumBaseMovement = 0f;
            for (int index = pillow.TopVertexCount; index < current.Length; index++)
            {
                maximumBaseMovement = Mathf.Max(maximumBaseMovement,
                    Vector3.Distance(current[index], rest[index]));
            }
            Assert.That(maximumBaseMovement, Is.LessThan(0.0001f),
                "Compression must keep the authored lower pillow shell seated.");
            float minimumThickness = float.PositiveInfinity;
            for (int index = 0; index < pillow.TopVertexCount; index++)
            {
                Vector3 vertex = current[index];
                // A closed lens intentionally tapers to a seam at its edge.
                if (Mathf.Abs(vertex.x) < pillow.SizeX * 0.35f &&
                    Mathf.Abs(vertex.z) < pillow.SizeZ * 0.35f)
                {
                    minimumThickness = Mathf.Min(minimumThickness,
                        vertex.y - pillow.SampleRestBottomHeight(vertex.x, vertex.z));
                }
            }
            Assert.That(minimumThickness, Is.InRange(0.012f, pillow.Thickness),
                "The loaded crown must retain stuffing above its lower shell.");
            HomeBedDeformableSurface mattress = FindBedSurface("Home Bed Mattress");
            Vector2 mattressPoint = mattress.WorldToLocalPlanar(pillow.transform.position);
            float mattressY = mattress.RestTopWorldY -
                home.BedSurface.MattressModel.SampleDepth(mattressPoint.x, mattressPoint.y);
            float pillowBottomY = pillow.transform.TransformPoint(
                new Vector3(0f, pillow.SampleRestBottomHeight(0f, 0f), 0f)).y;
            Assert.That(pillowBottomY, Is.LessThanOrEqualTo(mattressY + 0.003f),
                "The pillow's base must remain in contact with the mattress below it.");
        }

        private static float MaximumVertexDisplacement(Vector3[] rest, Vector3[] current)
        {
            Assert.That(current.Length, Is.EqualTo(rest.Length));
            float maximum = 0f;
            for (int index = 0; index < rest.Length; index++)
            {
                maximum = Mathf.Max(maximum, Vector3.Distance(rest[index], current[index]));
            }
            return maximum;
        }

        /// <summary>
        /// The complaint this exists for is "he sinks into the bed". Nothing
        /// grounds the rig while a contextual clip owns it, so the only proof
        /// is to look at where the visible meshes actually are.
        /// </summary>
        // Measure posed geometry: rotated AABBs extend below the boots even
        // when their real vertices remain supported. Bedding still allows the
        // same bounded compression as the Blender bed support validator.
        private const float SleepBeddingGive = 0.01f;
        private const float WakeBeddingGive = 0.03f;

        private void AssertBodyRestsOnMattress(
            HomeInteriorRoot home,
            string context,
            bool requireContact,
            float give)
        {
            Assert.That(home, Is.SameAs(this.home));
            AssertPosedVerticesClearMattress(context, requireContact, give);
        }

        private Vector3 AssertHeadRestsOnPillow(
            HomeInteriorRoot home)
        {
            Player3DCharacterPresentation presentation =
                home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Assert.That(
                presentation.Registry.TryGetPart(
                    Player3DAnatomicalPart.Head,
                    out Player3DAnatomicalPartBinding head),
                Is.True);

            Assert.That(home.BedSurface, Is.Not.Null);
            HomeBedDeformableSurface pillow = FindBedSurface("Home Pillow");
            Vector3 supportPoint = new Vector3(0f, float.PositiveInfinity, 0f);
            int measuredMeshes = 0;
            foreach (Player3DMeshBinding binding in presentation.Registry.MeshBindings)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null ||
                    (renderer != head.Renderer && binding.MeshName != "GEO_HairBack"))
                {
                    continue;
                }
                Assert.That(ReadPosedVertices(renderer), Is.True, binding.MeshName);
                Vector3 lowestPoint = new Vector3(0f, float.PositiveInfinity, 0f);
                foreach (Vector3 localVertex in bodyProbeVertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(localVertex);
                    if (point.y < lowestPoint.y)
                    {
                        lowestPoint = point;
                    }
                }
                Assert.That(pillow.ContainsPlanar(lowestPoint), Is.True,
                    $"{binding.MeshName}'s posed underside at {lowestPoint:F3} " +
                    "must remain over the pillow footprint.");
                float liveHeight = home.BedSurface.GetSurfaceHeight(lowestPoint);
                Assert.That(lowestPoint.y, Is.GreaterThan(liveHeight - 0.03f),
                    $"{binding.MeshName}'s posed underside at {lowestPoint:F3} " +
                    $"must ride on its local deformed pillow at {liveHeight:F3} m; " +
                    $"renderer AABB minimum={renderer.bounds.min.y:F3} m.");
                if (lowestPoint.y < supportPoint.y)
                {
                    supportPoint = lowestPoint;
                }
                measuredMeshes++;
            }
            Assert.That(measuredMeshes, Is.EqualTo(2),
                "Both the actual head and back-of-head hair must carry the support check.");
            float supportHeight = home.BedSurface.GetSurfaceHeight(supportPoint);
            Assert.That(supportPoint.y, Is.LessThan(supportHeight + 0.03f),
                $"The head's lowest posed point at {supportPoint:F3} must rest on " +
                $"the local pillow at {supportHeight:F3} m without an air gap.");
            return supportPoint;
        }

        private static void AssertSleepingHeadToFootOrientation(
            HomeInteriorRoot home)
        {
            Player3DCharacterPresentation presentation =
                home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Player3DBoneAnchors anchors =
                presentation.Registry.Anchors;
            Vector3 feet =
                (anchors.LeftFoot.position + anchors.RightFoot.position) *
                0.5f;
            Vector3 headToFeet = feet - anchors.Head.position;
            Vector3 headToFootAxis =
                home.BedInteractionPlan.HeadToFootAxis.normalized;

            Assert.That(
                Vector3.Dot(headToFeet.normalized, headToFootAxis),
                Is.GreaterThan(0.90f),
                "The sleeping 3D rig must put its head at the pillow end " +
                "and its feet at the open end of the bed.");
            Assert.That(
                Vector3.Dot(
                    anchors.Head.position - anchors.Pelvis.position,
                    headToFootAxis),
                Is.LessThan(-0.35f),
                "The sleeping head must remain headboard-side of the hips.");
            Assert.That(
                Vector3.Dot(
                    feet - anchors.Pelvis.position,
                    headToFootAxis),
                Is.GreaterThan(0.30f),
                "The sleeping feet must remain foot-side of the hips.");
        }

        private static void AssertSeatedOnDoorSideEdge(
            HomeInteriorRoot home)
        {
            Player3DCharacterPresentation presentation =
                home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Player3DBoneAnchors anchors =
                presentation.Registry.Anchors;

            Assert.That(
                Vector3.Distance(
                    anchors.Pelvis.position,
                    home.BedInteractionPlan.SeatHipPosition),
                Is.LessThan(0.035f),
                "Wake must settle the pelvis on the door-side bed edge " +
                "before the standing phase begins.");
            Assert.That(
                anchors.Head.position.y,
                Is.GreaterThan(
                    anchors.Pelvis.position.y +
                    presentation.Registry.Metrics.CanonicalHeight * 0.33f),
                "The waypoint must present an upright seated body, not " +
                "slide the lying pose across the mattress.");
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

        private static IEnumerator WaitForAnimationFrame(
            HomeInteriorRoot home,
            int minimumFrame)
        {
            float deadline =
                Time.realtimeSinceStartup + 3f;
            while (home.AnimatedInteraction.FrameIndex < minimumFrame &&
                   home.AnimatedInteraction.Phase ==
                       PlayerAnimatedInteractionPhase.Exiting &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                home.AnimatedInteraction.FrameIndex,
                Is.GreaterThanOrEqualTo(minimumFrame));
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
