using System;
using UnityEngine;

namespace BarPromenade
{
    public static class StairwellSoundscapeSynthesis
    {
        public const int SampleRate = 22050;
        public const float LoopDuration = 8f;

        private const float PipeKnockDuration = 0.62f;
        private const float MetalStressDuration = 1.75f;
        private const float DistantWaterDuration = 2.2f;
        private const float DistantMovementDuration = 1.45f;

        public static float[] GenerateVentilationLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = InteriorSoundscapeSynthesis.LoopPhase(
                    index,
                    sampleCount);
                float unevenFan =
                    0.74f +
                    Mathf.Sin(phase * 2f + 0.8f) * 0.11f +
                    Mathf.Sin(phase * 5f + 2.7f) * 0.055f;
                float ductBody =
                    Mathf.Sin(phase * 376f + 0.2f) * 0.28f +
                    Mathf.Sin(phase * 752f + 1.1f) * 0.10f +
                    Mathf.Sin(phase * 81f + 2.4f) * 0.13f;
                float air =
                    InteriorSoundscapeSynthesis.PeriodicNoise(
                        phase,
                        0.3f,
                        47f) *
                    0.16f;
                float sample =
                    (ductBody + air) * unevenFan * 0.62f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(sample);
            }

            return samples;
        }

        public static float[] GenerateElectricalBuzzLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = InteriorSoundscapeSynthesis.LoopPhase(
                    index,
                    sampleCount);
                float unstable =
                    0.82f +
                    Mathf.Sin(phase * 3f + 1.6f) * 0.09f +
                    Mathf.Sin(phase * 11f + 0.1f) * 0.035f;
                float mains =
                    Mathf.Sin(phase * 400f) * 0.43f +
                    Mathf.Sin(phase * 800f + 0.18f) * 0.19f +
                    Mathf.Sin(phase * 1200f + 0.64f) * 0.075f;
                float transformer =
                    Mathf.Sin(phase * 237f + 2.2f) * 0.08f +
                    Mathf.Sin(phase * 641f + 0.5f) * 0.04f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (mains * unstable + transformer) * 0.58f);
            }

            return samples;
        }

        public static float[] GenerateCueSamples(
            StairwellSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case StairwellSoundscapeCueKind.PipeKnock:
                    return GeneratePipeKnockSamples();
                case StairwellSoundscapeCueKind.MetalStress:
                    return GenerateMetalStressSamples();
                case StairwellSoundscapeCueKind.DistantWater:
                    return GenerateDistantWaterSamples();
                case StairwellSoundscapeCueKind.DistantMovement:
                    return GenerateDistantMovementSamples();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind));
            }
        }

        private static float[] GeneratePipeKnockSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * PipeKnockDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float first =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.018f,
                        12f,
                        154f,
                        0.68f);
                float second =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.19f,
                        16f,
                        219f,
                        0.34f);
                float pipeRing =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.02f,
                        5.4f,
                        413f,
                        0.17f);
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        first + second + pipeRing);
            }

            return samples;
        }

        private static float[] GenerateMetalStressSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * MetalStressDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x4D455441u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float envelope =
                    Mathf.Sin(Mathf.PI * normalized) *
                    (0.72f + normalized * 0.28f);
                float frequency =
                    318f +
                    normalized * 172f +
                    Mathf.Sin(normalized * Mathf.PI * 7f) * 34f;
                float scrape =
                    Mathf.Sin(2f * Mathf.PI * frequency * time) *
                    0.32f +
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (frequency * 1.71f) *
                        time +
                        0.8f) *
                    0.12f;
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.08f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (scrape + filteredNoise * 0.18f) *
                        envelope *
                        0.72f);
            }

            return samples;
        }

        private static float[] GenerateDistantWaterSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * DistantWaterDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x57415452u;
            float lowNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float edgeFade =
                    Mathf.Sin(Mathf.PI * normalized);
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                lowNoise += (noise - lowNoise) * 0.025f;
                float dripOne =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.44f,
                        22f,
                        690f,
                        0.19f);
                float dripTwo =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        1.38f,
                        19f,
                        532f,
                        0.14f);
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        lowNoise * edgeFade * 0.23f +
                        dripOne +
                        dripTwo);
            }

            return samples;
        }

        private static float[] GenerateDistantMovementSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * DistantMovementDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x4D4F5645u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float doorEnvelope =
                    Mathf.Sin(Mathf.PI * normalized) *
                    Mathf.Min(1f, normalized * 8f);
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.055f;
                float groan =
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (82f + normalized * 51f) *
                        time) *
                    0.20f;
                float stepOne =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.31f,
                        13f,
                        66f,
                        0.30f);
                float stepTwo =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.87f,
                        15f,
                        59f,
                        0.23f);
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (groan + filteredNoise * 0.14f) *
                        doorEnvelope +
                        stepOne +
                        stepTwo);
            }

            return samples;
        }
    }

    public static class HomeSoundscapeSynthesis
    {
        public const int SampleRate = 22050;
        public const float LoopDuration = 8f;
        public const float BathroomLightCrackleDuration = 0.055f;

        private const float SoftWoodDuration = 0.75f;
        private const float RadiatorTickDuration = 0.28f;
        private const float RadioMurmurDuration = 1.8f;
        private const float BathroomDetailDuration = 0.58f;

        public static float[] GenerateClosedRefrigeratorLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = InteriorSoundscapeSynthesis.LoopPhase(
                    index,
                    sampleCount);
                float gentleCycle =
                    0.76f +
                    Mathf.Sin(phase + 0.4f) * 0.08f +
                    Mathf.Sin(phase * 3f + 1.9f) * 0.035f;
                float compressor =
                    Mathf.Sin(phase * 376f + 0.2f) * 0.34f +
                    Mathf.Sin(phase * 752f + 0.7f) * 0.13f +
                    Mathf.Sin(phase * 1128f + 1.3f) * 0.045f;
                float cabinet =
                    Mathf.Sin(phase * 79f + 2.2f) * 0.055f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (compressor * gentleCycle + cabinet) *
                        0.50f);
            }

            return samples;
        }

        public static float[] GenerateOpenRefrigeratorLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = InteriorSoundscapeSynthesis.LoopPhase(
                    index,
                    sampleCount);
                float motorCycle =
                    0.80f +
                    Mathf.Sin(phase * 2f + 1.1f) * 0.075f +
                    Mathf.Sin(phase * 5f + 2.3f) * 0.035f;
                float exposedMotor =
                    Mathf.Sin(phase * 424f + 0.1f) * 0.28f +
                    Mathf.Sin(phase * 848f + 0.55f) * 0.115f +
                    Mathf.Sin(phase * 1272f + 1.25f) * 0.052f;
                float internalFan =
                    InteriorSoundscapeSynthesis.PeriodicNoise(
                        phase,
                        1.35f,
                        4211f) *
                    0.17f;
                float fanWhine =
                    Mathf.Sin(phase * 1568f + 0.75f) * 0.045f +
                    Mathf.Sin(phase * 6416f + 2.1f) * 0.032f +
                    Mathf.Sin(phase * 10400f + 1.4f) * 0.018f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (exposedMotor * motorCycle +
                         internalFan +
                         fanWhine) *
                        0.48f);
            }

            return samples;
        }

        public static float[] GenerateRefrigeratorLoopSamples()
        {
            return GenerateClosedRefrigeratorLoopSamples();
        }

        public static float[] GenerateBalconyNightAirLoopSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = InteriorSoundscapeSynthesis.LoopPhase(
                    index,
                    sampleCount);
                float breath =
                    0.68f +
                    Mathf.Sin(phase + 0.5f) * 0.12f +
                    Mathf.Sin(phase * 3f + 2.4f) * 0.05f;
                float air =
                    InteriorSoundscapeSynthesis.PeriodicNoise(
                        phase,
                        2.1f,
                        31f) *
                    0.22f;
                float farCity =
                    Mathf.Sin(phase * 113f + 0.9f) * 0.035f +
                    Mathf.Sin(phase * 181f + 2.8f) * 0.022f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (air * breath + farCity) * 0.72f);
            }

            return samples;
        }

        public static float[] GenerateBathroomLightCrackleSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * BathroomLightCrackleDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x4C414D50u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float edgeFade = Mathf.Sin(Mathf.PI * normalized);
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                filteredNoise +=
                    (noise - filteredNoise) * 0.18f;
                float brittleNoise = noise - filteredNoise;
                float firstSnap =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.003f,
                        55f,
                        2280f,
                        0.36f);
                float noiseEnvelope =
                    Mathf.Exp(-time * 68f) * 0.18f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (firstSnap +
                         brittleNoise * noiseEnvelope) *
                        edgeFade);
            }

            return samples;
        }

        public static float[] GenerateCueSamples(
            HomeSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case HomeSoundscapeCueKind.SoftWood:
                    return GenerateSoftWoodSamples();
                case HomeSoundscapeCueKind.RadiatorTick:
                    return GenerateRadiatorTickSamples();
                case HomeSoundscapeCueKind.RadioMurmur:
                    return GenerateRadioMurmurSamples();
                case HomeSoundscapeCueKind.BathroomDetail:
                    return GenerateBathroomDetailSamples();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind));
            }
        }

        private static float[] GenerateSoftWoodSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * SoftWoodDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x574F4F44u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float envelope =
                    Mathf.Sin(Mathf.PI * normalized) *
                    Mathf.Min(1f, normalized * 12f);
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.045f;
                float wood =
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (118f + normalized * 44f) *
                        time) *
                    0.21f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (wood + filteredNoise * 0.13f) *
                        envelope *
                        0.66f);
            }

            return samples;
        }

        private static float[] GenerateRadiatorTickSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * RadiatorTickDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float tick =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.015f,
                        21f,
                        930f,
                        0.38f);
                float warmRing =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.018f,
                        12f,
                        412f,
                        0.16f);
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        tick + warmRing);
            }

            return samples;
        }

        private static float[] GenerateRadioMurmurSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * RadioMurmurDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x52414449u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float edgeFade =
                    Mathf.Sin(Mathf.PI * normalized);
                float syllables =
                    0.52f +
                    Mathf.Sin(2f * Mathf.PI * 2.4f * time) *
                    0.23f +
                    Mathf.Sin(2f * Mathf.PI * 4.1f * time + 1.2f) *
                    0.11f;
                float voiceLike =
                    Mathf.Sin(2f * Mathf.PI * 173f * time) *
                    0.16f +
                    Mathf.Sin(2f * Mathf.PI * 247f * time + 0.7f) *
                    0.08f;
                float noise =
                    InteriorSoundscapeSynthesis.NextNoise(
                        ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.12f;
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        (voiceLike * syllables +
                         filteredNoise * 0.055f) *
                        edgeFade *
                        0.58f);
            }

            return samples;
        }

        private static float[] GenerateBathroomDetailSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * BathroomDetailDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float pipe =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.02f,
                        8.5f,
                        208f,
                        0.19f);
                float water =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.18f,
                        17f,
                        576f,
                        0.22f);
                float reflection =
                    InteriorSoundscapeSynthesis.DecayingTone(
                        time,
                        0.235f,
                        13f,
                        704f,
                        0.09f);
                samples[index] =
                    InteriorSoundscapeSynthesis.Quantize(
                        pipe + water + reflection);
            }

            return samples;
        }
    }

    internal static class InteriorSoundscapeSynthesis
    {
        internal static float LoopPhase(
            int sampleIndex,
            int sampleCount)
        {
            return sampleIndex /
                   (float)sampleCount *
                   Mathf.PI *
                   2f;
        }

        internal static float PeriodicNoise(
            float phase,
            float phaseOffset,
            float baseHarmonic)
        {
            return
                Mathf.Sin(
                    phase * baseHarmonic +
                    phaseOffset) *
                0.30f +
                Mathf.Sin(
                    phase * (baseHarmonic + 18f) +
                    phaseOffset * 1.7f) *
                0.23f +
                Mathf.Sin(
                    phase * (baseHarmonic + 42f) +
                    phaseOffset * 2.3f) *
                0.18f +
                Mathf.Sin(
                    phase * (baseHarmonic + 74f) +
                    phaseOffset * 0.8f) *
                0.13f +
                Mathf.Sin(
                    phase * (baseHarmonic + 121f) +
                    phaseOffset * 3.1f) *
                0.09f;
        }

        internal static float DecayingTone(
            float time,
            float startTime,
            float decay,
            float frequency,
            float amplitude)
        {
            float localTime = time - startTime;
            if (localTime < 0f)
            {
                return 0f;
            }

            float attack = Mathf.Min(1f, localTime * 900f);
            return
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    frequency *
                    localTime) *
                Mathf.Exp(-localTime * decay) *
                attack *
                amplitude;
        }

        internal static float Quantize(float sample)
        {
            return Mathf.Clamp(
                Mathf.Round(sample * 95f) / 95f,
                -0.86f,
                0.86f);
        }

        internal static float NextNoise(ref uint state)
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
