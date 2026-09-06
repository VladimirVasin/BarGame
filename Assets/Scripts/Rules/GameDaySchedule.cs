using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Something the calendar opens. One row per event, dated to the
    /// first day it is in effect.
    /// </summary>
    public enum GameDayEventId
    {
        None = 0,

        /// <summary>
        /// The cat starts asking to be fed, which is also what puts the
        /// descent blocker in the stairwell and reserves the tin. The
        /// first day is deliberately empty of it.
        /// </summary>
        FeedTheCatOpens = 1
    }

    public readonly struct GameDayScheduleEntry
    {
        internal GameDayScheduleEntry(
            GameDayEventId id,
            int firstDayNumber)
        {
            Id = id;
            FirstDayNumber = firstDayNumber;
        }

        public GameDayEventId Id { get; }

        /// <summary>The first day number on which this is in effect.
        /// Day numbers start at `1`.</summary>
        public int FirstDayNumber { get; }
    }

    /// <summary>
    /// The calendar of the game: which events belong to which day.
    ///
    /// It is a pure data table and knows nothing about what an event
    /// DOES. <c>GameSessionState</c> owns the doing, in one place, so
    /// this can be read and proved without a session, a scene or a
    /// clock — the same split the quest and inventory catalogs already
    /// use.
    ///
    /// Looked up BY ID rather than by enum ordinal, on purpose. A table
    /// addressed by ordinal hands every later row its neighbour's data
    /// the moment somebody files an entry out of order, and does it
    /// without an error; this project has been bitten by exactly that
    /// in the sound table.
    ///
    /// To date a new event: add an enum member, add its row here, and
    /// give <c>GameSessionState.ApplyDayEvent</c> the one case that
    /// performs it. Nothing else in the game has to learn about days.
    /// </summary>
    public static class GameDaySchedule
    {
        /// <summary>The first day of a new game.</summary>
        public const int FirstDayNumber = 1;

        private static readonly GameDayScheduleEntry[] Entries =
        {
            new GameDayScheduleEntry(
                GameDayEventId.FeedTheCatOpens,
                2)
        };

        private static readonly IReadOnlyList<GameDayScheduleEntry>
            EntriesView = Array.AsReadOnly(Entries);

        public static IReadOnlyList<GameDayScheduleEntry> All =>
            EntriesView;

        public static bool TryGet(
            GameDayEventId id,
            out GameDayScheduleEntry entry)
        {
            for (int index = 0; index < Entries.Length; index++)
            {
                if (Entries[index].Id == id)
                {
                    entry = Entries[index];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        public static GameDayScheduleEntry Get(GameDayEventId id)
        {
            if (TryGet(id, out GameDayScheduleEntry entry))
            {
                return entry;
            }

            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "The event is not present in the day schedule.");
        }

        /// <summary>
        /// The first day this event is in effect, or `0` for an event
        /// the calendar does not carry.
        /// </summary>
        public static int GetFirstDayNumber(GameDayEventId id)
        {
            return TryGet(id, out GameDayScheduleEntry entry)
                ? entry.FirstDayNumber
                : 0;
        }

        /// <summary>
        /// Whether this event has arrived by that day. An unscheduled
        /// event never has.
        /// </summary>
        public static bool IsDueOn(GameDayEventId id, int dayNumber)
        {
            return TryGet(id, out GameDayScheduleEntry entry) &&
                   dayNumber >= entry.FirstDayNumber;
        }
    }
}
