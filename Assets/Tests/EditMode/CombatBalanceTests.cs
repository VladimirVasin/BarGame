using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>Headless checks for the two-action crowbar duel: fixed strikes and timed parries.</summary>
    public sealed class CombatBalanceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void HeldOrRepeatedGuardCannotSpendBreathOrCreateGuardBreaks(bool tapping)
        {
            ICombatPolicy defense = tapping ? (ICombatPolicy)new CombatPolicies.TapGuard() : new CombatPolicies.Turtle();
            var duel = new CombatDuelSimulator(new CombatPolicies.Spammer(), defense, 5u);
            duel.Run(10f);
            Assert.That(duel.Contacts[0], Is.GreaterThan(0));
            Assert.That(duel.GuardBreaks[0] + duel.GuardBreaks[1], Is.Zero);
            Assert.That(duel.GuardBrokenAt, Is.LessThan(0f));
            Assert.That(duel.BreathEmptyAt, Is.LessThan(0f));
            Assert.That(duel.Actors[0].AttackPower, Is.Zero);
            Assert.That(duel.Actors[1].AttackPower, Is.Zero);
        }

        [Test]
        public void ReadingTheContactCanParryAnOrdinaryStrike()
        {
            var duel = new CombatDuelSimulator(new CombatPolicies.ParryBot(), new CombatPolicies.Spammer(), 17u);
            duel.Run(10f);
            Assert.That(duel.Parries[0], Is.GreaterThan(0), "The observed contact opens a usable fresh-press parry.");
            Assert.That(duel.GuardBreaks[0] + duel.GuardBreaks[1], Is.Zero);
            Assert.That(duel.Actors[0].AttackPower, Is.Zero);
            Assert.That(duel.Actors[1].AttackPower, Is.Zero);
        }

        [Test]
        public void RetiredChargeCommandsCannotCommitASwingOrSpendEffort()
        {
            var duel = new CombatDuelSimulator(new CombatPolicies.ChargeHolder(), new CombatPolicies.Turtle(), 5u);
            duel.Run(7f);
            Assert.That(duel.Actors[0].Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(duel.Actors[0].IsCharging || duel.Actors[0].HasBufferedCharge, Is.False);
            Assert.That(duel.Actors[0].Stamina, Is.EqualTo(MeleeCombatSettings.Crowbar.MaxStamina));
            Assert.That(duel.Contacts[0], Is.Zero);
            Assert.That(duel.Actors[0].ReleaseCharge(), Is.False);
            Assert.That(duel.Actors[0].Phase, Is.EqualTo(MeleePhase.Ready));
        }

        [Test]
        public void TradesStaySymmetric()
        {
            var duel = new CombatDuelSimulator(new CombatPolicies.Trader(), new CombatPolicies.Trader(), 9u);
            for (int tick = 0; tick < 120 * 30 && !duel.Finished; tick++)
            {
                duel.Tick();
                Assert.That(duel.Actors[0].Health, Is.EqualTo(duel.Actors[1].Health).Within(.001f),
                    "Two identical fighters swinging together must always share a health.");
            }
            Assert.That(duel.Contacts[0], Is.GreaterThan(0));
        }
    }
}
