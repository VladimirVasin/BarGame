namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object combatGripOwner;
        private CombatSupportGrip combatGrip;

        internal void SetCombatSupportGrip(object owner, CombatSupportGrip contact)
        {
            if (owner == null || contact == null ||
                (combatGripOwner != null && !ReferenceEquals(combatGripOwner, owner))) return;
            combatGripOwner = owner; combatGrip = contact;
        }

        internal void ClearCombatSupportGrip(object owner)
        {
            if (!ReferenceEquals(combatGripOwner, owner)) return;
            if (ragdollPoseActive) combatGrip?.Forget(); else combatGrip?.Restore();
            combatGripOwner = null; combatGrip = null;
        }

        private void RestoreCombatSupportGrip() => combatGrip?.Restore();

        private void ApplyCombatSupportGrip()
        {
            if (combatGripOwner is UnityEngine.Object actor && actor == null)
            { ClearCombatSupportGrip(combatGripOwner); return; }
            if (!ragdollPoseActive && !risePose.Active && !interactionHandoffLocked &&
                (!IsClipActive || OwnsClip(combatGripOwner))) combatGrip?.Apply();
        }
    }
}
