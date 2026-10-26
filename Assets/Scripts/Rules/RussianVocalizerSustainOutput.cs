using System;

namespace BarPromenade
{
    /// <summary>
    /// Pulls only the current output block from the shared vowel. Playback rate
    /// resamples the whole voice (including formants), without a future PCM queue.
    /// </summary>
    public sealed class RussianVocalizerSustainOutput
    {
        private readonly RussianVocalizerSustain synthesis;
        private double phase;
        private float previous, next;
        private bool primed;
        public float SmoothedFrequencyHz => synthesis.SmoothedFrequencyHz;
        public float Envelope => synthesis.Envelope;

        public RussianVocalizerSustainOutput(in RussianVocalizerVoice voice)
        {
            synthesis = new RussianVocalizerSustain(voice, RussianVocalizerSynthesis.SampleRate);
        }

        public void Reset()
        {
            synthesis.Reset(); phase = 0; previous = next = 0; primed = false;
        }

        public void Render(float[] output, int channels, int outputSampleRate,
            float targetFrequencyHz, bool singing, float playbackRate)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (channels <= 0 || outputSampleRate < 8000 || output.Length % channels != 0)
                throw new ArgumentOutOfRangeException(nameof(channels));
            int frames = output.Length / channels;
            if (frames == 0) return;
            double rate = float.IsFinite(playbackRate) ? Math.Clamp(playbackRate, .25f, 1f) : 1f;
            double step = synthesis.SampleRate * rate / outputSampleRate;
            synthesis.SetControls(targetFrequencyHz, singing);
            if (!primed) { previous = synthesis.NextSample(); next = synthesis.NextSample(); primed = true; }
            for (int frame = 0; frame < frames; frame++)
            {
                float value = previous + (next - previous) * (float)phase;
                int first = frame * channels;
                for (int channel = 0; channel < channels; channel++) output[first + channel] = value;
                phase += step;
                while (phase >= 1)
                {
                    phase -= 1;
                    previous = next;
                    next = synthesis.NextSample();
                }
            }
        }
    }
}
