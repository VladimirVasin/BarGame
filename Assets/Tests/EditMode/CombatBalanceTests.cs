using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Headless proof that the range's numbers leave no dominant habit: the turtle breaks,
    /// the tap-guard runs dry, a read beats a spam, a rhythm does not, a held charge
    /// preserves breath, a step earns its counter, and trades stay symmetric.
    /// </summary>
    public sealed class CombatBalanceTests
    {
        private const int Rounds = 100;
        private const float RoundSeconds = 60f;

        private static ICombatPolicy Make(string name) => name switch
        {
            "Turtle" => new CombatPolicies.Turtle(),
            "TapGuard" => new CombatPolicies.TapGuard(),
            "Spammer" => new CombatPolicies.Spammer(),
            "ChainPresser" => new CombatPolicies.ChainPresser(),
            "ParryBot" => new CombatPolicies.ParryBot(),
            "RhythmMasher" => new CombatPolicies.RhythmMasher(),
            "ChargeHolder" => new CombatPolicies.ChargeHolder(),
            "StepBot" => new CombatPolicies.StepBot(),
            "BlockCounter" => new CombatPolicies.BlockCounter(),
            "Trader" => new CombatPolicies.Trader(),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        private static (float winRate, float medianTtk, List<CombatDuelSimulator> duels) Series(string a, string b)
        {
            var duels = new List<CombatDuelSimulator>(Rounds);
            int wins = 0;
            var ttk = new List<float>();
            for (int round = 0; round < Rounds; round++)
            {
                var duel = new CombatDuelSimulator(Make(a), Make(b), (uint)(round * 2654435761u + 17u));
                duel.Run(RoundSeconds);
                duels.Add(duel);
                if (duel.Winner == 0) wins++;
                if (duel.Finished) ttk.Add(duel.Clock);
            }
            ttk.Sort();
            float median = ttk.Count == 0 ? float.PositiveInfinity : ttk[ttk.Count / 2];
            return (wins / (float)Rounds, median, duels);
        }

        [Test]
        public void SpammerBreaksAHeldGuardQuicklyAndWinsTheRound()
        {
            (float winRate, float median, List<CombatDuelSimulator> duels) = Series("Spammer", "Turtle");
            Assert.That(winRate, Is.GreaterThanOrEqualTo(.9f), "A held guard must be a losing strategy.");
            Assert.That(duels.Where(d => d.GuardBrokenAt >= 0f).Select(d => d.GuardBrokenAt).DefaultIfEmpty(float.PositiveInfinity).Max(),
                Is.LessThanOrEqualTo(7f), "Five blocks of breath and the sixth contact breaks it.");
            Assert.That(median, Is.LessThan(40f));
        }

        [Test]
        public void SpammerBreaksATapGuardWithinSixteenSeconds()
        {
            (float winRate, _, List<CombatDuelSimulator> duels) = Series("Spammer", "TapGuard");
            // A break keeps the remaining breath, so the honest sign of exhaustion is the break itself.
            float slowest = duels.Select(d => d.GuardBrokenAt < 0f ? float.PositiveInfinity : d.GuardBrokenAt).Max();
            Assert.That(slowest, Is.LessThanOrEqualTo(16f), "A guard that only taps still cannot out-breathe free swings.");
            Assert.That(winRate, Is.GreaterThanOrEqualTo(.9f));
        }

        [Test]
        public void ReadingTheSwingBeatsSpammingButARhythmDoesNot()
        {
            (float parryWins, _, List<CombatDuelSimulator> parryDuels) = Series("ParryBot", "Spammer");
            Assert.That(parryWins, Is.GreaterThanOrEqualTo(.7f), "A timed guard press is the answer to spam.");
            Assert.That(parryDuels.Sum(d => d.Parries[0]), Is.GreaterThan(Rounds), "Reads must actually parry.");
            (float masherWins, _, _) = Series("RhythmMasher", "Spammer");
            // A rhythm still catches some swings, but the re-arm makes it a losing lottery;
            // against the range's heavies and feints it fares worse than against pure spam.
            Assert.That(masherWins, Is.LessThanOrEqualTo(.45f), "Mashing the guard on a rhythm loses more than it wins.");
            Assert.That(masherWins, Is.LessThan(parryWins), "A read must beat a rhythm.");
        }

        [Test]
        public void AHeldChargePreservesBreathAndNeverFiresItself()
        {
            var duel = new CombatDuelSimulator(new CombatPolicies.ChargeHolder(), new CombatPolicies.Turtle(), 5u);
            duel.Run(7f);
            Assert.That(duel.Actors[0].IsCharging, Is.True, "A held cap never fires by itself.");
            MeleeCombatSettings s = MeleeCombatSettings.Crowbar;
            Assert.That(duel.Actors[0].Stamina, Is.EqualTo(s.MaxStamina - s.ChargeStaminaCost).Within(.001f));
            Assert.That(duel.BreathEmptyAt, Is.LessThan(0f));
            Assert.That(duel.Contacts[0], Is.Zero);
        }

        [Test]
        public void SteppingOffTheLineEarnsCounterHitsAgainstSpam()
        {
            (float winRate, _, List<CombatDuelSimulator> duels) = Series("StepBot", "Spammer");
            float counterHitsPerRound = duels.Sum(d => d.CounterHits[0]) / (float)Rounds;
            Assert.That(counterHitsPerRound, Is.GreaterThanOrEqualTo(2f), "A step at the tell turns the whiff into a counter-hit.");
            Assert.That(winRate, Is.GreaterThanOrEqualTo(.5f));
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

        [Test]
        public void NoHabitDominatesAndFightsStayShort()
        {
            string[] fighters = { "Spammer", "ChainPresser", "ParryBot", "StepBot", "BlockCounter", "Trader" };
            var wins = fighters.ToDictionary(f => f, _ => 0f);
            var ttk = new List<float>();
            foreach (string a in fighters)
            foreach (string b in fighters)
            {
                if (a == b) continue;
                (float winRate, float median, _) = Series(a, b);
                wins[a] += winRate / (fighters.Length - 1);
                if (!float.IsInfinity(median)) ttk.Add(median);
            }
            foreach (KeyValuePair<string, float> entry in wins)
                Assert.That(entry.Value, Is.LessThanOrEqualTo(.85f), entry.Key + " must not beat every other habit.");
            ttk.Sort();
            Assert.That(ttk[ttk.Count / 2], Is.InRange(5f, 45f), "A typical fight lasts seconds, not a minute.");
        }
    }
}
