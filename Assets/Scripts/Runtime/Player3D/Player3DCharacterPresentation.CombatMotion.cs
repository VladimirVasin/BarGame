namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object combatMotionOwner;
        private CombatBodyMotion combatBodyMotion;
        private CombatFootwork combatFootwork;

        internal void SetCombatFootwork(object owner, CombatFootwork motion)
        {
            if (!OwnsClip(owner)) return;
            combatMotionOwner = owner;
            combatFootwork = motion;
        }

        internal void SetCombatBodyMotion(object owner, CombatBodyMotion motion)
        {
            if (!OwnsClip(owner)) return;
            combatMotionOwner = owner;
            combatBodyMotion = motion;
        }

        internal void ClearCombatBodyMotion(object owner)
        {
            if (!ReferenceEquals(owner, combatMotionOwner)) return;
            combatFootwork?.Restore();
            combatBodyMotion?.Restore();
            combatFootwork = null;
            combatBodyMotion = null;
            combatMotionOwner = null;
        }

        private void ApplyCombatBodyMotion()
        {
            if (!ragdollPoseActive && OwnsClip(combatMotionOwner)) combatBodyMotion?.Apply();
            else ClearCombatBodyMotion(combatMotionOwner);
        }

        private void RestoreCombatBodyMotion() => combatBodyMotion?.Restore();
        private void RestoreCombatFootwork() => combatFootwork?.Restore();
        private void ApplyCombatFootwork()
        {
            if (!ragdollPoseActive && OwnsClip(combatMotionOwner)) combatFootwork?.Apply();
        }
        private void ConstrainCombatFootContacts()
        {
            if (!ragdollPoseActive && OwnsClip(combatMotionOwner)) combatFootwork?.ConstrainContacts();
        }
    }
}
