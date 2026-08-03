using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    public sealed class InventoryController : MonoBehaviour
    {
        public const int GridColumns = 5;

        private static InventoryController activeController;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly InventoryMenuModel model =
            new InventoryMenuModel();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private Func<bool> additionalCanOpen;
        private float previousTimeScale = 1f;
        private int inputUnlockFrame;
        private bool ownsTimeState;

        public static bool IsAnyOpen =>
            activeController != null && activeController.IsOpen;
        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsExamining => model.IsExamining;
        public int SelectedItemIndex => model.SelectedItemIndex;
        public InventoryView View { get; private set; }

        public bool HasSelection =>
            SelectedItemIndex >= 0 &&
            SelectedItemIndex < GameSessionState.InventoryItems.Count;

        public InventoryItemStack SelectedStack =>
            HasSelection
                ? GameSessionState.InventoryItems[SelectedItemIndex]
                : default;

        public InventoryItemDefinition SelectedDefinition =>
            HasSelection
                ? InventoryItemCatalog.Get(SelectedStack.ItemId)
                : default;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeController = null;
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            Func<bool> canOpen = null)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The inventory requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The inventory is already initialized.");
            }

            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            additionalCanOpen = canOpen;
            View = GetComponent<InventoryView>();
            if (View == null)
            {
                View = gameObject.AddComponent<InventoryView>();
            }

            View.Initialize(this);
            IsInitialized = true;
        }

        public bool Open()
        {
            if (!CanOpen() ||
                !modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            previousTimeScale = Time.timeScale;
            ownsTimeState = true;
            activeController = this;
            model.Open(GameSessionState.InventoryItems.Count);
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            View.RefreshPreview();
            Time.timeScale = 0f;
            RetroAudio.Play(RetroSfxId.MapOpen);
            GameLog.Info(
                "inventory",
                "opened",
                GameLog.Field("scene", gameObject.scene.name),
                GameLog.Field(
                    "item_stack_count",
                    GameSessionState.InventoryItems.Count));
            return true;
        }

        public bool Close()
        {
            return Close("user", true);
        }

        public bool SelectItem(int index)
        {
            if (!IsOpen || index == SelectedItemIndex ||
                !model.SelectItem(
                    index,
                    GameSessionState.InventoryItems.Count))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            View.RefreshPreview();
            return true;
        }

        public bool MoveSelection(int delta)
        {
            if (!IsOpen || IsExamining ||
                !model.MoveSelection(
                    delta,
                    GameSessionState.InventoryItems.Count))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            View.RefreshPreview();
            return true;
        }

        public bool ExamineSelected()
        {
            if (!IsOpen ||
                !model.BeginExamine(
                    GameSessionState.InventoryItems.Count))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool Cancel()
        {
            if (!IsOpen)
            {
                return false;
            }

            if (model.CancelExamine())
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
                return true;
            }

            return Close();
        }

        private bool CanOpen()
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   !IsOpen &&
                   !ownsTimeState &&
                   activeController == null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning &&
                   player.Motor != null &&
                   player.Motor.InputEnabled &&
                   player.Interactor.InputEnabled &&
                   (additionalCanOpen == null || additionalCanOpen());
        }

        private void Update()
        {
            if (!IsOpen)
            {
                if (WasInventoryTogglePressed())
                {
                    Open();
                }

                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                Close("transition", false);
                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasInventoryTogglePressed())
            {
                Close();
                return;
            }

            if (WasCancelPressed())
            {
                Cancel();
                return;
            }

            if (IsExamining)
            {
                if (WasConfirmPressed())
                {
                    Cancel();
                }

                return;
            }

            int delta = ReadSelectionDelta();
            if (delta != 0)
            {
                MoveSelection(delta);
            }

            if (WasConfirmPressed())
            {
                ExamineSelected();
            }
        }

        private bool Close(string reason, bool playSound)
        {
            if (!IsOpen && !ownsTimeState)
            {
                return false;
            }

            if (ownsTimeState)
            {
                Time.timeScale = previousTimeScale;
                ownsTimeState = false;
            }

            modalLock.Restore();
            IsOpen = false;
            model.CancelExamine();
            View?.HidePreview();
            if (activeController == this)
            {
                activeController = null;
            }

            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            GameLog.Info(
                "inventory",
                "closed",
                GameLog.Field("reason", reason));
            return true;
        }

        private void OnDisable()
        {
            Close("disabled", false);
        }

        private void OnDestroy()
        {
            Close("destroyed", false);
        }

        private static bool WasInventoryTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonNorth.wasPressedThisFrame;
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame)
                {
                    return 1;
                }

                if (keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame)
                {
                    return -GridColumns;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame ||
                    keyboard.sKey.wasPressedThisFrame)
                {
                    return GridColumns;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    return 1;
                }

                if (gamepad.dpad.up.wasPressedThisFrame)
                {
                    return -GridColumns;
                }

                if (gamepad.dpad.down.wasPressedThisFrame)
                {
                    return GridColumns;
                }
            }

            return 0;
        }
    }
}
