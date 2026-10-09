using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ShotgunRulesTests
    {
        private const float Eps = .0001f;

        [Test]
        public void EachRequestSpendsExactlyOneChamberAndEmptyBarrelsCannotFire()
        {
            var shotgun = new ShotgunState();
            Assert.That(shotgun.TryReload(), Is.False);
            Assert.That(shotgun.TryFire(), Is.False);
            shotgun.SetAim(true);
            shotgun.Advance(.349f);
            Assert.That(shotgun.IsRaising, Is.True);
            Assert.That(shotgun.TryFire(), Is.False);
            shotgun.Advance(.001f);
            int sequence = shotgun.ShotSequence;
            for (int barrel = 0; barrel < 2; barrel++)
            {
                Assert.That(shotgun.NextBarrel, Is.EqualTo(barrel));
                Assert.That(shotgun.TryFire(), Is.True);
                Assert.That(shotgun.LastFiredBarrel, Is.EqualTo(barrel));
                Assert.That(shotgun.ChamberLoaded(barrel), Is.False);
                Assert.That(shotgun.ChamberSpent(barrel), Is.True);
                Assert.That(shotgun.Rounds, Is.EqualTo(1 - barrel));
                Assert.That(shotgun.ShotSequence, Is.EqualTo(sequence + barrel + 1));
                Assert.That(shotgun.TryFire(), Is.False);
                shotgun.Advance(.549f);
                Assert.That(shotgun.TryFire(), Is.False);
                shotgun.Advance(10f);
                Assert.That(shotgun.ShotSequence, Is.EqualTo(sequence + barrel + 1),
                    "Rejected fire requests cannot become automatic shots.");
            }
            Assert.That(shotgun.NextBarrel, Is.EqualTo(-1));
            Assert.That(shotgun.TryFire(), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ReloadEjectsOnlySpentCasesAndCommitsEachNeededShellAtItsOwnBoundary(int shots)
        {
            var shotgun = FiredShotgun(shots);
            Assert.That(shotgun.TryReload(), Is.True);
            Assert.That(shotgun.ReloadChamberMask, Is.EqualTo(shots == 1 ? 1 : 3));
            shotgun.Advance(.45f);
            Assert.That(shotgun.BreakOpen01, Is.EqualTo(1f).Within(Eps));
            Assert.That(shotgun.ChamberSpent(0), Is.True);
            shotgun.Advance(.199f);
            Assert.That(shotgun.ChamberSpent(0), Is.True);
            shotgun.Advance(.001f);
            Assert.That(shotgun.ChamberSpent(0) || shotgun.ChamberSpent(1), Is.False);
            Assert.That(shotgun.ChamberLoaded(1), Is.EqualTo(shots == 1),
                "An unused live shell remains in its chamber throughout the reload.");
            shotgun.Advance(.949f);
            Assert.That(shotgun.ChamberLoaded(0), Is.False);
            shotgun.Advance(.001f);
            Assert.That(shotgun.ChamberLoaded(0), Is.True);
            Assert.That(shotgun.Rounds, Is.EqualTo(shots == 1 ? 2 : 1));
            shotgun.Advance(.599f);
            Assert.That(shotgun.ChamberLoaded(1), Is.EqualTo(shots == 1));
            shotgun.Advance(.001f);
            Assert.That(shotgun.Rounds, Is.EqualTo(2));
            Assert.That(shotgun.TryFire(), Is.False, "Loaded shells cannot fire while the action is open.");
            shotgun.Advance(.599f);
            Assert.That(shotgun.ReloadPending, Is.True);
            shotgun.Advance(.001f);
            Assert.That(shotgun.MagazineAttached, Is.True);
            Assert.That(shotgun.ReloadPending, Is.False);
            Assert.That(shotgun.IsRaising, Is.True);
            shotgun.Advance(.35f);
            Assert.That(shotgun.CanFire, Is.True);
        }

        [TestCase(0f)]
        [TestCase(.2f)]
        [TestCase(.65f)]
        [TestCase(1.6f)]
        [TestCase(2.2f)]
        [TestCase(2.6f)]
        public void CancellationPreservesChambersAngleAndClockUntilExplicitResume(float elapsed)
        {
            var shotgun = FiredShotgun(2);
            shotgun.TryReload();
            shotgun.Advance(elapsed);
            int rounds = shotgun.Rounds;
            float angle = shotgun.BreakOpen01;
            bool firstSpent = shotgun.ChamberSpent(0), secondSpent = shotgun.ChamberSpent(1);
            shotgun.CancelAction();
            shotgun.CancelAction();
            shotgun.Advance(20f);
            shotgun.SetAim(true);
            Assert.That(shotgun.ReloadPending, Is.True);
            Assert.That(shotgun.IsReloading, Is.False);
            Assert.That(shotgun.Rounds, Is.EqualTo(rounds));
            Assert.That(shotgun.ReloadElapsed, Is.EqualTo(elapsed).Within(Eps));
            Assert.That(shotgun.BreakOpen01, Is.EqualTo(angle).Within(Eps));
            Assert.That(shotgun.ChamberSpent(0), Is.EqualTo(firstSpent));
            Assert.That(shotgun.ChamberSpent(1), Is.EqualTo(secondSpent));
            Assert.That(shotgun.TryFire(), Is.False);
            Assert.That(shotgun.TryReload(), Is.True);
            shotgun.Advance(shotgun.Settings.ReloadSeconds - elapsed + shotgun.Settings.RaiseSeconds);
            Assert.That(shotgun.Rounds, Is.EqualTo(2));
            Assert.That(shotgun.ReloadPending, Is.False);
            Assert.That(shotgun.CanFire, Is.True);
            Assert.That(shotgun.TryReload(), Is.False, "Resuming cannot duplicate shells or queue another full reload.");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void FineStepsAndAHitchCrossTheSameShellAndRaiseBoundaries(int shots)
        {
            var fine = FiredShotgun(shots);
            var hitch = FiredShotgun(shots);
            fine.TryReload();
            hitch.TryReload();
            for (int step = 0; step < 378; step++) fine.Advance(1f / 120f);
            hitch.Advance(378f / 120f);
            Assert.That(fine.Rounds, Is.EqualTo(hitch.Rounds));
            Assert.That(fine.ReloadPending, Is.EqualTo(hitch.ReloadPending));
            Assert.That(fine.AimProgress, Is.EqualTo(hitch.AimProgress).Within(Eps));
            Assert.That(fine.CanFire && hitch.CanFire, Is.True);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidElapsedCannotMutateACommittedShotOrAnActiveReload(float seconds)
        {
            var shotgun = FiredShotgun(1, advanceCooldown: false);
            shotgun.TryReload();
            Assert.Throws<ArgumentOutOfRangeException>(() => shotgun.Advance(seconds));
            Assert.That(shotgun.Rounds, Is.EqualTo(1));
            Assert.That(shotgun.ReloadElapsed, Is.Zero);
            Assert.That(shotgun.CooldownRemaining, Is.EqualTo(.55f).Within(Eps));
            shotgun.Advance(0f);
            Assert.That(shotgun.ReloadElapsed, Is.Zero);
            Assert.That(shotgun.CooldownRemaining, Is.EqualTo(.55f).Within(Eps));
            int sequence = shotgun.ShotSequence;
            shotgun.Reset();
            Assert.That(shotgun.Rounds, Is.EqualTo(2));
            Assert.That(shotgun.ChamberSpent(0) || shotgun.ChamberSpent(1), Is.False);
            Assert.That(shotgun.ReloadPending || shotgun.AimRequested, Is.False);
            Assert.That(shotgun.CooldownRemaining, Is.Zero);
            Assert.That(shotgun.ShotSequence, Is.Not.EqualTo(sequence));
        }

        [TestCase(0f, 26f, 420f)]
        [TestCase(1f, 24f, 380f)]
        [TestCase(2f, 22f, 290f)]
        [TestCase(3f, 20f, 200f)]
        [TestCase(8f, 10f, 50f)]
        [TestCase(15f, 4f, 10f)]
        [TestCase(25f, 1.5f, 2f)]
        [TestCase(40f, 0f, 0f)]
        [TestCase(100f, 0f, 0f)]
        public void DistanceScalesEveryPelletBeforeRegionalDamage(float distance, float expected, float momentum)
        {
            var settings = ShotgunSettings.Prototype;
            Assert.That(settings.PelletCount, Is.EqualTo(12));
            Assert.That(settings.ResolvePelletDamage(distance), Is.EqualTo(expected).Within(Eps));
            Assert.That(settings.ResolvePelletMomentum(distance) * settings.PelletCount, Is.EqualTo(momentum).Within(Eps));
            if (distance < 40f)
            {
                Assert.That(settings.ResolvePelletDamage(distance + .01f), Is.LessThan(expected));
                Assert.That(settings.ResolvePelletMomentum(distance + .01f), Is.LessThan(momentum / settings.PelletCount));
            }
            var torso = new MeleeCombatant();
            for (int pellet = 0; pellet < settings.PelletCount; pellet++)
                torso.ReceiveProjectileHit(settings.ResolvePelletDamage(distance), profile: settings.DamageProfile);
            Assert.That(torso.Health, Is.EqualTo(Math.Max(0f, 100f - settings.PelletCount * expected)).Within(Eps));
            Assert.That(settings.ResolvePelletHeadTrauma(distance) * settings.PelletCount,
                Is.EqualTo(expected / 24f * 14f).Within(Eps));
        }

        [TestCase(MeleeBodyRegion.Head, 30f)]
        [TestCase(MeleeBodyRegion.Torso, 15f)]
        [TestCase(MeleeBodyRegion.LeftArm, 7.5f)]
        [TestCase(MeleeBodyRegion.RightArm, 7.5f)]
        [TestCase(MeleeBodyRegion.LeftLeg, 11.25f)]
        [TestCase(MeleeBodyRegion.RightLeg, 11.25f)]
        public void SinglePelletUsesRegionalDamageWithoutThePistolsTerminalHeadPolicy(MeleeBodyRegion region, float damage)
        {
            foreach (MeleeHitSide side in new[] { MeleeHitSide.Front, MeleeHitSide.Rear })
            {
                var body = new MeleeCombatant();
                body.SetBlocking(true);
                Assert.That(body.ReceiveProjectileHit(15f, new MeleeHitLocation(region, side),
                    profile: ProjectileDamageProfile.Shotgun), Is.EqualTo(MeleeHitResult.Hit));
                Assert.That(body.Health, Is.EqualTo(100f - damage).Within(Eps));
                Assert.That(body.Stamina, Is.EqualTo(100f));
                Assert.That(body.IsDefeated || body.IsBlocking, Is.False);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void SpreadHasTwelveDistinctReproducibleDirectionsInsideTheThreeDegreeCone(int sequence)
        {
            var settings = ShotgunSettings.Prototype;
            double maxRadius = Math.Tan(3d * Math.PI / 180d);
            for (int pellet = 0; pellet < settings.PelletCount; pellet++)
            {
                var spread = settings.PelletSpread(pellet, sequence);
                Assert.That(spread, Is.EqualTo(settings.PelletSpread(pellet, sequence)));
                Assert.That(spread, Is.Not.EqualTo(settings.PelletSpread(pellet, unchecked(sequence + 1))));
                double radius = Math.Sqrt(spread.x * spread.x + spread.y * spread.y);
                Assert.That(radius, Is.GreaterThan(0d).And.LessThan(maxRadius));
                for (int earlier = 0; earlier < pellet; earlier++)
                    Assert.That(spread, Is.Not.EqualTo(settings.PelletSpread(earlier, sequence)));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.PelletSpread(-1, sequence));
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.PelletSpread(settings.PelletCount, sequence));
        }

        [Test]
        public void SharedFirearmAdapterRetainsThePistolMagazineAndTerminalHeadContract()
        {
            var pistol = new PistolState();
            IFirearmState firearm = pistol;
            Assert.That(firearm.Settings, Is.SameAs(pistol.Settings));
            Assert.That(firearm.Settings.MagazineCapacity, Is.EqualTo(8));
            firearm.SetAim(true);
            firearm.Advance(.25f);
            Assert.That(firearm.TryFire(), Is.True);
            Assert.That(firearm.CooldownRemaining, Is.EqualTo(.4f).Within(Eps));
            firearm.TryReload();
            firearm.Advance(1.3f);
            Assert.That(firearm.MagazineAttached, Is.True);
            Assert.That(firearm.Rounds, Is.EqualTo(7));
            firearm.CancelAction();
            Assert.That(firearm.ReloadElapsed, Is.EqualTo(1.3f).Within(Eps));
            firearm.TryReload();
            firearm.Advance(.5f);
            Assert.That(firearm.Rounds, Is.EqualTo(8));
            var body = new MeleeCombatant(new MeleeCombatSettings(maxHealth: 500f));
            body.ReceiveProjectileHit(.01f, new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front));
            Assert.That(body.IsDefeated, Is.True);
        }

        private static ShotgunState FiredShotgun(int shots, bool advanceCooldown = true)
        {
            var shotgun = new ShotgunState();
            shotgun.SetAim(true);
            shotgun.Advance(shotgun.Settings.RaiseSeconds);
            for (int shot = 0; shot < shots; shot++)
            {
                Assert.That(shotgun.TryFire(), Is.True);
                if (advanceCooldown) shotgun.Advance(shotgun.Settings.FireCooldownSeconds);
            }
            return shotgun;
        }
    }
}
