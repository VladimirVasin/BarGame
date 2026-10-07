using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_MetalContactsStopAtTheFirstSurfaceAndShareOnePausedSparkPool()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            VerifyRelativeMetalSweep();
            foreach (bool heroActs in new[] { true, false }) VerifyUnoccludedPhysicalBodyContact(heroActs);
            foreach (bool heroActs in new[] { true, false })
            foreach (bool guarding in new[] { false, true })
                VerifyCollectedMetalContact(heroActs, guarding);

            // The pending list, not its collection order, owns mutual outcomes.
            foreach (bool reverse in new[] { false, true })
            {
                PlacePair(4f);
                PreparePendingMetalActor(root.Hero); PreparePendingMetalActor(root.Opponent);
                Vector3 point = root.Hero.Weapon.transform.position;
                var pending = new List<CombatActor.Contact>
                {
                    new CombatActor.Contact(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward,
                        default, time: .8f, physicalBody: true),
                    CombatActor.Contact.Obstacle(root.Opponent, root.Hero, point, Vector3.forward, Vector3.back, .4f, true, true),
                    new CombatActor.Contact(root.Opponent, root.Hero, point, Vector3.forward, Vector3.back,
                        default, time: .8f, physicalBody: true),
                    CombatActor.Contact.Obstacle(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward, .4f, true, true)
                };
                if (reverse) pending.Reverse();
                CombatActor.ApplyContacts(pending);
                foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
                {
                    Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                    Assert.That(actor.State.RecoveryRemaining, Is.EqualTo(S.ObstacleRecoverySeconds).Within(.0001f));
                    Assert.That(actor.State.Health, Is.EqualTo(S.MaxHealth));
                    Assert.That(actor.ReceivedImpactCount, Is.Zero, "The intercepted body candidate cannot resolve.");
                    Assert.That(actor.WeaponClashCount, Is.EqualTo(1));
                }
                Assert.That(root.SparkEffects.EmissionCount, Is.EqualTo(1), "A mutual pair has one burst and one sound path.");
                Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
            }

            VerifyWorldBeforeMovingMetal();
            VerifyBodyBeforeMetal();

            // Use the actual authored guard for the visual evidence, not the
            // controlled prop poses used by the collision/ordering oracles above.
            foreach (bool heroActs in new[] { true, false })
            {
                PlacePair(1.1f);
                CombatActor actor = heroActs ? root.Hero : root.Opponent;
                CombatActor target = heroActs ? root.Opponent : root.Hero;
                target.SetBlock(true);
                for (int tick = 0; tick < 24; tick++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(target.State.IsBlocking, Is.True);
                Assert.That(actor.TryAttack(), Is.True);
                int ticks = ContactTicks(120f) + 8;
                while (actor.WeaponClashCount == 0 && target.State.Health == S.MaxHealth && ticks-- > 0)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(actor.WeaponClashCount, Is.EqualTo(1), "The authored raised bar must physically meet this frontal arc.");
                Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                Assert.That(target.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(target.State.Stamina, Is.EqualTo(AfterBlock).Within(.001f));
                Assert.That(target.LastImpact.Result, Is.EqualTo(MeleeHitResult.Blocked));
                Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
                string subject = heroActs ? "Test hero metal contact" : "Test opponent metal contact";
                CaptureImpactRecoveryFrame(actor, subject, "contact", actor.transform.position, actor.transform.rotation, true);
                CaptureImpactPlayerCamera(subject, "contact-shoulder");

                float sparkTime = root.SparkEffects.SimulationSeconds;
                int particles = root.SparkEffects.ActiveParticleCount;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.2f); yield return null;
                    Assert.That(root.SparkEffects.SimulationSeconds, Is.EqualTo(sparkTime));
                    Assert.That(root.SparkEffects.ActiveParticleCount, Is.EqualTo(particles));
                }
                // The target's physical GuardImpact requested the ordinary hit stop.
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.SparkEffects.SimulationSeconds, Is.EqualTo(sparkTime), "Hit stop also freezes the manual particle clock.");
                for (int tick = 0; tick < 10; tick++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(actor.ActiveClipName, Is.EqualTo("CombatRecoil"));
                CaptureImpactRecoveryFrame(actor, subject, "recoil", actor.transform.position, actor.transform.rotation, true);
                int emissions = root.SparkEffects.EmissionCount;
                for (int repeat = 0; repeat < 4; repeat++) { actor.Present(); target.Present(); }
                Assert.That(root.SparkEffects.EmissionCount, Is.EqualTo(emissions));
                root.ResetRound();
                Assert.That(root.SparkEffects.ActiveParticleCount, Is.Zero);
                Assert.That(root.SparkEffects.EmissionCount, Is.Zero);
            }

            CombatSparkEffects formerPool = root.SparkEffects;
            Assert.That(root.ReturnToMenu(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "The metal contact scene did not unload.");
            Assert.That(formerPool == null, Is.True, "The bounded pool is owned by the unloaded duel.");
            LogAssert.NoUnexpectedReceived();
        }

        private static void VerifyRelativeMetalSweep()
        {
            var left = new Pose(new Vector3(-.3f, 1.2f, 0f), Quaternion.identity);
            var right = new Pose(new Vector3(.3f, 1.2f, 0f), Quaternion.identity);
            Assert.That(CombatWeaponGeometry.Sweep(left, left, right, right, out _, out _, out _), Is.False);
            Assert.That(CombatWeaponGeometry.Sweep(right, right, left, left, out _, out _, out _), Is.False);
            Assert.That(CombatWeaponGeometry.Sweep(left, right, right, left, out float moving, out Vector3 point, out Vector3 normal), Is.True,
                "Both thin bars pass through each other while their endpoints remain disjoint.");
            Assert.That(moving, Is.InRange(.3f, .6f));
            Assert.That(point.y, Is.InRange(.9f, 1.9f));
            Assert.That(normal.sqrMagnitude, Is.EqualTo(1f).Within(.001f));
            Assert.That(CombatWeaponGeometry.Sweep(left, left, right, left, out float stationary, out _, out _), Is.True);
            Assert.That(moving, Is.LessThan(stationary), "Freezing either prop changes the time of impact.");
            var separatedFrom = new Pose(right.position + Vector3.forward, right.rotation);
            var separatedTo = new Pose(left.position + Vector3.forward, left.rotation);
            Assert.That(CombatWeaponGeometry.Sweep(left, right, separatedFrom, separatedTo, out _, out _, out _), Is.False);
            var turnFrom = new Pose(new Vector3(0f, 1.2f, 0f), Quaternion.Euler(0f, 0f, -80f));
            var turnTo = new Pose(turnFrom.position, Quaternion.Euler(0f, 0f, 80f));
            var crossbar = new Pose(new Vector3(0f, 1.75f, 0f), Quaternion.Euler(0f, 0f, 90f));
            Assert.That(CombatWeaponGeometry.Sweep(turnFrom, turnFrom, crossbar, crossbar, out _, out _, out _), Is.False);
            Assert.That(CombatWeaponGeometry.Sweep(turnTo, turnTo, crossbar, crossbar, out _, out _, out _), Is.False);
            Assert.That(CombatWeaponGeometry.Sweep(turnFrom, turnTo, crossbar, crossbar, out float turn, out _, out _), Is.True,
                "A rotating hook/shaft can meet a bar outside the chord between its endpoint poses.");
            Assert.That(turn, Is.InRange(.1f, .9f));
        }

        private void VerifyUnoccludedPhysicalBodyContact(bool heroActs)
        {
            PlacePair(1.1f);
            CombatActor actor = heroActs ? root.Hero : root.Opponent;
            CombatActor target = heroActs ? root.Opponent : root.Hero;
            // Positive control for the physical-radius HP sweep. Remove only
            // the competing prop, keeping the authored target anatomy untouched.
            target.Weapon.SetActive(false);
            try
            {
                Assert.That(actor.TryAttack(), Is.True);
                for (int tick = 0; tick < ContactTicks(120f) + 8; tick++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(target.State.Health, Is.LessThan(S.MaxHealth), "The authored thin shaft must still reach real anatomy.");
                Assert.That(target.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.BloodEffects.EmissionCount, Is.EqualTo(1));
                Assert.That(root.SparkEffects.EmissionCount, Is.Zero);
            }
            finally { target.Weapon.SetActive(true); }
        }

        private void VerifyCollectedMetalContact(bool heroActs, bool guarding)
        {
            PlacePair(4f);
            CombatActor actor = heroActs ? root.Hero : root.Opponent;
            CombatActor target = heroActs ? root.Opponent : root.Hero;
            target.SetBlock(guarding); target.Step(.2f);
            Assert.That(actor.TryAttack(), Is.True);
            while (actor.State.AttackElapsed + CombatTestRoot.SimulationStep < actor.State.AttackWindupSeconds)
                actor.Step(CombatTestRoot.SimulationStep);
            actor.AdvanceSimulation(CombatTestRoot.SimulationStep); actor.Present();
            Transform bar = target.Weapon.transform;
            Vector3 savedPosition = bar.localPosition; Quaternion savedRotation = bar.localRotation;
            try
            {
                // A controlled physical positive: the target body stays outside
                // reach while its actual full prop intersects the sampled shaft.
                Vector3 center = actor.Weapon.transform.position + actor.Weapon.transform.up * .25f;
                bar.SetPositionAndRotation(center, actor.Weapon.transform.rotation * Quaternion.Euler(0f, 0f, 90f));
                actor.CaptureContactPose(); target.CaptureContactPose();
                var pending = new List<CombatActor.Contact>();
                Assert.That(actor.CollectContacts(pending), Is.True);
                target.CollectContacts(pending);
                Assert.That(actor.State.AttackOutcome, Is.Not.EqualTo(MeleeAttackOutcome.Obstacle),
                    "Collection cannot mutate either frozen fighter before the other sweep.");
                CombatActor.ApplyContacts(pending);
                Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                Assert.That(actor.State.RecoveryRemaining, Is.EqualTo(S.ObstacleRecoverySeconds).Within(.0001f));
                Assert.That(actor.WeaponClashCount, Is.EqualTo(1));
                Assert.That(target.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(target.State.Stamina, Is.EqualTo(guarding ? AfterBlock : S.MaxStamina).Within(.001f));
                Assert.That(root.SparkEffects.EmissionCount, Is.EqualTo(1));
                Assert.That(Vector3.Distance(root.SparkEffects.LastEmissionPoint, actor.LastWeaponClashPoint), Is.LessThan(.0001f));
                Assert.That(root.SparkEffects.ActiveParticleCount, Is.InRange(1, CombatSparkEffects.MaximumParticles));
                Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
            }
            finally { bar.localPosition = savedPosition; bar.localRotation = savedRotation; }
        }

        private static void PreparePendingMetalActor(CombatActor actor)
        {
            Assert.That(actor.TryAttack(), Is.True);
            actor.State.Advance(actor.State.AttackWindupSeconds + .01f);
            actor.CaptureContactPose();
        }

        private void VerifyWorldBeforeMovingMetal()
        {
            PlacePair(4f); PreparePendingMetalActor(root.Hero); PreparePendingMetalActor(root.Opponent);
            Vector3 point = root.Hero.Weapon.transform.position;
            var pending = new List<CombatActor.Contact>
            {
                CombatActor.Contact.Obstacle(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward, .4f, true, true),
                new CombatActor.Contact(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward,
                    default, time: .8f, physicalBody: true),
                CombatActor.Contact.Obstacle(root.Opponent, null, point, Vector3.forward, Vector3.back, .2f, false)
            };
            CombatActor.ApplyContacts(pending);
            Assert.That(root.Opponent.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
            Assert.That(root.Opponent.State.Health, Is.LessThan(S.MaxHealth), "An earlier wall invalidates the later moving-weapon interception.");
            Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit));
            Assert.That(root.SparkEffects.EmissionCount, Is.Zero);
        }

        private void VerifyBodyBeforeMetal()
        {
            PlacePair(4f); PreparePendingMetalActor(root.Hero);
            Vector3 point = root.Opponent.transform.position + Vector3.up;
            var pending = new List<CombatActor.Contact>
            {
                CombatActor.Contact.Obstacle(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward, .7f, true),
                new CombatActor.Contact(root.Hero, root.Opponent, point, Vector3.back, Vector3.forward,
                    default, time: .2f, physicalBody: true)
            };
            CombatActor.ApplyContacts(pending);
            Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), "A later metal surface cannot erase an earlier wound.");
            Assert.That(root.Opponent.State.Health, Is.LessThan(S.MaxHealth));
            Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
        }
    }
}
