using UnityEngine;

namespace BarPromenade
{
    /// <summary>Quiet low pressure and filtered air. One finite swell, with no voice, melody or impact transient.</summary>
    internal static class HawthornHealingSoundSynthesis
    {
        internal static float[] GenerateSamples(int sampleRate)
        {
            var samples = new float[Mathf.CeilToInt(HawthornHealingAudio.DurationSeconds * sampleRate)];
            uint state = 0x48415754u;
            float air = 0f, lowAir = 0f, output = 0f;
            float airFilter = 1f - Mathf.Exp(-2f * Mathf.PI * 850f / sampleRate);
            float lowAirFilter = 1f - Mathf.Exp(-2f * Mathf.PI * 150f / sampleRate);
            float outputFilter = 1f - Mathf.Exp(-2f * Mathf.PI * 1100f / sampleRate);
            for (int index = 0; index < samples.Length; index++)
            {
                float t = index / (float)sampleRate;
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                float noise = (state & 0x00FFFFFFu) / 8388607.5f - 1f;
                air += (noise - air) * airFilter;
                lowAir += (noise - lowAir) * lowAirFilter;
                // A smooth pressure onset and one long release: no heartbeat/stinger rhythm.
                float envelope = Smooth(t / .16f) * (1f - Smooth((t - .26f) / 1.74f));
                float pressure = Mathf.Sin(2f * Mathf.PI * (83f * t - 3f * t * t)) * .22f +
                    Mathf.Sin(2f * Mathf.PI * (141f * t - 1.5f * t * t)) * .035f;
                float airEnvelope = .75f + .25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.5f));
                // One rounded, falling throat resonance makes the swallow
                // tangible, then yields to the existing pressure tail.
                float throatPhase = 2f * Mathf.PI * (180f * t - 130f * t * t);
                float throatEnvelope = Smooth(t / .025f) * (1f - Smooth((t - .04f) / .26f));
                float throat = (Mathf.Sin(throatPhase) + .3f * Mathf.Sin(throatPhase * 2f)) * .10f * throatEnvelope;
                float sample = (pressure + (air - lowAir) * .19f * airEnvelope + lowAir * .10f) * envelope + throat;
                output += (sample - output) * outputFilter;
                samples[index] = output;
            }
            samples[0] = samples[samples.Length - 1] = 0f;
            return samples;
        }

        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
