using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_ObservedCounterAndRisingHeroReceiveRealPressure()
        {
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
            {
                actor.Body.height = 1.7f;
                actor.Body.radius = .32f;
                actor.Body.center = Vector3.up * .85f;
            }
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.SetSparring(true);
            root.ResetRound();
            root.Hero.ResetActor(ground, Vector3.back);
            root.Opponent.ResetActor(ground + Vector3.forward * 1.05f, Vector3.back);
            Physics.SyncTransforms();
            Assert.That(root.Hero.TryAttack(), Is.True);
            while (root.Hero.State.Phase != MeleePhase.Recovery && root.Hero.State.AttackElapsed < S.WindupSeconds + S.ActiveSeconds + .02f)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
            root.Tick(.18f);
            Assert.That(root.Opponent.State.IsAttacking, Is.False,
                "A real miss still needs the visible reaction delay before its answer.");
            float heroHealth = root.Hero.State.Health;
            bool struckBeforeReady = false;
            Quaternion counterDirection = root.Opponent.transform.rotation;
            for (int tick = 0; tick < Mathf.CeilToInt(S.RecoverySeconds / CombatTestRoot.SimulationStep) + 1; tick++)
            {
                bool stillRecovering = root.Hero.State.Phase == MeleePhase.Recovery;
                root.Tick(CombatTestRoot.SimulationStep);
                if (root.Opponent.State.Phase == MeleePhase.Windup)
                {
                    Assert.That(root.Opponent.State.AttackPower, Is.Zero);
                    Assert.That(root.Opponent.State.AttackWindupSeconds, Is.EqualTo(S.ChainWindupSeconds).Within(.00001f));
                    Assert.That(Quaternion.Angle(counterDirection, root.Opponent.transform.rotation),
                        Is.LessThanOrEqualTo(CombatActor.MaximumFacingSpeed * CombatTestRoot.SimulationStep + .05f),
                        "The short observed counter follows its target with the same bounded simulation turn.");
                }
                counterDirection = root.Opponent.transform.rotation;
                if (root.Hero.State.Health >= heroHealth) continue;
                struckBeforeReady = stillRecovering;
                break;
            }
            Assert.That(struckBeforeReady, Is.True,
                "The authored weapon must actually reach the whiffer before Ready, not merely start a response.");

            // A partner with its own planted stance must not wait for the hero's
            // independent balance tail before it can decide how to press.
            root.SetSparring(true);
            root.ResetRound();
            root.Hero.ResetActor(ground, Vector3.forward);
            root.Opponent.ResetActor(ground + Vector3.forward * 1.25f, Vector3.back);
            Physics.SyncTransforms();
            root.Hero.ApplyImpactForDiagnostics(TacticalRecoveryImpact(root.Hero, -Vector3.forward * 165f));
            Assert.That(root.Hero.HasAttackBalance, Is.False);
            Assert.That(root.Opponent.HasAttackBalance, Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.OpponentIntent, Is.Not.EqualTo(CombatOpponentIntent.Recover),
                "The hero's balance recovery must not reserve the grounded partner's next action.");

            // Exercise the actual ragdoll/authoring transition. Lying gets space;
            // the same physical hero becomes a target as soon as the rise begins.
            root.ResetRound();
            root.Hero.ResetActor(ground, Vector3.forward);
            root.Opponent.ResetActor(ground + Vector3.forward * 2f, Vector3.back);
            Physics.SyncTransforms();
            Assert.That(root.Hero.TryBeginKnockdown(TacticalRecoveryImpact(root.Hero, Vector3.down),
                Vector3.zero, Vector3.zero), Is.True);
            bool sawLying = false, approachedRise = false, attackedRise = false;
            for (int frame = 0; frame < 360 && !attackedRise; frame++)
            {
                MeleePhase heroPhase = root.Hero.State.Phase;
                float previousDistance = Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position);
                root.Tick(1f / 60f);
                if (heroPhase == MeleePhase.KnockedDown)
                {
                    sawLying = true;
                    Assert.That(root.Opponent.State.IsAttacking || root.Opponent.State.IsCharging, Is.False,
                        "The partner must still leave an actually lying body alone.");
                }
                else if (heroPhase == MeleePhase.Rising)
                {
                    approachedRise |= Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position) < previousDistance - .0001f;
                    attackedRise |= root.Opponent.State.IsAttacking;
                }
                yield return null;
            }
            Assert.That(sawLying && approachedRise && attackedRise, Is.True,
                "The partner must approach and try an ordinary weapon swing during the real vulnerable rise.");
            Assert.That(root.Opponent.State.AttackPower, Is.Zero, "A visible rise is pressed without holding a charge.");
            LogAssert.NoUnexpectedReceived();
        }

        private CombatImpact TacticalRecoveryImpact(CombatActor victim, Vector3 impulse)
        {
            Transform chest = victim.Ragdoll.PhysicsController.ChestBody.transform;
            Vector3 point = chest.position;
            return new CombatImpact(root.Opponent, victim, 1, point, -impulse.normalized, impulse.normalized,
                victim.State.Health, victim.State.Health, MeleeHitResult.Hit,
                new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front), 0f,
                Player3DAnatomicalPart.Torso, chest.InverseTransformPoint(point), 3f, impulse);
        }

    }
}
