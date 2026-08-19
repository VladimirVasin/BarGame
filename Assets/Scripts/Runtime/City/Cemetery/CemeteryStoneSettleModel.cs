using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The numbers behind standing a stone up straight.
    /// </summary>
    public readonly struct CemeteryStoneSettleSettings
    {
        private CemeteryStoneSettleSettings(
            float plumbTolerance,
            float settleRate,
            float slipRate,
            float driftAcceleration,
            float driftFrequency,
            float inputAcceleration,
            float damping,
            float tampBias)
        {
            PlumbTolerance = plumbTolerance;
            SettleRate = settleRate;
            SlipRate = slipRate;
            DriftAcceleration = driftAcceleration;
            DriftFrequency = driftFrequency;
            InputAcceleration = inputAcceleration;
            Damping = damping;
            TampBias = tampBias;
        }

        /// <summary>How far off plumb the stone may lean and still be
        /// worth tamping.</summary>
        public float PlumbTolerance { get; }

        /// <summary>Settle gained per second of honest tamping.
        /// </summary>
        public float SettleRate { get; }

        /// <summary>Settle lost per second while the stone stands out
        /// of true and nobody is correcting it.</summary>
        public float SlipRate { get; }

        /// <summary>How hard the soft ground under one edge keeps
        /// pulling it over.</summary>
        public float DriftAcceleration { get; }
        public float DriftFrequency { get; }

        /// <summary>How hard a shoulder against the stone answers
        /// that.</summary>
        public float InputAcceleration { get; }
        public float Damping { get; }

        /// <summary>
        /// Tamping an out-of-plumb stone drives the low side further
        /// down. This is the whole reason the act is not simply "hold
        /// the key": the obvious move is the wrong one.
        /// </summary>
        public float TampBias { get; }

        public static CemeteryStoneSettleSettings Default =>
            new CemeteryStoneSettleSettings(
                0.18f,
                0.46f,
                0.22f,
                1.55f,
                0.63f,
                3.10f,
                1.35f,
                1.05f);
    }

    /// <summary>
    /// Act four: the monument is stood at the head of the mound and
    /// tamped in. Fresh-turned earth will not hold a stone by itself,
    /// so it leans, and the hero shoulders it back while he treads the
    /// ground down around the foot.
    ///
    /// There is no way to lose. A stone can be got wrong for as long
    /// as the hero likes and the only cost is that the settle he had
    /// bleeds away — which is right for the last act of a job that is
    /// already paid for in effort. What it will not tolerate is
    /// tamping a leaning stone: that drives the low edge down and
    /// makes the lean worse, so the work has to be done in the order a
    /// mason would do it.
    /// </summary>
    public sealed class CemeteryStoneSettleModel
    {
        public const float FixedStep = 1f / 120f;

        private readonly CemeteryStoneSettleSettings settings;
        private float driftPhase;
        private float secondaryPhase;
        private float accumulator;

        public CemeteryStoneSettleModel(
            CemeteryStoneSettleSettings settleSettings,
            int seed)
        {
            settings = settleSettings;
            uint bits = unchecked((uint)seed);
            driftPhase = UnitFromBits(bits) * Mathf.PI * 2f;
            secondaryPhase =
                UnitFromBits(bits ^ 0x9E3779B9u) * Mathf.PI * 2f;
            Lean = (UnitFromBits(bits ^ 0x165667B1u) - 0.5f) * 0.5f;
        }

        public CemeteryStoneSettleSettings Settings => settings;

        /// <summary>How far the stone stands off plumb, from
        /// <c>-1</c> to <c>1</c>.</summary>
        public float Lean { get; private set; }

        public float LeanVelocity { get; private set; }

        /// <summary>How far into the ground it is trodden.
        /// </summary>
        public float Settle01 { get; private set; }

        /// <summary>True while the stone is straight enough that
        /// treading the earth down does any good.</summary>
        public bool IsPlumb =>
            Mathf.Abs(Lean) <= settings.PlumbTolerance;

        public bool IsComplete => Settle01 >= 1f;

        /// <summary>
        /// Steps the stone. <paramref name="input"/> is the shoulder
        /// against it, <c>-1</c> to <c>1</c>;
        /// <paramref name="tamping"/> is the foot on the earth.
        /// </summary>
        public void Advance(
            float deltaTime,
            float input,
            bool tamping)
        {
            if (IsComplete)
            {
                return;
            }

            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            float clamped = Mathf.Clamp(input, -1f, 1f);
            while (accumulator + 0.000001f >= FixedStep &&
                   !IsComplete)
            {
                SimulateStep(FixedStep, clamped, tamping);
                accumulator -= FixedStep;
            }
        }

        private void SimulateStep(
            float step,
            float input,
            bool tamping)
        {
            driftPhase += settings.DriftFrequency *
                          Mathf.PI *
                          2f *
                          step;
            secondaryPhase += settings.DriftFrequency *
                              1.63f *
                              Mathf.PI *
                              2f *
                              step;
            float drift =
                Mathf.Sin(driftPhase) +
                (Mathf.Sin(secondaryPhase) * 0.38f);
            float acceleration =
                (drift * settings.DriftAcceleration) +
                (input * settings.InputAcceleration) -
                (LeanVelocity * settings.Damping);

            // The foot on the earth is a lever on whichever side is
            // already low.
            if (tamping && !IsPlumb)
            {
                acceleration +=
                    Mathf.Sign(Lean) * settings.TampBias;
            }

            LeanVelocity = Mathf.Clamp(
                LeanVelocity + (acceleration * step),
                -2.4f,
                2.4f);
            Lean += LeanVelocity * step;
            if (Mathf.Abs(Lean) > 1f)
            {
                Lean = Mathf.Sign(Lean);
                LeanVelocity *= -0.18f;
            }

            if (tamping && IsPlumb)
            {
                Settle01 = Mathf.Clamp01(
                    Settle01 + (settings.SettleRate * step));
            }
            else if (!IsPlumb)
            {
                Settle01 = Mathf.Clamp01(
                    Settle01 - (settings.SlipRate * step));
            }
        }

        private static float UnitFromBits(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
