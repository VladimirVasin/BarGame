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
            Assert.That(GameSessionState.GameMinuteOfDay, Is.Zero);

            GameSessionState.BeginNewGame();

            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(GameSessionState.GameDayIndex, Is.Zero);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(5));
            Assert.That(GameSessionState.GameMinute, Is.EqualTo(59));
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(359));
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(359d));
        }
    }
}
