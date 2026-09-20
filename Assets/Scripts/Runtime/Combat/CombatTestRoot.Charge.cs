using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private bool attackInputOwned, requireAttackRelease, inputSuspended, releasedWhileSuspended;

        public bool ChargeMeterVisible => IsInitialized && Hero.State.IsCharging && !RoundFinished &&
            !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning;

        private bool UpdateCombatInput()
        {
            if (!GameInput.CanRead(GameInputContext.Gameplay))
            {
                inputSuspended = true;
                // Observe only the release while the menu owns input; never
                // advance charge or produce a gameplay command from that read.
                if (attackInputOwned && !GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu))
                    releasedWhileSuspended = true;
                return false;
            }
            bool held = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.Gameplay);
            if (inputSuspended)
            {
                if (attackInputOwned && (releasedWhileSuspended || !held))
                {
                    Hero.CancelCharge();
                    attackInputOwned = false;
                }
                if (!attackInputOwned && held) requireAttackRelease = true;
                inputSuspended = releasedWhileSuspended = false;
            }
            if (!held) requireAttackRelease = false;
            if (GameInput.WasPressed(GameInputAction.CombatReset, GameInputContext.Gameplay))
            { ResetRound(); return false; }
            if (GameInput.WasPressed(GameInputAction.CombatMode, GameInputContext.Gameplay))
            { SetSparring(!Sparring); return false; }

            bool blocking = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            Hero.SetBlock(blocking);
            bool stepping = !RoundFinished && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay);
            if (stepping) Hero.TryStep(GameInput.ReadMovement());
            if (blocking || stepping || RoundFinished)
            {
                Hero.CancelCharge();
                attackInputOwned = false;
                requireAttackRelease |= held;
            }
            else
            {
                if (attackInputOwned && !Hero.State.IsCharging && !Hero.State.HasBufferedCharge)
                {
                    // Damage interrupted the charge. A still-held button must
                    // not turn recovery into an automatic fresh swing.
                    attackInputOwned = false;
                    requireAttackRelease |= held;
                }
                if (!requireAttackRelease && !PointerOverToolbar() &&
                    GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay))
                    attackInputOwned = Hero.RequestCharge();
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

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            if (Hero != null) Hero.CancelCharge();
            attackInputOwned = false;
            requireAttackRelease = true;
        }

        private void OnDisable()
        {
            if (Hero != null) Hero.CancelCharge();
            if (Opponent != null) Opponent.CancelCharge();
            ResetChargeInput();
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
