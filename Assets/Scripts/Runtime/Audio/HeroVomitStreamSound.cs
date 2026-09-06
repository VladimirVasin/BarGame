using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The sound of the stream itself: a looping wet rush that runs for
    /// as long as the flow does, its loudness following the flow every
    /// frame — so the stomach's pump, the attack and the tail of each
    /// burst are heard as they are drawn, and nothing has to be re-cued.
    /// Generated once, deterministically, in the same PS1 crunch as the
    /// one-shots (sample-hold, coarse quantization, a dull low-pass), and
    /// closed into a loop by folding its tail into its head so the seam
    /// carries no click and no silence.
    ///
    /// Inside: a pulsing rush of noise (the pump at the flow's own 3.2 Hz,
    /// on top of the 7 Hz push the gush one-shot has always had), a low
    /// gurgling body under it, and bubbles that break at irregular
    /// intervals while the pump is high. Owned by the stream effect as
    /// one looping <c>AudioSource</c> at the mouth; never routed through
    /// the one-shot pool, whose voices end when their clips do.
    /// </summary>
    public static class HeroVomitStreamSound
    {
        public const int SampleRate = RetroSfxLibrary.SampleRate;
        public const float LoopSeconds = 2f;
        public const float SeamCrossfadeSeconds = 0.09f;
        public const float MaximumVolume = 0.62f;
        public const float MinimumDistanceMetres = 1.2f;
        public const float MaximumDistanceMetres = 13f;
        public const float PumpHertz = HeroVomitRules.PulseHertz;
        public const float PushHertz = 7f;
        public const int SampleHold = 3;
        public const int QuantizationSteps = 512;
        public const float LowPassFrequency = 2800f;

        private const uint NoiseSeed = 0x564D4C50u; // "VMLP"

        private static AudioClip cached;

        /// <summary>The one shared loop clip, built on first use.</summary>
        public static AudioClip LoopClip
        {
            get
            {
                if (cached == null)
                {
                    cached = CreateLoopClip();
                }

                return cached;
            }
        }

        public static AudioClip CreateLoopClip()
        {
            float[] samples = GenerateLoopSamples();
            AudioClip clip = AudioClip.Create(
                "HeroVomitStreamLoop",
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        /// <summary>
        /// The loop, crunched and seamed. Pure: the same array every call.
        /// </summary>
        public static float[] GenerateLoopSamples()
        {
            int count = Mathf.CeilToInt(LoopSeconds * SampleRate);
            float[] raw = new float[count];
            uint noiseState = NoiseSeed;
            float heldSample = 0f;
            float filtered = 0f;
            float lowPassAmount = 1f - Mathf.Exp(
                -2f * Mathf.PI * LowPassFrequency / SampleRate);
            for (int index = 0; index < count; index++)
            {
                if (index % SampleHold == 0)
                {
                    float time = index / (float)SampleRate;
                    heldSample = GenerateRawSample(time, ref noiseState);
                    heldSample = Mathf.Round(heldSample * QuantizationSteps) /
                                 QuantizationSteps;
                }

                filtered += (heldSample - filtered) * lowPassAmount;
                raw[index] = Mathf.Clamp(filtered, -0.98f, 0.98f);
            }

            // The seam: the last SeamCrossfadeSeconds fade out while the
            // first fade in over them, and the folded head replaces the
            // tail — the loop point then sits inside one continuous rush.
            int seam = Mathf.Min(
                count / 2,
                Mathf.CeilToInt(SeamCrossfadeSeconds * SampleRate));
            float[] samples = new float[count - seam];
            for (int index = 0; index < samples.Length; index++)
            {
                if (index < seam)
                {
                    float blend = (index + 1) / (float)(seam + 1);
                    samples[index] = raw[count - seam + index] * (1f - blend) +
                                     raw[index] * blend;
                }
                else
                {
                    samples[index] = raw[index];
                }
            }

            return samples;
        }

        private static float GenerateRawSample(float time, ref uint noiseState)
        {
            // The pump: the flow's own beat, the crest broad and the floor
            // never dry, so the loop keeps rushing between pushes.
            float pump = 0.55f + 0.45f * Mathf.Max(
                0f,
                Mathf.Sin(2f * Mathf.PI * PumpHertz * time));
            // The push inside each pump, the gush one-shot's 7 Hz.
            float push = 0.72f + 0.28f * Mathf.Max(
                0f,
                Mathf.Sin(2f * Mathf.PI * PushHertz * time));
            float noise = NextNoise(ref noiseState);
            float rush = noise * 0.50f * pump * push;

            // The body: a low, slightly wobbling gurgle and its second
            // harmonic, thickest at the crest of the pump.
            float wobble = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 1.7f * time);
            float body =
                (Mathf.Sin(2f * Mathf.PI * 84f * wobble * time) * 0.20f +
                 Mathf.Sin(2f * Mathf.PI * 168f * wobble * time) * 0.09f) *
                (0.4f + 0.6f * pump);

            // The throat under it: a rattle that reads as the voice
            // forced through a closing gullet, only at the crests.
            float rattle = Triangle(58f, time) *
                           Mathf.Max(0f, pump - 0.7f) / 0.3f *
                           0.16f;

            // The bubbles: one breaks every 0.09..0.16 s on a fixed,
            // irregular grid; each is a short chirp falling from 420 to
            // 180 Hz, and louder when the pump is high.
            float bubble = 0f;
            float bubbleClock = time;
            float slot = 0f;
            int slotIndex = 0;
            while (slot <= bubbleClock)
            {
                float slotLength = 0.09f + 0.07f * Hash01(slotIndex);
                if (bubbleClock < slot + slotLength)
                {
                    float into = bubbleClock - slot;
                    const float bubbleSeconds = 0.035f;
                    if (into < bubbleSeconds)
                    {
                        float shape = Mathf.Sin(Mathf.PI * into / bubbleSeconds);
                        float frequency = Mathf.Lerp(420f, 180f, into / bubbleSeconds) *
                                          (0.85f + 0.3f * Hash01(slotIndex + 977));
                        bubble = Mathf.Sin(2f * Mathf.PI * frequency * into) *
                                 shape *
                                 (0.16f + 0.24f * pump);
                    }

                    break;
                }

                slot += slotLength;
                slotIndex++;
            }

            return rush + body + rattle + bubble;
        }

        private static float Triangle(float frequency, float time)
        {
            float phase = Mathf.Repeat(frequency * time, 1f);
            return 1f - 4f * Mathf.Abs(phase - 0.5f);
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                uint hash = (uint)value * 2654435761u;
                hash ^= hash >> 15;
                hash *= 0x2C1B3C6Du;
                hash ^= hash >> 12;
                return (hash & 0x00FFFFFFu) / 16777216f;
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
    }
}
