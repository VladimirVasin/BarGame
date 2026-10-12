using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CombatEncounterRulesTests
    {
        [Test]
        public void RandomScheduleIsSeededWithinBoundsAndOneLargeFrameCannotAccumulateSpawns()
        {
            var first = new EndlessCombatState();
            var second = new EndlessCombatState();
            first.Reset(CombatEncounterSettings.Default, 321);
            second.Reset(CombatEncounterSettings.Default, 321);
            Assert.That(first.RemainingSeconds, Is.InRange(2f, 4f));
            Assert.That(first.RemainingSeconds, Is.EqualTo(second.RemainingSeconds));
            float pausedRemaining = first.RemainingSeconds;
            Assert.That(first.Advance(0f, 0), Is.False);
            Assert.That(first.RemainingSeconds, Is.EqualTo(pausedRemaining));
            for (int liveCount = 1; liveCount <= 5; liveCount++)
            {
                Assert.That(first.Advance(100f, liveCount - 1), Is.True);
                Assert.That(second.Advance(100f, liveCount - 1), Is.True);
                first.ConfirmSpawn(liveCount);
                second.ConfirmSpawn(liveCount);
                Assert.That(first.RemainingSeconds, Is.EqualTo(second.RemainingSeconds));
                Assert.That(first.Advance(0f, liveCount), Is.False,
                    "Elapsed time before creation must not carry into the next creation.");
                if (liveCount < 5) Assert.That(first.RemainingSeconds, Is.InRange(3f, 7f));
            }
            Assert.That(first.IsWaiting, Is.False);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void FilledCapacityDiscardsPendingTimeAndFreeingAPlaceStartsFreshInterval(int capacity)
        {
            var state = new EndlessCombatState();
            var settings = new CombatEncounterSettings(capacity, 2f, 2f, 3f, 3f);
            state.Reset(settings, 0);
            state.Advance(1f, 0);
            state.Observe(false, capacity);
            Assert.That(state.IsWaiting, Is.False);
            Assert.That(state.Advance(100f, capacity), Is.False);
            state.Observe(false, capacity - 1, 1);
            Assert.That(state.Kills, Is.EqualTo(1));
            Assert.That(state.RemainingSeconds, Is.EqualTo(3f));
            Assert.That(state.Advance(2f, capacity - 1), Is.False);
            Assert.That(state.Advance(1f, capacity - 1), Is.True);
            state.ConfirmSpawn(capacity);
            Assert.That(state.IsWaiting, Is.False);
            Assert.That(state.Advance(100f, capacity), Is.False);
        }

        [Test]
        public void BlockedSpawnRetriesOnlyOnePendingOpponentAndHeroDefeatCancelsIt()
        {
            var state = new EndlessCombatState();
            state.Reset(new CombatEncounterSettings(5, 2f, 2f, 3f, 3f), 0);
            Assert.Throws<InvalidOperationException>(() => state.ConfirmSpawn(1));
            Assert.That(state.Advance(2f, 0), Is.True);
            state.DeferSpawn();
            Assert.That(state.Advance(.25f, 0), Is.False);
            Assert.That(state.Advance(.25f, 0), Is.True);
            state.Observe(true, 0, 2);
            Assert.That(state.Kills, Is.EqualTo(2), "A mutual defeat counts every newly defeated opponent once.");
            Assert.That(state.IsFinished, Is.True);
            Assert.That(state.IsWaiting, Is.False);
            Assert.That(state.Advance(100f, 0), Is.False);
            Assert.Throws<InvalidOperationException>(() => state.ConfirmSpawn(1));
            state.Observe(false, 0, 1);
            Assert.That(state.Kills, Is.EqualTo(2));
            state.Reset(CombatEncounterSettings.Default, 0);
            Assert.That(state.Kills, Is.Zero);
            Assert.That(state.IsFinished, Is.False);
            Assert.That(state.RemainingSeconds, Is.InRange(2f, 4f));
        }

        [TestCase(0)]
        [TestCase(6)]
        public void EncounterSettingsRejectCapacityOutsidePermanentPool(int maximum)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatEncounterSettings(maximum));
        }

        [Test]
        public void SelectionIncludesTimingAndCapacityAndRejectsUninitializedSettings()
        {
            var one = new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless,
                CombatEncounterSettings.Default.WithMaximumLiveOpponents(1));
            var five = new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless);
            Assert.That(one, Is.Not.EqualTo(five));
            Assert.That(five.EncounterSettings, Is.EqualTo(CombatEncounterSettings.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Endless, default(CombatEncounterSettings)));
        }
    }
}
