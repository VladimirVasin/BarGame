using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        public CombatBloodEffects BloodEffects { get; private set; }

        private void InitializeDamageEffects()
        {
            BloodEffects = gameObject.AddComponent<CombatBloodEffects>();
            BloodEffects.Initialize(transform);
            Hero.ImpactReceived += ShowImpact;
            Opponent.ImpactReceived += ShowImpact;
            Hero.DamageReset += ResetActorDamage;
            Opponent.DamageReset += ResetActorDamage;
        }

        private void ShowImpact(CombatImpact impact)
        {
            if (impact.Damage > 0f && BloodEffects != null)
                BloodEffects.Emit(impact.Target, impact.Point, impact.Direction, impact.Damage);
        }

        private void ResetActorDamage(CombatActor actor)
        {
            if (BloodEffects != null) BloodEffects.ResetActor(actor);
        }

        private void ReleaseDamageEffects()
        {
            if (Hero != null) { Hero.ImpactReceived -= ShowImpact; Hero.DamageReset -= ResetActorDamage; }
            if (Opponent != null) { Opponent.ImpactReceived -= ShowImpact; Opponent.DamageReset -= ResetActorDamage; }
        }
    }
}
