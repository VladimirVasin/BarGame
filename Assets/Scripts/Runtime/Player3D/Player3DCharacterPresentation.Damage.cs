using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object combatDamageOwner;
        private CombatDamagePose combatDamagePose;
        private float combatDamageWeight;

        /// <summary>A scene-owned combat layer; contextual actions and physics retain priority.</summary>
        public bool SetCombatDamagePose(object owner, CombatDamagePose pose, float weight)
        {
            if (owner == null || pose == null || !pose.IsInitialized || !isActiveAndEnabled ||
                ragdollPoseActive || float.IsNaN(weight) || float.IsInfinity(weight) ||
                (combatDamageOwner != null && !ReferenceEquals(combatDamageOwner, owner))) return false;
            if (!ReferenceEquals(combatDamagePose, pose)) combatDamagePose?.Restore();
            combatDamageOwner = owner;
            combatDamagePose = pose;
            combatDamageWeight = Mathf.Clamp01(weight);
            return true;
        }

        public void ClearCombatDamagePose(object owner)
        {
            if (owner != null && ReferenceEquals(combatDamageOwner, owner)) ClearCombatDamagePose();
        }

        private void RestoreCombatDamagePose() => combatDamagePose?.Restore();

        private void ApplyCombatDamagePose()
        {
            if (combatDamageOwner is Object unityOwner && unityOwner == null)
            {
                ClearCombatDamagePose();
                return;
            }
            bool allowed = !ragdollPoseActive && !risePose.Active && !interactionHandoffLocked &&
                           (!IsClipActive || OwnsClip(combatDamageOwner));
            combatDamagePose?.Apply(allowed ? combatDamageWeight : 0f);
        }

        private void ForgetCombatDamagePose()
        {
            combatDamagePose?.ForgetBase();
            combatDamageOwner = null;
            combatDamagePose = null;
            combatDamageWeight = 0f;
        }

        private void ClearCombatDamagePose()
        {
            RestoreCombatDamagePose();
            ForgetCombatDamagePose();
        }
    }
}
