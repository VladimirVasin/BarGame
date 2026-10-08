using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly RaycastHit[] pistolAimHits = new RaycastHit[64];
        private bool pistolCursorOwned, pistolCursorVisible, pistolApplicationFocused = true, requirePistolAimRelease;
        private CursorLockMode pistolCursorLock;

        private void ReleaseFreePistolAim()
        {
            if (CameraFollow != null) CameraFollow.ClearFreeAim(this);
            if (!pistolCursorOwned) return;
            Cursor.lockState = pistolCursorLock;
            Cursor.visible = pistolCursorVisible;
            pistolCursorOwned = false;
        }

        private Vector3 PrepareFreePistolAim()
        {
            CameraFollow.PrepareFreeAimFrame(this);
            if (!pistolCursorOwned)
            {
                pistolCursorLock = Cursor.lockState;
                pistolCursorVisible = Cursor.visible;
                pistolCursorOwned = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Ray ray = CameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            float distance = CombatProjectilePool.MaximumDistance;
            int count = Physics.RaycastNonAlloc(ray, pistolAimHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (pistolAimHits[i].transform != null && !pistolAimHits[i].transform.IsChildOf(Hero.transform))
                    distance = Mathf.Min(distance, pistolAimHits[i].distance);
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
            bool freeAim = aim && !IsOpponentFocused && Hero.PistolBodyAvailable && CameraFollow.SetFreeAim(this, heroChest);
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
            bool step = IsOpponentFocused && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay);
            bool kick = IsOpponentFocused && GameInput.WasPressed(GameInputAction.CombatKick, GameInputContext.Gameplay);
            if (step || kick)
            {
                if (trigger) Hero.RejectPistolInput(step ? "step_requested" : "kick_requested");
                Hero.Pistol.CancelAction();
                if (step) Hero.TryStep(GameInput.ReadMovement());
                else Hero.TryKick();
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
                Color colour = Hero.Pistol.CanFire && Hero.PistolAimAligned ? RetroUiTheme.Text : RetroUiTheme.Muted;
                RetroUiTheme.FillRect(new Rect(point.x - 3f, point.y - 1f, 7f, 3f), RetroUiTheme.Ink);
                RetroUiTheme.FillRect(new Rect(point.x - 1f, point.y - 3f, 3f, 7f), RetroUiTheme.Ink);
                RetroUiTheme.FillRect(new Rect(point.x - 2f, point.y, 5f, 1f), colour);
                RetroUiTheme.FillRect(new Rect(point.x, point.y - 2f, 1f, 5f), colour);
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
    }
}
