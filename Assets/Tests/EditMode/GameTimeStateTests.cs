using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameTimeStateTests
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
        public void FreshState_HoldsAt0559UntilWake()
        {
            GameTimeState state = new GameTimeState();

            state.Advance(GameTimeState.RealSecondsPerGameDay);

            Assert.That(state.IsRunning, Is.False);
            Assert.That(state.DayIndex, Is.Zero);
            Assert.That(state.DayNumber, Is.EqualTo(1));
            Assert.That(state.Hour, Is.EqualTo(5));
            Assert.That(state.Minute, Is.EqualTo(59));
            Assert.That(state.MinuteOfDay, Is.EqualTo(359));
            Assert.That(state.TimeOfDayMinutes, Is.EqualTo(359d));
        }

        [Test]
        public void TryStartFromWake_SnapsOnceTo0600()
        {
            GameTimeState state = new GameTimeState();

            Assert.That(state.TryStartFromWake(), Is.True);
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.DayIndex, Is.Zero);
            Assert.That(state.DayNumber, Is.EqualTo(1));
            Assert.That(state.Hour, Is.EqualTo(6));
            Assert.That(state.Minute, Is.Zero);
            Assert.That(state.MinuteOfDay, Is.EqualTo(360));

            state.Advance(10f);
            double advancedTime = state.TimeOfDayMinutes;

            Assert.That(state.TryStartFromWake(), Is.False);
            Assert.That(
                state.TimeOfDayMinutes,
                Is.EqualTo(advancedTime),
                "A repeated wake must not rewind an already running day.");
        }

        [Test]
        public void Exactly1440RealSeconds_AdvancesOneCompleteGameDay()
        {
            GameTimeState state = new GameTimeState();
            state.TryStartFromWake();

            state.Advance(GameTimeState.RealSecondsPerGameDay);

            Assert.That(
                GameTimeState.RealSecondsPerGameDay,
                Is.EqualTo(1440f));
            Assert.That(
                GameTimeState.GameMinutesPerRealSecond,
                Is.EqualTo(1d));
            Assert.That(state.DayIndex, Is.EqualTo(1));
            Assert.That(state.DayNumber, Is.EqualTo(2));
            Assert.That(state.Hour, Is.EqualTo(6));
            Assert.That(state.Minute, Is.Zero);
            Assert.That(
                state.TimeOfDayMinutes,
                Is.EqualTo(360d).Within(0.000000001d));
            Assert.That(
                state.DayFraction,
                Is.EqualTo(0.25d).Within(0.000000001d));
        }

        [Test]
        public void BeginNewGame_ResetsSessionTimeToFrozen0559()
        {
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime(1080f);
            Assert.That(GameSessionState.GameDayIndex, Is.EqualTo(1));
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(2));
            Assert.That(GameSessionState.GameMinuteOfDay, Is.Zero);

            GameSessionState.BeginNewGame();

            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(GameSessionState.GameDayIndex, Is.Zero);
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(1));
            Assert.That(GameSessionState.GameHour, Is.EqualTo(5));
            Assert.That(GameSessionState.GameMinute, Is.EqualTo(59));
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(359));
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(359d));
        }

        [Test]
        public void TrySetDayNumber_PreservesClockAndContinuesAtMidnight()
        {
            GameTimeState state = new GameTimeState();
            state.TryStartFromWake();
            state.Advance(394.5f);
            double timeBeforeChange = state.TimeOfDayMinutes;

            Assert.That(state.TrySetDayNumber(7), Is.True);
            Assert.That(state.DayIndex, Is.EqualTo(6));
            Assert.That(state.DayNumber, Is.EqualTo(7));
            Assert.That(state.TimeOfDayMinutes, Is.EqualTo(timeBeforeChange));
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.TrySetDayNumber(7), Is.False);
            Assert.That(state.TrySetDayNumber(0), Is.False);

            state.Advance(685.5f);

            Assert.That(state.DayNumber, Is.EqualTo(8));
            Assert.That(state.Hour, Is.Zero);
            Assert.That(state.Minute, Is.Zero);
        }

        [Test]
        public void DebugDayChange_IsLimitedAndDoesNotAdvanceNeeds()
        {
            Assert.That(GameSessionState.TrySetDebugGameDay(7), Is.True);
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(7));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(359d));

            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime(394.5f);
            double timeBeforeChange =
                GameSessionState.GameTimeOfDayMinutes;
            int hungerBeforeChange = GameSessionState.HungerLevel;
            int fatigueBeforeChange = GameSessionState.FatigueLevel;

            Assert.That(GameSessionState.TrySetDebugGameDay(7), Is.True);
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(7));
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(timeBeforeChange));
            Assert.That(
                GameSessionState.HungerLevel,
                Is.EqualTo(hungerBeforeChange));
            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(fatigueBeforeChange));
            Assert.That(GameSessionState.TrySetDebugGameDay(0), Is.False);
            Assert.That(GameSessionState.TrySetDebugGameDay(8), Is.False);
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(7));
        }

        [Test]
        public void Announcement_QueuesDayChangesUntilGameplayCanPresent()
        {
            GameDayAnnouncementState state =
                new GameDayAnnouncementState(false, 1);

            state.Tick(true, 1, false, 0.5f);
            Assert.That(state.IsVisible, Is.False);

            state.Tick(true, 1, true, 0.5f);
            Assert.That(state.IsVisible, Is.True);
            Assert.That(state.DisplayedDayNumber, Is.EqualTo(1));
            Assert.That(
                state.RemainingSeconds,
                Is.EqualTo(
                    GameDayAnnouncementState.DisplayDurationSeconds));

            state.Tick(true, 7, false, 1f);
            Assert.That(
                state.RemainingSeconds,
                Is.EqualTo(
                    GameDayAnnouncementState.DisplayDurationSeconds));

            state.Tick(true, 7, true, 1f);
            Assert.That(state.DisplayedDayNumber, Is.EqualTo(7));
            Assert.That(
                state.RemainingSeconds,
                Is.EqualTo(
                    GameDayAnnouncementState.DisplayDurationSeconds));

            state.Tick(
                true,
                7,
                true,
                GameDayAnnouncementState.DisplayDurationSeconds);
            Assert.That(state.IsVisible, Is.False);

            state.Tick(false, 1, true, 0f);
            Assert.That(state.IsVisible, Is.False);
            Assert.That(state.DisplayedDayNumber, Is.Zero);
        }
    }
}
