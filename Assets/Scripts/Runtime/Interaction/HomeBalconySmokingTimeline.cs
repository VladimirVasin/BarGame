using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure presentation timing layered over the atlas-driven player
    /// interaction. The atlas controller remains the authority for animation
    /// phase changes; this timeline supplies camera, drift and music data.
    /// </summary>
    public sealed class HomeBalconySmokingTimeline
    {
        public const float EnterDurationSeconds = 4f;
        public const float CameraHoldSeconds = 0.35f;
        public const float CameraArrivalSeconds = 3.60f;
        public const float MusicFadeInSeconds = 3.20f;
        public const float ExitDurationSeconds = 2f;

        private double phaseElapsedSeconds;
        private double presentationElapsedSeconds;

        public PlayerAnimatedInteractionPhase Phase
        {
            get;
            private set;
        } = PlayerAnimatedInteractionPhase.Idle;

        public double PhaseElapsedSeconds => phaseElapsedSeconds;

        public double PresentationElapsedSeconds =>
            presentationElapsedSeconds;

        public float CameraBlend => EvaluateCameraBlend(
            Phase,
            phaseElapsedSeconds);

        public float CameraDriftBlend => CameraBlend;

        public HomeBalconySmokingCameraDriftSample CameraDrift =>
            HomeBalconySmokingCameraDrift.Evaluate(
                presentationElapsedSeconds,
                CameraDriftBlend);

        public float MusicGain => EvaluateMusicGain(
            Phase,
            phaseElapsedSeconds);

        public bool Begin()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Idle)
            {
                return false;
            }

            presentationElapsedSeconds = 0d;
            SetPhase(PlayerAnimatedInteractionPhase.Entering);
            return true;
        }

        public bool EnterLooping()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Entering)
            {
                return false;
            }

            SetPhase(PlayerAnimatedInteractionPhase.Looping);
            return true;
        }

        public bool BeginExit()
        {
            if (Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            SetPhase(PlayerAnimatedInteractionPhase.Exiting);
            return true;
        }

        public void Advance(float deltaTime)
        {
            ValidateDeltaTime(deltaTime);
            if (Phase == PlayerAnimatedInteractionPhase.Idle ||
                deltaTime <= 0f)
            {
                return;
            }

            phaseElapsedSeconds += deltaTime;
            presentationElapsedSeconds += deltaTime;
        }

        public void Reset()
        {
            SetPhase(PlayerAnimatedInteractionPhase.Idle);
            presentationElapsedSeconds = 0d;
        }

        public static bool IsSafeExitLoopFrame(
            int localLoopFrame)
        {
            return localLoopFrame >= 0 &&
                   localLoopFrame <
                   HomeBalconySmokingPlan.LoopFrameCount &&
                   (localLoopFrame <= 3 || localLoopFrame >= 21);
        }

        public static float EvaluateCameraBlend(
            PlayerAnimatedInteractionPhase phase,
            double elapsedSeconds)
        {
            ValidateElapsed(elapsedSeconds);
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return Smooth01(
                        InverseLerp(
                            CameraHoldSeconds,
                            CameraArrivalSeconds,
                            elapsedSeconds));
                case PlayerAnimatedInteractionPhase.Looping:
                    return 1f;
                case PlayerAnimatedInteractionPhase.Exiting:
                    return 1f - Smooth01(
                        (float)(elapsedSeconds /
                        ExitDurationSeconds));
                default:
                    return 0f;
            }
        }

        public static float EvaluateMusicGain(
            PlayerAnimatedInteractionPhase phase,
            double elapsedSeconds)
        {
            ValidateElapsed(elapsedSeconds);
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return Smooth01(
                        (float)(elapsedSeconds /
                        MusicFadeInSeconds));
                case PlayerAnimatedInteractionPhase.Looping:
                    return 1f;
                case PlayerAnimatedInteractionPhase.Exiting:
                    return 1f - Smooth01(
                        (float)(elapsedSeconds /
                        ExitDurationSeconds));
                default:
                    return 0f;
            }
        }

        private void SetPhase(
            PlayerAnimatedInteractionPhase phase)
        {
            Phase = phase;
            phaseElapsedSeconds = 0d;
        }

        private static float InverseLerp(
            float start,
            float end,
            double value)
        {
            return Mathf.Clamp01(
                (float)((value - start) / (end - start)));
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static void ValidateDeltaTime(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "Delta time must be finite and non-negative.");
            }
        }

        private static void ValidateElapsed(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    elapsedSeconds,
                    "Elapsed time must be finite and non-negative.");
            }
        }
    }

    /// <summary>
    /// Local-space camera offset layered over the authored smoking camera
    /// path. Position is measured in metres and rotation in degrees.
    /// </summary>
    public readonly struct HomeBalconySmokingCameraDriftSample
    {
        public HomeBalconySmokingCameraDriftSample(
            Vector3 localPosition,
            Vector3 localEulerAngles)
        {
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
        }

        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
    }

    /// <summary>
    /// Pure, deterministic low-frequency camera motion for the balcony
    /// vignette. Harmonic periods are deliberately long so the result reads
    /// as breathing drift rather than handheld shake.
    /// </summary>
    public static class HomeBalconySmokingCameraDrift
    {
        public const float LateralAmplitudeMeters = 0.016f;
        public const float VerticalAmplitudeMeters = 0.007f;
        public const float DepthAmplitudeMeters = 0.005f;
        public const float PitchAmplitudeDegrees = 0.12f;
        public const float YawAmplitudeDegrees = 0.20f;
        public const float RollAmplitudeDegrees = 0.08f;

        private const double TwoPi = Math.PI * 2d;

        public static HomeBalconySmokingCameraDriftSample Evaluate(
            double elapsedSeconds,
            float normalizedWeight)
        {
            ValidateElapsed(elapsedSeconds);
            if (float.IsNaN(normalizedWeight) ||
                float.IsInfinity(normalizedWeight))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedWeight),
                    normalizedWeight,
                    "Camera drift weight must be finite.");
            }

            float weight = Mathf.Clamp01(normalizedWeight);
            if (weight <= 0f)
            {
                return new HomeBalconySmokingCameraDriftSample(
                    Vector3.zero,
                    Vector3.zero);
            }

            Vector3 localPosition = new Vector3(
                LateralAmplitudeMeters * Harmonic(
                    elapsedSeconds,
                    19d,
                    13d,
                    0.35d,
                    2.10d),
                VerticalAmplitudeMeters * Harmonic(
                    elapsedSeconds,
                    17d,
                    23d,
                    1.25d,
                    4.20d),
                DepthAmplitudeMeters * Harmonic(
                    elapsedSeconds,
                    23d,
                    15d,
                    3.05d,
                    0.70d)) * weight;
            Vector3 localEulerAngles = new Vector3(
                PitchAmplitudeDegrees * Harmonic(
                    elapsedSeconds,
                    21d,
                    14d,
                    2.35d,
                    5.10d),
                YawAmplitudeDegrees * Harmonic(
                    elapsedSeconds,
                    23d,
                    16d,
                    0.90d,
                    3.65d),
                RollAmplitudeDegrees * Harmonic(
                    elapsedSeconds,
                    18d,
                    13d,
                    4.45d,
                    1.55d)) * weight;
            return new HomeBalconySmokingCameraDriftSample(
                localPosition,
                localEulerAngles);
        }

        private static float Harmonic(
            double elapsedSeconds,
            double primaryPeriodSeconds,
            double secondaryPeriodSeconds,
            double primaryPhase,
            double secondaryPhase)
        {
            const double PrimaryWeight = 0.72d;
            const double SecondaryWeight = 1d - PrimaryWeight;
            double primary = Math.Sin(
                elapsedSeconds * TwoPi / primaryPeriodSeconds +
                primaryPhase);
            double secondary = Math.Sin(
                elapsedSeconds * TwoPi / secondaryPeriodSeconds +
                secondaryPhase);
            return (float)(
                primary * PrimaryWeight +
                secondary * SecondaryWeight);
        }

        private static void ValidateElapsed(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    elapsedSeconds,
                    "Elapsed time must be finite and non-negative.");
            }
        }
    }
}
