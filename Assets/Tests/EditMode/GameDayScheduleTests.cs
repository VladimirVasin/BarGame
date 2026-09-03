using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The calendar itself, apart from anything it opens. The table is
    /// pure, so this needs no session, no scene and no clock; the one
    /// test that does drive a session is the one proving an event
    /// actually fires when its day arrives.
    /// </summary>
    public sealed class GameDayScheduleTests
    {
        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void Schedule_IsWellFormedAndAddressedById()
        {
            IReadOnlyList<GameDayScheduleEntry> all =
                GameDaySchedule.All;
            Assert.That(all, Is.Not.Empty);

            var seen = new HashSet<GameDayEventId>();
            for (int index = 0; index < all.Count; index++)
            {
                GameDayScheduleEntry entry = all[index];
                Assert.That(
                    entry.Id,
                    Is.Not.EqualTo(GameDayEventId.None),
                    "None is the absence of an event, not a row.");
                Assert.That(
                    seen.Add(entry.Id),
                    Is.True,
                    "An event may be dated once.");
                Assert.That(
                    entry.FirstDayNumber,
                    Is.GreaterThanOrEqualTo(
                        GameDaySchedule.FirstDayNumber),
                    "Day numbers start at one.");

                // Looked up BY ID, never by position, so a row filed
                // out of order cannot hand a later event this one's
                // date without anybody noticing.
                Assert.That(
                    GameDaySchedule.TryGet(
                        entry.Id,
                        out GameDayScheduleEntry found),
                    Is.True);
                Assert.That(
                    found.FirstDayNumber,
                    Is.EqualTo(entry.FirstDayNumber));
                Assert.That(
                    GameDaySchedule.GetFirstDayNumber(entry.Id),
                    Is.EqualTo(entry.FirstDayNumber));
            }
        }

        [Test]
        public void IsDueOn_OpensOnItsDayAndStaysOpen()
        {
            const GameDayEventId id = GameDayEventId.FeedTheCatOpens;
            int day = GameDaySchedule.GetFirstDayNumber(id);
            Assert.That(day, Is.GreaterThan(1));

            Assert.That(
                GameDaySchedule.IsDueOn(id, day - 1),
                Is.False);
            Assert.That(GameDaySchedule.IsDueOn(id, day), Is.True);
            Assert.That(
                GameDaySchedule.IsDueOn(id, day + 5),
                Is.True,
                "A day that has arrived does not un-arrive.");
        }

        [Test]
        public void UnscheduledEvent_IsNeverDue()
        {
            Assert.That(
                GameDaySchedule.TryGet(GameDayEventId.None, out _),
                Is.False);
            Assert.That(
                GameDaySchedule.GetFirstDayNumber(GameDayEventId.None),
                Is.Zero);
            Assert.That(
                GameDaySchedule.IsDueOn(GameDayEventId.None, 9999),
                Is.False,
                "An event the calendar does not carry never arrives.");
        }

        /// <summary>
        /// The mechanism, not the cat: a dated event is closed on the
        /// first day and open once its own day is reached, and the
        /// session records that it happened.
        /// </summary>
        [Test]
        public void ArrivingAtADay_FiresEveryEventDatedToItOrEarlier()
        {
            IReadOnlyList<GameDayScheduleEntry> all =
                GameDaySchedule.All;
            for (int index = 0; index < all.Count; index++)
            {
                GameDayScheduleEntry entry = all[index];
                if (entry.FirstDayNumber <=
                    GameDaySchedule.FirstDayNumber)
                {
                    continue;
                }

                Assert.That(
                    GameSessionState.HasDayEventFired(entry.Id),
                    Is.False,
                    entry.Id + " must not be open on the first day.");
            }

            Assert.That(
                GameSessionState.TrySetDebugGameDay(
                    GameSessionState.LastDebugGameDayNumber),
                Is.True);

            for (int index = 0; index < all.Count; index++)
            {
                Assert.That(
                    GameSessionState.HasDayEventFired(all[index].Id),
                    Is.True,
                    all[index].Id +
                    " is dated before the last debug day and must " +
                    "have opened by it.");
            }
        }

        [Test]
        public void AFiredEvent_StaysFiredWhenTheDayJumpsBack()
        {
            const GameDayEventId id = GameDayEventId.FeedTheCatOpens;
            Assert.That(
                GameSessionState.TrySetDebugGameDay(
                    GameDaySchedule.GetFirstDayNumber(id)),
                Is.True);
            Assert.That(
                GameSessionState.HasDayEventFired(id),
                Is.True);

            Assert.That(
                GameSessionState.TrySetDebugGameDay(
                    GameDaySchedule.FirstDayNumber),
                Is.True);
            Assert.That(
                GameSessionState.HasDayEventFired(id),
                Is.True,
                "An event is a thing that happened; going back to an " +
                "earlier day does not un-happen it.");
        }

        [Test]
        public void ANewGame_ForgetsEveryFiredEvent()
        {
            const GameDayEventId id = GameDayEventId.FeedTheCatOpens;
            GameSessionState.TrySetDebugGameDay(
                GameDaySchedule.GetFirstDayNumber(id));
            Assert.That(
                GameSessionState.HasDayEventFired(id),
                Is.True);

            GameSessionState.BeginNewGame();

            Assert.That(
                GameSessionState.HasDayEventFired(id),
                Is.False);
            Assert.That(
                GameSessionState.GameDayNumber,
                Is.EqualTo(GameDaySchedule.FirstDayNumber));
            Assert.That(GameSessionState.Quests, Is.Empty);
        }
    }
}
