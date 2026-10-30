using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private bool requireAttackRelease, requireParryRelease;
        private bool requireMouseAttackRelease, requireMouseAimRelease;

        // Compatibility for retired capture fixtures; there is no charge presentation.
        public bool ChargeMeterVisible => false;

        private bool UpdateCombatInput()
        {
            UpdateCombatMouseOwnership();
            if (!pistolApplicationFocused || !GameInput.CanRead(GameInputContext.Gameplay))
            {
                if (Hero.IsFirearm && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.PauseMenu))
                    Hero.RejectPistolInput(pistolApplicationFocused ? "input_gate" : "application_focus");
                ReleaseFreePistolAim();
                Hero.SuspendPistolInput();
                Hero.CancelPendingKick("input_gate");
                Hero.SetBlock(false);
                Hero.State.CancelParry();
                requireAttackRelease |= GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
                requireParryRelease |= GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
                return false;
            }
            bool held = ReadCombatPointerAction(GameInputAction.MeleeAttack, true);
            bool parryHeld = ReadCombatPointerAction(GameInputAction.MeleeBlock, true);
            JournalAttackInput(held);
            if (!held) requireAttackRelease = false;
            if (!parryHeld) requireParryRelease = false;
            if (Hero.IsRestrained)
            {
                ReleaseFreePistolAim();
                Hero.SuspendPistolInput();
                Hero.SetBlock(false);
                Hero.State.CancelParry();
                requireAttackRelease |= held;
                requireParryRelease |= parryHeld;
                requirePistolAimRelease = true;
                return true;
            }
            if ((Mode == CombatTestMode.Standard || IsLaboratory) && GameInput.WasPressed(GameInputAction.CombatMode, GameInputContext.Gameplay))
            { SetSparring(!Sparring); return false; }
            if (GameCursor.GameplayMouseInputAllowed && GameInput.WasPressed(GameInputAction.CombatFocus, GameInputContext.Gameplay))
                SetOpponentFocus(!IsOpponentFocused);
            ReadWeaponSelectionInput();
            if (Hero.IsSwitchingWeapon)
            {
                ReleaseFreePistolAim(); Hero.SuspendPistolInput();
                Hero.SetBlock(false); Hero.State.CancelParry();
                requireAttackRelease |= held; requireParryRelease |= parryHeld;
                requirePistolAimRelease = true;
                return true;
            }
            if (Hero.IsFirearm) return UpdatePistolInput(held);
            Hero.SetBlock(parryHeld && !requireParryRelease && !PointerOverToolbar());
            if (ReadCombatPointerAction(GameInputAction.MeleeAttack, false))
            {
                if (!requireAttackRelease && !PointerOverToolbar() && !Hero.State.IsBlocking && !Hero.State.IsDefeated)
                    Hero.TryAttack();
                else duelJournal?.Record("input_rejected", actor: 1,
                    f0: GameLog.Field("reason", requireAttackRelease ? "AwaitingRelease" : "ParryOrUi"));
            }
            return true;
        }

        private void UpdateCombatMouseOwnership()
        {
            Mouse mouse = Mouse.current;
            bool attackHeld = mouse != null && mouse.leftButton.isPressed;
            bool aimHeld = mouse != null && mouse.rightButton.isPressed;
            if (!GameCursor.FreeCursorRequested)
            {
                if (!attackHeld) requireMouseAttackRelease = false;
                if (!aimHeld) requireMouseAimRelease = false;
                return;
            }

            // UI clicks never become combat commands on Alt release. Keep the
            // gamepad independent so its triggers still work with a free pointer.
            requireMouseAttackRelease |= attackHeld;
            requireMouseAimRelease |= aimHeld;
            Gamepad pad = Gamepad.current;
            if (pad == null || !pad.rightTrigger.isPressed)
            {
                Hero.CancelPendingPistolShot("free_cursor");
            }
            if (pad == null || !pad.leftTrigger.isPressed)
            {
                ReleaseFreePistolAim();
                Hero.SetPistolAim(false);
                Hero.SetBlock(false);
                Hero.State.CancelParry();
            }
        }

        private bool ReadCombatPointerAction(GameInputAction action, bool held)
        {
            bool requested = held ? GameInput.IsHeld(action, GameInputContext.Gameplay) :
                GameInput.WasPressed(action, GameInputContext.Gameplay);
            if (!requested) return false;
            bool attack = action == GameInputAction.MeleeAttack;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                var trigger = attack ? pad.rightTrigger : pad.leftTrigger;
                if (held ? trigger.isPressed : trigger.wasPressedThisFrame) return true;
            }
            return GameCursor.GameplayMouseInputAllowed &&
                !(attack ? requireMouseAttackRelease : requireMouseAimRelease);
        }

        private void ResetCombatAttackInput()
        {
            requireAttackRelease = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
            requireParryRelease = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
            Hero?.State.CancelParry();
        }

        private void OnApplicationFocus(bool focused)
        {
            pistolApplicationFocused = focused;
            roomTableau?.SetSoundPaused(!focused || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused);
            GameCursor.SetApplicationFocused(focused);
            if (IsInitialized)
                SetDuelFrozen(!focused || hitStopSubsteps > 0 || roundEndFreeze > 0d);
            AmmoSpawns?.SetInteractionEnabled(focused && isActiveAndEnabled && Hero != null && !Hero.State.IsDefeated && !Hero.IsRestrained);
            JournalApplicationFocus(focused);
            if (focused) return;
            ResetHudPresentation();
            ResetPistolCrosshair();
            requirePistolAimRelease = true;
            ReleaseFreePistolAim();
            Hero?.CancelPendingKick("focus_lost");
            Hero?.SuspendPistolInput();
            Hero?.SetBlock(false);
            Hero?.State.CancelParry();
            requireParryRelease = true;
            requireAttackRelease = true;
        }

        private void OnDisable()
        {
            if (DoorAmbush != null) DoorAmbush.enabled = false;
            if (Cutscenes != null)
            {
                Cutscenes.enabled = false;
                Cutscenes.Catalog.Clear();
                PauseMenu?.BindCutscenes(null);
            }
            CancelRoomAttackSequence();
            Hero?.CancelRestraint();
            ClearOpponentAttackSuppression();
            ClearRoomAttackCooldowns();
            ResetHudPresentation(closeDebug: true);
            UnsubscribeEndlessSpawnGate();
            SuspendWarehouseAudio();
            ReleaseWarehouseAcoustics();
            Hero?.ClearWarehouseTorsoLook();
            flashlightEquipment?.SetPresentationSuppressed(true);
            AmmoSpawns?.SetInteractionEnabled(false);
            ResetPistolCrosshair();
            ClearWeaponPools();
            HeadEffects?.ResetRound();
            BodyEffects?.ResetRound();
            ReleaseFreePistolAim();
            Hero?.SuspendPistolInput();
            Hero?.CancelPendingKick("disabled");
            CloseDuelJournal("disabled");
            ResetAllOpponentMovement();
            if (IsInitialized) SetDuelFrozen(false);
            Hero?.State.CancelParry();
            Opponent?.State.CancelParry();
            ResetCombatAttackInput();
            ClearFocusTracking();
        }

    }
}
