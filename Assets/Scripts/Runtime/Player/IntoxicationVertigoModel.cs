using UnityEngine;

namespace BarPromenade
{
    public enum IntoxicationVertigoPhase
    {
        Rest = 0,
        Out,
        Peak,
        Back
    }

    /// <summary>
    /// The drunk vertigo: a pure, seeded oscillator that winds the frame
    /// around the hero above the balance threshold. It carries two outputs
    /// that live on different clocks on purpose.
    ///
    /// <see cref="Twist"/> is the whirlpool, and it breathes in episodes the
    /// way <see cref="IntoxicationDollyZoomModel"/> does — rest, a wind-up to
    /// one side, a hold there, an unwind, a rest again — with the direction,
    /// reach, pace and easing shape all drawn when a cycle starts. The legs
    /// are longer than the lens's: a whirlpool takes seconds to spin up, and
    /// a vertigo attack that never let go would be a state rather than an
    /// episode. Its sign is which way the frame turns, drawn 50/50, so the
    /// water does not always run the same way.
    ///
    /// <see cref="CoreOffsetPixels"/> is the disc over the hero's own body,
    /// and it never stops: above the threshold his own image always floats a
    /// couple of internal pixels, slowly circling, even while the whirlpool
    /// around him is resting. That is the split the effect was asked for —
    /// the hero's region distorted a hair, everything around him winding up.
    ///
    /// Both are advanced from the caller's delta, so a paused game freezes
    /// the water. The shader mirrors the geometry; this class only says how
    /// far and which way.
    /// </summary>
    public sealed class IntoxicationVertigoModel
    {
        /// <summary>
        /// The reach at the frame's farthest corner when a cycle is drawn at
        /// full strength. Chosen with the amplitude floor below so a breath
        /// at the top level peaks between 32 and 44 degrees: past a quarter
        /// turn the corners stop reading as the frame being sucked in.
        /// </summary>
        public const float MaximumTwistDegrees = 44f;
        public const float MaximumTwistRadians =
            MaximumTwistDegrees * Mathf.Deg2Rad;

        /// <summary>
        /// How far the hero's own disc drifts, in internal pixels — the
        /// composite multiplies it by the internal texel size, so it means
        /// the same thing at every output resolution.
        /// </summary>
        public const float CoreWobbleInternalPixels = 2f;

        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// Longer than the dolly zoom's opening rest, so the first drink
        /// past the threshold does not start both breaths on the same frame.
        /// </summary>
        public const float InitialRestSeconds = 2f;

        public const float MinimumAmplitudeFraction = 0.72f;
        public const float SlowLegMinimumSeconds = 4.5f;
        public const float SlowLegMaximumSeconds = 9f;
        public const float FastLegMinimumSeconds = 1.8f;
        public const float FastLegMaximumSeconds = 4.2f;
        public const float PeakHoldMinimumFraction = 0.18f;
        public const float PeakHoldMaximumFraction = 0.5f;
        public const float RestHoldMinimumFraction = 0.4f;
        public const float RestHoldMaximumFraction = 1.2f;
        public const float MinimumShapeExponent = 0.65f;
        public const float MaximumShapeExponent = 1.6f;
        public const float ClockwiseProbability = 0.5f;
        public const float CoreRateMinimumRadians = 0.45f;
        public const float CoreRateMaximumRadians = 1.1f;

        private const float MinimumPhaseSeconds = 0.01f;

        private readonly System.Random random;
        private IntoxicationVertigoPhase phase;
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
        private float coreAngle;
        private float coreRate =
            (CoreRateMinimumRadians + CoreRateMaximumRadians) * 0.5f;

        public IntoxicationVertigoModel(int seed)
        {
            random = new System.Random(seed);
            Reset(InitialRestSeconds);
        }

        /// <summary>Signed reach, -1..1, as a fraction of the maximum twist.</summary>
        public float Twist { get; private set; }

        /// <summary>The same reach in radians, which is what the shader takes.</summary>
        public float TwistRadians => Twist * MaximumTwistRadians;

        /// <summary>
        /// Where the hero's own disc sits this frame, in internal pixels.
        /// </summary>
        public Vector2 CoreOffsetPixels { get; private set; }

        public IntoxicationVertigoPhase Phase => phase;
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
        /// Back to still water, holding for <paramref name="restSeconds"/>
        /// before the next attack may begin. The random stream is kept.
        /// </summary>
        public void Reset(float restSeconds = InitialRestSeconds)
        {
            phase = IntoxicationVertigoPhase.Rest;
            phaseElapsed = 0f;
            phaseDuration = float.IsNaN(restSeconds)
                ? 0f
                : Mathf.Max(0f, restSeconds);
            cycleAmplitude = 0f;
            Twist = 0f;
            coreAngle = 0f;
            CoreOffsetPixels = Vector2.zero;
        }

        /// <summary>
        /// Advances the water. <paramref name="strength"/> is the profile's
        /// reach (0 keeps still water and empties the disc, and a running
        /// cycle finishes with the reach it latched); <paramref name="pace"/>
        /// is 0 at the balance threshold and 1 at the top level and sets how
        /// fast a new attack may be drawn.
        /// </summary>
        public void Advance(
            float deltaTime,
            float strength,
            float pace)
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
            float step = Mathf.Min(deltaTime, MaximumStepSeconds);
            float remaining = step;
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
                if (!CompletePhase(clampedStrength, clampedPace))
                {
                    break;
                }
            }

            Twist = Evaluate();
            // The disc circles on its own clock: the hero keeps floating
            // while the whirlpool rests, and stops only when he sobers up.
            coreAngle = Mathf.Repeat(
                coreAngle + coreRate * step,
                Mathf.PI * 2f);
            float coreAmplitude =
                clampedStrength * CoreWobbleInternalPixels;
            CoreOffsetPixels = new Vector2(
                Mathf.Cos(coreAngle) * coreAmplitude,
                Mathf.Sin(coreAngle) * coreAmplitude);
        }

        private bool CompletePhase(float strength, float pace)
        {
            switch (phase)
            {
                case IntoxicationVertigoPhase.Rest:
                    if (strength <= 0f)
                    {
                        // Sober enough: stay in still water with the hold
                        // spent, so the first drink past the threshold winds
                        // up without waiting out an old rest.
                        return false;
                    }

                    StartCycle(strength, pace);
                    EnterPhase(
                        IntoxicationVertigoPhase.Out,
                        outLegSeconds);
                    return true;
                case IntoxicationVertigoPhase.Out:
                    EnterPhase(
                        IntoxicationVertigoPhase.Peak,
                        peakHoldSeconds);
                    return true;
                case IntoxicationVertigoPhase.Peak:
                    EnterPhase(
                        IntoxicationVertigoPhase.Back,
                        backLegSeconds);
                    return true;
                default:
                    EnterPhase(
                        IntoxicationVertigoPhase.Rest,
                        restHoldSeconds);
                    return true;
            }
        }

        private void EnterPhase(
            IntoxicationVertigoPhase nextPhase,
            float seconds)
        {
            phase = nextPhase;
            phaseElapsed = 0f;
            phaseDuration = Mathf.Max(MinimumPhaseSeconds, seconds);
        }

        private void StartCycle(float strength, float pace)
        {
            // Every draw happens on every cycle, in this order, so a seed
            // replays the same water however the level moved in between.
            sign = NextUnit() < ClockwiseProbability ? 1f : -1f;
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
            coreRate = Mathf.Lerp(
                CoreRateMinimumRadians,
                CoreRateMaximumRadians,
                NextUnit());
        }

        private float Evaluate()
        {
            // The lens's easing, not a second copy of it: smootherstep over a
            // warped time axis, zero slope at both ends.
            switch (phase)
            {
                case IntoxicationVertigoPhase.Out:
                    return sign *
                           cycleAmplitude *
                           IntoxicationDollyZoomModel.Ease(
                               phaseElapsed / phaseDuration,
                               outShape);
                case IntoxicationVertigoPhase.Peak:
                    return sign * cycleAmplitude;
                case IntoxicationVertigoPhase.Back:
                    return sign *
                           cycleAmplitude *
                           (1f -
                            IntoxicationDollyZoomModel.Ease(
                                phaseElapsed / phaseDuration,
                                backShape));
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
