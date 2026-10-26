using System;

namespace BarPromenade
{
    /// <summary>
    /// One bounded continuous vowel using the same source/filter state as speech.
    /// A single audio thread owns this instance; the caller supplies controls per block.
    /// Frequency and envelope describe the signal before spatial/perception processing.
    /// </summary>
    public sealed class RussianVocalizerSustain
    {
        public const float MinimumFrequencyHz = 110f;
        public const float MaximumFrequencyHz = 440f;
        public const float InitialFrequencyHz = 220f;
        public const double PitchResponseSeconds = .006;
        public const double AttackSeconds = .012;
        public const double ReleaseSeconds = .025;

        private const int CalibrationSamples = 4096;
        private const int CalibrationWarmup = 512;
        private const double SilenceThreshold = .00001;
        private readonly RussianVocalizerDsp dsp;
        private readonly double pitchAlpha, attackAlpha, releaseAlpha, fixedGain;
        private double frequencyHz = InitialFrequencyHz;
        private double envelope;
        private double targetFrequencyHz, envelopeTarget, envelopeAlpha;

        public float SmoothedFrequencyHz => (float)frequencyHz;
        public float Envelope => (float)envelope;
        public int SampleRate { get; }

        public RussianVocalizerSustain(in RussianVocalizerVoice voice, int sampleRate)
        {
            dsp = new RussianVocalizerDsp(voice, sampleRate);
            SampleRate = sampleRate;
            pitchAlpha = 1 - Math.Exp(-1 / (sampleRate * PitchResponseSeconds));
            attackAlpha = 1 - Math.Exp(-1 / (sampleRate * AttackSeconds));
            releaseAlpha = 1 - Math.Exp(-1 / (sampleRate * ReleaseSeconds));
            RussianVocalizerDsp.Shape vowel = RussianVocalizerDsp.Shape.For(RussianSpeechSound.A, false);
            dsp.SetPhone(vowel, RussianSpeechSound.A, false);
            dsp.Tune(vowel.F1, vowel.F2, vowel.F3);

            // Calibrate once at the middle of the range, never per callback or note.
            // The voice keeps a fixed gain as pitch moves; the final soft knee bounds peaks.
            double energy = 0;
            for (int i = 0; i < CalibrationSamples; i++)
            {
                double sample = dsp.Process(InitialFrequencyHz, .5, 0, 1, 1, 1);
                if (i >= CalibrationWarmup) energy += sample * sample;
            }
            double rms = Math.Sqrt(energy / (CalibrationSamples - CalibrationWarmup));
            fixedGain = rms > .000001 ? Math.Min(32, .13 / rms) : 0;
            Reset();
        }

        /// <summary>Clears phase/filter/envelope state without allocating or changing the voice.</summary>
        public void Reset()
        {
            dsp.Reset();
            frequencyHz = InitialFrequencyHz;
            envelope = 0;
        }

        /// <summary>
        /// Fills mono PCM without allocations. Gate release reaches exact silence;
        /// splitting the same controls across buffers does not restart phase or filters.
        /// </summary>
        public void Render(float[] output, int offset, int count, float targetFrequencyHz, bool singing)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (offset < 0 || count < 0 || offset > output.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            SetControls(targetFrequencyHz, singing);
            int end = offset + count;
            for (int i = offset; i < end; i++) output[i] = NextSample();
        }

        internal void SetControls(float target, bool singing)
        {
            targetFrequencyHz = float.IsFinite(target)
                ? Math.Max(MinimumFrequencyHz, Math.Min(MaximumFrequencyHz, target)) : frequencyHz;
            envelopeTarget = singing ? 1 : 0;
            envelopeAlpha = singing ? attackAlpha : releaseAlpha;
        }

        internal float NextSample()
        {
            frequencyHz += pitchAlpha * (targetFrequencyHz - frequencyHz);
            envelope += envelopeAlpha * (envelopeTarget - envelope);
            if (envelopeTarget == 0 && envelope < SilenceThreshold)
            {
                envelope = 0;
                return 0;
            }
            double sample = dsp.Process(frequencyHz, .5, 0, 1, 1, 1) * fixedGain;
            double limit = RussianVocalizerSynthesis.MaximumAmplitude;
            sample /= 1 + Math.Abs(sample) / limit;
            return (float)(sample * envelope);
        }
    }
}
