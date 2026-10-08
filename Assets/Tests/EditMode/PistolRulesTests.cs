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
            Assert.That(pistol.ReloadPending, Is.True);
            Assert.That(pistol.IsRaising, Is.False);
            Assert.That(pistol.ReloadElapsed, Is.EqualTo(1.79f).Within(Eps));
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(.01f);
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.IsReloading, Is.False);
            Assert.That(pistol.IsRaising, Is.True);
            Assert.That(pistol.TryFire(), Is.False);
            pistol.Advance(.25f);
            Assert.That(pistol.TryFire(), Is.True);
        }

        [TestCase(.25f, false)]
        [TestCase(.55f, false)]
        [TestCase(.8f, false)]
        [TestCase(1.0f, false)]
        [TestCase(1.3f, true)]
        [TestCase(1.5f, true)]
        [TestCase(1.6f, true)]
        public void InterruptedExchangeRetainsTheMagazineStageAndResumesWithoutCreatingAmmo(float elapsed, bool attached)
        {
            var pistol = AimedPistol();
            pistol.TryFire();
            pistol.TryReload();
            pistol.Advance(elapsed);
            pistol.CancelAction();
            Assert.That(pistol.ReloadPending, Is.True);
            Assert.That(pistol.IsReloading, Is.False);
            Assert.That(pistol.MagazineAttached, Is.EqualTo(attached));
            float slide = pistol.SlideBack;
            pistol.Advance(3f);
            pistol.SetAim(true);
            Assert.That(pistol.ReloadElapsed, Is.EqualTo(elapsed).Within(Eps));
            Assert.That(pistol.SlideBack, Is.EqualTo(slide).Within(Eps));
            Assert.That(pistol.TryFire(), Is.False, "A physically incomplete exchange cannot shoot, even with old rounds remaining.");
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(pistol.Settings.ReloadSeconds - elapsed - .001f);
            Assert.That(pistol.Rounds, Is.EqualTo(7));
            pistol.Advance(.001f);
            Assert.That(pistol.ReloadPending, Is.False);
            Assert.That(pistol.MagazineAttached, Is.True);
            Assert.That(pistol.Rounds, Is.EqualTo(8));
            Assert.That(pistol.IsRaising, Is.True);
        }

        [Test]
        public void SlideReturnsAfterALiveShotAndLocksUntilTheEmptyReloadRackReleasesIt()
        {
            var pistol = new PistolState(new PistolSettings(magazineCapacity: 2));
            pistol.SetAim(true);
            pistol.Advance(pistol.Settings.RaiseSeconds);
            Assert.That(pistol.TryFire(), Is.True);
            Assert.That(pistol.SlideBack, Is.Zero);
            pistol.Advance(.035f);
            Assert.That(pistol.SlideBack, Is.EqualTo(1f).Within(Eps));
            pistol.Advance(.06f);
            Assert.That(pistol.SlideBack, Is.Zero.Within(Eps));
            pistol.Advance(.305f);
            Assert.That(pistol.TryFire(), Is.True);
            pistol.Advance(.035f);
            Assert.That(pistol.Rounds, Is.Zero);
            Assert.That(pistol.SlideBack, Is.EqualTo(1f).Within(Eps));
            pistol.Advance(1f);
            Assert.That(pistol.SlideBack, Is.EqualTo(1f));
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(1.5f);
            Assert.That(pistol.SlideBack, Is.EqualTo(1f).Within(Eps));
            pistol.Advance(.08f);
            Assert.That(pistol.SlideBack, Is.Zero.Within(Eps));
            Assert.That(pistol.Rounds, Is.Zero, "Magazine insertion and slide release do not commit ammunition early.");
            pistol.CancelAction();
            Assert.That(pistol.SlideBack, Is.Zero.Within(Eps));
            Assert.That(pistol.TryFire(), Is.False);
            Assert.That(pistol.TryReload(), Is.True);
            pistol.Advance(.22f);
            Assert.That(pistol.Rounds, Is.EqualTo(2));
            Assert.That(pistol.SlideBack, Is.Zero);
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

        [TestCase(MeleeBodyRegion.Head, 100f)]
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
                Assert.That(actor.IsDefeated, Is.EqualTo(region == MeleeBodyRegion.Head));
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

        [TestCase(MeleeHitSide.Front, 100f)]
        [TestCase(MeleeHitSide.Rear, 250f)]
        [TestCase(MeleeHitSide.Left, 500f)]
        [TestCase(MeleeHitSide.Right, 100f)]
        [TestCase(MeleeHitSide.Top, 100f)]
        [TestCase(MeleeHitSide.Bottom, 100f)]
        public void ProjectileHeadHitImmediatelyDefeatsAnyHealthPoolAndZeroDamageNeverInterrupts(MeleeHitSide side, float maxHealth)
        {
            var actor = new MeleeCombatant(new MeleeCombatSettings(maxHealth: maxHealth));
            var head = new MeleeHitLocation(MeleeBodyRegion.Head, side);
            Assert.That(actor.TryStartAttack(), Is.True);
            int sequence = actor.AttackSequence;
            Assert.That(actor.ReceiveProjectileHit(0f, head), Is.EqualTo(MeleeHitResult.Ignored));
            Assert.That(actor.AttackSequence, Is.EqualTo(sequence));
            Assert.That(actor.IsAttacking, Is.True);
            Assert.That(actor.ReceiveProjectileHit(.01f, head),
                Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.Zero, "A positive head wound must be terminal independently of HP and side.");
            Assert.That(actor.IsDefeated, Is.True);
            Assert.That(actor.ReceiveProjectileHit(25f, head), Is.EqualTo(MeleeHitResult.Ignored));

            actor.Reset();
            actor.BeginKnockdown();
            actor.BeginRise();
            Assert.That(actor.ReceiveProjectileHit(25f, head), Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(actor.Health, Is.Zero);
            Assert.That(actor.Phase, Is.EqualTo(MeleePhase.Defeated), "Head wounds cannot preserve a recoverable rise.");
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
