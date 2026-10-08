namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object firearmOwner;
        private CombatActor firearm;

        internal void SetCombatFirearm(object owner, CombatActor actor)
        {
            if (!OwnsFirearmPresentation(owner)) return;
            firearmOwner = owner;
            firearm = actor;
        }

        internal void ClearCombatFirearm(object owner)
        {
            if (!ReferenceEquals(owner, firearmOwner)) return;
            if (!ragdollPoseActive) RestoreCombatFirearm();
            firearm = null;
            firearmOwner = null;
        }

        private void RestoreCombatFirearm()
        {
            // Firearm IK is part of recovery's target. Unwind the final blend
            // before restoring its additive arms, including repeated late passes.
            if (firearm != null) RestoreRecoveryPoseTransition();
            firearm?.RestorePistolAimPose();
        }
        private bool OwnsFirearmPresentation(object owner) => OwnsClip(owner) ||
            (!IsClipActive && !interactionHandoffLocked && OwnsCarryPose(owner));

        private void ApplyCombatFirearm()
        {
            // The authored lowered pistol may ride above ordinary gait. A
            // contextual full-body clip still owns its arms and hands outright.
            if (!ragdollPoseActive && !risePose.Active && OwnsFirearmPresentation(firearmOwner)) firearm?.ApplyPistolAimPose();
        }

        private void CompleteCombatFirearmPresentation()
        {
            if (!ragdollPoseActive && !risePose.Active && OwnsFirearmPresentation(firearmOwner))
                firearm?.CompletePistolPresentation();
        }
    }
}
