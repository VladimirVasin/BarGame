using System;

namespace BarPromenade
{
    public sealed class SplitTheGSession
    {
        private const double TimeEpsilon = 0.0000000001d;

        private double phaseElapsed;
        private double drinkElapsed;
        private SplitTheGAttemptResult lastResult;
        private SplitTheGAttemptResult bestResult;

        public SplitTheGSession()
            : this(SplitTheGSettings.Normal)
        {
        }

        public SplitTheGSession(SplitTheGSettings settings)
        {
            Settings = settings ??
                throw new ArgumentNullException(nameof(settings));
            ResetGlassAndEnterCountdown();
        }

        public SplitTheGSettings Settings { get; }
        public SplitTheGPhase Phase { get; private set; }
        public double RemainingLevel { get; private set; }
        public double CurrentConsumedFraction =>
            Math.Max(0d, 1d - RemainingLevel);
        public double DrinkElapsed => drinkElapsed;
        public double PhaseElapsed => phaseElapsed;
        public int AttemptsCompleted { get; private set; }
        public int CurrentAttemptNumber => IsFinished
            ? Math.Max(1, AttemptsCompleted)
            : Math.Min(
                Settings.MaximumAttempts,
                AttemptsCompleted + 1);
        public int AttemptsRemaining =>
            Math.Max(
                0,
                Settings.MaximumAttempts - AttemptsCompleted);
        public bool HasLastResult { get; private set; }
        public SplitTheGAttemptResult LastResult
        {
            get
            {
                if (!HasLastResult)
                {
                    throw new InvalidOperationException(
                        "No Split the G attempt has finished.");
                }

                return lastResult;
            }
        }
        public double LastConsumedFraction =>
            HasLastResult ? lastResult.ConsumedFraction : 0d;
        public bool HasBestResult { get; private set; }
        public SplitTheGAttemptResult BestResult
        {
            get
            {
                if (!HasBestResult)
                {
                    throw new InvalidOperationException(
                        "No Split the G result is available.");
                }

                return bestResult;
            }
        }
        public int BestScore =>
            HasBestResult ? bestResult.Score : 0;
        public bool CanBeginDrink =>
            Phase == SplitTheGPhase.Armed;
        public bool CanRetry =>
            Phase == SplitTheGPhase.AttemptResult &&
            AttemptsCompleted < Settings.MaximumAttempts;
        public bool CanCompleteEarly =>
            Phase == SplitTheGPhase.AttemptResult;
        public bool IsFinished =>
            Phase == SplitTheGPhase.FinalResult;

        public void Advance(double deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime));
            }

            double remainingDelta = deltaTime;
            while (true)
            {
                switch (Phase)
                {
                    case SplitTheGPhase.Countdown:
                        if (!AdvanceCountdown(ref remainingDelta))
                        {
                            return;
                        }

                        break;
                    case SplitTheGPhase.Drinking:
                        if (!AdvanceDrinking(ref remainingDelta))
                        {
                            return;
                        }

                        break;
                    case SplitTheGPhase.Settling:
                        if (!AdvanceSettling(ref remainingDelta))
                        {
                            return;
                        }

                        break;
                    default:
                        return;
                }
            }
        }

        public void BeginDrink()
        {
            if (Phase != SplitTheGPhase.Armed)
            {
                throw new InvalidOperationException(
                    "Drinking can begin only after the countdown.");
            }

            Phase = SplitTheGPhase.Drinking;
            phaseElapsed = 0d;
            drinkElapsed = 0d;
        }

        public SplitTheGAttemptResult ReleaseDrink()
        {
            if (Phase != SplitTheGPhase.Drinking)
            {
                throw new InvalidOperationException(
                    "Only an active drink can be released.");
            }

            FinalizeAttempt(false);
            return lastResult;
        }

        public void Retry()
        {
            if (!CanRetry)
            {
                throw new InvalidOperationException(
                    "A new glass can start only after a non-final result.");
            }

            ResetGlassAndEnterCountdown();
        }

        public void CompleteEarly()
        {
            if (!CanCompleteEarly)
            {
                throw new InvalidOperationException(
                    "The session can finish early only from an attempt result.");
            }

            Phase = SplitTheGPhase.FinalResult;
            phaseElapsed = 0d;
        }

        private bool AdvanceCountdown(ref double remainingDelta)
        {
            double remainingTime =
                Math.Max(
                    0d,
                    Settings.CountdownTime - phaseElapsed);
            if (remainingTime <= TimeEpsilon)
            {
                Phase = SplitTheGPhase.Armed;
                phaseElapsed = 0d;
                return true;
            }

            if (remainingDelta <= 0d)
            {
                return false;
            }

            double step = Math.Min(remainingDelta, remainingTime);
            phaseElapsed += step;
            remainingDelta -= step;
            if (phaseElapsed + TimeEpsilon <
                Settings.CountdownTime)
            {
                return false;
            }

            Phase = SplitTheGPhase.Armed;
            phaseElapsed = 0d;
            return true;
        }

        private bool AdvanceDrinking(ref double remainingDelta)
        {
            double timeToEmpty = 1d / Settings.DrinkSpeed;
            double automaticStopTime = Math.Min(
                Settings.MaximumDrinkTime,
                timeToEmpty);
            double remainingTime =
                Math.Max(0d, automaticStopTime - drinkElapsed);
            if (remainingTime <= TimeEpsilon)
            {
                FinalizeAttempt(true);
                return true;
            }

            if (remainingDelta <= 0d)
            {
                return false;
            }

            double step = Math.Min(remainingDelta, remainingTime);
            drinkElapsed += step;
            phaseElapsed = drinkElapsed;
            remainingDelta -= step;
            RemainingLevel = Clamp01(
                1d - Settings.DrinkSpeed * drinkElapsed);
            if (drinkElapsed + TimeEpsilon <
                automaticStopTime)
            {
                return false;
            }

            FinalizeAttempt(true);
            return true;
        }

        private bool AdvanceSettling(ref double remainingDelta)
        {
            double remainingTime =
                Math.Max(
                    0d,
                    Settings.SettlingTime - phaseElapsed);
            if (remainingTime <= TimeEpsilon)
            {
                CompleteSettling();
                return true;
            }

            if (remainingDelta <= 0d)
            {
                return false;
            }

            double step = Math.Min(remainingDelta, remainingTime);
            phaseElapsed += step;
            remainingDelta -= step;
            if (phaseElapsed + TimeEpsilon <
                Settings.SettlingTime)
            {
                return false;
            }

            CompleteSettling();
            return true;
        }

        private void FinalizeAttempt(bool wasAutoStopped)
        {
            RemainingLevel = Clamp01(
                1d - Settings.DrinkSpeed * drinkElapsed);
            AttemptsCompleted++;
            lastResult = SplitTheGScoring.Evaluate(
                AttemptsCompleted,
                RemainingLevel,
                Settings.TargetLevel,
                wasAutoStopped);
            HasLastResult = true;
            if (!HasBestResult ||
                IsBetterResult(lastResult, bestResult))
            {
                bestResult = lastResult;
                HasBestResult = true;
            }

            Phase = SplitTheGPhase.Settling;
            phaseElapsed = 0d;
            if (Settings.SettlingTime <= TimeEpsilon)
            {
                CompleteSettling();
            }
        }

        private void CompleteSettling()
        {
            Phase = AttemptsCompleted >=
                    Settings.MaximumAttempts
                ? SplitTheGPhase.FinalResult
                : SplitTheGPhase.AttemptResult;
            phaseElapsed = 0d;
        }

        private void ResetGlassAndEnterCountdown()
        {
            RemainingLevel = 1d;
            drinkElapsed = 0d;
            phaseElapsed = 0d;
            Phase = Settings.CountdownTime <= TimeEpsilon
                ? SplitTheGPhase.Armed
                : SplitTheGPhase.Countdown;
        }

        private static bool IsBetterResult(
            SplitTheGAttemptResult candidate,
            SplitTheGAttemptResult incumbent)
        {
            return candidate.Score > incumbent.Score ||
                   (candidate.Score == incumbent.Score &&
                    candidate.AbsoluteError <
                    incumbent.AbsoluteError);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0d, Math.Min(1d, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }
    }
}
