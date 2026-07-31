using System;

namespace BarPromenade
{
    public enum HomeRefrigeratorInteractionPhase
    {
        Closed = 0,
        CameraApproach = 1,
        Reach = 2,
        Unsealing = 3,
        Opening = 4,
        Inspecting = 5,
        Closing = 6,
        Sealing = 7,
        CameraReturn = 8
    }

    /// <summary>
    /// One evaluated refrigerator-interaction pose. All channels are normalized
    /// except DoorOpen, which deliberately contains a small opening overshoot
    /// and gasket rebound for direct use as a door-angle multiplier.
    /// </summary>
    public readonly struct HomeRefrigeratorInteractionFrame
    {
        internal HomeRefrigeratorInteractionFrame(
            HomeRefrigeratorInteractionPhase phase,
            float phaseElapsedSeconds,
            float phaseDurationSeconds,
            float phaseProgress,
            float cameraBlend,
            float doorOpen,
            float handleTurn,
            float handReach,
            float lightIntensity)
        {
            Phase = phase;
            PhaseElapsedSeconds = phaseElapsedSeconds;
            PhaseDurationSeconds = phaseDurationSeconds;
            PhaseProgress = phaseProgress;
            CameraBlend = cameraBlend;
            DoorOpen = doorOpen;
            HandleTurn = handleTurn;
            HandReach = handReach;
            LightIntensity = lightIntensity;
        }

        public HomeRefrigeratorInteractionPhase Phase { get; }
        public float PhaseElapsedSeconds { get; }
        public float PhaseDurationSeconds { get; }
        public float PhaseProgress { get; }
        public float CameraBlend { get; }
        public float DoorOpen { get; }
        public float HandleTurn { get; }
        public float HandReach { get; }
        public float LightIntensity { get; }
    }

    /// <summary>
    /// Pure, frame-rate-independent state for the complete refrigerator
    /// interaction. Advance receives unscaled time. Inspecting persists until
    /// BeginClose is requested; Cancel immediately restores the closed pose.
    /// </summary>
    public sealed class HomeRefrigeratorInteractionTimeline
    {
        public const float CameraApproachDurationSeconds = 0.75f;
        public const float ReachDurationSeconds = 0.32f;
        public const float UnsealingDurationSeconds = 0.18f;
        public const float OpeningDurationSeconds = 0.90f;
        public const float ClosingDurationSeconds = 0.85f;
        public const float SealingDurationSeconds = 0.22f;
        public const float CameraReturnDurationSeconds = 0.70f;
        public const float DoorOpeningOvershoot = 0.011f;
        public const float DoorGasketRebound = 0.012f;

        public const float OpenSequenceDurationSeconds =
            CameraApproachDurationSeconds +
            ReachDurationSeconds +
            UnsealingDurationSeconds +
            OpeningDurationSeconds;
        public const float CloseSequenceDurationSeconds =
            ClosingDurationSeconds +
            SealingDurationSeconds +
            CameraReturnDurationSeconds;

        private double phaseElapsedSeconds;

        public HomeRefrigeratorInteractionPhase Phase { get; private set; } =
            HomeRefrigeratorInteractionPhase.Closed;
        public bool IsActive =>
            Phase != HomeRefrigeratorInteractionPhase.Closed;
        public bool IsInspecting =>
            Phase == HomeRefrigeratorInteractionPhase.Inspecting;
        public bool CanBeginOpen =>
            Phase == HomeRefrigeratorInteractionPhase.Closed;
        public bool CanBeginClose => IsInspecting;
        public float PhaseElapsedSeconds =>
            (float)phaseElapsedSeconds;
        public float PhaseDurationSeconds =>
            GetPhaseDurationSeconds(Phase);
        public float PhaseProgress => GetPhaseProgress();
        public HomeRefrigeratorInteractionFrame CurrentFrame =>
            EvaluateCurrentFrame();

        public bool BeginOpen()
        {
            if (!CanBeginOpen)
            {
                return false;
            }

            SetPhase(HomeRefrigeratorInteractionPhase.CameraApproach);
            return true;
        }

        public bool BeginClose()
        {
            if (!CanBeginClose)
            {
                return false;
            }

            SetPhase(HomeRefrigeratorInteractionPhase.Closing);
            return true;
        }

        public void Advance(float unscaledDeltaTime)
        {
            ValidateDeltaTime(unscaledDeltaTime);
            if (!IsActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            phaseElapsedSeconds += unscaledDeltaTime;
            if (IsInspecting)
            {
                return;
            }

            while (IsActive && !IsInspecting)
            {
                double duration = GetPhaseDurationSeconds(Phase);
                if (phaseElapsedSeconds < duration)
                {
                    return;
                }

                phaseElapsedSeconds -= duration;
                AdvanceToNextPhase();
            }
        }

        public bool Cancel()
        {
            if (!IsActive)
            {
                return false;
            }

            SetPhase(HomeRefrigeratorInteractionPhase.Closed);
            return true;
        }

        private HomeRefrigeratorInteractionFrame EvaluateCurrentFrame()
        {
            float progress = GetPhaseProgress();
            float cameraBlend = 0f;
            float doorOpen = 0f;
            float handleTurn = 0f;
            float handReach = 0f;
            float lightIntensity = 0f;

            switch (Phase)
            {
                case HomeRefrigeratorInteractionPhase.CameraApproach:
                    cameraBlend = SmootherStep(progress);
                    break;
                case HomeRefrigeratorInteractionPhase.Reach:
                    cameraBlend = 1f;
                    handReach = SmootherStep(progress);
                    break;
                case HomeRefrigeratorInteractionPhase.Unsealing:
                    cameraBlend = 1f;
                    handleTurn = SmootherStep(progress);
                    handReach = 1f;
                    break;
                case HomeRefrigeratorInteractionPhase.Opening:
                    cameraBlend = 1f;
                    doorOpen = EaseOutBack(progress);
                    handleTurn = 1f - SmootherRange(
                        progress,
                        0f,
                        0.32f);
                    handReach = 1f - SmootherRange(
                        progress,
                        0.18f,
                        0.72f);
                    lightIntensity = SmootherRange(
                        progress,
                        0.05f,
                        0.42f);
                    break;
                case HomeRefrigeratorInteractionPhase.Inspecting:
                    cameraBlend = 1f;
                    doorOpen = 1f;
                    lightIntensity = 1f;
                    break;
                case HomeRefrigeratorInteractionPhase.Closing:
                    cameraBlend = 1f;
                    doorOpen = 1f - SmootherRange(
                        progress,
                        0.18f,
                        1f);
                    handleTurn = SmootherRange(
                        progress,
                        0.75f,
                        1f);
                    handReach = SmootherRange(
                        progress,
                        0f,
                        0.22f);
                    lightIntensity = 1f - SmootherRange(
                        progress,
                        0.58f,
                        0.95f);
                    break;
                case HomeRefrigeratorInteractionPhase.Sealing:
                    cameraBlend = 1f;
                    doorOpen = DoorGasketRebound * GasketBump(progress);
                    handleTurn = 1f - SmootherRange(
                        progress,
                        0.10f,
                        0.75f);
                    handReach = 1f - SmootherRange(
                        progress,
                        0.35f,
                        1f);
                    break;
                case HomeRefrigeratorInteractionPhase.CameraReturn:
                    cameraBlend = 1f - SmootherStep(progress);
                    break;
            }

            return new HomeRefrigeratorInteractionFrame(
                Phase,
                PhaseElapsedSeconds,
                PhaseDurationSeconds,
                progress,
                cameraBlend,
                doorOpen,
                handleTurn,
                handReach,
                lightIntensity);
        }

        private float GetPhaseProgress()
        {
            if (Phase == HomeRefrigeratorInteractionPhase.Closed)
            {
                return 0f;
            }

            if (Phase == HomeRefrigeratorInteractionPhase.Inspecting)
            {
                return 1f;
            }

            float duration = GetPhaseDurationSeconds(Phase);
            return Clamp01((float)(phaseElapsedSeconds / duration));
        }

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case HomeRefrigeratorInteractionPhase.CameraApproach:
                    Phase = HomeRefrigeratorInteractionPhase.Reach;
                    break;
                case HomeRefrigeratorInteractionPhase.Reach:
                    Phase = HomeRefrigeratorInteractionPhase.Unsealing;
                    break;
                case HomeRefrigeratorInteractionPhase.Unsealing:
                    Phase = HomeRefrigeratorInteractionPhase.Opening;
                    break;
                case HomeRefrigeratorInteractionPhase.Opening:
                    Phase = HomeRefrigeratorInteractionPhase.Inspecting;
                    break;
                case HomeRefrigeratorInteractionPhase.Closing:
                    Phase = HomeRefrigeratorInteractionPhase.Sealing;
                    break;
                case HomeRefrigeratorInteractionPhase.Sealing:
                    Phase = HomeRefrigeratorInteractionPhase.CameraReturn;
                    break;
                case HomeRefrigeratorInteractionPhase.CameraReturn:
                    SetPhase(HomeRefrigeratorInteractionPhase.Closed);
                    break;
                default:
                    throw new InvalidOperationException(
                        "A persistent refrigerator phase cannot advance " +
                        "automatically.");
            }
        }

        private void SetPhase(HomeRefrigeratorInteractionPhase phase)
        {
            Phase = phase;
            phaseElapsedSeconds = 0d;
        }

        private static float GetPhaseDurationSeconds(
            HomeRefrigeratorInteractionPhase phase)
        {
            switch (phase)
            {
                case HomeRefrigeratorInteractionPhase.CameraApproach:
                    return CameraApproachDurationSeconds;
                case HomeRefrigeratorInteractionPhase.Reach:
                    return ReachDurationSeconds;
                case HomeRefrigeratorInteractionPhase.Unsealing:
                    return UnsealingDurationSeconds;
                case HomeRefrigeratorInteractionPhase.Opening:
                    return OpeningDurationSeconds;
                case HomeRefrigeratorInteractionPhase.Closing:
                    return ClosingDurationSeconds;
                case HomeRefrigeratorInteractionPhase.Sealing:
                    return SealingDurationSeconds;
                case HomeRefrigeratorInteractionPhase.CameraReturn:
                    return CameraReturnDurationSeconds;
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

        private static float EaseOutBack(float amount)
        {
            float clamped = Clamp01(amount);
            if (clamped <= 0f || clamped >= 1f)
            {
                return clamped;
            }

            const float overshootControl = 0.55f;
            const float cubicControl = 1f + overshootControl;
            float shifted = clamped - 1f;
            return 1f +
                cubicControl * shifted * shifted * shifted +
                overshootControl * shifted * shifted;
        }

        private static float SmootherRange(
            float amount,
            float start,
            float end)
        {
            float normalized = Clamp01(
                (amount - start) / (end - start));
            return SmootherStep(normalized);
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }

        private static float GasketBump(float amount)
        {
            float clamped = Clamp01(amount);
            float parabola = 4f * clamped * (1f - clamped);
            return parabola * parabola;
        }

        private static float Clamp01(float amount)
        {
            return Math.Max(0f, Math.Min(amount, 1f));
        }
    }
}
