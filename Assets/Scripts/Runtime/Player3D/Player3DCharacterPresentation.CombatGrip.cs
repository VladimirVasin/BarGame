namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object combatGripOwner;
        private CombatSupportGrip combatGrip;
        private CombatWeaponConstraint combatWeapon;

        internal void SetCombatSupportGrip(object owner, CombatSupportGrip contact, CombatWeaponConstraint weapon)
        {
            if (owner == null || contact == null ||
                (combatGripOwner != null && !ReferenceEquals(combatGripOwner, owner))) return;
            combatGripOwner = owner; combatGrip = contact; combatWeapon = weapon;
        }

        internal void ClearCombatSupportGrip(object owner)
        {
            if (!ReferenceEquals(combatGripOwner, owner)) return;
            if (ragdollPoseActive) { combatGrip?.Forget(); combatWeapon?.Forget(); }
            else { combatGrip?.Restore(); combatWeapon?.Restore(); }
            combatGripOwner = null; combatGrip = null; combatWeapon = null;
        }

        private void RestoreCombatSupportGrip()
        { combatGrip?.Restore(); combatWeapon?.Restore(); }

        private void ApplyCombatSupportGrip()
        {
            if (combatGripOwner is UnityEngine.Object actor && actor == null)
            { ClearCombatSupportGrip(combatGripOwner); return; }
            // Recovery owns the limb; only the open finger shape is applied here.
            if (combatGrip != null && combatGrip.IsRecoveryOwned)
            { combatGrip.Apply(); return; }
            if (!ragdollPoseActive && !risePose.Active && !interactionHandoffLocked &&
                (!IsClipActive || OwnsClip(combatGripOwner)))
            {
                combatWeapon?.Apply();
                if (combatWeapon == null || !combatWeapon.MotionBlocked) combatGrip?.Apply();
                combatWeapon?.CommitPresentedPose(combatGrip);
            }
        }
    }
}
