using NUnit.Framework;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Real business-hour prerequisites for the existing service and full-cast contracts.</summary>
    internal static class BarTestTime
    {
        public static void StartOpen()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(18 * 60), Is.True,
                "A service fixture starts its fresh clock while the bar is open.");
        }

        public static void StartFullAttendance(string barId)
        {
            int seed = unchecked((int)BarInteriorLayoutPlanner.ComputeStableSeed(GameSessionState.CitySeed, barId));
            for (int day = 0; day < GameSessionState.LastDebugGameDayNumber; day++)
                for (int hour = 19; hour < 23; hour++)
                {
                    int minute = hour * 60;
                    if (BarBusinessHours.TargetGuestCount(day, minute, seed) != BarBusinessHours.MaximumGuests) continue;
                    Assert.That(GameSessionState.TryStartGameTimeAt(minute), Is.True,
                        "The full-cast fixture needs a fresh stopped clock.");
                    if (day > 0) Assert.That(GameSessionState.TrySetDebugGameDay(day + 1), Is.True);
                    Assert.That(BarBusinessHours.TargetGuestCount(GameSessionState.GameDayIndex,
                        GameSessionState.GameMinuteOfDay, seed), Is.EqualTo(BarBusinessHours.MaximumGuests));
                    return;
                }
            Assert.Fail("No real full-attendance evening exists in the fixture's available days for " + barId);
        }
    }
}
