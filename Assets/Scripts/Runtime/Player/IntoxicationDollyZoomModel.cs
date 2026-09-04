using UnityEngine;

namespace BarPromenade
{
    public enum IntoxicationDollyZoomPhase
    {
        Rest = 0,
        Out,
        Peak,
        Back
    }

    /// <summary>
    /// The drunk camera's breath: a pure, seeded oscillator that drives the
    /// dolly zoom above the balance threshold. Each cycle leaves rest for
    /// one extreme, lingers there, comes back and lingers again, and the
    /// cycle's direction, reach, pace and easing shape are all drawn when
    /// it starts, so no two breaths match while every one of them stays
    /// smooth. The output is a signed exponent in [-1, 1] — positive is
    /// the wide, pushed-in side, negative the narrow, pulled-back side —
    /// which <see cref="PlayerCameraFollow"/> maps onto its field of view
    /// and arm length together, so the hero keeps his size while the
    /// world stretches or flattens behind him.
    ///
    /// The holds are the point the user asked for: a fast excursion
    /// lingers briefly, a slow one longer, because each hold is a fraction
    /// of the leg that led into it. Easing is a smootherstep over a
    /// randomly warped time axis; its slope is zero at both ends for any
    /// warp above one third, so a leg joins its holds without a kink no
    /// matter how the pace was drawn.
    /// </summary>
    public sealed class IntoxicationDollyZoomModel
    {
        public const float MaximumStepSeconds = 0.1f;
        public const float InitialRestSeconds = 1.2f;
        public const float MinimumAmplitudeFraction = 0.55f;
        public const float SlowLegMinimumSeconds = 3.2f;
        public const float SlowLegMaximumSeconds = 6.5f;
        public const float FastLegMinimumSeconds = 0.8f;
        public const float FastLegMaximumSeconds = 2.6f;
        public const float PeakHoldMinimumFraction = 0.12f;
        public const float PeakHoldMaximumFraction = 0.4f;
        public const float RestHoldMinimumFraction = 0.25f;
        public const float RestHoldMaximumFraction = 0.8f;
        public const float MinimumShapeExponent = 0.65f;
        public const float MaximumShapeExponent = 1.6f;
        public const float WideSideProbability = 0.65f;

        private const float MinimumPhaseSeconds = 0.01f;

        private readonly System.Random random;
        private IntoxicationDollyZoomPhase phase;
        private float phaseElapsed;
        private float phaseDuration;
        private float sign = 1f;
        private float cycleAmplitude;
        private float outLegSeconds;
        private float backLegSeconds;
        private float outShape = 1f;
        private float backShape = 1f;
        private float peakHoldSeconds;
        private float restHoldSeconds;

        public IntoxicationDollyZoomModel(int seed)
        {
            random = new System.Random(seed);
            Reset(InitialRestSeconds);
        }

        /// <summary>Signed reach, -1..1: positive wide/pushed-in, negative narrow/pulled-back.</summary>
        public float Exponent { get; private set; }
        public IntoxicationDollyZoomPhase Phase => phase;
        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;

        /// <summary>The running cycle's unsigned reach, latched when it left rest.</summary>
        public float CycleAmplitude => cycleAmplitude;
        public float CycleSign => sign;
        public float OutLegSeconds => outLegSeconds;
        public float BackLegSeconds => backLegSeconds;
        public float PeakHoldSeconds => peakHoldSeconds;
        public float RestHoldSeconds => restHoldSeconds;

        /// <summary>
        /// Back to rest, holding there for <paramref name="restSeconds"/>
        /// before the next breath may begin. The random stream is kept.
        /// </summary>
        public void Reset(float restSeconds = InitialRestSeconds)
        {
            phase = IntoxicationDollyZoomPhase.Rest;
            phaseElapsed = 0f;
            phaseDuration = float.IsNaN(restSeconds)
                ? 0f
                : Mathf.Max(0f, restSeconds);
            cycleAmplitude = 0f;
            Exponent = 0f;
        }

        /// <summary>
        /// Advances the breath. <paramref name="strength"/> is the profile's
        /// reach (0 keeps rest, and a running cycle finishes with the reach
        /// it latched); <paramref name="pace"/> is 0 at the threshold and 1
        /// at the top level and sets how fast a new cycle may be drawn;
        /// <paramref name="narrowAllowed"/> is false when the camera has
        /// no room behind it, so a new cycle can only push in.
        /// </summary>
        public void Advance(
            float deltaTime,
            float strength,
            float pace,
            bool narrowAllowed)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            float clampedStrength = float.IsNaN(strength)
                ? 0f
                : Mathf.Clamp01(strength);
            float clampedPace = float.IsNaN(pace)
                ? 0f
                : Mathf.Clamp01(pace);
            float remaining = Mathf.Min(deltaTime, MaximumStepSeconds);
            while (remaining > 0f)
            {
                float room = phaseDuration - phaseElapsed;
                if (remaining < room)
                {
                    phaseElapsed += remaining;
                    break;
                }

                remaining -= Mathf.Max(0f, room);
                phaseElapsed = phaseDuration;
                if (!CompletePhase(
                        clampedStrength,
                        clampedPace,
                        narrowAllowed))
                {
                    break;
                }
            }

            Exponent = Evaluate();
        }

        /// <summary>
        /// Smootherstep over a warped time axis. Zero slope at both ends
        /// for any <paramref name="shape"/> above one third.
        /// </summary>
        public static float Ease(float t, float shape)
        {
            float warped = Mathf.Pow(
                Mathf.Clamp01(t),
                Mathf.Max(0.34f, shape));
            return warped * warped * warped *
                   (warped * (warped * 6f - 15f) + 10f);
        }

        private bool CompletePhase(
            float strength,
            float pace,
            bool narrowAllowed)
        {
            switch (phase)
            {
                case IntoxicationDollyZoomPhase.Rest:
                    if (strength <= 0f)
                    {
                        // Sober enough: stay at rest with the hold spent,
                        // so the first drink above the threshold breathes
                        // without waiting out an old hold.
                        return false;
                    }

                    StartCycle(strength, pace, narrowAllowed);
                    EnterPhase(
                        IntoxicationDollyZoomPhase.Out,
                        outLegSeconds);
                    return true;
                case IntoxicationDollyZoomPhase.Out:
                    EnterPhase(
                        IntoxicationDollyZoomPhase.Peak,
                        peakHoldSeconds);
                    return true;
                case IntoxicationDollyZoomPhase.Peak:
                    EnterPhase(
                        IntoxicationDollyZoomPhase.Back,
                        backLegSeconds);
                    return true;
                default:
                    EnterPhase(
                        IntoxicationDollyZoomPhase.Rest,
                        restHoldSeconds);
                    return true;
            }
        }

        private void EnterPhase(
            IntoxicationDollyZoomPhase nextPhase,
            float seconds)
        {
            phase = nextPhase;
            phaseElapsed = 0f;
            phaseDuration = Mathf.Max(MinimumPhaseSeconds, seconds);
        }

        private void StartCycle(
            float strength,
            float pace,
            bool narrowAllowed)
        {
            // Every draw happens on every cycle, in this order, so the
            // stream replays for a seed whatever the room behind the camera.
            bool wide = NextUnit() < WideSideProbability;
            sign = wide || !narrowAllowed ? 1f : -1f;
            cycleAmplitude =
                strength *
                Mathf.Lerp(MinimumAmplitudeFraction, 1f, NextUnit());
            float legMinimum = Mathf.Lerp(
                SlowLegMinimumSeconds,
                FastLegMinimumSeconds,
                pace);
            float legMaximum = Mathf.Lerp(
                SlowLegMaximumSeconds,
                FastLegMaximumSeconds,
                pace);
            outLegSeconds = Mathf.Lerp(legMinimum, legMaximum, NextUnit());
            backLegSeconds = Mathf.Lerp(legMinimum, legMaximum, NextUnit());
            outShape = Mathf.Lerp(
                MinimumShapeExponent,
                MaximumShapeExponent,
                NextUnit());
            backShape = Mathf.Lerp(
                MinimumShapeExponent,
                MaximumShapeExponent,
                NextUnit());
            peakHoldSeconds =
                outLegSeconds *
                Mathf.Lerp(
                    PeakHoldMinimumFraction,
                    PeakHoldMaximumFraction,
                    NextUnit());
            restHoldSeconds =
                backLegSeconds *
                Mathf.Lerp(
                    RestHoldMinimumFraction,
                    RestHoldMaximumFraction,
                    NextUnit());
        }

        private float Evaluate()
        {
            switch (phase)
            {
                case IntoxicationDollyZoomPhase.Out:
                    return sign *
                           cycleAmplitude *
                           Ease(phaseElapsed / phaseDuration, outShape);
                case IntoxicationDollyZoomPhase.Peak:
                    return sign * cycleAmplitude;
                case IntoxicationDollyZoomPhase.Back:
                    return sign *
                           cycleAmplitude *
                           (1f - Ease(phaseElapsed / phaseDuration, backShape));
                default:
                    return 0f;
            }
        }

        private float NextUnit()
        {
            return (float)random.NextDouble();
        }
    }
}
