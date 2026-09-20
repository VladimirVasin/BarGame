using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatDamagePose damagePose;
        public Transform DamageRigRoot => hero != null ? hero.Registry.ModelRoot : npc != null ? npc.ModelRoot : null;
        public CombatDamagePose DamagePose => damagePose;
        public CombatImpact LastImpact { get; private set; }
        public int ReceivedImpactCount { get; private set; }
        public event Action<CombatImpact> ImpactReceived;
        public event Action<CombatActor> DamageReset;

        private void InitializeDamagePose()
        {
            damagePose = new CombatDamagePose();
            damagePose.Initialize(DamageRigRoot, transform);
        }

        private void PresentDamagePose()
        {
            // The committed weapon pose remains the exact pose sampled by SweepWeapon.
            float weight = State.IsCharging || State.IsAttacking || State.IsBlocking || State.Phase == MeleePhase.GuardImpact ||
                State.Phase == MeleePhase.Step ? 0f : 1f;
            if (hero != null) hero.SetCombatDamagePose(this, damagePose, weight);
            else damagePose?.Apply(weight);
        }

        private void PublishImpact(CombatImpact impact)
        {
            LastImpact = impact;
            ReceivedImpactCount++;
            if (impact.Damage > 0f)
                damagePose?.Hit(impact.Direction, Mathf.Clamp(impact.Damage / 25f, .2f, 2f));
            ImpactReceived?.Invoke(impact);
        }

        private void ResetDamage()
        {
            damagePose?.Reset();
            ReceivedImpactCount = 0;
            LastImpact = default;
            DamageReset?.Invoke(this);
        }

        private void ReleaseDamagePose()
        {
            if (IsRagdollActive) damagePose?.ForgetBase();
            else damagePose?.Restore();
            if (hero != null) hero.ClearCombatDamagePose(this);
        }
    }
}
