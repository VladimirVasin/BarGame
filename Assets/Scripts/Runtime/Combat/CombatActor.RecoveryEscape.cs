using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float RecoveryArmEscapeSeconds = .75f;
        internal const float RecoveryWeaponReleaseSeconds = 1.25f;
        private float recoveryBlockedSeconds;
        private bool recoveryArmEscape;

        public float RecoveryBlockedSeconds => recoveryBlockedSeconds;
        public string RecoveryEscapeStage => weaponDropped ? "released" : recoveryArmEscape ? "arm" : "none";

        // Called only for a rejected, clock-rolled-back simulation substep.
        // Neither render calls, pause nor hit-stop spend this budget.
        private bool WaitForRecoveryWeapon(float seconds)
        {
            recoveryBlockedSeconds += seconds;
            if (!recoveryArmEscape && recoveryBlockedSeconds >= RecoveryArmEscapeSeconds)
            {
                recoveryArmEscape = true;
                weaponConstraint?.BeginRecoveryEscape();
                JournalEvent("rise_escape_started", f0: GameLog.Field("blocked_seconds", recoveryBlockedSeconds),
                    f1: GameLog.Field("shape", WeaponBlockingShape));
            }
            if (!weaponDropped && recoveryBlockedSeconds >= RecoveryWeaponReleaseSeconds)
            {
                // Keep the complete displayed pose. Only the hand opens and its
                // actual prop becomes a free physical body at its present location.
                ReleaseWeapon(Vector3.zero, Vector3.zero);
                recoveryRegrip = false;
                State.CancelCharge();
                State.CancelBufferedAction(State.BufferedAction);
                guardHeld = false;
                State.SetBlocking(false);
                sweepValid = collectSweep = collectShove = false;
                supportGrip?.Forget();
                supportGrip?.SetRecoveryOwned(true);
                supportGrip?.AllowRegrip(false);
                supportGrip?.SetTarget(false, false);
                weaponConstraint?.Forget();
                weaponConstraint?.EndRecoveryContact();
                JournalEvent("rise_weapon_released", f0: GameLog.Field("blocked_seconds", recoveryBlockedSeconds),
                    f1: GameLog.Field("health", State.Health));
            }
            return JournalRiseWait(weaponDropped ? "weapon_released" : "weapon_motion_blocked");
        }

        private void ResetRecoveryEscape()
        {
            recoveryBlockedSeconds = 0f;
            recoveryArmEscape = false;
            weaponConstraint?.EndRecoveryContact();
        }
    }
}
