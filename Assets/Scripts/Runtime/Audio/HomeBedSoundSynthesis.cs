using UnityEngine;

namespace BarPromenade
{
    /// <summary>Dry, deterministic fabric, weight and breath. No voices or room tone.</summary>
    internal static class HomeBedSoundSynthesis
    {
        internal static bool IsBedCue(RetroSfxId id) =>
            (int)id >= (int)RetroSfxId.HomeBedCloth && (int)id <= (int)RetroSfxId.HomeBedGasp;

        internal static float[] GenerateSamples(RetroSfxDefinition definition, int variant, int sampleRate)
        {
            var samples = new float[Mathf.CeilToInt(definition.Duration * sampleRate)];
            uint noiseState = 0x534F4641u ^ (uint)definition.Id * 0x85EBCA6Bu ^
                (uint)variant * 0xC2B2AE35u;
            float fast = 0f, slow = 0f, body = 0f, output = 0f;
            float detune = variant == 0 ? 1f : .957f;
            float outputFilter = 1f - Mathf.Exp(-2f * Mathf.PI * definition.LowPassFrequency / sampleRate);
            bool inhale = definition.Id == RetroSfxId.HomeBedInhale;
            bool exhale = definition.Id == RetroSfxId.HomeBedExhale;
            bool gasp = definition.Id == RetroSfxId.HomeBedGasp;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float u = t / definition.Duration;
                float noise = NextNoise(ref noiseState);
                fast += (noise - fast) * (exhale ? .27f : .45f);
                slow += (noise - slow) * .055f;
                body += (noise - body) * .008f;
                float cloth = fast - slow;
                float soft = slow - body;
                float sample;
                if (inhale || exhale || gasp)
                {
                    // Filtered air with a soft mouth opening and an uneven air
                    // stream. No oscillating vowel/formant or synthetic speech.
                    float attack = gasp ? .016f : inhale ? .16f : .09f;
                    float envelope = Smooth01(t / attack) * Smooth01((definition.Duration - t) /
                        (gasp ? .20f : .28f));
                    float airflow = .84f + .10f * Mathf.Sin(t * 17f + variant) +
                        .06f * Mathf.Sin(t * 31f + .9f);
                    float opening = Mathf.Pow(Mathf.Sin(Mathf.PI * u), gasp ? .42f : .7f);
                    float air = gasp ? .40f : exhale ? .10f : .16f;
                    sample = (cloth * air + soft * (exhale ? .85f : .75f)) *
                        envelope * opening * airflow * (gasp ? 1.45f : 1.15f);
                }
                else if (definition.Id == RetroSfxId.HomeBedCloth)
                {
                    float envelope = Smooth01(t / .025f) * Smooth01((definition.Duration - t) / .15f);
                    float rub = .40f + .35f * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 2f) +
                        .25f * Mathf.Pow(Mathf.Sin(t * 35f * detune), 2f);
                    sample = (cloth * .20f + soft * .65f) * envelope * rub;
                }
                else if (definition.Id == RetroSfxId.HomeBedSettle)
                {
                    float envelope = Smooth01(t / .014f) * Mathf.Exp(-t * 9f) *
                        Smooth01((definition.Duration - t) / .10f);
                    float pressure = Mathf.Sin(2f * Mathf.PI * (77f * t - 25f * t * t) * detune);
                    sample = (pressure * .25f + soft * 1.1f + cloth * .22f) * envelope;
                }
                else
                {
                    // A loaded timber joint: low friction with two brief
                    // resonances, deliberately unlike the transformation cue.
                    float envelope = Smooth01(t / .055f) * Smooth01((definition.Duration - t) / .27f);
                    float friction = .35f + .65f * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 2f);
                    float wood = Mathf.Sin(2f * Mathf.PI * (184f * t - 39f * t * t) * detune) * .035f +
                        Mathf.Sin(2f * Mathf.PI * (297f * t + 21f * t * t) * detune) * .014f;
                    sample = (wood + soft * 1.1f + cloth * .18f) * envelope * friction;
                }
                output += (sample - output) * outputFilter;
                samples[i] = Mathf.Clamp(output, -.9f, .9f);
            }
            // Every pool voice ends at silence; no discontinuity at release.
            samples[0] = samples[samples.Length - 1] = 0f;
            return samples;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
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
