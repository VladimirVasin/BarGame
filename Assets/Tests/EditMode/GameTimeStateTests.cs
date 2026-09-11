using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameTimeStateTests
    {
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(10)]
        public void Tempo_NestedPausePreservesClockRateAndLatestIntoxication(
            int debugMultiplier)
        {
            GameTimeScaleState tempo = new GameTimeScaleState(0.75f, 0.02f);
            if (debugMultiplier != 1)
            {
                Assert.That(tempo.ToggleDebugTimeMultiplier(debugMultiplier), Is.True);
            }

            tempo.SetIntoxicationLevel(100f);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(debugMultiplier));
            Assert.That(tempo.EffectiveTimeScale,
                Is.EqualTo(0.66f * debugMultiplier).Within(0.00001f));
            Assert.That(tempo.FixedDeltaTime, Is.EqualTo(0.0132f).Within(0.00001f));
            Assert.That(tempo.RealGameplayDelta(10f), Is.EqualTo(10f),
                "Debug speed must not change real-time presentation smoothing.");
            Assert.That(tempo.CalendarDelta(10f), Is.EqualTo(10f * debugMultiplier));
            GameTimeState clock = new GameTimeState();
            clock.Advance(tempo.CalendarDelta(10f));
            Assert.That(clock.MinuteOfDay, Is.EqualTo(359), "Pre-wake stays frozen.");
            clock.TryStartFromWake();
            clock.Advance(tempo.CalendarDelta(GameTimeState.RealSecondsPerGameDay));
            Assert.That(clock.DayIndex, Is.EqualTo(debugMultiplier),
                "Debug speed advances the calendar; intoxication and baseline slow motion do not.");
            Assert.That(clock.MinuteOfDay, Is.EqualTo(360));

            long first = tempo.AcquirePause();
            long second = tempo.AcquirePause();
            tempo.ReleasePause(first);
            Assert.That(tempo.EffectiveTimeScale, Is.Zero);
            Assert.That(tempo.RealGameplayDelta(5f), Is.Zero);
            Assert.That(tempo.CalendarDelta(5f), Is.Zero);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(debugMultiplier));
            tempo.SetIntoxicationLevel(0f);
            Assert.That(tempo.EffectiveTimeScale, Is.Zero);
            tempo.ReleasePause(second);
            Assert.That(tempo.EffectiveTimeScale, Is.EqualTo(0.75f * debugMultiplier));
            Assert.That(tempo.CalendarDelta(5f), Is.EqualTo(5f * debugMultiplier));
            Assert.That(tempo.ReleasePause(second), Is.False);

            long obsolete = tempo.AcquirePause();
            tempo.ResetSession();
            long current = tempo.AcquirePause();
            Assert.That(tempo.ReleasePause(obsolete), Is.False);
            Assert.That(tempo.IsPaused, Is.True);
            tempo.ReleasePause(current);
            Assert.That(tempo.EffectiveTimeScale, Is.EqualTo(1f));
            Assert.That(tempo.FixedDeltaTime, Is.EqualTo(0.02f));
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            Assert.That(tempo.CalendarDelta(5f), Is.EqualTo(5f));
        }

        [Test]
        public void Tempo_DebugSelectionTogglesSupportedSpeedsAndHonorsDisable()
        {
            GameTimeScaleState tempo = new GameTimeScaleState(1f, 0.02f);
            Assert.That(tempo.DebugSpeedSelectionEnabled, Is.True);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));

            foreach (int multiplier in new[] { 3, 5, 10 })
            {
                Assert.That(tempo.ToggleDebugTimeMultiplier(multiplier), Is.True);
                Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(multiplier));
                Assert.That(tempo.ToggleDebugTimeMultiplier(multiplier), Is.True);
                Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            }

            Assert.That(tempo.ToggleDebugTimeMultiplier(3), Is.True);
            Assert.That(tempo.ToggleDebugTimeMultiplier(5), Is.True);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(5));
            foreach (int invalid in new[] { int.MinValue, -1, 0, 1, 2, 4, int.MaxValue })
            {
                Assert.That(tempo.ToggleDebugTimeMultiplier(invalid), Is.False);
                Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(5));
            }

            long pause = tempo.AcquirePause();
            Assert.That(tempo.ToggleDebugTimeMultiplier(10), Is.True);
            Assert.That(tempo.EffectiveTimeScale, Is.Zero);
            tempo.SetDebugSpeedSelectionEnabled(false);
            Assert.That(tempo.DebugSpeedSelectionEnabled, Is.False);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            foreach (int multiplier in new[] { 3, 5, 10 })
            {
                Assert.That(tempo.ToggleDebugTimeMultiplier(multiplier), Is.False);
                Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            }

            Assert.That(tempo.IsPaused, Is.True,
                "Disabling debug speed must not release an unrelated pause.");
            Assert.That(tempo.ReleasePause(pause), Is.True);
            Assert.That(tempo.EffectiveTimeScale, Is.EqualTo(1f));
            tempo.SetDebugSpeedSelectionEnabled(true);
            Assert.That(tempo.DebugSpeedSelectionEnabled, Is.True);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            Assert.That(tempo.ToggleDebugTimeMultiplier(10), Is.True);
            tempo.SetDebugSpeedSelectionEnabled(false);
            tempo.ResetSession();
            Assert.That(tempo.DebugSpeedSelectionEnabled, Is.True);
            Assert.That(tempo.DebugTimeMultiplier, Is.EqualTo(1));
            Assert.That(tempo.ToggleDebugTimeMultiplier(3), Is.True);
        }

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
        public void TryStartAt_OpensDayOneOnTheGivenMinuteWithoutElapsing()
        {
            GameTimeState state = new GameTimeState();

            Assert.That(
                state.TryStartAt(
                    StartMenuRoot.VillageMorningMinuteOfDay),
                Is.True);
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.DayIndex, Is.Zero);
            Assert.That(state.DayNumber, Is.EqualTo(1));
            Assert.That(state.Hour, Is.EqualTo(7));
            Assert.That(state.Minute, Is.EqualTo(40));
            Assert.That(state.MinuteOfDay, Is.EqualTo(460));

            Assert.That(
                state.TryStartAt(0),
                Is.False,
                "A running day is never restarted at another hour.");
            Assert.That(state.MinuteOfDay, Is.EqualTo(460));
        }

        [TestCase(-1)]
        [TestCase(1440)]
        [TestCase(int.MaxValue)]
        public void TryStartAt_RefusesAMinuteOutsideTheDay(int minuteOfDay)
        {
            GameTimeState state = new GameTimeState();

            Assert.That(state.TryStartAt(minuteOfDay), Is.False);
            Assert.That(state.IsRunning, Is.False);
            Assert.That(state.MinuteOfDay, Is.EqualTo(359));
        }

        [Test]
        public void Exactly2880RealSeconds_AdvancesOneCompleteGameDay()
        {
            GameTimeState state = new GameTimeState();
            state.TryStartFromWake();

            state.Advance(GameTimeState.RealSecondsPerGameDay / 2f);
            Assert.That(state.DayIndex, Is.Zero);
            Assert.That(state.Hour, Is.EqualTo(18), "24 real minutes now advance only half a day.");
            state.Advance(GameTimeState.RealSecondsPerGameDay / 2f);

            Assert.That(
                GameTimeState.RealSecondsPerGameDay,
                Is.EqualTo(2880f));
            Assert.That(
                GameTimeState.GameMinutesPerRealSecond,
                Is.EqualTo(0.5d));
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
            GameSessionState.AdvanceGameTime((float)(1080d / GameTimeState.GameMinutesPerRealSecond));
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
            state.Advance((float)(394.5d / GameTimeState.GameMinutesPerRealSecond));
            double timeBeforeChange = state.TimeOfDayMinutes;

            Assert.That(state.TrySetDayNumber(7), Is.True);
            Assert.That(state.DayIndex, Is.EqualTo(6));
            Assert.That(state.DayNumber, Is.EqualTo(7));
            Assert.That(state.TimeOfDayMinutes, Is.EqualTo(timeBeforeChange));
            Assert.That(state.IsRunning, Is.True);
            Assert.That(state.TrySetDayNumber(7), Is.False);
            Assert.That(state.TrySetDayNumber(0), Is.False);

            state.Advance((float)(685.5d / GameTimeState.GameMinutesPerRealSecond));

            Assert.That(state.DayNumber, Is.EqualTo(8));
            Assert.That(
                HomeApartmentDayRules.ResolveDay(state.DayNumber),
                Is.EqualTo(7),
                "Apartment appearance stops at day seven; the calendar continues.");
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

            foreach (int previewDay in new[] { 3, 1, 7 })
            {
                Assert.That(GameSessionState.TrySetDebugGameDay(previewDay), Is.True);
                Assert.That(
                    HomeApartmentDayRules.ResolveDay(GameSessionState.GameDayNumber),
                    Is.EqualTo(previewDay),
                    "Debug backtracking must show that day's apartment again.");
                Assert.That(GameSessionState.GameTimeOfDayMinutes,
                    Is.EqualTo(timeBeforeChange));
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hungerBeforeChange));
                Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(fatigueBeforeChange));
            }

            GameSessionState.BeginNewGame();
            Assert.That(
                HomeApartmentDayRules.ResolveDay(GameSessionState.GameDayNumber),
                Is.EqualTo(1),
                "A new session starts in the normal apartment again.");
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
