using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private bool attackInputOwned, requireAttackRelease, inputSuspended, releasedWhileSuspended;

        public bool ChargeMeterVisible => IsOpponentFocused && Hero.State.IsCharging &&
            !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning;

        private bool UpdateCombatInput()
        {
            if (Hero.IsFirearm && !pistolApplicationFocused || !GameInput.CanRead(GameInputContext.Gameplay))
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
            bool held = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.Gameplay);
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
            if (GameInput.WasPressed(GameInputAction.CombatMode, GameInputContext.Gameplay))
            { SetSparring(!Sparring); return false; }
            if (GameInput.WasPressed(GameInputAction.CombatFocus, GameInputContext.Gameplay))
                SetOpponentFocus(!IsOpponentFocused);
            if (Hero.IsFirearm) return UpdatePistolInput(held);
            if (!IsOpponentFocused)
            {
                // Free movement leaves the same live duel clock and vulnerable body running.
                Hero.SetBlock(false);
                CancelHeldHeroCharge();
                attackInputOwned = false;
                requireAttackRelease |= held;
                return true;
            }

            bool blocking = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            Hero.SetBlock(blocking);
            bool stepping = !RoundFinished && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay);
            bool kicking = !RoundFinished && GameInput.WasPressed(GameInputAction.CombatKick, GameInputContext.Gameplay);
            bool kickAccepted = kicking && Hero.TryKick();
            if (stepping)
            {
                Hero.CancelPendingKick("step_requested");
                Hero.TryStep(GameInput.ReadMovement());
            }
            if (blocking || stepping || kickAccepted || RoundFinished)
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
                    GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay))
                    attackInputOwned = Hero.RequestCharge();
                else if (duelJournal != null && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay))
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
            JournalApplicationFocus(focused);
            if (focused) return;
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
            ResetPistolCrosshair();
            Projectiles?.Clear();
            Casings?.Clear();
            HeadEffects?.ResetRound();
            ReleaseFreePistolAim();
            Hero?.SuspendPistolInput();
            Hero?.CancelPendingKick("disabled");
            CloseDuelJournal("disabled");
            ResetOpponentMovement();
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
