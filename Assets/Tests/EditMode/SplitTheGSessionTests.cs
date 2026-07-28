using System;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class SplitTheGSessionTests
    {
        [Test]
        public void Session_DrainsOnlyDuringDrinkingAndLocksRelease()
        {
            SplitTheGSession session = new SplitTheGSession();

            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.Countdown));
            session.Advance(session.Settings.CountdownTime * 0.5d);
            Assert.That(session.RemainingLevel, Is.EqualTo(1d));
            session.Advance(session.Settings.CountdownTime * 0.5d);
            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));

            session.Advance(3d);
            Assert.That(session.RemainingLevel, Is.EqualTo(1d));
            session.BeginDrink();
            session.Advance(1d);
            Assert.That(session.RemainingLevel, Is.EqualTo(0.78d)
                .Within(0.000000001d));

            SplitTheGAttemptResult result =
                session.ReleaseDrink();
            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));
            Assert.That(
                result.ConsumedFraction,
                Is.EqualTo(0.22d).Within(0.000000001d));
            double releasedLevel = session.RemainingLevel;
            session.Advance(session.Settings.SettlingTime * 0.5d);
            Assert.That(session.RemainingLevel, Is.EqualTo(releasedLevel));
            Assert.Throws<InvalidOperationException>(
                () => session.ReleaseDrink());
            Assert.Throws<InvalidOperationException>(
                () => session.BeginDrink());
        }

        [Test]
        public void AutomaticStop_ClampsTimeAndCannotResume()
        {
            var settings = new SplitTheGSettings(
                0.5d,
                0.1d,
                2d,
                0.6d,
                3,
                0d);
            var session = new SplitTheGSession(settings);
            session.BeginDrink();

            session.Advance(5d);

            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.AttemptResult));
            Assert.That(session.DrinkElapsed, Is.EqualTo(2d));
            Assert.That(
                session.RemainingLevel,
                Is.EqualTo(0.8d).Within(0.000000001d));
            Assert.That(session.LastResult.WasAutoStopped, Is.True);
            Assert.Throws<InvalidOperationException>(
                () => session.ReleaseDrink());
            Assert.Throws<InvalidOperationException>(
                () => session.BeginDrink());
        }

        [Test]
        public void Advance_IsInvariantToFrameChunking()
        {
            SplitTheGSession singleStep = CreateArmedSession();
            SplitTheGSession chunked = CreateArmedSession();
            singleStep.BeginDrink();
            chunked.BeginDrink();

            singleStep.Advance(2.125d);
            for (int index = 0; index < 125; index++)
            {
                chunked.Advance(0.017d);
            }

            SplitTheGAttemptResult singleResult =
                singleStep.ReleaseDrink();
            SplitTheGAttemptResult chunkedResult =
                chunked.ReleaseDrink();

            Assert.That(
                chunked.RemainingLevel,
                Is.EqualTo(singleStep.RemainingLevel)
                    .Within(0.000000001d));
            Assert.That(
                chunkedResult.AbsoluteError,
                Is.EqualTo(singleResult.AbsoluteError)
                    .Within(0.000000001d));
            Assert.That(
                chunkedResult.Score,
                Is.EqualTo(singleResult.Score));
            Assert.That(
                chunkedResult.Band,
                Is.EqualTo(singleResult.Band));
        }

        [Test]
        public void AutomaticStop_IsInvariantToFrameChunking()
        {
            var settings = new SplitTheGSettings(
                0.5d,
                0.1d,
                2d,
                0.6d,
                3,
                0d);
            var singleStep = new SplitTheGSession(settings);
            var chunked = new SplitTheGSession(settings);
            singleStep.BeginDrink();
            chunked.BeginDrink();

            singleStep.Advance(5d);
            for (int index = 0; index < 50; index++)
            {
                chunked.Advance(0.1d);
            }

            Assert.That(chunked.Phase, Is.EqualTo(singleStep.Phase));
            Assert.That(
                chunked.RemainingLevel,
                Is.EqualTo(singleStep.RemainingLevel)
                    .Within(0.000000001d));
            Assert.That(
                chunked.LastResult.AbsoluteError,
                Is.EqualTo(singleStep.LastResult.AbsoluteError)
                    .Within(0.000000001d));
        }

        [Test]
        public void Retry_UsesNewFullGlassAndKeepsBestResult()
        {
            SplitTheGSession session = CreateArmedSession();
            session.BeginDrink();
            session.Advance(1d);
            session.ReleaseDrink();
            session.Advance(session.Settings.SettlingTime);
            int firstScore = session.LastResult.Score;

            Assert.That(session.CanRetry, Is.True);
            session.Retry();

            Assert.That(session.RemainingLevel, Is.EqualTo(1d));
            Assert.That(session.CurrentConsumedFraction, Is.Zero);
            Assert.That(session.AttemptsCompleted, Is.EqualTo(1));
            Assert.That(session.BestScore, Is.EqualTo(firstScore));
            Arm(session);
            session.BeginDrink();
            session.Advance(
                (1d - session.Settings.TargetLevel) /
                session.Settings.DrinkSpeed);
            session.ReleaseDrink();
            session.Advance(session.Settings.SettlingTime);

            Assert.That(session.AttemptsCompleted, Is.EqualTo(2));
            Assert.That(session.LastResult.Score, Is.EqualTo(100));
            Assert.That(session.BestScore, Is.EqualTo(100));
            Assert.That(
                session.BestResult.AttemptNumber,
                Is.EqualTo(2));
            Assert.That(
                session.LastConsumedFraction,
                Is.EqualTo(0.5d).Within(0.000000001d));
        }

        [Test]
        public void CompleteEarly_EndsFromAcceptedAttemptResult()
        {
            SplitTheGSession session = CreateArmedSession();
            FinishAttempt(session, 1.2d);

            Assert.That(session.CanCompleteEarly, Is.True);
            session.CompleteEarly();

            Assert.That(session.IsFinished, Is.True);
            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.FinalResult));
            Assert.Throws<InvalidOperationException>(
                () => session.Retry());
        }

        [Test]
        public void ThirdAttempt_TransitionsDirectlyToFinalResult()
        {
            SplitTheGSession session = CreateArmedSession();
            for (int attempt = 1;
                 attempt <= session.Settings.MaximumAttempts;
                 attempt++)
            {
                FinishAttempt(session, 1d + attempt * 0.1d);
                Assert.That(
                    session.AttemptsCompleted,
                    Is.EqualTo(attempt));

                if (attempt < session.Settings.MaximumAttempts)
                {
                    Assert.That(
                        session.Phase,
                        Is.EqualTo(
                            SplitTheGPhase.AttemptResult));
                    session.Retry();
                    Arm(session);
                }
            }

            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.AttemptsRemaining, Is.Zero);
            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.FinalResult));
        }

        [Test]
        public void Advance_RejectsNegativeOrNonFiniteTime()
        {
            SplitTheGSession session = new SplitTheGSession();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => session.Advance(-0.01d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => session.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => session.Advance(double.PositiveInfinity));
        }

        private static SplitTheGSession CreateArmedSession()
        {
            SplitTheGSession session = new SplitTheGSession();
            Arm(session);
            return session;
        }

        private static void Arm(SplitTheGSession session)
        {
            session.Advance(session.Settings.CountdownTime);
            Assert.That(
                session.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));
        }

        private static void FinishAttempt(
            SplitTheGSession session,
            double drinkTime)
        {
            session.BeginDrink();
            session.Advance(drinkTime);
            session.ReleaseDrink();
            session.Advance(session.Settings.SettlingTime);
        }
    }
}
