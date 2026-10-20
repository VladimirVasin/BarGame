using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DebugFreeCameraPlayModeTests
    {
        private readonly List<Behaviour> suspendedReaders = new List<Behaviour>();
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;
        private Scene testScene;
        private GameObject fixture;
        private PlayerRuntime player;
        private Camera camera;
        private PlayerCameraFollow follow;
        private PlayerAnimatedInteractionController animation;
        private DebugFreeCamera flight;
        private PauseMenuController pause;
        private BarMinigameModalLock foreignModal;
        private float previousTimeScale;
        private bool previousAudioPause;
        private bool previousFocus;
        private float measuredSpeed;
        private DebugFreeCameraLateSample lateSample;
        private CombatTestRoot combat;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (DebugFreeCamera reader in Object.FindObjectsByType<DebugFreeCamera>(FindObjectsInactive.Exclude))
                Suspend(reader);
            foreach (PauseMenuController reader in Object.FindObjectsByType<PauseMenuController>(FindObjectsInactive.Exclude))
                Suspend(reader);
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            previousFocus = GameCursor.ApplicationFocused;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SetTransition(false);
            GameInput.HandleApplicationFocus(true);
            input = new InputTestFixture();
            input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();

            testScene = SceneManager.CreateScene("Test free camera controls");
            fixture = new GameObject("Test free camera fixture");
            SceneManager.MoveGameObjectToScene(fixture, testScene);
            fixture.transform.position = new Vector3(200f, 100f, 200f);
            GameObject ground = Child("Test free camera floor");
            ground.transform.localPosition = Vector3.down * .1f;
            ground.AddComponent<BoxCollider>().size = new Vector3(40f, .2f, 40f);
            GameObject hero = Child("Test free camera player");
            hero.transform.localPosition = Vector3.up * PlayerFactory.GroundedRootOffset;
            CharacterController capsule = hero.AddComponent<CharacterController>();
            capsule.height = 1.7f;
            capsule.radius = .32f;
            capsule.center = Vector3.up * .85f;
            capsule.skinWidth = PlayerFactory.GroundedRootOffset;
            PlayerClipPresentationTestDouble visual = hero.AddComponent<PlayerClipPresentationTestDouble>();
            PlayerMotor motor = hero.AddComponent<PlayerMotor>();
            motor.Initialize(null, visual);
            PlayerInteractor interactor = hero.AddComponent<PlayerInteractor>();
            interactor.Initialize(null);
            player = new PlayerRuntime(hero, motor, interactor, visual);

            camera = Child("Test free camera view").AddComponent<Camera>();
            lateSample = camera.gameObject.AddComponent<DebugFreeCameraLateSample>();
            follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, hero.transform, false);
            follow.SetCinematicMotionEnabled(false);
            animation = hero.AddComponent<PlayerAnimatedInteractionController>();
            animation.Initialize(player, camera);
            flight = hero.AddComponent<DebugFreeCamera>();
            flight.Initialize(player, camera);
            pause = Child("Test free camera pause menu").AddComponent<PauseMenuController>();
            pause.Initialize(player, follow, null);
            yield return null;
            yield return null;
            GameInput.ReadMovement();
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                "The neutral synthetic scene must start with ordinary gameplay ownership.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetTransition(false);
            if (combat != null) combat.AutomaticSimulation = false;
            foreignModal?.Restore();
            if (animation != null) animation.CancelActiveInteraction();
            if (flight != null) flight.enabled = false;
            if (pause != null) pause.enabled = false;
            if (keyboard != null && keyboard.added)
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (combat != null) RetroAudioService.Instance?.StopAll();
                yield return UnloadFixture();
            }
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            input?.TearDown();
            input = null;
            foreach (Behaviour reader in suspendedReaders)
                if (reader != null) reader.enabled = true;
            suspendedReaders.Clear();
            GameInput.HandleApplicationFocus(previousFocus);
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
        }

        [UnityTest]
        public IEnumerator RawControls_FlyWithoutMovingHero_AndEscapeReturnsOwnedInput()
        {
            Vector3 heroPosition = player.GameObject.transform.position;
            Quaternion heroRotation = player.GameObject.transform.rotation;
            Pose originalView = new Pose(camera.transform.position, camera.transform.rotation);
            float originalFieldOfView = camera.fieldOfView;
            float originalTimeScale = Time.timeScale;
            input.Press(keyboard.f4Key, queueEventOnly: true);
            yield return null;
            Assert.That(flight.IsActive, Is.True, "Raw F4 must enter the shared camera.");
            input.Release(keyboard.f4Key, queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False,
                "Flight reserves device commands without replacing the actor's input ownership.");
            Assert.That(GameInput.CanRead(GameInputContext.Movement), Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.Contextual), Is.False);
            Assert.That(follow.PresentationSuspended, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale), "Inspection must keep world time running.");

            Quaternion viewBeforeLook = camera.transform.rotation;
            input.Set(mouse.delta, new Vector2(80f, -35f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(viewBeforeLook, camera.transform.rotation), Is.GreaterThan(1f));

            Key[] keys = { Key.W, Key.S, Key.A, Key.D, Key.E, Key.Q };
            Vector3[] axes = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                               Vector3.up, Vector3.down };
            for (int index = 0; index < keys.Length; index++)
            {
                Vector3 direction = index < 4 ? camera.transform.rotation * axes[index] : axes[index];
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys[index]));
                yield return null;
                Vector3 start = camera.transform.position;
                yield return null;
                Assert.That(Vector3.Dot(camera.transform.position - start, direction), Is.GreaterThan(.00001f),
                    $"Raw {keys[index]} must translate the inspection camera on its documented axis.");
            }
            yield return MeasureForwardSpeed(false);
            float ordinarySpeed = measuredSpeed;
            yield return MeasureForwardSpeed(true);
            Assert.That(measuredSpeed, Is.GreaterThan(ordinarySpeed * 3f), "Shift must accelerate free flight.");
            AssertHeroUnchanged(heroPosition, heroRotation);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.E, Key.Escape));
            yield return null;
            Assert.That(flight.IsActive, Is.False);
            AssertRestored();
            Assert.That(Vector3.Distance(camera.transform.position, originalView.position), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, originalView.rotation), Is.LessThan(.1f));
            Assert.That(camera.fieldOfView, Is.EqualTo(originalFieldOfView).Within(.001f));
            yield return null;
            Assert.That(pause.IsOpen, Is.False, "The Escape that exits flight must not also open pause.");
            Assert.That(GameInput.CanRead(GameInputContext.Movement), Is.False,
                "Held flight W/E must stay owned until their controls are released.");
            AssertHeroUnchanged(heroPosition, heroRotation);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
            Assert.That(pause.IsOpen, Is.False);
            Vector3 viewBeforeFollow = camera.transform.position;
            player.Motor.Teleport(player.GameObject.transform.position + Vector3.right * .5f);
            yield return null;
            yield return null;
            Assert.That(camera.transform.position.x - viewBeforeFollow.x, Is.GreaterThan(.25f),
                "The ordinary chase camera must follow the hero again after the handoff.");
        }

        [UnityTest]
        public IEnumerator BusyActionsContinue_AndDisableTransitionUnloadReleaseOnlyTheCamera()
        {
            SetTransition(true);
            Assert.That(flight.TryEnter(), Is.False);
            SetTransition(false);
            float runningScale = Time.timeScale;
            Time.timeScale = 0f;
            Assert.That(flight.TryEnter(), Is.False);
            Time.timeScale = runningScale;

            foreignModal = new BarMinigameModalLock();
            Assert.That(foreignModal.TryCaptureAndDisable(player.Interactor, follow, null), Is.True);
            var definition = new PlayerAnimatedInteractionDefinition("TestEnter", "TestLoop", "TestExit",
                enterFrameCount: 1, enterFramesPerSecond: 5f,
                loopFrameCount: 4, loopFramesPerSecond: 5f,
                exitFrameCount: 2, exitFramesPerSecond: 10f);
            Vector3 support = player.GameObject.transform.position + Vector3.up * .72f;
            Assert.That(animation.BeginLooping(definition, support, support), Is.True);
            Vector3 heroPosition = player.GameObject.transform.position;
            Quaternion heroRotation = player.GameObject.transform.rotation;
            Pose currentShot = new Pose(fixture.transform.position + new Vector3(-3f, 2.5f, -4f),
                Quaternion.Euler(18f, 40f, 0f));

            using (follow.SuspendPresentation())
            {
                input.Press(keyboard.f4Key, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.f4Key, queueEventOnly: true);
                yield return null;
                Assert.That(flight.IsActive, Is.True,
                    "Raw F4 must overlay a live action, modal input and another camera loan.");
                Assert.That(foreignModal.IsLocked, Is.True);
                Assert.That(player.Motor.InputEnabled || player.Interactor.InputEnabled, Is.False);
                Assert.That(follow.OrbitInputEnabled, Is.False);
                Assert.That(animation.IsActive, Is.True);

                Pose flyingView = new Pose(camera.transform.position, camera.transform.rotation);
                float flyingLens = camera.fieldOfView;
                follow.SetFixedPose(currentShot.position, currentShot.rotation, 47f);
                Assert.That(camera.transform.position, Is.EqualTo(flyingView.position),
                    "An underlying action updates its current shot without overwriting the free camera.");
                Assert.That(Quaternion.Angle(camera.transform.rotation, flyingView.rotation), Is.LessThan(.001f));
                Assert.That(camera.fieldOfView, Is.EqualTo(flyingLens).Within(.001f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.E));
                float beforeActionProgress = animation.PhaseProgress;
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, flyingView.position), Is.GreaterThan(.001f));
                Assert.That(animation.PhaseProgress, Is.Not.EqualTo(beforeActionProgress),
                    "The busy action's real frame clock must continue while its camera is borrowed.");
                AssertHeroUnchanged(heroPosition, heroRotation);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());

                Assert.That(animation.RequestExit(), Is.True);
                float completionDeadline = Time.realtimeSinceStartup + 2f;
                while (animation.IsActive && Time.realtimeSinceStartup < completionDeadline) yield return null;
                Assert.That(animation.IsActive, Is.False,
                    "The action must finish its actual exit and cleanup during free flight.");
                Assert.That(flight.IsActive, Is.True);
                Assert.That(foreignModal.IsLocked, Is.True, "Action completion must preserve its outer modal owner.");
                Assert.That(player.Motor.InputEnabled || player.Interactor.InputEnabled, Is.False);
                foreignModal.Restore();
                Assert.That(player.Motor.InputEnabled && player.Interactor.InputEnabled, Is.True,
                    "The action's owner restores its live baseline without waiting for the debug camera.");
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
                Assert.That(flight.IsActive, Is.True);
            }
            Assert.That(follow.PresentationSuspended, Is.True, "Releasing the action's loan must retain the flight loan.");
            Assert.That(flight.Exit(), Is.True);
            AssertRestored();
            Assert.That(camera.transform.position, Is.EqualTo(currentShot.position));
            Assert.That(Quaternion.Angle(camera.transform.rotation, currentShot.rotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(47f).Within(.001f),
                "Flight returns the latest action camera, never its stale entry snapshot.");
            yield return null;
            yield return null;

            Pose beforeDisable = new Pose(camera.transform.position, camera.transform.rotation);
            Assert.That(flight.TryEnter(), Is.True);
            camera.transform.position += Vector3.one * 4f;
            flight.enabled = false;
            Assert.That(flight.IsActive, Is.False);
            AssertRestored();
            Assert.That(camera.transform.position, Is.EqualTo(beforeDisable.position));
            Assert.That(Quaternion.Angle(camera.transform.rotation, beforeDisable.rotation), Is.LessThan(.001f));
            flight.enabled = true;
            yield return null;
            yield return null;
            Assert.That(flight.TryEnter(), Is.True);
            SetTransition(true);
            yield return null;
            Assert.That(flight.IsActive, Is.False, "Starting scene travel must release an active flight.");
            AssertRestored();
            SetTransition(false);
            yield return null;
            yield return null;
            Assert.That(flight.TryEnter(), Is.True);
            yield return UnloadFixture();
            yield return null;
            yield return null;
            Assert.That(DebugFreeCamera.IsAnyActive, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False, "Scene teardown must not strand the global lock.");
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
            yield return VerifyActualDefeatKeepsInspectionAndWorldTime();
        }

        private IEnumerator VerifyActualDefeatKeepsInspectionAndWorldTime()
        {
            // CombatActor requires the production continuous rig. Build the
            // real bounded range in this test's scene rather than faking death.
            GameSessionState.BeginNewGame();
            testScene = SceneManager.CreateScene("Test free camera combat inspection");
            Scene previousScene = SceneManager.GetActiveScene();
            bool previousLegacy = CombatTestRoot.UseLegacyArenaForTests;
            bool previousDocker = CombatTestRoot.UseDockerPopulationForTests;
            CombatEnemyKind? previousEnemy = CombatTestRoot.AuthoredEnemyKindForTests;
            try
            {
                CombatTestRoot.UseLegacyArenaForTests = true;
                CombatTestRoot.UseDockerPopulationForTests = false;
                CombatTestRoot.AuthoredEnemyKindForTests = null;
                foreach (Camera existing in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                    Suspend(existing);
                foreach (AudioListener existing in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                    Suspend(existing);
                SceneManager.SetActiveScene(testScene);
                var combatView = new GameObject("Test free camera combat view");
                combatView.tag = "MainCamera";
                combatView.AddComponent<Camera>();
                combat = new GameObject("Test free camera combat range").AddComponent<CombatTestRoot>();
            }
            finally
            {
                SceneManager.SetActiveScene(previousScene);
                CombatTestRoot.UseLegacyArenaForTests = previousLegacy;
                CombatTestRoot.UseDockerPopulationForTests = previousDocker;
                CombatTestRoot.AuthoredEnemyKindForTests = previousEnemy;
            }
            Assert.That(combat.IsInitialized, Is.True);
            combat.AutomaticSimulation = false;
            combat.SetSparring(false);
            player = combat.Player;
            follow = combat.CameraFollow;
            camera = follow.Camera;
            animation = player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            flight = player.GameObject.GetComponent<DebugFreeCamera>();
            pause = combat.PauseMenu;
            Assert.That(flight, Is.Not.Null, "The actual combat hero receives the shared camera from PlayerFactory.");
            yield return null;
            yield return null;
            input.Press(keyboard.f4Key, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.f4Key, queueEventOnly: true);
            yield return null;
            Assert.That(flight.IsActive, Is.True);
            long liveSteps = combat.SimulationSubsteps;
            combat.AutomaticSimulation = true;
            float advanceDeadline = Time.realtimeSinceStartup + 1f;
            while (combat.SimulationSubsteps == liveSteps && Time.realtimeSinceStartup < advanceDeadline)
                yield return null;
            combat.AutomaticSimulation = false;
            Assert.That(combat.SimulationSubsteps, Is.GreaterThan(liveSteps),
                "Camera inspection owns player input while the actual Update-driven combat simulation keeps advancing.");

            float runningScale = Time.timeScale;
            Vector3 resetPosition = player.GameObject.transform.position;
            Vector3 resetFacing = player.GameObject.transform.forward;
            combat.Hero.State.ReceiveUnarmedHit(combat.Hero.State.Settings.MaxHealth, 0f, false);
            Assert.That(combat.Hero.State.IsDefeated, Is.True);
            combat.Tick(CombatAssetProvider.DefeatHandoffSeconds + .2f);
            Assert.That(combat.Hero.IsRagdollActive, Is.True,
                "The real finished-round handler must advance defeat into physics during inspection.");
            Assert.That(combat.DefeatResultVisible, Is.True);
            Assert.That(flight.IsActive, Is.True, "Defeat must not release the active inspection camera.");
            Assert.That(Time.timeScale, Is.EqualTo(runningScale));
            Assert.That(GameTimeScaleRuntime.IsPaused || pause.IsOpen, Is.False);
            Vector3 beforeMove = camera.transform.position;
            Quaternion beforeLook = camera.transform.rotation;
            float beforeWorldTime = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            input.Set(mouse.delta, new Vector2(65f, 20f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(camera.transform.position, beforeMove), Is.GreaterThan(.001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, beforeLook), Is.GreaterThan(1f));
            Assert.That(Time.time, Is.GreaterThan(beforeWorldTime), "Death must not pause world time.");
            Assert.That(flight.IsActive, Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F4));
            yield return null;
            Assert.That(flight.IsActive, Is.False);
            Assert.That(player.Motor.InputEnabled || player.Interactor.InputEnabled, Is.False,
                "Exiting inspection cannot resurrect the defeated hero's controls.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F4));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(flight.IsActive, Is.True, "A defeated actor must also admit the camera-only overlay.");
            Assert.That(player.Motor.InputEnabled || player.Interactor.InputEnabled, Is.False);
            // ResetRound is a guarded user command and correctly refuses
            // flight input. Exercise the authoritative actor reset that
            // PlaceRound itself uses without borrowing physical commands.
            combat.Hero.ResetActor(resetPosition, resetFacing);
            Assert.That(combat.Hero.State.IsDefeated, Is.False);
            Assert.That(player.Motor.InputEnabled && player.Interactor.InputEnabled, Is.True,
                "A reset during inspection must restore the actor's current living baseline.");
            Assert.That(flight.IsActive, Is.True);
            Assert.That(GameInput.CanRead(GameInputContext.Movement), Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F4));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            Assert.That(flight.IsActive, Is.False);
            Assert.That(player.Motor.InputEnabled && player.Interactor.InputEnabled, Is.True,
                "Leaving flight must not restore the defeated flags captured before the round reset.");
        }

        private IEnumerator MeasureForwardSpeed(bool accelerated)
        {
            InputSystem.QueueStateEvent(keyboard, accelerated
                ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
            yield return null;
            Vector3 start = camera.transform.position;
            yield return null;
            measuredSpeed = Vector3.Distance(start, camera.transform.position) /
                Mathf.Max(.000001f, Mathf.Min(lateSample.FrameSeconds, .1f));
        }

        private void AssertRestored()
        {
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            Assert.That(follow.CinematicMotionEnabled, Is.False, "Restore the previous cinematic setting exactly.");
            Assert.That(follow.PresentationSuspended, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        private void AssertHeroUnchanged(Vector3 position, Quaternion rotation)
        {
            Vector3 actual = player.GameObject.transform.position;
            Assert.That(Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(position.x, position.z)),
                Is.LessThan(.001f), "Free-camera controls must never drive the hero.");
            Assert.That(Quaternion.Angle(player.GameObject.transform.rotation, rotation), Is.LessThan(.001f));
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(fixture.transform, false);
            return child;
        }

        private void Suspend(Behaviour reader)
        {
            if (!reader.enabled) return;
            suspendedReaders.Add(reader);
            reader.enabled = false;
        }

        private IEnumerator UnloadFixture()
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(testScene);
            Assert.That(unload, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(unload.isDone, Is.True, "The synthetic free-camera scene must unload within its bounded wait.");
        }

        private static void SetTransition(bool value)
        {
            PropertyInfo property = typeof(SceneTransitionService).GetProperty(
                nameof(SceneTransitionService.IsTransitioning), BindingFlags.Public | BindingFlags.Static);
            property.GetSetMethod(true).Invoke(null, new object[] { value });
        }
    }

    // Observe the completed flight step without an end-of-frame yield, which
    // the headless test runner does not dispatch.
    [DefaultExecutionOrder(1000)]
    internal sealed class DebugFreeCameraLateSample : MonoBehaviour
    {
        public float FrameSeconds { get; private set; }
        private void LateUpdate() => FrameSeconds = Time.unscaledDeltaTime;
    }
}
