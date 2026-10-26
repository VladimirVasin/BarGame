using System;

namespace BarPromenade
{
    /// <summary>Continuous semitone control and fresh-click admission after an input interruption.</summary>
    public sealed class KaraokeControlState
    {
        public const float MinimumFrequencyHz = RussianVocalizerSustain.MinimumFrequencyHz,
            MaximumFrequencyHz = RussianVocalizerSustain.MaximumFrequencyHz;
        public const float SemitoneRange = 24f;
        public const float MaximumDriftSemitones = 1.5f;
        private const double Tau = Math.PI * 2;
        private const float MaximumDriftStepSeconds = .1f;
        private bool awaitingRelease = true;
        private double slowDriftPhase, fastDriftPhase;
        public float NormalizedPitch { get; private set; } = .5f;
        public float FrequencyHz => FrequencyAt(NormalizedPitch);
        public float DriftSemitones { get; private set; }
        public float EffectiveFrequencyHz => FrequencyAt(NormalizedPitch + DriftSemitones / SemitoneRange);
        public bool IsSinging { get; private set; }

        public void Advance(float normalizedDelta, bool held, bool available)
        {
            if (!available) { Suspend(); return; }
            if (float.IsFinite(normalizedDelta))
                NormalizedPitch = Math.Clamp(NormalizedPitch + normalizedDelta, 0f, 1f);
            if (!held) awaitingRelease = false;
            IsSinging = held && !awaitingRelease;
        }

        /// <summary>
        /// Adds bounded pitch instability over the independent mouse selection.
        /// The caller supplies the shared smoothed level and active real-time delta;
        /// unavailable input freezes both phase and offset without a later catch-up.
        /// </summary>
        public void AdvanceDrift(float realSeconds, float intoxicationLevel, bool available)
        {
            if (!available || !float.IsFinite(realSeconds) || realSeconds < 0f) return;
            // A focus return or long frame must not jump across an unseen sway.
            double seconds = Math.Min(realSeconds, MaximumDriftStepSeconds);
            slowDriftPhase = (slowDriftPhase + Tau * .31 * seconds) % Tau;
            fastDriftPhase = (fastDriftPhase + Tau * .77 * seconds) % Tau;
            double level = float.IsFinite(intoxicationLevel)
                ? Math.Clamp(intoxicationLevel, 0f, 100f) / 100d : 0;
            if (level == 0) { DriftSemitones = 0f; return; }
            double amplitude = MaximumDriftSemitones * level * level * (3 - 2 * level);
            DriftSemitones = (float)(amplitude *
                (.7 * Math.Sin(slowDriftPhase) + .3 * Math.Sin(fastDriftPhase)));
        }

        public void Suspend() { awaitingRelease = true; IsSinging = false; }
        public static float FrequencyAt(float normalized) => MinimumFrequencyHz *
            (float)Math.Pow(2, Math.Clamp(float.IsFinite(normalized) ? normalized : .5f, 0f, 1f) * 2);
        public static float Normalize(float frequencyHz) => float.IsFinite(frequencyHz) && frequencyHz > 0f
            ? Math.Clamp((float)(Math.Log(frequencyHz / MinimumFrequencyHz, 2) / 2), 0f, 1f) : .5f;
    }
}
