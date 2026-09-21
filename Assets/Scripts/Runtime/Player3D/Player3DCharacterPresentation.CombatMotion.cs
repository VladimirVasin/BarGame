namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object combatMotionOwner;
        private CombatBodyMotion combatBodyMotion;

        internal void SetCombatBodyMotion(object owner, CombatBodyMotion motion)
        {
            if (!OwnsClip(owner)) return;
            combatMotionOwner = owner;
            combatBodyMotion = motion;
        }

        internal void ClearCombatBodyMotion(object owner)
        {
            if (!ReferenceEquals(owner, combatMotionOwner)) return;
            combatBodyMotion?.Restore();
            combatBodyMotion = null;
            combatMotionOwner = null;
        }

        private void ApplyCombatBodyMotion()
        {
            if (!ragdollPoseActive && OwnsClip(combatMotionOwner)) combatBodyMotion?.Apply();
            else ClearCombatBodyMotion(combatMotionOwner);
        }

        private void RestoreCombatBodyMotion() => combatBodyMotion?.Restore();
    }
}
