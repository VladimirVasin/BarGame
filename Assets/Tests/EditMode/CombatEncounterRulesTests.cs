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
            var settings = CombatEncounterSettings.Default;
            first.Reset(settings, 321);
            second.Reset(settings, 321);
            Assert.That(first.RemainingSeconds, Is.InRange(2f, 4f));
            Assert.That(first.RemainingSeconds, Is.EqualTo(second.RemainingSeconds));
            float pausedRemaining = first.RemainingSeconds;
            Assert.That(first.Advance(0f, 0), Is.False);
            Assert.That(first.RemainingSeconds, Is.EqualTo(pausedRemaining));
            for (int liveCount = 1; liveCount <= settings.MaximumLiveOpponents; liveCount++)
            {
                Assert.That(first.Advance(100f, liveCount - 1), Is.True);
                Assert.That(second.Advance(100f, liveCount - 1), Is.True);
                first.ConfirmSpawn(liveCount);
                second.ConfirmSpawn(liveCount);
                Assert.That(first.RemainingSeconds, Is.EqualTo(second.RemainingSeconds));
                Assert.That(first.Advance(0f, liveCount), Is.False,
                    "Elapsed time before creation must not carry into the next creation.");
                if (liveCount < settings.MaximumLiveOpponents) Assert.That(first.RemainingSeconds, Is.InRange(3f, 7f));
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

        [TestCase(0, 2)]
        [TestCase(6, 2)]
        [TestCase(2, -1)]
        [TestCase(2, 3)]
        public void EncounterSettingsRejectCapacityOutsidePermanentPool(int maximum, int maximumCorpses)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatEncounterSettings(maximum,
                maximumCorpses: maximumCorpses));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SelectionIncludesTimingAndCapacityAndRejectsUninitializedSettings(int maximumCorpses)
        {
            Assert.That(CombatEncounterSettings.Default.MaximumCorpses, Is.EqualTo(CombatEncounterSettings.CorpseCapacity));
            var settings = CombatEncounterSettings.Default.WithMaximumCorpses(maximumCorpses);
            var one = new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless,
                settings.WithMaximumLiveOpponents(1));
            var defaultLive = new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless, settings);
            Assert.That(one, Is.Not.EqualTo(defaultLive));
            Assert.That(defaultLive.EncounterSettings, Is.EqualTo(settings));
            Assert.That(settings.MaximumLiveOpponents, Is.EqualTo(CombatEncounterSettings.DefaultMaximumLiveOpponents));
            Assert.That(settings.MaximumCorpses, Is.EqualTo(maximumCorpses));
            Assert.That(one.EncounterSettings.MaximumCorpses, Is.EqualTo(maximumCorpses));
            var otherCorpses = settings.WithMaximumCorpses((maximumCorpses + 1) % (CombatEncounterSettings.CorpseCapacity + 1));
            Assert.That(defaultLive, Is.Not.EqualTo(new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Endless, otherCorpses)));
            Assert.That(settings.GetHashCode(), Is.Not.EqualTo(otherCorpses.GetHashCode()));
            Assert.That(otherCorpses.WithMaximumLiveOpponents(1).WithMaximumCorpses(maximumCorpses),
                Is.EqualTo(one.EncounterSettings), "Both builders preserve the remaining encounter settings.");
            Assert.DoesNotThrow(settings.Validate);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Endless, default(CombatEncounterSettings)));
        }
    }
}
