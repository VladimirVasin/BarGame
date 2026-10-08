using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly RaycastHit[] focusVisibilityHits = new RaycastHit[32];

        public bool IsOpponentFocused => isActiveAndEnabled && IsInitialized &&
            Hero != null && Hero.CombatFocused && !RoundFinished && CameraFollow.TargetLockActive;
        public bool FocusMarkerVisible => TryGetFocusMarkerScreenPosition(out _);

        /// <summary>Only the hero leaves focus; the opponent and duel retain their live state.</summary>
        public bool SetOpponentFocus(bool focused)
        {
            if (!isActiveAndEnabled || !IsInitialized || RoundFinished ||
                !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            if (focused == IsOpponentFocused) return true;
            if (focused && !TryFocusOpponent()) return false;
            if (!focused)
            {
                ClearFocusTracking();
                Hero.SetCombatFocused(false);
            }
            // A held mouse button may not launch or release an old charge across the handoff.
            ResetChargeInput();
            duelJournal?.Record("target_focus", actor: 1, f0: GameLog.Field("enabled", focused));
            return true;
        }

        private bool TryFocusOpponent()
        {
            ReleaseFreePistolAim();
            if (!CameraFollow.SetTargetLock(this, opponentObject.transform, opponentChest, heroChest, Hero.IsPistol)) return false;
            if (!Player.Motor.SetMovementTarget(this, opponentChest, true))
            {
                CameraFollow.ClearTargetLock(this);
                return false;
            }
            Player.Motor.SetMovementTargetFrozen(this, hitStopSubsteps > 0 || roundEndFreeze > 0d);
            Hero.SetCombatFocused(true);
            return true;
        }

        private void ClearFocusTracking(bool preserveCameraPose = false)
        {
            if (CameraFollow != null) CameraFollow.ClearTargetLock(this, preserveCameraPose);
            if (Player.Motor != null) Player.Motor.ClearMovementTarget(this);
        }

        /// <summary>Physical screen coordinates, bottom-left origin, from the live chest pose.</summary>
        public bool TryGetFocusMarkerScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;
            if (!IsOpponentFocused || opponentChest == null ||
                !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            Camera camera = CameraFollow.Camera;
            Vector3 screen = camera.WorldToScreenPoint(opponentChest.position);
            if (screen.z <= 0f || !camera.pixelRect.Contains(new Vector2(screen.x, screen.y))) return false;
            Vector3 ray = opponentChest.position - camera.transform.position;
            int count = Physics.RaycastNonAlloc(camera.transform.position, ray.normalized, focusVisibilityHits,
                ray.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == focusVisibilityHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = focusVisibilityHits[i].transform;
                if (hit != null && !hit.IsChildOf(Hero.transform) && !hit.IsChildOf(Opponent.transform)) return false;
            }
            screenPosition = new Vector2(screen.x, screen.y);
            return true;
        }

        private void DrawFocusMarker(RetroUiCanvas canvas)
        {
            if (!TryGetFocusMarkerScreenPosition(out Vector2 screen)) return;
            Vector2 point = canvas.ScreenToLogical(new Vector2(screen.x, Screen.height - screen.y));
            point = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
            // A bone-white centre and charcoal rim remain readable on either body or background.
            RetroUiTheme.FillRect(new Rect(point.x - 2f, point.y - 1f, 5f, 3f), RetroUiTheme.Ink);
            RetroUiTheme.FillRect(new Rect(point.x - 1f, point.y - 2f, 3f, 5f), RetroUiTheme.Ink);
            RetroUiTheme.FillRect(new Rect(point.x - 1f, point.y - 1f, 3f, 3f), RetroUiTheme.Text);
        }
    }
}
