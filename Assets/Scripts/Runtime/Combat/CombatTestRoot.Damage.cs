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
            // Only a wounding contact bleeds: never a block, a parry or a miss.
            if ((impact.Result == MeleeHitResult.Hit || impact.Result == MeleeHitResult.GuardBroken) &&
                impact.Damage > 0f && BloodEffects != null)
                BloodEffects.Emit(impact.Target, impact.Point, impact.Direction, impact.Damage);
            // Weight is time: a few frozen substeps and a small kick on the shoulder
            // camera, graded by what happened. A killing blow holds longest.
            bool heavy = impact.AttackPower >= .5f;
            int substeps;
            float kick;
            switch (impact.Result)
            {
                case MeleeHitResult.Blocked: substeps = 3; kick = .01f; break;
                case MeleeHitResult.Parried: substeps = 8; kick = .03f; break;
                case MeleeHitResult.GuardBroken: substeps = 8; kick = .04f; break;
                case MeleeHitResult.Hit: substeps = heavy ? 10 : 6; kick = heavy ? .04f : .025f; break;
                default: return;
            }
            if (impact.Target != null && impact.Target.State.IsDefeated) { substeps = 24; kick = .05f; }
            RequestHitStop(substeps);
            Vector3 direction = impact.Direction;
            direction.y = 0f;
            if (direction.sqrMagnitude > .0001f && CameraFollow != null)
                CameraFollow.Nudge(direction.normalized * kick);
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
