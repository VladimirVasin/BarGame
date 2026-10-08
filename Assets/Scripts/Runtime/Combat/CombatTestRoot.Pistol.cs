using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly RaycastHit[] pistolAimHits = new RaycastHit[64];
        private bool pistolCursorOwned, pistolCursorVisible, pistolApplicationFocused = true, requirePistolAimRelease;
        private CursorLockMode pistolCursorLock;
        private const float CrosshairExpandSeconds = .035f, CrosshairReturnSeconds = .165f;
        private float pistolCrosshairElapsed = CrosshairExpandSeconds + CrosshairReturnSeconds;
        private float pistolCrosshairFrom;

        internal float PistolCrosshairExpansion
        {
            get
            {
                if (pistolCrosshairElapsed < CrosshairExpandSeconds)
                    return Mathf.Lerp(pistolCrosshairFrom, 4f,
                        Mathf.SmoothStep(0f, 1f, pistolCrosshairElapsed / CrosshairExpandSeconds));
                return 4f * (1f - Mathf.SmoothStep(0f, 1f,
                    (pistolCrosshairElapsed - CrosshairExpandSeconds) / CrosshairReturnSeconds));
            }
        }

        private void AdvancePistolCrosshair(float seconds) => pistolCrosshairElapsed = Mathf.Min(
            CrosshairExpandSeconds + CrosshairReturnSeconds, pistolCrosshairElapsed + seconds);

        private void PulsePistolCrosshair()
        {
            if (!Hero.IsFreePistolAiming || !CameraFollow.FreeAimActive) return;
            pistolCrosshairFrom = PistolCrosshairExpansion;
            pistolCrosshairElapsed = 0f;
        }

        private void ResetPistolCrosshair()
        {
            pistolCrosshairElapsed = CrosshairExpandSeconds + CrosshairReturnSeconds;
            pistolCrosshairFrom = 0f;
        }

        private void ReleaseFreePistolAim()
        {
            // A pause hides the HUD and freezes the live clock. Other handoffs
            // discard the impulse so a new aim lease cannot resurrect it.
            if (!GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused) ResetPistolCrosshair();
            if (CameraFollow != null) CameraFollow.ClearFreeAim(this);
            if (Player.Motor != null) Player.Motor.ClearMovementBasis(this);
            if (!pistolCursorOwned) return;
            Cursor.lockState = pistolCursorLock;
            Cursor.visible = pistolCursorVisible;
            pistolCursorOwned = false;
        }

        private Vector3 PrepareFreePistolAim()
        {
            CameraFollow.PrepareFreeAimFrame(this);
            Player.Motor.SetMovementBasis(this, CameraFollow.Camera.transform.forward);
            if (!pistolCursorOwned)
            {
                pistolCursorLock = Cursor.lockState;
                pistolCursorVisible = Cursor.visible;
                pistolCursorOwned = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Ray ray = CameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            return ResolveFreePistolAim(ray);
        }

        private Vector3 ResolveFreePistolAim(Ray ray)
        {
            float distance = CombatProjectilePool.MaximumDistance;
            int count = Physics.RaycastNonAlloc(ray, pistolAimHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (CombatProjectilePool.IsWorld(pistolAimHits[i].collider, Hero))
                    distance = Mathf.Min(distance, pistolAimHits[i].distance);
            // A standing motor capsule is not the surface visible under the
            // crosshair. Converge the actual muzzle on the same anatomy that
            // bullets hit, while allowing nearer world geometry to occlude it.
            if (Opponent?.Hurtboxes != null)
            {
                Opponent.Hurtboxes.Capture();
                if (Opponent.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(distance), 0f, ray.direction, out var hit))
                    distance *= hit.Fraction;
            }
            return ray.GetPoint(Mathf.Max(distance, Vector3.Dot(heroChest.position - ray.origin, ray.direction) + .5f));
        }

        public bool ReturnToWeapons()
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            return CombatTestStartService.ReturnToPreparation(HeroWeapon);
        }

        private bool UpdatePistolInput(bool held)
        {
            Hero.SetBlock(false);
            bool trigger = GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay);
            if (RoundFinished && Hero.State.IsDefeated)
            {
                if (trigger) Hero.RejectPistolInput("defeated");
                ReleaseFreePistolAim();
                Hero.SuspendPistolInput();
                requireAttackRelease |= held;
                return true;
            }
            bool aimHeld = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            if (!aimHeld) requirePistolAimRelease = false;
            bool aim = aimHeld && !requirePistolAimRelease;
            bool freeAim = aim && !IsOpponentFocused && Hero.PistolAimBodyAvailable && CameraFollow.SetFreeAim(this, heroChest);
            if (freeAim) Hero.SetPistolAim(true, PrepareFreePistolAim());
            else
            {
                ReleaseFreePistolAim();
                Hero.SetPistolAim(aim && IsOpponentFocused);
            }
            if (RoundFinished && roundEndFreeze > 0d)
            {
                // Contact freezes the bodies and trigger, not the winner's
                // held aim lease. Releasing it here switched the live camera
                // to chase for one frame on every later corpse impact.
                if (trigger) Hero.RejectPistolInput("round_freeze");
                Hero.CancelPendingPistolShot("round_freeze");
                requireAttackRelease |= held;
                return true;
            }
            if (GameInput.WasPressed(GameInputAction.CombatReload, GameInputContext.Gameplay)) Hero.TryReloadPistol();
            bool step = (IsOpponentFocused || freeAim) && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay);
            bool kick = IsOpponentFocused && GameInput.WasPressed(GameInputAction.CombatKick, GameInputContext.Gameplay);
            if (step || kick)
            {
                if (trigger) Hero.RejectPistolInput(step ? "step_requested" : "kick_requested");
                Hero.CancelPendingPistolShot(step ? "step_requested" : "kick_requested");
                if (step)
                {
                    Hero.Pistol.CancelReload();
                    Hero.TryStep(GameInput.ReadMovement());
                }
                else
                {
                    Hero.Pistol.CancelAction();
                    Hero.TryKick();
                }
                requireAttackRelease |= held;
            }
            else if (trigger)
            {
                if (requireAttackRelease) Hero.RejectPistolInput("awaiting_release");
                else if (!freeAim && PointerOverToolbar()) Hero.RejectPistolInput("pointer_over_toolbar");
                else Hero.RequestPistolShot();
            }
            return true;
        }

        private void DrawPistolHud(RetroUiCanvas canvas)
        {
            if (!Hero.IsPistol || Hero.Pistol == null) return;
            if (Hero.IsFreePistolAiming && CameraFollow.FreeAimActive && GameInput.CanRead(GameInputContext.Gameplay))
            {
                Vector3 centre = CameraFollow.Camera.ViewportToScreenPoint(new Vector3(.5f, .5f, 0f));
                Vector2 point = canvas.ScreenToLogical(new Vector2(centre.x, Screen.height - centre.y));
                Color colour = Hero.PistolBodyAvailable && Hero.Pistol.CanFire && Hero.PistolAimAligned ? RetroUiTheme.Text : RetroUiTheme.Muted;
                float spread = Mathf.Round(PistolCrosshairExpansion);
                RetroUiTheme.FillRect(new Rect(point.x - 1f, point.y - 1f, 3f, 3f), RetroUiTheme.Ink);
                DrawPistolCrosshairArms(point, spread, RetroUiTheme.Ink, 1f);
                DrawPistolCrosshairArms(point, spread, colour, 0f);
                RetroUiTheme.FillRect(new Rect(point.x, point.y, 1f, 1f), colour);
            }
            var rect = new Rect(14, 273, 170, 25);
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            string ammunition = LocalizationService.Get("combat.pistol.ammo") + " " + Hero.Pistol.Rounds + "/" +
                Hero.Pistol.Settings.MagazineCapacity;
            GUI.Label(new Rect(rect.x + 6f, rect.y + 1f, rect.width - 12f, 12f), ammunition, small);
            string status = Hero.Pistol.IsReloading ? LocalizationService.Get("combat.pistol.reloading") :
                Hero.Pistol.Rounds == 0 ? LocalizationService.Get("combat.pistol.empty") : LocalizationService.Get("combat.pistol.reload");
            GUI.Label(new Rect(rect.x + 6f, rect.y + 12f, rect.width - 12f, 12f), status, controls);
            if (Hero.Pistol.IsReloading)
                RetroUiTheme.FillRect(new Rect(rect.x + 6f, rect.y + rect.height - 2f,
                    (rect.width - 12f) * Hero.Pistol.ReloadProgress, 1f), RetroUiTheme.Text);
        }

        private static void DrawPistolCrosshairArms(Vector2 point, float spread, Color colour, float rim)
        {
            RetroUiTheme.FillRect(new Rect(point.x - 2f - spread - rim, point.y - rim, 2f + rim * 2f, 1f + rim * 2f), colour);
            RetroUiTheme.FillRect(new Rect(point.x + 1f + spread - rim, point.y - rim, 2f + rim * 2f, 1f + rim * 2f), colour);
            RetroUiTheme.FillRect(new Rect(point.x - rim, point.y - 2f - spread - rim, 1f + rim * 2f, 2f + rim * 2f), colour);
            RetroUiTheme.FillRect(new Rect(point.x - rim, point.y + 1f + spread - rim, 1f + rim * 2f, 2f + rim * 2f), colour);
        }
    }
}
