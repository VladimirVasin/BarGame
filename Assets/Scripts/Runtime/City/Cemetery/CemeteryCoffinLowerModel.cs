using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The numbers behind lowering a coffin on two ropes.
    /// </summary>
    public readonly struct CemeteryCoffinLowerSettings
    {
        private CemeteryCoffinLowerSettings(
            float headPayRate,
            float footPayRate,
            float tiltTolerance,
            float riskGain,
            float riskRecovery,
            float driftAmplitude,
            float driftFrequency,
            float driftSecondFrequency)
        {
            HeadPayRate = headPayRate;
            FootPayRate = footPayRate;
            TiltTolerance = tiltTolerance;
            RiskGain = riskGain;
            RiskRecovery = riskRecovery;
            DriftAmplitude = driftAmplitude;
            DriftFrequency = driftFrequency;
            DriftSecondFrequency = driftSecondFrequency;
        }

        /// <summary>
        /// How fast each end is let down while its key is held. The
        /// head end is the heavy one and runs through the hands
        /// faster, so the two are not the same rope.
        /// </summary>
        public float HeadPayRate { get; }
        public float FootPayRate { get; }

        /// <summary>How far off the balance point the box may hang
        /// before it starts to slip in the slings.</summary>
        public float TiltTolerance { get; }

        public float RiskGain { get; }
        public float RiskRecovery { get; }

        /// <summary>
        /// How far the balance point itself wanders, and how slowly.
        /// The amplitude is well past the tolerance on purpose: there
        /// is no setting of the two ropes that is safe for long, so
        /// the hero is never done correcting.
        ///
        /// The frequencies are low, and that is load bearing rather
        /// than taste. The point has to crawl slower than the ropes
        /// can chase it — a faster wander is not harder, it is simply
        /// impossible, and the first pass at these numbers was exactly
        /// that.
        /// </summary>
        public float DriftAmplitude { get; }
        public float DriftFrequency { get; }
        public float DriftSecondFrequency { get; }

        /// <summary>
        /// Tuned against the four ways of not playing. Touching
        /// nothing, holding one rope for the whole descent and holding
        /// both at once all lose the box; letting rope out at whichever
        /// end is high lands it in about five seconds, and does so
        /// whether the hand on the keys is twitchy or a quarter-second
        /// slow.
        /// </summary>
        public static CemeteryCoffinLowerSettings Default =>
            new CemeteryCoffinLowerSettings(
                0.42f,
                0.26f,
                0.22f,
                1.60f,
                0.80f,
                0.32f,
                0.22f,
                0.13f);
    }

    /// <summary>
    /// Act two, and the one thing it is not is a button.
    ///
    /// `Q` lets the head rope out and `E` lets the foot rope out. Touch
    /// nothing and nothing is let out: the box hangs exactly where it
    /// was left, because two ropes nobody is paying out do not move.
    ///
    /// What stops it being a matter of holding both keys is that the
    /// balance point does not stay put. Fresh-dug ground gives under
    /// one bearer and then the other, so the setting of the two ropes
    /// that hangs the box level crawls the whole time it is going down,
    /// and it crawls further than the tolerance is wide. That is why
    /// standing still loses it too: the box does not move, but level
    /// moves out from under it. Rope can only ever go out, never come
    /// back, so every correction is paid for in depth — and the head
    /// end runs faster than the foot, so the two are not interchangeable.
    ///
    /// Nothing here touches the world. The controller reads
    /// <see cref="Depth01"/> and <see cref="Tilt"/> and puts the coffin
    /// where they say.
    /// </summary>
    public sealed class CemeteryCoffinLowerModel
    {
        public const float FixedStep = 1f / 120f;

        private readonly CemeteryCoffinLowerSettings settings;
        private readonly float driftPhase;
        private readonly float driftSecondPhase;
        private float elapsed;
        private float accumulator;

        public CemeteryCoffinLowerModel(
            CemeteryCoffinLowerSettings lowerSettings,
            int seed)
        {
            settings = lowerSettings;
            uint bits = unchecked((uint)seed);
            driftPhase = UnitFromBits(bits) * Mathf.PI * 2f;
            driftSecondPhase =
                UnitFromBits(bits ^ 0x9E3779B9u) * Mathf.PI * 2f;
            Drift = EvaluateDrift(0f);
        }

        public CemeteryCoffinLowerSettings Settings => settings;

        /// <summary>Rope out at the head end, from <c>0</c> to
        /// <c>1</c>.</summary>
        public float HeadPayout { get; private set; }

        /// <summary>Rope out at the foot end.</summary>
        public float FootPayout { get; private set; }

        /// <summary>
        /// The only thing the hero directly controls: how much more
        /// rope is out at the head than at the foot.
        /// </summary>
        public float Balance => HeadPayout - FootPayout;

        /// <summary>
        /// Where the ground has moved the balance point to. The box
        /// hangs level when <see cref="Balance"/> sits at minus this,
        /// which is what the gauge draws and what the hero chases.
        /// </summary>
        public float Drift { get; private set; }

        /// <summary>How far down the box is: the two ends
        /// averaged.</summary>
        public float Depth01 =>
            (HeadPayout + FootPayout) * 0.5f;

        /// <summary>How far out of true it actually hangs — the
        /// balance against the point it should be at. Positive hangs
        /// head-down.</summary>
        public float Tilt { get; private set; }

        /// <summary>How close the slings are to letting go.
        /// </summary>
        public float SlipRisk { get; private set; }

        public bool IsComplete { get; private set; }
        public bool Succeeded { get; private set; }
        public bool Failed => IsComplete && !Succeeded;

        /// <summary>
        /// Steps the ropes. Each flag is that end's key held down this
        /// frame, and a held key lets that end down. Nothing held means
        /// nothing moves.
        /// </summary>
        public void Advance(
            float deltaTime,
            bool payHead,
            bool payFoot)
        {
            if (IsComplete)
            {
                return;
            }

            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            while (accumulator + 0.000001f >= FixedStep &&
                   !IsComplete)
            {
                SimulateStep(FixedStep, payHead, payFoot);
                accumulator -= FixedStep;
            }
        }

        private void SimulateStep(
            float step,
            bool payHead,
            bool payFoot)
        {
            if (payHead)
            {
                HeadPayout = Mathf.Clamp01(
                    HeadPayout + (settings.HeadPayRate * step));
            }

            if (payFoot)
            {
                FootPayout = Mathf.Clamp01(
                    FootPayout + (settings.FootPayRate * step));
            }

            elapsed += step;
            Drift = EvaluateDrift(elapsed);
            Tilt = Mathf.Clamp(Balance + Drift, -1f, 1f);

            float outside =
                Mathf.Abs(Tilt) - settings.TiltTolerance;
            if (outside > 0f)
            {
                float weight = outside /
                               Mathf.Max(
                                   0.0001f,
                                   1f - settings.TiltTolerance);
                SlipRisk += settings.RiskGain *
                            Mathf.Lerp(0.5f, 1.4f, weight) *
                            step;
            }
            else
            {
                SlipRisk -= settings.RiskRecovery * step;
            }

            SlipRisk = Mathf.Clamp01(SlipRisk);
            if (SlipRisk >= 1f)
            {
                IsComplete = true;
                Succeeded = false;
                return;
            }

            // Both ends have to be down. Averaging would let him drop
            // one rope to the floor and call it buried.
            if (HeadPayout >= 1f && FootPayout >= 1f)
            {
                IsComplete = true;
                Succeeded = true;
            }
        }

        /// <summary>
        /// Two slow waves that do not divide into each other, so the
        /// point never settles into a rhythm the hero can hold a key
        /// against.
        /// </summary>
        private float EvaluateDrift(float time)
        {
            float first = Mathf.Sin(
                (settings.DriftFrequency * Mathf.PI * 2f * time) +
                driftPhase);
            float second = Mathf.Sin(
                (settings.DriftSecondFrequency *
                 Mathf.PI *
                 2f *
                 time) +
                driftSecondPhase);
            return settings.DriftAmplitude *
                   ((first * 0.62f) + (second * 0.38f));
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
