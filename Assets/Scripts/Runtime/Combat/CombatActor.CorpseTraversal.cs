namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatCorpseStepPose corpseStepPose;
        internal CombatCorpseStepPose CorpseStepPose => hero != null ? hero.CorpseStepPose : corpseStepPose;
        internal bool CanPresentCorpseStep => isActiveAndEnabled && !State.IsDefeated &&
            !IsKnockedDown && !IsRagdollActive && BodyDamage.CanStand && !State.IsKicking &&
            !(footwork?.RecoveryEpisodeActive ?? false);

        private void AdvanceCorpseStepPose(float seconds)
        {
            if (hero != null) return; // The hero's free and owned clips share its normal Update clock.
            if (!CanPresentCorpseStep) { corpseStepPose?.Forget(); return; }
            if (corpseStepPose == null) corpseStepPose = new CombatCorpseStepPose(DamageRigRoot, transform);
            corpseStepPose.Advance(seconds);
        }

        private void ApplyCorpseStepPose()
        {
            if (hero != null) return; // Its final lower-body pass precedes grip/footwear/shadow/scarf.
            if (!CanPresentCorpseStep) { corpseStepPose?.Forget(); return; }
            if (corpseStepPose == null) corpseStepPose = new CombatCorpseStepPose(DamageRigRoot, transform);
            corpseStepPose.Apply();
        }

        private void RestoreCorpseStepPose()
        {
            if (hero != null) hero.RestoreCorpseStepPose();
            else corpseStepPose?.Restore();
        }

        private void ResetCorpseStepPose()
        {
            if (hero != null) hero.ResetCorpseStepPose();
            else if (IsRagdollActive) corpseStepPose?.Forget();
            else corpseStepPose?.Reset();
        }

        private void ForgetCorpseStepPose()
        {
            if (hero != null) hero.CorpseStepPose?.Forget();
            else corpseStepPose?.Forget();
        }
    }
}
