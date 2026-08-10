using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class InventoryPlayModeTests
    {
        private readonly List<InventoryController> suspendedInventories =
            new List<InventoryController>();
        private readonly List<PauseMenuController> suspendedPauseMenus =
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
        private InventoryController inventory;
        private PauseMenuController pauseMenu;
        private GameTimeRuntime gameTimeRuntime;
        private bool ownsGameTimeRuntime;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SuspendExistingControllers();
            GameSessionState.BeginNewGame();
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0.75f;

            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();

            playerObject = new GameObject("Inventory Test Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor = playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Inventory Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraFollow = cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(camera, playerObject.transform, true);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject("Inventory Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            inventory = uiObject.AddComponent<InventoryController>();
            inventory.Initialize(
                new PlayerRuntime(
                    playerObject,
                    motor,
                    interactor,
                    null),
                cameraFollow,
                hud);
            pauseMenu = uiObject.AddComponent<PauseMenuController>();
            pauseMenu.Initialize(
                new PlayerRuntime(
                    playerObject,
                    motor,
                    interactor,
                    null),
                cameraFollow,
                hud);
            gameTimeRuntime = Object.FindAnyObjectByType<GameTimeRuntime>();
            if (gameTimeRuntime == null)
            {
                gameTimeRuntime =
                    BarPromenadeRuntimeBootstrap
                        .EnsureGameTimeRuntimeInstalled();
                ownsGameTimeRuntime = true;
            }

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (inventory != null)
            {
                inventory.enabled = false;
            }

            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(playerObject);
            if (ownsGameTimeRuntime && gameTimeRuntime != null)
            {
                Destroy(gameTimeRuntime.gameObject);
            }

            gameTimeRuntime = null;
            ownsGameTimeRuntime = false;
            Time.timeScale = previousTimeScale;
            inputFixture?.TearDown();
            inputFixture = null;
            RestoreExistingControllers();
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator IKey_OpensAndEscapeRestoresExactGameplayState()
        {
            inputFixture.Press(keyboard.iKey, queueEventOnly: true);
            yield return null;

            Assert.That(inventory.IsOpen, Is.True);
            Assert.That(InventoryController.IsAnyOpen, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);
            Assert.That(inventory.View.PreviewRenderer, Is.Not.Null);
            Assert.That(
                inventory.View.PreviewRenderer.IsRendering,
                Is.True);
            Assert.That(
                inventory.View.PreviewRenderer.CurrentItemId,
                Is.EqualTo(InventoryItemId.ApartmentKeys));
            Assert.That(
                inventory.View.PreviewRenderer.Texture,
                Is.Not.Null);

            inputFixture.Release(keyboard.iKey, queueEventOnly: true);
            yield return null;
            inputFixture.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;

            Assert.That(inventory.IsOpen, Is.False);
            Assert.That(InventoryController.IsAnyOpen, Is.False);
            Assert.That(pauseMenu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            Assert.That(
                inventory.View.PreviewRenderer.IsRendering,
                Is.False);
        }

        [UnityTest]
        public IEnumerator Open_ShowsCurrentGameTimeAndFreezesIt()
        {
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(394f);
            double gameTimeBeforeOpen =
                GameSessionState.GameTimeOfDayMinutes;
            int hungerBeforeOpen = GameSessionState.HungerLevel;
            int fatigueBeforeOpen = GameSessionState.FatigueLevel;
            Assert.That(hungerBeforeOpen, Is.GreaterThan(0));
            Assert.That(fatigueBeforeOpen, Is.GreaterThan(0));

            Assert.That(inventory.Open(), Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(
                inventory.View.CurrentGameTimeText,
                Is.EqualTo("12:34"));

            yield return null;

            Assert.That(
                inventory.View.CurrentGameTimeText,
                Is.EqualTo("12:34"));
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(gameTimeBeforeOpen));
            Assert.That(
                GameSessionState.HungerLevel,
                Is.EqualTo(hungerBeforeOpen));
            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(fatigueBeforeOpen));
        }

        [UnityTest]
        public IEnumerator SelectionAndExamine_UsePureMenuState()
        {
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.ChickenEgg),
                Is.True);
            Assert.That(inventory.Open(), Is.True);

            Assert.That(inventory.SelectedItemIndex, Is.Zero);
            Assert.That(inventory.MoveSelection(1), Is.True);
            Assert.That(inventory.SelectedItemIndex, Is.EqualTo(1));
            Assert.That(
                inventory.View.PreviewRenderer.CurrentItemId,
                Is.EqualTo(InventoryItemId.Lighter));
            Assert.That(inventory.MoveSelection(1), Is.True);
            Assert.That(inventory.SelectedItemIndex, Is.EqualTo(2));
            Assert.That(
                inventory.View.PreviewRenderer.CurrentItemId,
                Is.EqualTo(InventoryItemId.ChickenEgg));
            Assert.That(inventory.ExamineSelected(), Is.True);
            Assert.That(inventory.IsExamining, Is.True);
            Assert.That(
                inventory.View.PreviewRenderer.IsRendering,
                Is.True);
            Assert.That(inventory.Cancel(), Is.True);
            Assert.That(inventory.IsExamining, Is.False);
            Assert.That(inventory.IsOpen, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UKey_DrinksAtZeroStressAndKeepsMenuOpen()
        {
            Assert.That(GameSessionState.StressLevel, Is.Zero);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.VodkaBottle),
                Is.True);
            Assert.That(inventory.Open(), Is.True);
            Assert.That(inventory.MoveSelection(1), Is.True);
            Assert.That(inventory.MoveSelection(1), Is.True);
            Assert.That(
                inventory.SelectedStack.ItemId,
                Is.EqualTo(InventoryItemId.VodkaBottle));
            Assert.That(inventory.CanUseSelected, Is.True);

            yield return null;
            inputFixture.Press(keyboard.uKey, queueEventOnly: true);
            yield return null;

            Assert.That(inventory.IsOpen, Is.True);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.VodkaBottle),
                Is.Zero);
            Assert.That(GameSessionState.StressLevel, Is.Zero);
            Assert.That(GameSessionState.IntoxicationLevel, Is.GreaterThan(0));
            Assert.That(inventory.SelectedItemIndex, Is.EqualTo(1));
            Assert.That(
                inventory.View.PreviewRenderer.CurrentItemId,
                Is.EqualTo(InventoryItemId.Lighter));
            Assert.That(inventory.UseFeedbackSucceeded, Is.True);
            Assert.That(
                inventory.UseFeedbackMessage,
                Is.Not.Null.And.Not.Empty);

            inputFixture.Release(keyboard.uKey, queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Preview_RendersVisiblePixelsAndRotatesWhilePaused()
        {
            Assert.That(inventory.Open(), Is.True);
            InventoryItemPreviewRenderer preview =
                inventory.View.PreviewRenderer;
            Assert.That(preview.PreviewCamera, Is.Not.Null);
            Assert.That(preview.Texture, Is.TypeOf<RenderTexture>());
            Assert.That(preview.ModelRoot, Is.Not.Null);
            float rotationBefore = preview.RotationDegrees;

            yield return null;
            yield return null;

            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(
                preview.RotationDegrees,
                Is.GreaterThan(rotationBefore));

            RenderTexture target = (RenderTexture)preview.Texture;
            preview.PreviewCamera.Render();
            var readback = new Texture2D(
                target.width,
                target.height,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0,
                    false);
                readback.Apply(false, false);
                RenderTexture.active = previous;

                Color32[] pixels = readback.GetPixels32();
                int visiblePixels = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    if (pixel.a > 32 &&
                        pixel.r + pixel.g + pixel.b > 30)
                    {
                        visiblePixels++;
                    }
                }

                Assert.That(
                    visiblePixels,
                    Is.GreaterThan(24),
                    "The live 3D preview must render visible model pixels.");
            }
            finally
            {
                Object.Destroy(readback);
            }
        }

        [UnityTest]
        public IEnumerator ExistingModalAndDisabledMotor_BlockOpening()
        {
            var otherLock = new BarMinigameModalLock();
            Assert.That(
                otherLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.True);
            Assert.That(inventory.Open(), Is.False);
            Assert.That(otherLock.Restore(), Is.True);

            motor.SetInputEnabled(false);
            Assert.That(inventory.Open(), Is.False);
            motor.SetInputEnabled(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableWhileOpen_RestoresTimeAndModalState()
        {
            Assert.That(inventory.Open(), Is.True);

            inventory.enabled = false;

            Assert.That(inventory.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.75f));
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(
                inventory.View.PreviewRenderer.IsRendering,
                Is.False);
            yield return null;
        }

        private void SuspendExistingControllers()
        {
            InventoryController[] inventories =
                Object.FindObjectsByType<InventoryController>(
                    FindObjectsInactive.Exclude);
            for (int index = 0; index < inventories.Length; index++)
            {
                suspendedInventories.Add(inventories[index]);
                inventories[index].enabled = false;
            }

            PauseMenuController[] pauseMenus =
                Object.FindObjectsByType<PauseMenuController>(
                    FindObjectsInactive.Exclude);
            for (int index = 0; index < pauseMenus.Length; index++)
            {
                suspendedPauseMenus.Add(pauseMenus[index]);
                pauseMenus[index].enabled = false;
            }
        }

        private void RestoreExistingControllers()
        {
            for (int index = 0; index < suspendedInventories.Count; index++)
            {
                if (suspendedInventories[index] != null)
                {
                    suspendedInventories[index].enabled = true;
                }
            }

            for (int index = 0; index < suspendedPauseMenus.Count; index++)
            {
                if (suspendedPauseMenus[index] != null)
                {
                    suspendedPauseMenus[index].enabled = true;
                }
            }

            suspendedInventories.Clear();
            suspendedPauseMenus.Clear();
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
