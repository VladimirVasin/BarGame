using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The numbers behind a kettle that never stops boiling: pressure that
    /// climbs, a lid that trembles harder as it climbs, a vent that throws
    /// the lid up and lets the pressure go, and the steam rate that follows
    /// the same curve. Kept pure - no transform, no <c>Time</c>, no scene -
    /// so every one of those claims can be asserted in EditMode without a
    /// kettle in the room.
    ///
    /// The boil is deliberately NOT a function of the walker's clip or of
    /// the locomotion mixer. The lid keeps going while he stands, walks,
    /// sits on a bench or rides the bus, so it is fed the same delta the
    /// presentation advances with - accelerated for a distant walker
    /// exactly as his stride is - and nothing else. Lid and steam share one
    /// phase, which is what makes them read as a single boil rather than a
    /// jitter and a particle system standing next to each other.
    ///
    /// Amplitudes are chosen to READ at 640x360 through the city fog, not
    /// to be physically modest: the coin and the bartender's sway both
    /// record that a first pass at the honest size was invisible. Nothing
    /// here accumulates a pose - every output is recomputed from the cycle
    /// clock and the tremble clock, so a long session drifts nowhere.
    /// </summary>
    public sealed class KettleBoilModel
    {
        /// <summary>Shortest and longest gap between two vents. Hashed per
        /// instance and re-rolled after every vent, so three kettles on one
        /// street never vent in step.</summary>
        public const float MinimumVentPeriodSeconds = 2.2f;
        public const float MaximumVentPeriodSeconds = 3.1f;

        /// <summary>How long one vent takes from the lid leaving its seat to
        /// settling back.</summary>
        public const float VentSeconds = 0.6f;

        /// <summary>Peak lift of the lid during a vent, in metres.</summary>
        public const float VentLidLiftMetres = 0.014f;

        /// <summary>Peak tilt of the lid during a vent, in degrees.</summary>
        public const float VentLidTiltDegrees = 5.5f;

        /// <summary>The tremble between vents: a few millimetres and about a
        /// degree, growing with pressure.</summary>
        public const float TrembleLiftMetres = 0.003f;
        public const float TrembleTiltDegrees = 1.2f;
        public const float TrembleSpeed = 14f;

        /// <summary>Steam, in particles per second, between vents and at the
        /// top of one.</summary>
        public const float RestSteamRate = 3f;
        public const float VentSteamRate = 16f;

        /// <summary>Particles thrown in one go the moment a vent fires.</summary>
        public const int VentBurstCount = 6;

        /// <summary>A hitch longer than this is stepped as this, so a stalled
        /// frame cannot run several vents in one call.</summary>
        public const float MaximumStepSeconds = 0.25f;

        /// <summary>The tremble clock wraps here so float precision never
        /// erodes the wave after hours of play.</summary>
        public const float TrembleWrapSeconds = 1024f;

        /// <summary>Fraction of a vent spent throwing the lid up; the rest
        /// is the fall back.</summary>
        public const float VentAttackFraction = 0.15f;

        /// <summary>The tremble never dies out completely: even a freshly
        /// vented kettle is still on the boil.</summary>
        public const float MinimumTrembleAmplitude = 0.35f;

        /// <summary>The vent tilts the lid mostly one way, with a smaller
        /// wobble across it.</summary>
        public const float VentCrossTiltFraction = 0.45f;

        /// <summary>Steam thins as the pressure drops after a vent; this is
        /// how far it thins.</summary>
        public const float SteamPressureFloor = 0.7f;

        private const float SecondaryTrembleRate = 0.63f;
        private const float SecondaryTremblePhase = 2.4f;
        private const float TwoPi = Mathf.PI * 2f;
        private const uint PhaseSteps = 10000u;
        private const uint HashMultiplier = 2654435761u;
        private const int HashShift = 15;
        private const int StartFractionShift = 8;
        private const uint LinearCongruentialMultiplier = 1664525u;
        private const uint LinearCongruentialIncrement = 1013904223u;
        private const uint PeriodSteps = 1000u;

        private readonly float phase;
        private uint randomState;
        private float trembleTime;
        private int ventSide = 1;

        public KettleBoilModel(uint seed)
        {
            // Same hashing as the church candles: a phase from something
            // stable, or every kettle in the pool agrees on when to vent.
            uint hash = seed * HashMultiplier;
            hash ^= hash >> HashShift;
            phase = (hash % PhaseSteps) * (1f / PhaseSteps) * TwoPi;
            randomState = hash | 1u;
            Period = NextPeriod();
            CycleTime = Period *
                        (((hash >> StartFractionShift) % PhaseSteps) *
                         (1f / PhaseSteps));
            ventSide = (hash & 1u) == 0u ? 1 : -1;
            Evaluate();
        }

        /// <summary>Seconds into the current cycle.</summary>
        public float CycleTime { get; private set; }

        /// <summary>Length of the current cycle, in seconds.</summary>
        public float Period { get; private set; }

        /// <summary>`0` just after a vent, `1` on the point of the next.</summary>
        public float Pressure { get; private set; }

        /// <summary>The vent envelope, `0` between vents and `1` at the top
        /// of one.</summary>
        public float VentAmount { get; private set; }

        /// <summary>Lid lift in metres along the kettle's axis. Never below
        /// zero: the lid rests on its seat, it does not sink into it.</summary>
        public float LidLift { get; private set; }

        /// <summary>Lid tilt in degrees about the two axes across the kettle:
        /// `x` across the spout line, `y` along it.</summary>
        public Vector2 LidTilt { get; private set; }

        /// <summary>The raw tremble wave this step, for tests.</summary>
        public float Tremble { get; private set; }

        /// <summary>True only on the step that crossed a cycle boundary.</summary>
        public bool VentJustFired { get; private set; }

        /// <summary>Steam emission for this step, in particles per second.</summary>
        public float SteamRate { get; private set; }

        public void Advance(float deltaTime)
        {
            float step = Mathf.Clamp(
                Sanitize(deltaTime),
                0f,
                MaximumStepSeconds);
            VentJustFired = false;
            if (step > 0f)
            {
                CycleTime += step;
                if (CycleTime >= Period)
                {
                    CycleTime -= Period;
                    Period = NextPeriod();
                    ventSide = -ventSide;
                    VentJustFired = true;
                }

                trembleTime = Mathf.Repeat(
                    trembleTime + step,
                    TrembleWrapSeconds);
            }

            Evaluate();
        }

        /// <summary>
        /// Pure: the lid's excursion through one vent for
        /// <paramref name="x"/> = time since the vent fired over
        /// <see cref="VentSeconds"/>. A fast smooth rise, then a squared
        /// fall so the lid drops back faster than it left.
        /// </summary>
        public static float VentEnvelope(float x)
        {
            if (float.IsNaN(x) || x < 0f || x >= 1f)
            {
                return 0f;
            }

            if (x < VentAttackFraction)
            {
                return Mathf.SmoothStep(0f, 1f, x / VentAttackFraction);
            }

            float fall = 1f - ((x - VentAttackFraction) /
                               (1f - VentAttackFraction));
            return fall * fall;
        }

        private void Evaluate()
        {
            Pressure = Mathf.Clamp01(CycleTime / Period);
            VentAmount = VentEnvelope(CycleTime / VentSeconds);

            float waveA = ChurchCandleFlame.Wave(
                (trembleTime * TrembleSpeed) + phase);
            float waveB = ChurchCandleFlame.Wave(
                (trembleTime * TrembleSpeed * SecondaryTrembleRate) +
                phase +
                SecondaryTremblePhase);
            Tremble = waveA;
            float amplitude = MinimumTrembleAmplitude +
                              ((1f - MinimumTrembleAmplitude) * Pressure);

            LidLift = (VentAmount * VentLidLiftMetres) +
                      (amplitude * TrembleLiftMetres * Mathf.Max(0f, waveA));
            LidTilt = new Vector2(
                (VentAmount * VentLidTiltDegrees * ventSide) +
                (amplitude * TrembleTiltDegrees * waveA),
                (VentAmount * VentLidTiltDegrees * VentCrossTiltFraction *
                 waveB) +
                (amplitude * TrembleTiltDegrees * waveB));
            SteamRate = Mathf.Lerp(RestSteamRate, VentSteamRate, VentAmount) *
                        (SteamPressureFloor +
                         ((1f - SteamPressureFloor) * Pressure));
        }

        private float NextPeriod()
        {
            randomState = (randomState * LinearCongruentialMultiplier) +
                          LinearCongruentialIncrement;
            float unit = ((randomState >> 16) % PeriodSteps) *
                         (1f / PeriodSteps);
            return Mathf.Lerp(
                MinimumVentPeriodSeconds,
                MaximumVentPeriodSeconds,
                unit);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }
}
