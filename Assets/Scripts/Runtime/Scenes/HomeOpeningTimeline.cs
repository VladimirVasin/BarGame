using System;

namespace BarPromenade
{
    public enum HomeOpeningPhase
    {
        NotStarted = 0,
        ClockHold = 1,
        AwaitingWake = 2,
        AlarmRinging = 3,
        Waking = 4,
        Complete = 5
    }

    /// <summary>
    /// Frame-rate-independent state for the one-shot opening presentation.
    /// The wake phase completes only when the bed animation reports Idle.
    /// </summary>
    public sealed class HomeOpeningTimeline
    {
        public const float LockedClockSeconds = 5f;
        public const int LockedHour = 5;
        public const int LockedMinute = 59;
        public const int AlarmHour = 6;
        public const int AlarmMinute = 0;
        public const float DisplayBlinkInitialDelaySeconds = 0.8f;
        public const float DisplayBlinkPeriodSeconds = 3f;
        public const float DisplayBlinkOffSeconds = 0.16f;
        public const float WakeAlarmSeconds = 3f;
        public const float WakeCameraTransitionSeconds = 2.25f;

        private double phaseElapsedSeconds;
        private double displayElapsedSeconds;
        private bool wakeRequested;

        public HomeOpeningPhase Phase { get; private set; } =
            HomeOpeningPhase.NotStarted;
        public bool IsActive =>
            Phase != HomeOpeningPhase.NotStarted &&
            Phase != HomeOpeningPhase.Complete;
        public bool MenuVisible =>
            Phase == HomeOpeningPhase.AwaitingWake;
        public bool ShouldRing =>
            Phase == HomeOpeningPhase.AlarmRinging;
        public bool WakeAnimationReady =>
            Phase == HomeOpeningPhase.AlarmRinging &&
            phaseElapsedSeconds >= WakeAlarmSeconds;
        public int DisplayHour =>
            ShouldShowLockedTime
                ? LockedHour
                : AlarmHour;
        public int DisplayMinute =>
            ShouldShowLockedTime
                ? LockedMinute
                : AlarmMinute;
        public bool DisplayVisible
        {
            get
            {
                if (!IsPreWakeDisplayPhase ||
                    displayElapsedSeconds <
                    DisplayBlinkInitialDelaySeconds)
                {
                    return true;
                }

                double blinkElapsed =
                    displayElapsedSeconds -
                    DisplayBlinkInitialDelaySeconds;
                double periodElapsed =
                    blinkElapsed %
                    DisplayBlinkPeriodSeconds;
                return periodElapsed >=
                    DisplayBlinkOffSeconds;
            }
        }
        public float PhaseElapsedSeconds =>
            (float)phaseElapsedSeconds;
        public float DisplayElapsedSeconds =>
            (float)displayElapsedSeconds;
        public float WakeCameraTransitionProgress =>
            Phase == HomeOpeningPhase.Waking
                ? Math.Min(
                    (float)phaseElapsedSeconds /
                    WakeCameraTransitionSeconds,
                    1f)
                : 0f;
        public float WakeCameraTransitionBlend =>
            SmootherStep(WakeCameraTransitionProgress);
        public bool WakeCameraTransitionComplete =>
            Phase == HomeOpeningPhase.Waking &&
            phaseElapsedSeconds >=
            WakeCameraTransitionSeconds;

        public bool Begin()
        {
            if (Phase != HomeOpeningPhase.NotStarted)
            {
                return false;
            }

            Phase = HomeOpeningPhase.ClockHold;
            phaseElapsedSeconds = 0d;
            displayElapsedSeconds = 0d;
            wakeRequested = false;
            return true;
        }

        public void Advance(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime),
                    unscaledDeltaTime,
                    "Opening delta time must be finite and non-negative.");
            }

            if (!IsActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            if (IsPreWakeDisplayPhase)
            {
                displayElapsedSeconds +=
                    unscaledDeltaTime;
            }

            phaseElapsedSeconds += unscaledDeltaTime;
            while (true)
            {
                switch (Phase)
                {
                    case HomeOpeningPhase.ClockHold:
                        if (phaseElapsedSeconds <
                            LockedClockSeconds)
                        {
                            return;
                        }

                        phaseElapsedSeconds -=
                            LockedClockSeconds;
                        Phase = HomeOpeningPhase.AwaitingWake;
                        return;
                    default:
                        return;
                }
            }
        }

        public bool RequestWake()
        {
            if (Phase != HomeOpeningPhase.AwaitingWake)
            {
                return false;
            }

            wakeRequested = true;
            Phase = HomeOpeningPhase.AlarmRinging;
            phaseElapsedSeconds = 0d;
            return true;
        }

        public bool BeginWakeAnimation()
        {
            if (!WakeAnimationReady)
            {
                return false;
            }

            Phase = HomeOpeningPhase.Waking;
            phaseElapsedSeconds = 0d;
            return true;
        }

        public bool CompleteWake()
        {
            if (Phase != HomeOpeningPhase.Waking)
            {
                return false;
            }

            Phase = HomeOpeningPhase.Complete;
            phaseElapsedSeconds = 0d;
            return true;
        }

        public bool Cancel()
        {
            if (!IsActive)
            {
                return false;
            }

            Phase = HomeOpeningPhase.Complete;
            phaseElapsedSeconds = 0d;
            return true;
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Math.Max(
                0f,
                Math.Min(amount, 1f));
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }

        private bool IsPreWakeDisplayPhase =>
            Phase == HomeOpeningPhase.ClockHold ||
            Phase == HomeOpeningPhase.AwaitingWake;
        private bool ShouldShowLockedTime =>
            Phase != HomeOpeningPhase.NotStarted &&
            !wakeRequested;
    }
}
