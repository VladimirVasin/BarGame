using UnityEngine;

namespace BarPromenade
{
    public static class HomeAlarmClockSynthesis
    {
        public const int SampleRate = 22050;
        public const int Channels = 1;
        public const float LoopDuration = 1f;

        private const float QuantizationSteps = 127f;

        public static float[] GenerateMechanicalAlarmLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            float phaseDivisor = Mathf.Max(1f, sampleCount - 1f);

            for (int index = 0; index < sampleCount; index++)
            {
                float phase =
                    index /
                    phaseDivisor *
                    Mathf.PI *
                    2f;
                float hammerLift =
                    0.5f -
                    Mathf.Cos(phase * 12f) * 0.5f;
                float hammerStrike =
                    Mathf.Pow(hammerLift, 5f);
                float alternatingBell =
                    0.82f +
                    Mathf.Sin(phase * 6f) * 0.18f;
                float bellBody =
                    Mathf.Sin(phase * 930f) * 0.34f +
                    Mathf.Sin(phase * 1270f) * 0.22f +
                    Mathf.Sin(phase * 1860f) * 0.12f;
                float clockwork =
                    Mathf.Sin(phase * 54f) * 0.075f +
                    Mathf.Sin(phase * 108f) * 0.028f;
                float mechanicalGrain =
                    Mathf.Sin(phase * 137f) * 0.035f +
                    Mathf.Sin(phase * 263f) * 0.022f +
                    Mathf.Sin(phase * 419f) * 0.014f;
                float ringGain =
                    0.16f +
                    hammerStrike * alternatingBell * 0.84f;
                float sample =
                    bellBody * ringGain +
                    clockwork +
                    mechanicalGrain *
                    (0.55f + hammerStrike * 0.45f);

                samples[index] = Quantize(sample * 0.78f);
            }

            // The duplicated endpoint makes the explicit buffer seam
            // sample-identical while every oscillator completes an integer
            // number of cycles over the loop.
            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Round(
                       Mathf.Clamp(sample, -0.86f, 0.86f) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }
}
