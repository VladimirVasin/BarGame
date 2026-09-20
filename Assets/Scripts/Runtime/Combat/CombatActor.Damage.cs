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
            // The tell and the swept arc stay on the exact authored pose; a hurt
            // fighter still recovers and guards hurt.
            float weight = State.Phase switch
            {
                MeleePhase.Charging => 0f,
                MeleePhase.Windup => 0f,
                MeleePhase.Active => 0f,
                MeleePhase.Step => 0f,
                MeleePhase.Recovery => .6f,
                MeleePhase.GuardImpact => .5f,
                _ => State.IsBlocking ? .5f : 1f
            };
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
