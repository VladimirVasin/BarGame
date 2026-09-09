using System;

namespace BarPromenade
{
    public enum WorldItemInspectionPhase
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
    public readonly struct WorldItemInspectionFrame
    {
        internal WorldItemInspectionFrame(
            WorldItemInspectionPhase phase,
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

        public WorldItemInspectionPhase Phase { get; }
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
    /// Pure, frame-rate-independent state for holding one item up and turning
    /// it: the refrigerator's shelf examination and the screen a thing picked
    /// off the ground opens are the same motion, so it is one timeline.
    /// Advance receives unscaled time. Inspecting persists until BeginReturn;
    /// Cancel immediately restores the browsing pose for lifecycle cleanup.
    /// </summary>
    public sealed class WorldItemInspectionTimeline
    {
        public const float FlyingInDurationSeconds = 0.42f;
        public const float FlyingOutDurationSeconds = 0.34f;
        public const float RotationDegreesPerSecond = 18f;

        /// <summary>
        /// How long the idle turn stays out of the way after the player has
        /// turned the item himself. Without the pause the drift would fight
        /// the hand that is holding the object still.
        /// </summary>
        public const float ManualRotationHoldSeconds = 0.7f;

        private double phaseElapsedSeconds;
        private double rotationElapsedSeconds;
        private double manualHoldRemainingSeconds;
        private double manualRotationDegrees;
        private float returnStartMoveProgress;

        public WorldItemInspectionPhase Phase { get; private set; } =
            WorldItemInspectionPhase.Browsing;
        public bool IsBrowsing =>
            Phase == WorldItemInspectionPhase.Browsing;
        public bool IsActive => !IsBrowsing;
        public bool IsInspecting =>
            Phase == WorldItemInspectionPhase.Inspecting;
        public bool CanBeginInspection => IsBrowsing;
        public bool CanBeginReturn =>
            IsInspecting ||
            Phase == WorldItemInspectionPhase.FlyingIn;
        public float PhaseElapsedSeconds => (float)phaseElapsedSeconds;
        public float PhaseDurationSeconds => GetPhaseDurationSeconds(Phase);
        public float PhaseProgress => GetPhaseProgress();

        /// <summary>
        /// Whether the idle turn is currently yielding to the player's own.
        /// </summary>
        public bool IsManuallyTurned => manualHoldRemainingSeconds > 0d;

        public float ManualRotationDegrees =>
            (float)manualRotationDegrees;

        public WorldItemInspectionFrame CurrentFrame =>
            EvaluateCurrentFrame();

        public bool BeginInspection()
        {
            if (!CanBeginInspection)
            {
                return false;
            }

            rotationElapsedSeconds = 0d;
            manualHoldRemainingSeconds = 0d;
            manualRotationDegrees = 0d;
            SetPhase(WorldItemInspectionPhase.FlyingIn);
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

            SetPhase(WorldItemInspectionPhase.FlyingOut);
            return true;
        }

        /// <summary>
        /// Turns the item by the player's own hand and holds the idle turn
        /// off for <see cref="ManualRotationHoldSeconds"/>. Degrees are taken
        /// as given, so the caller owns pointer and stick sensitivity.
        /// </summary>
        public bool AddManualRotation(float degrees)
        {
            ValidateDegrees(degrees);
            if (!IsActive)
            {
                return false;
            }

            manualRotationDegrees =
                Wrap360(manualRotationDegrees + degrees);
            manualHoldRemainingSeconds = ManualRotationHoldSeconds;
            return true;
        }

        public void Advance(float unscaledDeltaTime)
        {
            ValidateDeltaTime(unscaledDeltaTime);
            if (!IsActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            // The idle turn and the phase clock run on the same delta but
            // not on the same rule: a held item still flies to the eye
            // while the player is turning it.
            rotationElapsedSeconds += ConsumeManualHold(unscaledDeltaTime);

            double remainingSeconds = unscaledDeltaTime;
            while (IsActive && remainingSeconds > 0d)
            {
                if (IsInspecting)
                {
                    phaseElapsedSeconds += remainingSeconds;
                    return;
                }

                double duration = GetPhaseDurationSeconds(Phase);
                double phaseRemaining = Math.Max(
                    0d,
                    duration - phaseElapsedSeconds);
                double step = Math.Min(remainingSeconds, phaseRemaining);
                phaseElapsedSeconds += step;
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

        private double ConsumeManualHold(double deltaSeconds)
        {
            if (manualHoldRemainingSeconds <= 0d)
            {
                return deltaSeconds;
            }

            double consumed = Math.Min(
                deltaSeconds,
                manualHoldRemainingSeconds);
            manualHoldRemainingSeconds -= consumed;
            return deltaSeconds - consumed;
        }

        private WorldItemInspectionFrame EvaluateCurrentFrame()
        {
            float progress = GetPhaseProgress();
            float moveProgress;
            switch (Phase)
            {
                case WorldItemInspectionPhase.FlyingIn:
                    moveProgress = SmootherStep(progress);
                    break;
                case WorldItemInspectionPhase.Inspecting:
                    moveProgress = 1f;
                    break;
                case WorldItemInspectionPhase.FlyingOut:
                    moveProgress =
                        returnStartMoveProgress *
                        (1f - SmootherStep(progress));
                    break;
                default:
                    moveProgress = 0f;
                    break;
            }

            return new WorldItemInspectionFrame(
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
                rotationElapsedSeconds * RotationDegreesPerSecond +
                manualRotationDegrees;
            return (float)Wrap360(degrees);
        }

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case WorldItemInspectionPhase.FlyingIn:
                    SetPhase(WorldItemInspectionPhase.Inspecting);
                    break;
                case WorldItemInspectionPhase.FlyingOut:
                    ResetToBrowsing();
                    break;
                default:
                    throw new InvalidOperationException(
                        "A persistent item-inspection phase cannot advance " +
                        "automatically.");
            }
        }

        private void SetPhase(WorldItemInspectionPhase phase)
        {
            Phase = phase;
            phaseElapsedSeconds = 0d;
        }

        private void ResetToBrowsing()
        {
            SetPhase(WorldItemInspectionPhase.Browsing);
            rotationElapsedSeconds = 0d;
            manualHoldRemainingSeconds = 0d;
            manualRotationDegrees = 0d;
            returnStartMoveProgress = 0f;
        }

        private float GetPhaseDurationSeconds(
            WorldItemInspectionPhase phase)
        {
            switch (phase)
            {
                case WorldItemInspectionPhase.FlyingIn:
                    return FlyingInDurationSeconds;
                case WorldItemInspectionPhase.FlyingOut:
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

        private static void ValidateDegrees(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(degrees),
                    degrees,
                    "Manual rotation must be finite.");
            }
        }

        private static double Wrap360(double degrees)
        {
            double wrapped = degrees % 360d;
            return wrapped < 0d ? wrapped + 360d : wrapped;
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
