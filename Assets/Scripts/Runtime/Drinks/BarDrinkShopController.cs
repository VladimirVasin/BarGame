using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarDrinkShopController : MonoBehaviour
    {
        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private BarDrinkShopView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private int inputUnlockFrame;

        public bool IsOpen { get; private set; }
        public int SelectedIndex { get; private set; }
        public string FeedbackKey { get; private set; } = string.Empty;
        public IReadOnlyList<BarDrinkOffer> Offers =>
            BarDrinkCatalog.Offers;
        public int CashBalance => GameSessionState.CashBalance;
        public int IntoxicationLevel =>
            GameSessionState.IntoxicationLevel;
        public BarDrinkOffer SelectedOffer =>
            Offers.Count > 0
                ? Offers[Mathf.Clamp(
                    SelectedIndex,
                    0,
                    Offers.Count - 1)]
                : default;

        public void Initialize(
            BarDrinkShopView shopView,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow)
        {
            Close();
            view = shopView;
            hud = intoxicationHud;
            cameraFollow = follow;
            view?.Initialize(this);
        }

        public bool Open(PlayerInteractor interactor)
        {
            if (IsOpen ||
                interactor == null ||
                Offers.Count == 0 ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            SelectedIndex = 0;
            FeedbackKey = string.Empty;
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool Select(int index)
        {
            if (!IsOpen ||
                index < 0 ||
                index >= Offers.Count)
            {
                return false;
            }

            bool changed = SelectedIndex != index;
            SelectedIndex = index;
            FeedbackKey = string.Empty;
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return true;
        }

        public bool MoveSelection(int direction)
        {
            if (!IsOpen ||
                Offers.Count == 0 ||
                direction == 0)
            {
                return false;
            }

            int offset = Math.Sign(direction);
            int nextIndex =
                (SelectedIndex + offset + Offers.Count) %
                Offers.Count;
            return Select(nextIndex);
        }

        public bool ConfirmSelection()
        {
            if (!IsOpen || Offers.Count == 0)
            {
                return false;
            }

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(
                    SelectedOffer.DrinkId);
            if (!result.Succeeded)
            {
                FeedbackKey = GetFailureKey(result.Status);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            RetroAudio.Play(RetroSfxId.DrinkGulp);
            RetroAudio.Play(RetroSfxId.UiConfirm);
            Close();
            return true;
        }

        public DrinkPurchaseResult PreviewSelection()
        {
            BarDrinkOffer offer = SelectedOffer;
            return DrinkPurchaseRules.Evaluate(
                offer.DrinkId,
                GameSessionState.CashBalance,
                GameSessionState.IntoxicationLevel,
                GameSessionState.LastAlcoholicDrink,
                GameSessionState.DrinksConsumed);
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            RetroAudio.Play(RetroSfxId.UiCancel);
            Close();
        }

        public void Close()
        {
            IsOpen = false;
            FeedbackKey = string.Empty;
            modalLock.Restore();
        }

        private void Update()
        {
            if (!IsOpen ||
                Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Cancel();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
                return;
            }

            if (IsConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame ||
                    keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.sKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return 0;
            }

            if (gamepad.dpad.up.wasPressedThisFrame ||
                gamepad.dpad.left.wasPressedThisFrame ||
                gamepad.leftStick.up.wasPressedThisFrame ||
                gamepad.leftStick.left.wasPressedThisFrame)
            {
                return -1;
            }

            if (gamepad.dpad.down.wasPressedThisFrame ||
                gamepad.dpad.right.wasPressedThisFrame ||
                gamepad.leftStick.down.wasPressedThisFrame ||
                gamepad.leftStick.right.wasPressedThisFrame)
            {
                return 1;
            }

            return 0;
        }

        private static bool IsConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static string GetFailureKey(
            DrinkPurchaseStatus status)
        {
            switch (status)
            {
                case DrinkPurchaseStatus.InsufficientFunds:
                    return
                        "drink_shop.failure.insufficient_funds";
                case DrinkPurchaseStatus.MaximumIntoxication:
                    return
                        "drink_shop.failure.maximum_intoxication";
                case DrinkPurchaseStatus.NotOffered:
                default:
                    return "drink_shop.failure.not_offered";
            }
        }
    }
}
