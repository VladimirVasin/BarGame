using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Dark, rounded mechanical sounds for the two passing fishing boats.</summary>
    public static class CityOffshoreBoatSynthesis
    {
        public const int SampleRate = 22050;
        public const float EngineDuration = 8f;
        public const float FirstHornDuration = 3.4f;
        public const float SecondHornDuration = 2.8f;
        private const int CrossfadeSamples = 4096;

        public static float[] GenerateEngineSamples(int seed, int variant)
        {
            ValidateVariant(variant);
            int count = Mathf.RoundToInt(SampleRate * EngineDuration);
            var extended = new float[count + CrossfadeSamples];
            uint noiseState = CreateSeed(seed, variant);
            float exhaust = 0f;
            float casing = 0f;
            float dark = 0f;
            float strokeRate = variant == 0 ? 6.5f : 7.75f;
            float bodyFrequency = variant == 0 ? 48f : 53f;
            for (int index = 0; index < extended.Length; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                exhaust += (white - exhaust) * 0.050f;
                casing += (white - casing) * 0.013f;
                // Broad, unequal exhaust strokes: no impulse, metal click or
                // high-frequency rattle is carried across the water.
                float cycle = 2f * Mathf.PI * strokeRate * time +
                    0.075f * Mathf.Sin(2f * Mathf.PI * time / EngineDuration);
                float stroke = 0.5f + 0.5f * Mathf.Cos(cycle);
                stroke = stroke * stroke * stroke;
                float uneven = 0.88f + 0.12f * Mathf.Sin(cycle * 0.5f + 0.7f);
                float body =
                    Mathf.Sin(2f * Mathf.PI * bodyFrequency * time) * 0.18f +
                    Mathf.Sin(2f * Mathf.PI * bodyFrequency * 2f * time + 0.4f) * 0.10f +
                    Mathf.Sin(2f * Mathf.PI * bodyFrequency * 3f * time + 1.1f) * 0.035f;
                float load = 0.95f + 0.05f * Mathf.Sin(
                    2f * Mathf.PI * time / EngineDuration + variant);
                float raw = (body * (0.40f + 0.60f * stroke * uneven) +
                    exhaust * (0.24f + stroke * 1.15f) + casing * 0.28f) * load;
                dark += (raw - dark) * 0.105f;
                extended[index] = dark;
            }

            // The tail continues directly into the beginning; blend back into
            // the original head over 186 ms. Do not insert a silent loop seam.
            var samples = new float[count];
            Array.Copy(extended, samples, count);
            for (int index = 0; index < CrossfadeSamples; index++)
            {
                float amount = index / (float)(CrossfadeSamples - 1);
                amount = amount * amount * (3f - 2f * amount);
                samples[index] = Mathf.Lerp(extended[count + index], samples[index], amount);
            }

            return samples;
        }

        public static float[] GenerateHornSamples(int seed, int variant)
        {
            ValidateVariant(variant);
            float duration = GetHornDuration(variant);
            int count = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[count];
            uint noiseState = CreateSeed(seed ^ 0x484F524E, variant);
            float breath = 0f;
            float dark = 0f;
            float phase = 0f;
            float frequency = variant == 0 ? 112f : 136f;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)SampleRate;
                float attack = Mathf.SmoothStep(0f, 1f, time / 0.38f);
                float release = Mathf.SmoothStep(0f, 1f, (duration - time) / 0.95f);
                float pressure = attack * release;
                float drift = 1f - 0.024f * (1f - attack) - 0.018f * (1f - release);
                phase += 2f * Mathf.PI * frequency * drift / SampleRate;
                breath += (NextNoise(ref noiseState) - breath) * 0.085f;
                float tone = Mathf.Sin(phase) * 0.38f +
                    Mathf.Sin(phase * 2f + 0.18f) * 0.15f +
                    Mathf.Sin(phase * 3f + 0.41f) * 0.055f +
                    Mathf.Sin(phase * 4f) * 0.012f;
                float hoarseness = breath * (0.12f + 0.16f * (1f - attack));
                dark += (tone + hoarseness - dark) * 0.14f;
                samples[index] = dark * pressure;
            }

            samples[0] = 0f;
            samples[samples.Length - 1] = 0f;
            return samples;
        }

        public static float GetHornDuration(int variant)
        {
            ValidateVariant(variant);
            return variant == 0 ? FirstHornDuration : SecondHornDuration;
        }

        internal static AudioClip CreateEngineClip(int seed, int variant)
        {
            return CreateClip("Offshore Fishing Boat Engine " + variant,
                GenerateEngineSamples(seed, variant));
        }

        internal static AudioClip CreateHornClip(int seed, int variant)
        {
            return CreateClip("Offshore Fishing Boat Horn " + variant,
                GenerateHornSamples(seed, variant));
        }

        private static AudioClip CreateClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        private static void ValidateVariant(int variant)
        {
            if (variant < 0 || variant > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }
        }

        private static uint CreateSeed(int seed, int variant)
        {
            uint state = unchecked((uint)seed) ^ ((uint)variant + 1u) * 0x9E3779B9u;
            return state == 0u ? 0x424F4154u : state;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }
}
