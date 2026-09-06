using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PauseMenuPlayModeTests
    {
        private readonly List<PauseMenuController> suspendedMenus =
            new List<PauseMenuController>();

        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private PauseMenuController menu;
        private float previousTimeScale;
        private bool previousAudioPause;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PauseMenuController[] existingMenus =
                Object.FindObjectsByType<PauseMenuController>(
                    FindObjectsInactive.Exclude);
            for (int index = 0;
                 index < existingMenus.Length;
                 index++)
            {
                PauseMenuController existing = existingMenus[index];
                suspendedMenus.Add(existing);
                existing.enabled = false;
            }

            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            GameSessionState.ResetDrinkingState();
            GameTimeScaleRuntime.SetIntoxicationLevel(0f);
            Time.timeScale = 0.75f;
            AudioListener.pause = false;

            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();

            playerObject = new GameObject("Pause Test Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Pause Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                true);
            motor.Initialize(null, null);

            uiObject = new GameObject("Pause Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            menu = uiObject.AddComponent<PauseMenuController>();
            menu.Initialize(
                new PlayerRuntime(
                    playerObject,
                    motor,
                    interactor,
                    null),
                cameraFollow,
                hud);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (menu != null)
            {
                menu.enabled = false;
            }

            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(playerObject);
            GameSessionState.ResetDrinkingState();
            GameTimeScaleRuntime.ResetSession();
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            inputFixture?.TearDown();
            inputFixture = null;

            for (int index = 0;
                 index < suspendedMenus.Count;
                 index++)
            {
                if (suspendedMenus[index] != null)
                {
                    suspendedMenus[index].enabled = true;
                }
            }

            suspendedMenus.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator NestedPause_RestoresLatestTempoAndIgnoresOldSessionLeases()
        {
            GameSessionState.BeginNewGame();
            Time.timeScale = 0.75f;
            GameTimeScaleRuntime.EnsureInstalled();
            GameTimeScaleRuntime.SetIntoxicationLevel(0f);
            float normalFixedDelta = Time.fixedDeltaTime / 0.75f;
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.None, 0);
            GameTimeScaleRuntime.SetIntoxicationLevel(100f);
            Assert.That(Time.timeScale, Is.EqualTo(0.66f).Within(0.00001f));
            Assert.That(Time.fixedDeltaTime,
                Is.EqualTo(normalFixedDelta * 0.66f).Within(0.00001f));
            Assert.That(menu.Open(), Is.True);
            System.IDisposable nested = GameTimeScaleRuntime.AcquirePause();
            try
            {
                Assert.That(menu.Cancel(), Is.True);
                yield return null;
                Assert.That(menu.IsOpen, Is.False);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(GameTimeScaleRuntime.CalendarDeltaTime, Is.Zero);
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.None, 0);
                GameTimeScaleRuntime.SetIntoxicationLevel(0f);
                Assert.That(Time.timeScale, Is.Zero);
            }
            finally
            {
                nested.Dispose();
            }

            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.None, 0);
            GameTimeScaleRuntime.SetIntoxicationLevel(100f);
            System.IDisposable obsolete = GameTimeScaleRuntime.AcquirePause();
            GameSessionState.BeginNewGame();
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.fixedDeltaTime,
                Is.EqualTo(normalFixedDelta).Within(0.00001f));
            Assert.That(GameTimeScaleRuntime.PerceptionIntensity, Is.Zero);
            System.IDisposable current = GameTimeScaleRuntime.AcquirePause();
            try
            {
                obsolete.Dispose();
                Assert.That(Time.timeScale, Is.Zero,
                    "An outgoing scene's lease must not unpause a new session.");
            }
            finally
            {
                current.Dispose();
            }

            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator Escape_OpensAndCapturesGameplayState()
        {
            inputFixture.Press(keyboard.wKey, queueEventOnly: true);
            inputFixture.Press(keyboard.aKey, queueEventOnly: true);
            yield return null;
            Assert.That(PlayerDirectionalInput.ReadRaw(), Is.EqualTo(new Vector2(-1f, 1f)),
                "Tank steering must preserve both full axes on W+A.");
            inputFixture.Press(
                keyboard.escapeKey,
                queueEventOnly: true);
            yield return null;

            Assert.That(menu.IsOpen, Is.True);
            Assert.That(PauseMenuController.IsAnyPaused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.False);
            Assert.That(hud.Visible, Is.False);
            // Queue simultaneous bit-addressed keys in one state: separate
            // fixture delta snapshots can overlap and overwrite earlier keys.
            InputSystem.QueueStateEvent(keyboard,
                new KeyboardState(Key.W, Key.A, Key.E, Key.F10));
            yield return null;
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(keyboard.eKey.isPressed, Is.True,
                "The confirmation key must be present in the input snapshot.");
            Assert.That(keyboard.f10Key.isPressed, Is.True,
                "The skip key must be present in the input snapshot.");
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.PauseMenu), Is.True);
            Assert.That(PlayerDirectionalInput.ReadRaw(), Is.EqualTo(Vector2.zero));
            Assert.That(PlayerInteractor.IsInteractHeld(), Is.False);
            Assert.That(GameInput.WasPressed(
                GameInputAction.SkipRide, GameInputContext.Gameplay), Is.False);
            Assert.That(GameInput.IsHeld(
                GameInputAction.Confirm, GameInputContext.PauseMenu), Is.True,
                "The paused menu must retain its confirmation aliases.");
        }

        [UnityTest]
        public IEnumerator Cancel_RestoresExactStateAfterInputGuard()
        {
            Assert.That(menu.Open(), Is.True);

            Assert.That(menu.Cancel(), Is.True);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            yield return null;

            Assert.That(menu.IsOpen, Is.False);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.True);
            Assert.That(hud.Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator OptionsPage_TogglesAndPersistsGraphicsFlag()
        {
            bool previousDither =
                GraphicsEffectsSettings.DitherEnabled;
            bool hadKey = PlayerPrefs.HasKey("graphics.dither");
            int previousKeyValue = hadKey
                ? PlayerPrefs.GetInt("graphics.dither")
                : 0;
            try
            {
                GraphicsEffectsSettings.DitherEnabled = true;
                Assert.That(menu.Open(), Is.True);

                menu.MoveSelection(1);
                Assert.That(
                    menu.SelectedOption,
                    Is.EqualTo(PauseMenuOption.Options));
                menu.ConfirmSelection();
                Assert.That(
                    menu.Page,
                    Is.EqualTo(PauseMenuPage.Options));

                menu.MoveSelection(1);
                menu.MoveSelection(1);
                Assert.That(
                    menu.SelectedOptionsRow,
                    Is.EqualTo(PauseMenuOptionsRow.Dither));
                menu.ConfirmSelection();

                Assert.That(
                    GraphicsEffectsSettings.DitherEnabled,
                    Is.False,
                    "Confirming a toggle row must flip the flag.");
                Assert.That(
                    PlayerPrefs.GetInt("graphics.dither", 1),
                    Is.Zero,
                    "The toggle must persist to PlayerPrefs.");
                Assert.That(
                    menu.Page,
                    Is.EqualTo(PauseMenuPage.Options));

                Assert.That(menu.Cancel(), Is.True);
                Assert.That(
                    menu.Page,
                    Is.EqualTo(PauseMenuPage.Main));
                Assert.That(menu.Cancel(), Is.True);
                yield return null;
                Assert.That(menu.IsOpen, Is.False);
            }
            finally
            {
                GraphicsEffectsSettings.DitherEnabled =
                    previousDither;
                if (hadKey)
                {
                    PlayerPrefs.SetInt(
                        "graphics.dither",
                        previousKeyValue);
                }
                else
                {
                    PlayerPrefs.DeleteKey("graphics.dither");
                }

                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator ExistingModalOwner_BlocksPauseMenu()
        {
            var otherLock = new BarMinigameModalLock();
            Assert.That(
                otherLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.True);

            Assert.That(menu.Open(), Is.False);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));

            Assert.That(otherLock.Restore(), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneSpecificBlocker_PreventsOpening()
        {
            menu.enabled = false;
            Destroy(uiObject);
            yield return null;

            uiObject = new GameObject("Blocked Pause Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            menu = uiObject.AddComponent<PauseMenuController>();
            menu.Initialize(
                new PlayerRuntime(
                    playerObject,
                    motor,
                    interactor,
                    null),
                cameraFollow,
                hud,
                () => false);

            Assert.That(menu.Open(), Is.False);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
        }

        [UnityTest]
        public IEnumerator DisableWhileOpen_RestoresGlobalsImmediately()
        {
            Assert.That(menu.Open(), Is.True);

            menu.enabled = false;

            Assert.That(menu.IsOpen, Is.False);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            yield return null;
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                Object.Destroy(gameObject);
            }
        }
    }
}
