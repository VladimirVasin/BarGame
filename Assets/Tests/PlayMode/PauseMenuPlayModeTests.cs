using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
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
            Assert.That(GameTimeScaleRuntime.ToggleDebugTimeMultiplier(3), Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1.98f).Within(0.00001f));
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
                Assert.That(GameTimeScaleRuntime.ToggleDebugTimeMultiplier(10), Is.True);
                Assert.That(Time.timeScale, Is.Zero);
            }
            finally
            {
                nested.Dispose();
            }

            Assert.That(Time.timeScale, Is.EqualTo(7.5f));
            Assert.That(GameTimeScaleRuntime.CalendarDeltaTime,
                Is.EqualTo(Time.unscaledDeltaTime * 10f).Within(0.00001f));
            // Explicit external overrides must be adopted without applying debug twice.
            Time.timeScale = 5f;
            GameTimeScaleRuntime.SetIntoxicationLevel(0f);
            Assert.That(Time.timeScale, Is.EqualTo(5f));
            GameTimeScaleRuntime.SetDebugSpeedSelectionEnabled(false);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.None, 0);
            GameTimeScaleRuntime.SetIntoxicationLevel(100f);
            System.IDisposable obsolete = GameTimeScaleRuntime.AcquirePause();
            GameSessionState.BeginNewGame();
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.fixedDeltaTime,
                Is.EqualTo(normalFixedDelta).Within(0.00001f));
            Assert.That(GameTimeScaleRuntime.PerceptionIntensity, Is.Zero);
            Assert.That(GameTimeScaleRuntime.DebugTimeMultiplier, Is.EqualTo(1));
            Assert.That(GameTimeScaleRuntime.DebugSpeedSelectionEnabled, Is.True);
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
        public IEnumerator DebugSpeedHotkeys_ToggleGateAndSurviveSceneChanges()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene city = SceneManager.CreateScene(SceneIds.City);
            Scene village = SceneManager.CreateScene(SceneIds.AlpineVillage);
            GameSessionState.BeginNewGame();
            try
            {
                Assert.That(SceneManager.SetActiveScene(city), Is.True);
                KeyControlTest[] selections =
                {
                    new KeyControlTest(keyboard.f1Key, 3),
                    new KeyControlTest(keyboard.f2Key, 5),
                    new KeyControlTest(keyboard.f3Key, 10)
                };
                foreach (KeyControlTest selection in selections)
                {
                    inputFixture.Press(selection.Key, queueEventOnly: true);
                    yield return null;
                    Assert.That(Time.timeScale, Is.EqualTo(selection.Multiplier));
                    Assert.That(GameTimeScaleRuntime.CalendarDeltaTime,
                        Is.EqualTo(Time.unscaledDeltaTime * selection.Multiplier).Within(0.00001f));
                    inputFixture.Release(selection.Key, queueEventOnly: true);
                    yield return null;
                    inputFixture.Press(selection.Key, queueEventOnly: true);
                    yield return null;
                    Assert.That(Time.timeScale, Is.EqualTo(1f));
                    inputFixture.Release(selection.Key, queueEventOnly: true);
                    yield return null;
                }

                inputFixture.Press(keyboard.f2Key, queueEventOnly: true);
                yield return null;
                inputFixture.Release(keyboard.f2Key, queueEventOnly: true);
                Assert.That(SceneManager.SetActiveScene(village), Is.True);
                yield return null;
                Assert.That(Time.timeScale, Is.EqualTo(5f));
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    inputFixture.Press(keyboard.f3Key, queueEventOnly: true);
                    yield return null;
                    Assert.That(Time.timeScale, Is.Zero);
                    Assert.That(GameTimeScaleRuntime.DebugTimeMultiplier, Is.EqualTo(5));
                    inputFixture.Release(keyboard.f3Key, queueEventOnly: true);
                    yield return null;
                }
                Assert.That(Time.timeScale, Is.EqualTo(5f));
                GameTimeScaleRuntime.SetDebugSpeedSelectionEnabled(false);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                inputFixture.Press(keyboard.f1Key, queueEventOnly: true);
                yield return null;
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                inputFixture.Release(keyboard.f1Key, queueEventOnly: true);
                yield return null;
                GameTimeScaleRuntime.SetDebugSpeedSelectionEnabled(true);
                inputFixture.Press(keyboard.f3Key, queueEventOnly: true);
                yield return null;
                Assert.That(Time.timeScale, Is.EqualTo(10f));
                GameSessionState.BeginNewGame();
                Assert.That(Time.timeScale, Is.EqualTo(1f));
            }
            finally
            {
                SceneManager.SetActiveScene(previousScene);
                SceneManager.UnloadSceneAsync(city);
                SceneManager.UnloadSceneAsync(village);
            }
            yield return null;
        }

        private readonly struct KeyControlTest
        {
            public readonly UnityEngine.InputSystem.Controls.KeyControl Key;
            public readonly int Multiplier;
            public KeyControlTest(UnityEngine.InputSystem.Controls.KeyControl key, int multiplier)
            { Key = key; Multiplier = multiplier; }
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
            string[] keys = { "graphics.style", "graphics.clean_aa", "graphics.dof", "graphics.intoxication_fx",
                "graphics.dither", "graphics.scanlines", "graphics.aspect_4_3",
                "graphics.vertex_jitter", "graphics.begotten" };
            var saved = new Dictionary<string, int?>();
            foreach (string key in keys)
            {
                saved[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
                PlayerPrefs.DeleteKey(key);
            }
            Gamepad gamepad = null;
            try
            {
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
                Assert.That(PlayerPrefs.HasKey("graphics.style"), Is.False);
                Assert.That(System.Enum.GetValues(typeof(GraphicsStyle)), Has.Length.EqualTo(2));
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True);
                Assert.That(GraphicsEffectsSettings.IsCleanAntialiasingApplicable, Is.False);
                Assert.That(PlayerPrefs.HasKey("graphics.clean_aa"), Is.False);

                PlayerPrefs.SetInt("graphics.dither", 0);
                PlayerPrefs.SetInt("graphics.vertex_jitter", 1);
                foreach (int obsoleteOrUnknown in new[] { 1, 99 })
                {
                    PlayerPrefs.SetInt("graphics.style", obsoleteOrUnknown);
                    GraphicsEffectsSettings.ResetLoadedStateForTests();
                    Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
                    Assert.That(GraphicsEffectsSettings.DitherEnabled, Is.False);
                    Assert.That(GraphicsEffectsSettings.VertexJitterEnabled, Is.True);
                }

                GraphicsEffectsSettings.DitherEnabled = true;
                GraphicsEffectsSettings.ScanlinesEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = true;
                Assert.That(menu.Open(), Is.True);
                menu.MoveSelection(1);
                Assert.That(
                    menu.SelectedOption,
                    Is.EqualTo(PauseMenuOption.Options));
                menu.ConfirmSelection();
                Assert.That(menu.Page, Is.EqualTo(PauseMenuPage.Options));
                Assert.That(menu.SelectedOptionsRow, Is.EqualTo(PauseMenuOptionsRow.GraphicsStyle));

                int version = GraphicsEffectsSettings.Version;
                menu.ConfirmSelection();
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
                Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(version + 1));
                menu.ConfirmSelection();
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
                Assert.That(menu.CycleGraphicsStyle(-1), Is.True);
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
                Assert.That(menu.CycleGraphicsStyle(1), Is.True);

                // Horizontal keyboard and pad input change the value, not the row.
                yield return null;
                yield return null;
                inputFixture.Press(keyboard.rightArrowKey, queueEventOnly: true);
                yield return null;
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
                Assert.That(menu.SelectedOptionsRow, Is.EqualTo(PauseMenuOptionsRow.GraphicsStyle));
                inputFixture.Release(keyboard.rightArrowKey, queueEventOnly: true);
                yield return null;
                gamepad = InputSystem.AddDevice<Gamepad>();
                inputFixture.Press(gamepad.dpad.left, queueEventOnly: true);
                yield return null;
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
                Assert.That(menu.SelectedOptionsRow, Is.EqualTo(PauseMenuOptionsRow.GraphicsStyle));
                inputFixture.Release(gamepad.dpad.left, queueEventOnly: true);
                yield return null;

                menu.ConfirmSelection();
                Assert.That(GraphicsEffectsSettings.IsCleanAntialiasingApplicable, Is.True);
                SelectOptionsRow(PauseMenuOptionsRow.CleanAntialiasing);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
                Assert.That(PlayerPrefs.GetInt("graphics.clean_aa", 1), Is.Zero);
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
                GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
                Assert.That(PauseMenuController.IsGraphicsOptionAvailable(PauseMenuOptionsRow.CleanAntialiasing), Is.False);
                Assert.That(menu.ConfirmSelection(), Is.False);
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
                GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True);

                Assert.That(GraphicsEffectsSettings.IsDitherApplicable, Is.False);
                Assert.That(GraphicsEffectsSettings.IsScanlinesApplicable, Is.False);
                Assert.That(GraphicsEffectsSettings.IsVertexJitterApplicable, Is.False);
                SelectOptionsRow(PauseMenuOptionsRow.Scanlines);
                Assert.That(menu.ConfirmSelection(), Is.False);
                Assert.That(GraphicsEffectsSettings.ScanlinesEnabled, Is.True,
                    "Unavailable controls must preserve the stored preference.");
                SelectOptionsRow(PauseMenuOptionsRow.VertexJitter);
                Assert.That(menu.ConfirmSelection(), Is.False);
                Assert.That(GraphicsEffectsSettings.VertexJitterEnabled, Is.True);
                SelectOptionsRow(PauseMenuOptionsRow.Dither);
                Assert.That(menu.ConfirmSelection(), Is.False);
                Assert.That(GraphicsEffectsSettings.DitherEnabled, Is.True);
                Assert.That(PauseMenuController.IsGraphicsOptionAvailable(PauseMenuOptionsRow.Dither), Is.False);
                GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
                Assert.That(PauseMenuController.IsGraphicsOptionAvailable(PauseMenuOptionsRow.Dither), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(GraphicsEffectsSettings.DitherEnabled, Is.False);
                Assert.That(PlayerPrefs.GetInt("graphics.dither", 1), Is.Zero);
                Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True,
                    "Returning to PS1 preserves the stored Clean smoothing choice.");
                Assert.That(GraphicsEffectsSettings.ScanlinesEnabled, Is.True);
                Assert.That(GraphicsEffectsSettings.VertexJitterEnabled, Is.True);
                Assert.That(GraphicsEffectsSettings.DepthOfFieldEnabled, Is.False);
                Assert.That(GraphicsEffectsSettings.IntoxicationLensFxEnabled, Is.False);
                Assert.That(GraphicsEffectsSettings.AspectRatio43Enabled, Is.True);
                Assert.That(GraphicsEffectsSettings.BegottenModeEnabled, Is.False);
                Assert.That(menu.Page, Is.EqualTo(PauseMenuPage.Options));
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(motor.InputEnabled, Is.False);

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
                if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
                foreach (string key in keys)
                {
                    if (saved[key].HasValue) PlayerPrefs.SetInt(key, saved[key].Value);
                    else PlayerPrefs.DeleteKey(key);
                }

                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
            }
        }

        private void SelectOptionsRow(PauseMenuOptionsRow row)
        {
            for (int step = 0; step < (int)PauseMenuOptionsRow.Count && menu.SelectedOptionsRow != row; step++)
                menu.MoveSelection(1);
            Assert.That(menu.SelectedOptionsRow, Is.EqualTo(row));
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

        [UnityTest]
        public IEnumerator LevelCutscenes_DeferredLaunchClockReplayAndCleanup()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            var controller = uiObject.AddComponent<CutscenePlaybackController>();
            controller.Initialize(
                new PlayerRuntime(playerObject, motor, interactor, null),
                cameraFollow,
                hud,
                menu);
            controller.AutomaticPlayback = false;
            menu.BindCutscenes(controller);
            var playbacks = new List<TestCutscenePlayback>();
            const string cutsceneId = "test.warehouse.cutscene";
            try
            {
                Assert.That(controller.Catalog.Count, Is.Zero);
                Assert.That(menu.Open(), Is.True);
                for (int row = 0;
                     row < (int)PauseMenuOption.Count &&
                     menu.SelectedOption != PauseMenuOption.Cutscenes;
                     row++)
                {
                    menu.MoveSelection(1);
                }

                Assert.That(menu.SelectedOption, Is.EqualTo(PauseMenuOption.Cutscenes));
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.Page, Is.EqualTo(PauseMenuPage.Cutscenes),
                    "A level may expose its catalog before any authored cutscenes exist.");
                Assert.That(menu.TryStartCutscene("missing"), Is.False);
                yield return CaptureCutsceneUi("empty-list");

                controller.Catalog.Register(new CutsceneDefinition(
                    cutsceneId,
                    "Test warehouse cutscene",
                    () =>
                    {
                        var playback = new TestCutscenePlayback(camera, () =>
                            Time.timeScale == 0f && AudioListener.pause &&
                            !motor.InputEnabled && !interactor.InputEnabled &&
                            cameraFollow.PresentationSuspended && !hud.Visible);
                        playbacks.Add(playback);
                        return playback;
                    }));
                Assert.That(controller.Catalog.Count, Is.EqualTo(1));
                Assert.That(controller.Catalog[0], Is.Not.Null);
                Vector3 initialPlayerPosition = playerObject.transform.position;
                InputSystem.QueueStateEvent(keyboard,
                    new KeyboardState(Key.W, Key.E, Key.Space));
                Assert.That(menu.TryStartCutscene(cutsceneId), Is.True);
                Assert.That(menu.TryStartCutscene(cutsceneId), Is.False,
                    "A pending launch must not enqueue another playback.");
                Assert.That(menu.IsOpen, Is.True);
                Assert.That(controller.IsPlaying, Is.False);
                Assert.That(Time.timeScale, Is.Zero);
                yield return null;

                Assert.That(menu.IsOpen, Is.False);
                Assert.That(controller.IsPlaying, Is.True);
                Assert.That(controller.IsPlaybackPaused, Is.False,
                    "The menu's confirmation key must not pause the arriving cutscene.");
                Assert.That(controller.ElapsedSeconds, Is.Zero);
                Assert.That(controller.CurrentTimecode, Is.EqualTo("00:00.000"));
                Assert.That(playbacks[0].PrepareCount, Is.EqualTo(1));
                Assert.That(playbacks[0].LastSample, Is.Zero);
                Assert.That(playbacks[0].PreparedWhileOwned, Is.True);
                Assert.That(playbacks[0].SampledWhileOwned, Is.True);
                Assert.That(playerObject.transform.position, Is.EqualTo(initialPlayerPosition));
                Assert.That(PlayerDirectionalInput.ReadRaw(), Is.EqualTo(Vector2.zero));
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
                Assert.That(controller.TryPlay(cutsceneId), Is.False,
                    "A playing cutscene is the sole owner of playback.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;

                controller.Advance(12.35d);
                Assert.That(controller.ElapsedSeconds, Is.EqualTo(12.35d).Within(0.000001d));
                Assert.That(playbacks[playbacks.Count - 1].LastSample,
                    Is.EqualTo(controller.ElapsedSeconds));
                Assert.That(controller.CurrentTimecode, Is.EqualTo("00:12.350"));
                yield return CaptureCutsceneUi("playback");
                controller.TogglePause();
                Assert.That(controller.IsPlaybackPaused, Is.True);
                Assert.That(playbacks[playbacks.Count - 1].IsPaused, Is.True);
                controller.Advance(2d);
                Assert.That(controller.ElapsedSeconds, Is.EqualTo(12.35d).Within(0.000001d));
                Assert.That(controller.CurrentTimecode, Is.EqualTo("00:12.350"));
                Assert.That(playbacks[playbacks.Count - 1].LastSample,
                    Is.EqualTo(controller.ElapsedSeconds));

                controller.Restart();
                Assert.That(controller.IsPlaybackPaused, Is.False);
                Assert.That(playbacks[playbacks.Count - 1].IsPaused, Is.False);
                Assert.That(controller.ElapsedSeconds, Is.Zero);
                Assert.That(controller.CurrentTimecode, Is.EqualTo("00:00.000"));
                Assert.That(playbacks[playbacks.Count - 1].LastSample, Is.Zero);
                TestCutscenePlayback beforeReturn = playbacks[playbacks.Count - 1];
                controller.Advance(100d);
                Assert.That(controller.ElapsedSeconds, Is.EqualTo(30d));
                Assert.That(beforeReturn.LastSample, Is.EqualTo(30d));
                Assert.That(controller.CurrentTimecode, Is.EqualTo("00:30.000"),
                    "Completion keeps its exact terminal sample until the deferred return.");
                controller.ReturnToList();
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(motor.InputEnabled, Is.False);
                yield return null;
                Assert.That(controller.IsPlaying, Is.False);
                Assert.That(beforeReturn.StopCount, Is.EqualTo(1));
                Assert.That(menu.IsOpen, Is.True);
                Assert.That(menu.Page, Is.EqualTo(PauseMenuPage.Cutscenes));
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(motor.InputEnabled, Is.False);

                Assert.That(menu.TryStartCutscene(cutsceneId), Is.True);
                yield return null;
                Assert.That(controller.IsPlaying, Is.True);
                Assert.That(controller.ElapsedSeconds, Is.Zero);
                TestCutscenePlayback repeated = playbacks[playbacks.Count - 1];
                Assert.That(repeated, Is.Not.SameAs(beforeReturn));
                controller.Stop();
                Assert.That(repeated.StopCount, Is.EqualTo(1));
                AssertCutsceneStateRestored();
                Assert.That(controller.TryPlay("missing"), Is.False);

                Vector3 cameraPosition = camera.transform.position;
                Quaternion cameraRotation = camera.transform.rotation;
                float cameraFieldOfView = camera.fieldOfView;
                Assert.That(controller.TryPlay(cutsceneId), Is.True);
                controller.Advance(4d);
                TestCutscenePlayback beforeDisable = playbacks[playbacks.Count - 1];
                controller.enabled = false;
                Assert.That(controller.IsPlaying, Is.False);
                Assert.That(beforeDisable.StopCount, Is.EqualTo(1));
                AssertCutsceneStateRestored();
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(Quaternion.Angle(camera.transform.rotation, cameraRotation),
                    Is.LessThan(0.001f));
                Assert.That(camera.fieldOfView, Is.EqualTo(cameraFieldOfView));
                controller.Stop();
                Assert.That(beforeDisable.StopCount, Is.EqualTo(1),
                    "Repeated teardown must not stop authored content twice.");

                controller.enabled = true;
                var failed = new TestCutscenePlayback(camera, () =>
                    Time.timeScale == 0f && !motor.InputEnabled)
                {
                    FailPreparation = true
                };
                controller.Catalog.Register(new CutsceneDefinition(
                    "test.unavailable.cutscene", "Test unavailable cutscene", () => failed));
                Assert.That(controller.TryPlay("test.unavailable.cutscene"), Is.False);
                Assert.That(controller.IsPlaying, Is.False);
                Assert.That(failed.PreparedWhileOwned, Is.True);
                Assert.That(failed.StopCount, Is.EqualTo(1),
                    "Failed preparation still owns content cleanup.");
                AssertCutsceneStateRestored();
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(camera.fieldOfView, Is.EqualTo(cameraFieldOfView));
            }
            finally
            {
                controller.Stop();
                controller.enabled = false;
            }
        }

        private void AssertCutsceneStateRestored()
        {
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.enabled, Is.True);
            Assert.That(cameraFollow.PresentationSuspended, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
        }

        private sealed class TestCutscenePlayback : ICutscenePlayback
        {
            private readonly Camera camera;
            private readonly System.Func<bool> ownsPlayback;
            public double DurationSeconds => 30d;
            public int PrepareCount { get; private set; }
            public int StopCount { get; private set; }
            public double LastSample { get; private set; } = -1d;
            public bool PreparedWhileOwned { get; private set; }
            public bool SampledWhileOwned { get; private set; } = true;
            public bool IsPaused { get; private set; }
            public bool FailPreparation { get; set; }

            public TestCutscenePlayback(Camera camera, System.Func<bool> ownsPlayback)
            {
                this.camera = camera;
                this.ownsPlayback = ownsPlayback;
            }

            public void Prepare()
            {
                PrepareCount++;
                PreparedWhileOwned = ownsPlayback();
                if (FailPreparation)
                    throw new System.InvalidOperationException("Test cutscene preparation failed.");
            }

            public void Sample(double elapsedSeconds)
            {
                LastSample = elapsedSeconds;
                SampledWhileOwned &= ownsPlayback();
                camera.transform.SetPositionAndRotation(
                    new Vector3(6f + (float)elapsedSeconds, 3f, -8f),
                    Quaternion.Euler(8f, 35f, 0f));
                camera.fieldOfView = 42f;
            }

            public void Stop()
            {
                StopCount++;
            }

            public void SetPaused(bool paused)
            {
                IsPaused = paused;
            }
        }

        private static IEnumerator CaptureCutsceneUi(string name)
        {
            // The Game view includes IMGUI; Camera.Render cannot prove the overlay.
            System.IDisposable gameView = null;
            Texture2D frame = null;
            try
            {
#if UNITY_EDITOR
                System.Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType(
                    "GameViewResolution", System.Reflection.BindingFlags.NonPublic);
                gameView = (System.IDisposable)System.Activator.CreateInstance(sizeOwner, true);
                sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { 1280, 720 });
#endif
                yield return null;
                if (Application.isBatchMode) { yield return null; yield return null; }
                else yield return new WaitForEndOfFrame();
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                Assert.That(frame, Is.Not.Null,
                    "The cutscene UI capture requires a rendered framebuffer.");
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "..", "Captures", "CutsceneFoundation", name + ".png"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, frame.EncodeToPNG());
            }
            finally
            {
                if (frame != null) Object.Destroy(frame);
                gameView?.Dispose();
            }
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
