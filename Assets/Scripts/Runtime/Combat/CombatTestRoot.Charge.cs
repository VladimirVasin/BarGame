using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private bool attackInputOwned, requireAttackRelease, inputSuspended, releasedWhileSuspended;
        private bool requireMouseAttackRelease, requireMouseAimRelease;

        public bool ChargeMeterVisible => isActiveAndEnabled && IsInitialized && Hero.State.IsCharging &&
            !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning;

        private bool UpdateCombatInput()
        {
            UpdateCombatMouseOwnership();
            if (!pistolApplicationFocused || !GameInput.CanRead(GameInputContext.Gameplay))
            {
                if (Hero.IsFirearm && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.PauseMenu))
                    Hero.RejectPistolInput(pistolApplicationFocused ? "input_gate" : "application_focus");
                ReleaseFreePistolAim();
                Hero?.SuspendPistolInput();
                Hero?.CancelPendingKick("input_gate");
                inputSuspended = true;
                // Observe only the release while the menu owns input; never
                // advance charge or produce a gameplay command from that read.
                if (attackInputOwned && !GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu))
                    releasedWhileSuspended = true;
                return false;
            }
            bool held = ReadCombatPointerAction(GameInputAction.MeleeAttack, true);
            JournalAttackInput(held);
            if (inputSuspended)
            {
                if (attackInputOwned && (releasedWhileSuspended || !held))
                {
                    CancelHeldHeroCharge();
                    attackInputOwned = false;
                }
                if (!attackInputOwned && held) requireAttackRelease = true;
                inputSuspended = releasedWhileSuspended = false;
            }
            if (!held) requireAttackRelease = false;
            if (Hero.IsRestrained)
            {
                ReleaseFreePistolAim();
                Hero.SuspendPistolInput();
                Hero.SetBlock(false);
                CancelHeldHeroCharge();
                attackInputOwned = false;
                requireAttackRelease |= held;
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
                Hero.SetBlock(false); CancelHeldHeroCharge();
                attackInputOwned = false; requireAttackRelease |= held;
                requirePistolAimRelease = true;
                return true;
            }
            if (Hero.IsFirearm) return UpdatePistolInput(held);
            bool blocking = ReadCombatPointerAction(GameInputAction.MeleeBlock, true);
            Hero.SetBlock(blocking);
            bool stepping = !RoundFinished && IsOpponentFocused && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay);
            bool kicking = !RoundFinished && IsOpponentFocused && GameInput.WasPressed(GameInputAction.CombatKick, GameInputContext.Gameplay);
            bool kickAccepted = kicking && Hero.TryKick();
            if (stepping)
            {
                Hero.CancelPendingKick("step_requested");
                Hero.TryStep(GameInput.ReadMovement());
            }
            if (blocking || stepping || kickAccepted || Hero.State.IsDefeated)
            {
                CancelHeldHeroCharge();
                attackInputOwned = false;
                requireAttackRelease |= held;
            }
            else
            {
                if (attackInputOwned && !Hero.State.IsCharging && !Hero.State.HasBufferedCharge)
                {
                    // Damage interrupted the charge. The button no longer owns a
                    // swing; only a new press starts one, so nothing auto-fires and
                    // nothing has to be released first.
                    attackInputOwned = false;
                }
                if (!requireAttackRelease && !PointerOverToolbar() &&
                    ReadCombatPointerAction(GameInputAction.MeleeAttack, false))
                    attackInputOwned = Hero.RequestCharge();
                else if (duelJournal != null && ReadCombatPointerAction(GameInputAction.MeleeAttack, false))
                    duelJournal.Record("input_rejected", actor: 1,
                        f0: GameLog.Field("reason", requireAttackRelease ? "AwaitingRelease" : "PointerOverToolbar"));
                if (attackInputOwned && !held)
                {
                    Hero.ReleaseCharge();
                    attackInputOwned = false;
                }
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
                CancelHeldHeroCharge();
                attackInputOwned = false;
                Hero.CancelPendingPistolShot("free_cursor");
            }
            if (pad == null || !pad.leftTrigger.isPressed)
            {
                ReleaseFreePistolAim();
                Hero.SetPistolAim(false);
                Hero.SetBlock(false);
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

        private void ResetChargeInput()
        {
            attackInputOwned = inputSuspended = releasedWhileSuspended = false;
            requireAttackRelease = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
        }

        private void CancelHeldHeroCharge()
        {
            if (Hero != null && (Hero.State.IsCharging || Hero.State.HasBufferedCharge)) Hero.CancelCharge();
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
            CancelHeldHeroCharge();
            attackInputOwned = false;
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
            CancelHeldHeroCharge();
            if (Opponent != null && (Opponent.State.IsCharging || Opponent.State.HasBufferedCharge)) Opponent.CancelCharge();
            ResetChargeInput();
            ClearFocusTracking();
        }

        private void DrawChargeMeter()
        {
            if (!ChargeMeterVisible) return;
            var rect = new Rect(14, 282, 138, 16);
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 1f, 31f, 13f), LocalizationService.Get("combat.charge"), small);
            var track = new Rect(rect.x + 39f, rect.y + 7f, rect.width - 46f, 3f);
            RetroUiTheme.FillRect(track, RetroUiTheme.Shadow);
            RetroUiTheme.FillRect(new Rect(track.x, track.y, track.width * Hero.State.Charge01, track.height),
                Hero.State.Charge01 >= .999f ? RetroUiTheme.AccentPale : RetroUiTheme.Accent);
            // The notch shows the strength this actor can afford, including
            // a partial limit; it is separate from the growing charge fill.
            float limitX = track.x + (track.width - 1f) * Hero.State.ChargeLimit01;
            RetroUiTheme.FillRect(new Rect(limitX, track.y - 1f, 1f, 5f), RetroUiTheme.Muted);
        }
    }
}
