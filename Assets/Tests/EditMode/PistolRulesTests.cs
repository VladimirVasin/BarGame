using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PistolRulesTests
    {
        private const float Eps = .0001f;

        [Test]
        public void RaiseAndCooldownNeverBufferOrRepeatAnExplicitShot()
        {
            var pistol = new PistolState();
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.TryFire(), Is.False);
            pistol.SetAim(true);
            pistol.Advance(.24f);
            Assert.That(pistol.IsRaising, Is.True);
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(.01f);
            Assert.That(pistol.IsAiming, Is.True);
            int sequence = pistol.ShotSequence;
            Assert.That(pistol.TryFire(), Is.True);
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            Assert.That(pistol.ShotSequence, Is.EqualTo(sequence + 1));
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(.39f);
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(3f);
            Assert.That(pistol.CanFire, Is.True);
            Assert.That(pistol.Rounds, Is.EqualTo(7), "Advancing time must never fire a rejected request.");
            Assert.That(pistol.ShotSequence, Is.EqualTo(sequence + 1));
            Assert.That(pistol.TryFire(), Is.True);
            Assert.That(pistol.Rounds, Is.EqualTo(6));
        }

        [Test]
        public void ReloadCommitsOnlyOnCompletionAndRequiresAimToRiseAgain()
        {
            var pistol = AimedPistol();
            Assert.That(pistol.TryReload(), Is.False, "A full magazine must not start a reload.");
            Assert.That(pistol.TryFire(), Is.True);
            Assert.That(pistol.TryReload(), Is.True);
            Assert.That(pistol.TryReload(), Is.False);
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(1.79f);
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            Assert.That(pistol.IsReloading, Is.True);
            pistol.CancelReload();
            Assert.That(pistol.Rounds, Is.EqualTo(7), "Interrupted reloads cannot create or refund ammunition.");
            Assert.That(pistol.IsRaising, Is.True);
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(1.8f);
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.IsReloading, Is.False);
            Assert.That(pistol.IsRaising, Is.True);
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(.25f);
            Assert.That(pistol.TryFire(), Is.True);
        }

        [Test]
        public void EmptyMagazineCannotFireAndInfiniteReserveRefillsTheWholeMagazine()
        {
            var pistol = AimedPistol();
            for (int magazine = 0; magazine < 2; magazine++)
            {
                for (int round = 0; round < 8; round++)
                {
                    Assert.That(pistol.TryFire(), Is.True);
                    pistol.Advance(.4f);
                }
                Assert.That(pistol.Rounds, Is.Zero);
                Assert.That(pistol.TryFire(), Is.False);
                Assert.That(pistol.TryReload(), Is.True);
                pistol.Advance(pistol.Settings.ReloadSeconds + pistol.Settings.RaiseSeconds);
                Assert.That(pistol.Rounds, Is.EqualTo(8));
                Assert.That(pistol.CanFire, Is.True);
            }
        }

        [Test]
        public void ReloadAndAimCarryTheSameTimeThroughFineStepsAndAHitch()
        {
            var fine = AimedPistol();
            var hitch = AimedPistol();
            Assert.That(fine.TryFire() && hitch.TryFire(), Is.True);
            Assert.That(fine.TryReload() && hitch.TryReload(), Is.True);
            for (int step = 0; step < 246; step++) fine.Advance(1f / 120f);
            hitch.Advance(246f / 120f);
            Assert.That(fine.Rounds, Is.EqualTo(hitch.Rounds));
            Assert.That(fine.IsReloading, Is.EqualTo(hitch.IsReloading));
            Assert.That(fine.AimProgress, Is.EqualTo(hitch.AimProgress).Within(Eps));
            Assert.That(fine.CanFire, Is.True);
            Assert.That(hitch.CanFire, Is.True);
        }

        [Test]
        public void CancelAndZeroElapsedPreserveSpentAmmoAndCooldownWhileResetRestoresTheRound()
        {
            var pistol = AimedPistol();
            Assert.That(pistol.TryFire(), Is.True);
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(0f);
            Assert.That(pistol.ReloadProgress, Is.Zero);
            Assert.That(pistol.CooldownRemaining, Is.EqualTo(.4f).Within(Eps));
            pistol.CancelAction();
            pistol.CancelAction();
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            Assert.That(pistol.IsReloading || pistol.AimRequested || pistol.IsAiming, Is.False);
            Assert.That(pistol.CooldownRemaining, Is.EqualTo(.4f).Within(Eps));
            int sequence = pistol.ShotSequence;
            pistol.Reset();
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.CooldownRemaining, Is.Zero);
            Assert.That(pistol.ShotSequence, Is.Not.EqualTo(sequence));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidClockCannotChangeAnActiveReload(float seconds)
        {
            var pistol = AimedPistol();
            pistol.TryFire();
            pistol.TryReload();
            Assert.Throws<ArgumentOutOfRangeException>(() => pistol.Advance(seconds));
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            Assert.That(pistol.ReloadProgress, Is.Zero);
            Assert.That(pistol.CooldownRemaining, Is.EqualTo(.4f).Within(Eps));
        }

        [TestCase(MeleeBodyRegion.Head, 50f)]
        [TestCase(MeleeBodyRegion.Torso, 25f)]
        [TestCase(MeleeBodyRegion.LeftArm, 12.5f)]
        [TestCase(MeleeBodyRegion.RightArm, 12.5f)]
        [TestCase(MeleeBodyRegion.LeftLeg, 18.75f)]
        [TestCase(MeleeBodyRegion.RightLeg, 18.75f)]
        public void ProjectileRegionsHaveTheSameDamageFromFrontAndRear(MeleeBodyRegion region, float expected)
        {
            foreach (MeleeHitSide side in new[] { MeleeHitSide.Front, MeleeHitSide.Rear })
            {
                var actor = new MeleeCombatant();
                var location = new MeleeHitLocation(region, side);
                Assert.That(actor.ReceiveProjectileHit(25f, location), Is.EqualTo(MeleeHitResult.Hit));
                Assert.That(actor.Health, Is.EqualTo(100f - expected).Within(Eps));
                Assert.That(actor.IsDefeated, Is.False);
                Assert.That(actor.Stamina, Is.EqualTo(100f));
            }
        }

        [TestCase(0f)]
        [TestCase(.2f)]
        public void ProjectileBypassesBothParryAndHeldGuardWithoutSpendingBreath(float guardAge)
        {
            var actor = new MeleeCombatant();
            actor.SetBlocking(true);
            actor.Advance(guardAge);
            Assert.That(actor.IsBlocking, Is.True);
            Assert.That(actor.ReceiveProjectileHit(25f), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.EqualTo(75f));
            Assert.That(actor.Stamina, Is.EqualTo(100f));
            Assert.That(actor.IsBlocking, Is.False);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Stagger));
        }

        [Test]
        public void ProjectileInterruptsIntentButPreservesExistingStunAndPhysicalRecovery()
        {
            var actor = new MeleeCombatant();
            Assert.That(actor.TryStartAttack(), Is.True);
            Assert.That(actor.ReceiveProjectileHit(5f), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.IsAttacking || actor.HasBufferedAttack, Is.False);
            actor.Reset();
            actor.ReceiveHit(5f, 0f, false);
            float previousStun = actor.ActionRemaining;
            actor.ReceiveProjectileHit(5f);
            Assert.That(actor.ActionRemaining, Is.EqualTo(previousStun).Within(Eps));
            actor.BeginKnockdown();
            actor.ReceiveProjectileHit(5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.KnockedDown));
            actor.BeginRise();
            actor.ReceiveProjectileHit(5f);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Rising));
            actor.ReceiveProjectileHit(100f);
            Assert.That(actor.Health, Is.Zero);
            Assert.That(actor.IsDefeated, Is.True);
            Assert.That(actor.ReceiveProjectileHit(100f), Is.EqualTo(MeleeHitResult.Ignored));
        }

        [Test]
        public void ProjectileCanDefeatWithAnOrdinaryHeadHitAndZeroDamageNeverInterrupts()
        {
            var actor = new MeleeCombatant();
            var rearHead = new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Rear);
            Assert.That(actor.TryStartAttack(), Is.True);
            int sequence = actor.AttackSequence;
            Assert.That(actor.ReceiveProjectileHit(0f, rearHead), Is.EqualTo(MeleeHitResult.Ignored));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.IsAttacking, Is.True);
            Assert.That(actor.ReceiveProjectileHit(50f, new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front)),
                Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.Zero, "Projectile head damage must not inherit the crowbar's .99 max-health cap.");
            Assert.That(actor.IsDefeated, Is.True);
        }

        [Test]
        public void InvalidProjectileDamageOrStaggerCannotChangeTheBody()
        {
            var actor = new MeleeCombatant();
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.ReceiveProjectileHit(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.ReceiveProjectileHit(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.ReceiveProjectileHit(25f, staggerSeconds: 0f));
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Ready));
        }

        private static PistolState AimedPistol()
        {
            var pistol = new PistolState();
            pistol.SetAim(true);
            pistol.Advance(pistol.Settings.RaiseSeconds);
            return pistol;
        }
    }
}
