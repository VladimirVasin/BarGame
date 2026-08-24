using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Deterministic mono audio owned by the faulty south-tunnel fixture.
    /// The continuous ballast loop is deliberately quiet and local; the
    /// contact transient is played only when the visible lamp loses power.
    /// </summary>
    public static class CityTunnelLampSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const int Channels = 1;
        public const float BallastDuration = 2f;
        public const float CrackleDuration = 0.16f;
        public const int CrackleVariantCount = 3;

        private const float QuantizationSteps = 127f;
        private const float MaximumAmplitude = 0.72f;

        public static float[] GenerateBallastBuzzSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * BallastDuration);
            var samples = new float[sampleCount];
            float phaseDivisor = Mathf.Max(1f, sampleCount - 1f);
            for (int index = 0; index < sampleCount; index++)
            {
                float phase =
                    index /
                    phaseDivisor *
                    Mathf.PI *
                    2f;
                float mains =
                    Mathf.Sin(phase * 100f) * 0.115f +
                    Mathf.Sin(phase * 200f + 0.24f) * 0.051f +
                    Mathf.Sin(phase * 300f + 1.07f) * 0.023f;
                float tiredCore =
                    Mathf.Sin(phase * 87f + 2.31f) * 0.018f +
                    Mathf.Sin(phase * 523f + 0.67f) * 0.011f;
                float periodicGrain =
                    Mathf.Sin(phase * 761f + 0.11f) * 0.008f +
                    Mathf.Sin(phase * 1157f + 1.83f) * 0.006f;
                float load =
                    0.86f +
                    Mathf.Sin(phase * 3f + 0.38f) * 0.055f +
                    Mathf.Sin(phase * 7f + 2.14f) * 0.025f;
                samples[index] = Quantize(
                    mains * load + tiredCore + periodicGrain);
            }

            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        public static float[] GenerateContactCrackleSamples(
            int variant = 0)
        {
            if (variant < 0 || variant >= CrackleVariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }

            int sampleCount = Mathf.RoundToInt(
                SampleRate * CrackleDuration);
            var samples = new float[sampleCount];
            uint state =
                0xA341316Cu ^
                ((uint)(variant + 1) * 0x9E3779B9u);
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized =
                    index / Mathf.Max(1f, sampleCount - 1f);
                float noise = NextNoise(ref state);
                filteredNoise +=
                    (noise - filteredNoise) *
                    (0.08f + variant * 0.012f);
                float highNoise = noise - filteredNoise;
                float attack = Mathf.Min(1f, time * 900f);
                float decay = Mathf.Exp(
                    -time * (31f + variant * 2.5f));
                float contactTone =
                    Mathf.Sin(
                        Mathf.PI *
                        2f *
                        (1110f + variant * 79f) *
                        time) *
                    0.10f;
                float secondaryTime = time - 0.047f;
                float secondary = secondaryTime >= 0f
                    ? Mathf.Sin(
                          Mathf.PI *
                          2f *
                          (1670f + variant * 61f) *
                          secondaryTime) *
                      Mathf.Exp(-secondaryTime * 58f) *
                      0.055f
                    : 0f;
                float edgeFade =
                    Mathf.Min(1f, normalized * 180f) *
                    Mathf.Min(1f, (1f - normalized) * 90f);
                samples[index] = Quantize(
                    ((highNoise * 0.23f + contactTone) *
                     attack *
                     decay +
                     secondary) *
                    edgeFade);
            }

            samples[0] = 0f;
            samples[sampleCount - 1] = 0f;
            return samples;
        }

        internal static AudioClip CreateBallastRuntimeClip()
        {
            return CreateRuntimeClip(
                "CityTunnelLamp_Ballast",
                GenerateBallastBuzzSamples());
        }

        internal static AudioClip CreateCrackleRuntimeClip(int variant)
        {
            return CreateRuntimeClip(
                "CityTunnelLamp_Crackle_" + variant,
                GenerateContactCrackleSamples(variant));
        }

        private static AudioClip CreateRuntimeClip(
            string clipName,
            float[] samples)
        {
            AudioClip clip = AudioClip.Create(
                clipName,
                samples.Length,
                Channels,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float NextNoise(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return ((value & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }

        private static float Quantize(float sample)
        {
            return
                Mathf.Round(
                    Mathf.Clamp(
                        sample,
                        -MaximumAmplitude,
                        MaximumAmplitude) *
                    QuantizationSteps) /
                QuantizationSteps;
        }
    }
}
