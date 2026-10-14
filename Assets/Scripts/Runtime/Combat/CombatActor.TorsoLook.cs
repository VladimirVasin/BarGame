using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatTestRoot torsoLookArena;
        private PlayerCameraFollow torsoLookCamera;

        // This presentation belongs only to the warehouse hero. Pausing keeps
        // the accepted pose; authored actions and physical recovery own the rig.
        internal bool CanPresentTorsoLook => hero != null && torsoLookArena != null &&
            torsoLookArena.isActiveAndEnabled && isActiveAndEnabled && IsAvailable &&
            CanAttemptBodyAction && !HasWeaponSwitchRequest && reaction == null &&
            !(Firearm?.ReloadPending ?? false) && !(ImpactMotion?.IsActive ?? false) &&
            !(footwork?.RecoveryEpisodeActive ?? false) && !State.IsBlocking &&
            (State.Phase == MeleePhase.Ready ||
                State.Phase == MeleePhase.Step && IsFirearm && (Firearm?.AimRequested ?? false));

        internal void BindWarehouseTorsoLook(CombatTestRoot arena)
        {
            ClearWarehouseTorsoLook();
            torsoLookArena = arena;
            torsoLookCamera = arena != null ? arena.CameraFollow : null;
        }

        internal void ClearWarehouseTorsoLook() => hero?.ClearTorsoLook(this);

        private void AdvanceWarehouseTorsoLook(float seconds)
        {
            if (hero == null || torsoLookArena == null) return;
            if (!CanPresentTorsoLook)
            {
                ClearWarehouseTorsoLook();
                return;
            }

            if (IsFirearm && (Firearm?.AimRequested ?? false))
            {
                // A root-relative intent origin cannot feed back when the chest
                // bends. Novice sway/recoil remains the arm solver's contribution.
                Vector3 origin = transform.position + transform.up * 1.4f;
                hero.SetTorsoLookIntent(this, PistolAimIntentPoint - origin, 0f, false);
            }
            else if (torsoLookCamera != null && torsoLookCamera.TryGetOrdinaryLookDirection(
                out Vector3 direction, out float neutralPitch))
                hero.SetTorsoLookIntent(this, direction, neutralPitch, true);
            else
                // A target/fixed camera releases ordinary look smoothly. Its
                // focus is the hero, not a point for his chest to aim towards.
                hero.SetTorsoLookIntent(this, transform.forward, 0f, false);
            hero.AdvanceTorsoLook(this, seconds);
        }
    }
}
