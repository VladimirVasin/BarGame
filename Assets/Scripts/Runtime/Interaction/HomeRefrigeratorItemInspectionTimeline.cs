using System;

namespace BarPromenade
{
    public enum HomeRefrigeratorItemInspectionPhase
    {
        Browsing = 0,
        FlyingIn = 1,
        Inspecting = 2,
        FlyingOut = 3
    }

    /// <summary>
    /// One evaluated item-inspection pose. MoveProgress and BackdropAlpha are
    /// normalized presentation channels; RotationDegrees is wrapped to one
    /// clockwise revolution.
    /// </summary>
    public readonly struct HomeRefrigeratorItemInspectionFrame
    {
        internal HomeRefrigeratorItemInspectionFrame(
            HomeRefrigeratorItemInspectionPhase phase,
            float phaseElapsedSeconds,
            float phaseDurationSeconds,
            float phaseProgress,
            float moveProgress,
            float backdropAlpha,
            float rotationDegrees)
        {
            Phase = phase;
            PhaseElapsedSeconds = phaseElapsedSeconds;
            PhaseDurationSeconds = phaseDurationSeconds;
            PhaseProgress = phaseProgress;
            MoveProgress = moveProgress;
            BackdropAlpha = backdropAlpha;
            RotationDegrees = rotationDegrees;
        }

        public HomeRefrigeratorItemInspectionPhase Phase { get; }
        public float PhaseElapsedSeconds { get; }
        public float PhaseDurationSeconds { get; }
        public float PhaseProgress { get; }
        public float MoveProgress { get; }

        /// <summary>
        /// Alias used by consumers that apply the same blend to several
        /// item-presentation channels.
        /// </summary>
        public float ItemBlend => MoveProgress;

        public float BackdropAlpha { get; }
        public float RotationDegrees { get; }
    }

    /// <summary>
    /// Pure, frame-rate-independent state for examining one refrigerator item.
    /// Advance receives unscaled time. Inspecting persists until BeginReturn;
    /// Cancel immediately restores the browsing pose for lifecycle cleanup.
    /// </summary>
    public sealed class HomeRefrigeratorItemInspectionTimeline
    {
        public const float FlyingInDurationSeconds = 0.42f;
        public const float FlyingOutDurationSeconds = 0.34f;
        public const float RotationDegreesPerSecond = 18f;

        private double phaseElapsedSeconds;
        private double rotationElapsedSeconds;
        private float returnStartMoveProgress;

        public HomeRefrigeratorItemInspectionPhase Phase { get; private set; } =
            HomeRefrigeratorItemInspectionPhase.Browsing;
        public bool IsBrowsing =>
            Phase == HomeRefrigeratorItemInspectionPhase.Browsing;
        public bool IsActive => !IsBrowsing;
        public bool IsInspecting =>
            Phase == HomeRefrigeratorItemInspectionPhase.Inspecting;
        public bool CanBeginInspection => IsBrowsing;
        public bool CanBeginReturn =>
            IsInspecting ||
            Phase == HomeRefrigeratorItemInspectionPhase.FlyingIn;
        public float PhaseElapsedSeconds => (float)phaseElapsedSeconds;
        public float PhaseDurationSeconds => GetPhaseDurationSeconds(Phase);
        public float PhaseProgress => GetPhaseProgress();
        public HomeRefrigeratorItemInspectionFrame CurrentFrame =>
            EvaluateCurrentFrame();

        public bool BeginInspection()
        {
            if (!CanBeginInspection)
            {
                return false;
            }

            rotationElapsedSeconds = 0d;
            SetPhase(HomeRefrigeratorItemInspectionPhase.FlyingIn);
            return true;
        }

        public bool BeginReturn()
        {
            if (!CanBeginReturn)
            {
                return false;
            }

            returnStartMoveProgress = CurrentFrame.MoveProgress;
            if (returnStartMoveProgress <= 0f)
            {
                ResetToBrowsing();
                return true;
            }

            SetPhase(HomeRefrigeratorItemInspectionPhase.FlyingOut);
            return true;
        }

        public void Advance(float unscaledDeltaTime)
        {
            ValidateDeltaTime(unscaledDeltaTime);
            if (!IsActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            double remainingSeconds = unscaledDeltaTime;
            while (IsActive && remainingSeconds > 0d)
            {
                if (IsInspecting)
                {
                    phaseElapsedSeconds += remainingSeconds;
                    rotationElapsedSeconds += remainingSeconds;
                    return;
                }

                double duration = GetPhaseDurationSeconds(Phase);
                double phaseRemaining = Math.Max(
                    0d,
                    duration - phaseElapsedSeconds);
                double step = Math.Min(remainingSeconds, phaseRemaining);
                phaseElapsedSeconds += step;
                rotationElapsedSeconds += step;
                remainingSeconds -= step;

                if (phaseElapsedSeconds < duration)
                {
                    return;
                }

                AdvanceToNextPhase();
            }
        }

        public bool Cancel()
        {
            if (!IsActive)
            {
                return false;
            }

            ResetToBrowsing();
            return true;
        }

        private HomeRefrigeratorItemInspectionFrame EvaluateCurrentFrame()
        {
            float progress = GetPhaseProgress();
            float moveProgress;
            switch (Phase)
            {
                case HomeRefrigeratorItemInspectionPhase.FlyingIn:
                    moveProgress = SmootherStep(progress);
                    break;
                case HomeRefrigeratorItemInspectionPhase.Inspecting:
                    moveProgress = 1f;
                    break;
                case HomeRefrigeratorItemInspectionPhase.FlyingOut:
                    moveProgress =
                        returnStartMoveProgress *
                        (1f - SmootherStep(progress));
                    break;
                default:
                    moveProgress = 0f;
                    break;
            }

            return new HomeRefrigeratorItemInspectionFrame(
                Phase,
                PhaseElapsedSeconds,
                PhaseDurationSeconds,
                progress,
                moveProgress,
                moveProgress,
                GetRotationDegrees());
        }

        private float GetPhaseProgress()
        {
            if (IsBrowsing)
            {
                return 0f;
            }

            if (IsInspecting)
            {
                return 1f;
            }

            double duration = GetPhaseDurationSeconds(Phase);
            return Clamp01((float)(phaseElapsedSeconds / duration));
        }

        private float GetRotationDegrees()
        {
            if (IsBrowsing)
            {
                return 0f;
            }

            double degrees =
                rotationElapsedSeconds * RotationDegreesPerSecond;
            return (float)(degrees % 360d);
        }

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case HomeRefrigeratorItemInspectionPhase.FlyingIn:
                    SetPhase(
                        HomeRefrigeratorItemInspectionPhase.Inspecting);
                    break;
                case HomeRefrigeratorItemInspectionPhase.FlyingOut:
                    ResetToBrowsing();
                    break;
                default:
                    throw new InvalidOperationException(
                        "A persistent item-inspection phase cannot advance " +
                        "automatically.");
            }
        }

        private void SetPhase(HomeRefrigeratorItemInspectionPhase phase)
        {
            Phase = phase;
            phaseElapsedSeconds = 0d;
        }

        private void ResetToBrowsing()
        {
            SetPhase(HomeRefrigeratorItemInspectionPhase.Browsing);
            rotationElapsedSeconds = 0d;
            returnStartMoveProgress = 0f;
        }

        private float GetPhaseDurationSeconds(
            HomeRefrigeratorItemInspectionPhase phase)
        {
            switch (phase)
            {
                case HomeRefrigeratorItemInspectionPhase.FlyingIn:
                    return FlyingInDurationSeconds;
                case HomeRefrigeratorItemInspectionPhase.FlyingOut:
                    return
                        FlyingOutDurationSeconds *
                        returnStartMoveProgress;
                default:
                    return 0f;
            }
        }

        private static void ValidateDeltaTime(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime),
                    unscaledDeltaTime,
                    "Delta time must be finite and non-negative.");
            }
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }

        private static float Clamp01(float amount)
        {
            return Math.Max(0f, Math.Min(amount, 1f));
        }
    }
}
