using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        public CombatBloodEffects BloodEffects { get; private set; }
        public CombatHeadDestruction HeadEffects { get; private set; }
        public CombatBodyDestruction BodyEffects { get; private set; }
        internal long BloodImpactTicks { get; private set; }
        internal long BodyImpactTicks { get; private set; }
        internal long ImpactPresentationTicks { get; private set; }

        private void InitializeDamageEffects()
        {
            BloodEffects = gameObject.AddComponent<CombatBloodEffects>();
            BloodEffects.Initialize(transform);
            HeadEffects = gameObject.AddComponent<CombatHeadDestruction>();
            HeadEffects.PrepareActor(Hero);
            HeadEffects.PrepareActor(Opponent);
            BodyEffects = gameObject.AddComponent<CombatBodyDestruction>();
            BodyEffects.PrepareActor(Hero);
            BodyEffects.PrepareActor(Opponent);
            BloodEffects.PrepareActor(Hero);
            BloodEffects.PrepareActor(Opponent);
            Hero.ImpactReceived += ShowImpact;
            Opponent.ImpactReceived += ShowImpact;
            Hero.DamageReset += ResetActorDamage;
            Opponent.DamageReset += ResetActorDamage;
        }

        private void ShowImpact(CombatImpact impact)
        {
            if (impact.Target != null && impact.Target.State.IsDefeated) SparkEffects?.Clear();
            // Only a wounding contact bleeds: never a block, a parry or a miss.
            if ((impact.Result == MeleeHitResult.Hit || impact.Result == MeleeHitResult.GuardBroken) &&
                (impact.Kind == CombatImpactKind.Projectile || impact.Kind == CombatImpactKind.Weapon && Mathf.Max(impact.WoundDamage, impact.Damage) > 0f) && BloodEffects != null)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                BloodEffects.Emit(impact);
                BloodImpactTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
            if (impact.HeadFeedback) HeadEffects?.Apply(impact, BloodEffects);
            long bodyStart = System.Diagnostics.Stopwatch.GetTimestamp();
            BodyEffects?.Apply(impact);
            BodyImpactTicks += System.Diagnostics.Stopwatch.GetTimestamp() - bodyStart;
            if (!impact.PrimaryResponse) return;
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
            if (impact.Kind == CombatImpactKind.Kick) { substeps = 3; kick = .015f; }
            if (impact.Kind == CombatImpactKind.Projectile)
            {
                bool head = impact.Location.Region == MeleeBodyRegion.Head;
                bool arm = impact.Location.Region == MeleeBodyRegion.LeftArm || impact.Location.Region == MeleeBodyRegion.RightArm;
                // A surviving shotgun wound keeps the duel live. Even one weak
                // distant pellet used to stop both fighters, flights and blood
                // for the same fixed interval as a close, powerful impact.
                bool survivingPellet = impact.IsPellet && impact.Target != null && !impact.Target.State.IsDefeated;
                substeps = survivingPellet ? 0 : head ? 12 : arm ? 4 : 6;
                kick = head ? .09f : arm ? .04f : .06f;
                if (substeps > 0)
                {
                    // PublishImpact already resolved motion. Freeze its transition source;
                    // terminal hits have already handed their live pose to physics.
                    long presentationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    impact.Target?.Present();
                    ImpactPresentationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - presentationStart;
                }
            }
            else if (impact.Target != null && impact.Target.State.IsDefeated) { substeps = 24; kick = .05f; }
            if (substeps > 0) RequestHitStop(substeps);
            Vector3 direction = impact.Direction;
            direction.y = 0f;
            if ((impact.Kind != CombatImpactKind.Projectile || impact.Target == Hero) &&
                direction.sqrMagnitude > .0001f && CameraFollow != null)
                CameraFollow.Nudge(direction.normalized * kick);
        }

        private void ResetActorDamage(CombatActor actor)
        {
            if (BloodEffects != null) BloodEffects.ResetActor(actor);
            HeadEffects?.ResetActor(actor);
            BodyEffects?.ResetActor(actor);
            SparkEffects?.Clear();
        }

        private void ReleaseDamageEffects()
        {
            if (Hero != null) { Hero.ImpactReceived -= ShowImpact; Hero.DamageReset -= ResetActorDamage; }
            if (Opponent != null) { Opponent.ImpactReceived -= ShowImpact; Opponent.DamageReset -= ResetActorDamage; }
        }
    }
}
