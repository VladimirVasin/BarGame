using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Asset-free, mono, quantized raven caws for the cemetery pair.
    ///
    /// The one-shot contract here restates the ALPINE VILLAGE
    /// family's — <see cref="AlpineVillageSoundSynthesis"/>: 22 050 Hz
    /// mono, edge fade min(1, n*160) * min(1, (1-n)*80), amplitude
    /// capped at 0.72 and quantized to 127 steps, first and last
    /// samples forced to zero — restated locally because the village
    /// helpers are private and a bird does not belong in the village
    /// enum. The City source family's 0.82 cap is NOT this contract;
    /// if the village contract ever moves, this file moves with it.
    ///
    /// The pulse itself is the village dog's <c>BarkPulse</c> retuned
    /// into a corvid: harder attack, flatter pitch, a longer sustain
    /// before the stop, higher rougher partials and twice the noise —
    /// a caw is a shout through a dry throat, not a bark.
    /// </summary>
    public static class CemeteryRavenCallSynthesis
    {
        public const int SampleRate = CitySourceSoundSynthesis.SampleRate;
        public const int Channels = 1;
        public const float MaximumAmplitude = 0.72f;
        public const int VariantCount = 3;

        /// <summary>One shared clip length: a single caw, a double
        /// kra-kra and a rasp all fit, and one length keeps the
        /// voice's clip pool uniform.</summary>
        public const float DurationSeconds = 0.55f;

        private const float QuantizationSteps = 127f;

        /// <summary>Base pitch, stepped per variant so the three
        /// calls are told apart by ear and not only by rhythm.</summary>
        private const float FundamentalHz = 220f;
        private const float FundamentalStepHz = 18f;

        /// <summary>One-pole throat filter coefficient — heavier than
        /// the dog's 0.085 because a corvid's rasp lives lower in the
        /// noise.</summary>
        private const float ThroatNoiseCoefficient = 0.14f;

        private const float CawNoiseShare = 0.34f;
        private const float RaspNoiseShare = 0.40f;

        /// <summary>
        /// One deterministic caw, byte-identical per variant on every
        /// call: variant 0 is a single long caw, 1 a double
        /// kra-kra with a softer second note, 2 one longer rasp.
        /// </summary>
        public static float[] GenerateCaw(int variant)
        {
            if (variant < 0 || variant >= VariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }

            uint noiseState = Seed(variant);
            float throatNoise = 0f;
            float fundamental =
                FundamentalHz + variant * FundamentalStepHz;
            float noiseShare =
                variant == 2 ? RaspNoiseShare : CawNoiseShare;
            return GenerateOneShot(
                DurationSeconds,
                (time, normalized) =>
                {
                    float white = NextNoise(ref noiseState);
                    throatNoise +=
                        (white - throatNoise) *
                        ThroatNoiseCoefficient;
                    float rough =
                        white * 0.55f + throatNoise * 0.45f;
                    switch (variant)
                    {
                        case 0:
                            return CawPulse(
                                time,
                                0.02f,
                                0.30f,
                                fundamental,
                                rough,
                                noiseShare);
                        case 1:
                            return CawPulse(
                                       time,
                                       0.02f,
                                       0.22f,
                                       fundamental,
                                       rough,
                                       noiseShare) +
                                   CawPulse(
                                       time,
                                       0.30f,
                                       0.22f,
                                       fundamental,
                                       rough,
                                       noiseShare) *
                                   0.78f;
                        default:
                            return CawPulse(
                                time,
                                0.02f,
                                0.38f,
                                fundamental,
                                rough,
                                noiseShare);
                    }
                });
        }

        /// <summary>
        /// The runtime clip a voice plays. DontSave for the same
        /// reason every synthesized clip in the project carries it:
        /// the buffer is regenerated on demand and must never be
        /// serialized into a scene.
        /// </summary>
        public static AudioClip CreateRuntimeClip(int variant)
        {
            float[] samples = GenerateCaw(variant);
            AudioClip clip = AudioClip.Create(
                "CemeteryRavenCaw_" + variant,
                samples.Length,
                Channels,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        /// <summary>
        /// BarkPulse retuned: the attack is harder (a caw starts at
        /// full voice), the decay flatter and later (1.35 against the
        /// bark's 1.8 — it sustains, then stops), the pitch nearly
        /// level (a 0.12 droop against the bark's 0.38 — a caw does
        /// not whine down), and the partials sit at 2.4x and 3.7x,
        /// inharmonic on purpose so the tone stays wooden.
        /// </summary>
        private static float CawPulse(
            float time,
            float start,
            float duration,
            float frequency,
            float roughNoise,
            float noiseShare)
        {
            float local = time - start;
            if (local < 0f || local > duration)
            {
                return 0f;
            }

            float normalized = local / duration;
            float envelope =
                Mathf.Min(1f, local * 140f) *
                Mathf.Pow(1f - normalized, 1.35f);
            float pitchDrop =
                frequency * (1f - normalized * 0.12f);
            float throat =
                Mathf.Sin(Mathf.PI * 2f * pitchDrop * local) *
                0.34f +
                Mathf.Sin(
                    Mathf.PI * 2f * pitchDrop * 2.4f * local +
                    0.6f) *
                0.18f +
                Mathf.Sin(
                    Mathf.PI * 2f * pitchDrop * 3.7f * local +
                    1.3f) *
                0.07f;
            return (throat + roughNoise * noiseShare) * envelope;
        }

        private static float[] GenerateOneShot(
            float duration,
            Func<float, float, float> generator)
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[sampleCount];
            float inverse = 1f / Mathf.Max(1f, sampleCount - 1f);
            for (int index = 0; index < sampleCount; index++)
            {
                float normalized = index * inverse;
                float edge =
                    Mathf.Min(1f, normalized * 160f) *
                    Mathf.Min(1f, (1f - normalized) * 80f);
                samples[index] = Quantize(
                    generator(
                        index / (float)SampleRate,
                        normalized) *
                    edge);
            }

            samples[0] = 0f;
            samples[sampleCount - 1] = 0f;
            return samples;
        }

        private static uint Seed(int variant)
        {
            unchecked
            {
                return
                    0x9E3779B9u ^
                    (0xCA11u * 0x85EBCA6Bu) ^
                    ((uint)(variant + 1) * 0xC2B2AE35u);
            }
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
            return Mathf.Round(
                       Mathf.Clamp(
                           sample,
                           -MaximumAmplitude,
                           MaximumAmplitude) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }
}
