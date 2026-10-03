using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private readonly NpcAttentionHeadLayer combatNpcAttention = new NpcAttentionHeadLayer();
        private readonly NpcAttentionNotice combatAttentionNotice = new NpcAttentionNotice();
        private Transform combatAttentionHead;
        private Vector3? combatAttentionFocus;

        internal Vector3? CombatAttentionFocus => combatAttentionFocus;
        internal float CombatAttentionWeight => hero != null ? hero.AttentionWeight : combatNpcAttention.Weight;

        private void InitializeCombatAttention()
        {
            combatAttentionHead = hero != null ? hero.Registry.Anchors.Head : npc.Head;
            if (npc != null) combatNpcAttention.Bind(transform, npc.Head, npc.Animator);
            combatAttentionNotice.Reset();
        }

        private bool CanAttendCombat => isActiveAndEnabled && IsAvailable && !roundEnded &&
            !winnerPresentationReleased && !State.IsDefeated && !IsKnockedDown && !IsRagdollActive;

        private void RefreshCombatAttention()
        {
            if (PauseMenuController.IsAnyPaused) return;
            if (!CanAttendCombat) { ReleaseCombatAttention(); return; }
            Vector3? candidate = contactTarget != null && contactTarget.isActiveAndEnabled &&
                !contactTarget.State.IsDefeated && contactTarget.combatAttentionHead != null
                ? contactTarget.combatAttentionHead.position : (Vector3?)null;
            combatAttentionFocus = combatAttentionNotice.Resolve(transform.position, transform.eulerAngles.y, candidate);
            if (hero != null) hero.SetOwnedAttentionFocus(this, combatAttentionFocus);
            else combatNpcAttention.SetFocus(combatAttentionFocus);
        }

        private void RestoreCombatAttention()
        {
            if (PauseMenuController.IsAnyPaused) return;
            // Once physics owns the head, an old animation base must never be
            // restored over the live ragdoll, including the first fallen frame.
            if (IsRagdollActive) combatNpcAttention.Forget();
            else combatNpcAttention.Restore();
            if (!CanAttendCombat) ReleaseCombatAttention();
        }

        private void AdvanceCombatAttention(float seconds)
        {
            if (presentationFrozen || PauseMenuController.IsAnyPaused) return;
            RefreshCombatAttention();
            if (npc == null || !CanAttendCombat) return;
            // Use the existing NPC layer's smoothing on the duel clock, then
            // restore it before the next sampled pose. Present applies it at
            // zero time before the shared final-pose transition.
            combatNpcAttention.Restore();
            combatNpcAttention.Apply(seconds);
            combatNpcAttention.Restore();
        }

        private void ApplyCombatAttention()
        {
            if (npc == null || PauseMenuController.IsAnyPaused) return;
            RefreshCombatAttention();
            if (CanAttendCombat) combatNpcAttention.Apply(0f);
        }

        private void ReleaseCombatAttention()
        {
            if (IsRagdollActive) combatNpcAttention.Forget();
            else { combatNpcAttention.Restore(); combatNpcAttention.Clear(); }
            combatAttentionNotice.Reset();
            combatAttentionFocus = null;
            if (hero != null) hero.ClearOwnedAttentionFocus(this);
        }
    }
}
