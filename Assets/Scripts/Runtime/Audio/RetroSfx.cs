using System;
using UnityEngine;

namespace BarPromenade
{
    public enum RetroSfxCategory
    {
        None = 0,
        Ui,
        World,
        Bar,
        Count
    }

    public enum RetroSfxId
    {
        None = 0,
        UiMove,
        UiConfirm,
        UiCancel,
        MapOpen,
        Footstep,
        Door,
        Pour,
        Clink,
        Shake,
        Good,
        Bad,
        BeerPongThrow,
        BeerPongBounce,
        BeerPongRim,
        BeerPongSink,
        DrinkGulp,
        Count
    }

    public readonly struct RetroSfxDefinition
    {
        internal RetroSfxDefinition(
            RetroSfxId id,
            RetroSfxCategory category,
            float duration,
            float volume,
            float spatialBlend,
            int maxVoices,
            float cooldownSeconds,
            int sampleHold,
            int quantizationSteps,
            float lowPassFrequency,
            float pitchVariation,
            int priority)
        {
            Id = id;
            Category = category;
            Duration = duration;
            Volume = volume;
            SpatialBlend = spatialBlend;
            MaxVoices = maxVoices;
            CooldownSeconds = cooldownSeconds;
            SampleHold = sampleHold;
            QuantizationSteps = quantizationSteps;
            LowPassFrequency = lowPassFrequency;
            PitchVariation = pitchVariation;
            Priority = priority;
        }

        public RetroSfxId Id { get; }
        public RetroSfxCategory Category { get; }
        public float Duration { get; }
        public float Volume { get; }
        public float SpatialBlend { get; }
        public int MaxVoices { get; }
        public float CooldownSeconds { get; }
        public int SampleHold { get; }
        public int QuantizationSteps { get; }
        public float LowPassFrequency { get; }
        public float PitchVariation { get; }
        public int Priority { get; }
    }

    public static class RetroSfxLibrary
    {
        public const int SampleRate = 22050;

        private static readonly RetroSfxDefinition[] definitions =
        {
            default,
            new RetroSfxDefinition(
                RetroSfxId.UiMove,
                RetroSfxCategory.Ui,
                0.065f,
                0.30f,
                0f,
                2,
                0.025f,
                2,
                1024,
                7200f,
                0.025f,
                48),
            new RetroSfxDefinition(
                RetroSfxId.UiConfirm,
                RetroSfxCategory.Ui,
                0.13f,
                0.38f,
                0f,
                2,
                0.045f,
                2,
                2048,
                7800f,
                0.018f,
                40),
            new RetroSfxDefinition(
                RetroSfxId.UiCancel,
                RetroSfxCategory.Ui,
                0.15f,
                0.36f,
                0f,
                2,
                0.06f,
                2,
                1024,
                6800f,
                0.018f,
                42),
            new RetroSfxDefinition(
                RetroSfxId.MapOpen,
                RetroSfxCategory.Ui,
                0.28f,
                0.42f,
                0f,
                1,
                0.16f,
                2,
                2048,
                8000f,
                0f,
                32),
            new RetroSfxDefinition(
                RetroSfxId.Footstep,
                RetroSfxCategory.World,
                0.12f,
                0.23f,
                1f,
                3,
                0.075f,
                3,
                512,
                4400f,
                0.08f,
                132),
            new RetroSfxDefinition(
                RetroSfxId.Door,
                RetroSfxCategory.World,
                0.34f,
                0.48f,
                0.86f,
                2,
                0.18f,
                2,
                1024,
                6100f,
                0.025f,
                84),
            new RetroSfxDefinition(
                RetroSfxId.Pour,
                RetroSfxCategory.Bar,
                0.46f,
                0.34f,
                0f,
                2,
                0.10f,
                2,
                1024,
                5700f,
                0.035f,
                96),
            new RetroSfxDefinition(
                RetroSfxId.Clink,
                RetroSfxCategory.Bar,
                0.20f,
                0.42f,
                0f,
                3,
                0.055f,
                1,
                2048,
                9200f,
                0.04f,
                54),
            new RetroSfxDefinition(
                RetroSfxId.Shake,
                RetroSfxCategory.Bar,
                0.38f,
                0.38f,
                0f,
                1,
                0.20f,
                3,
                768,
                5200f,
                0.025f,
                88),
            new RetroSfxDefinition(
                RetroSfxId.Good,
                RetroSfxCategory.Bar,
                0.31f,
                0.44f,
                0f,
                2,
                0.11f,
                2,
                2048,
                8100f,
                0f,
                44),
            new RetroSfxDefinition(
                RetroSfxId.Bad,
                RetroSfxCategory.Bar,
                0.37f,
                0.46f,
                0f,
                2,
                0.14f,
                3,
                1024,
                6200f,
                0f,
                38),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongThrow,
                RetroSfxCategory.Bar,
                0.22f,
                0.38f,
                0f,
                2,
                0.05f,
                2,
                1024,
                6500f,
                0.04f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongBounce,
                RetroSfxCategory.Bar,
                0.12f,
                0.32f,
                0f,
                3,
                0.025f,
                2,
                1024,
                5200f,
                0.05f,
                72),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongRim,
                RetroSfxCategory.Bar,
                0.18f,
                0.45f,
                0f,
                3,
                0.035f,
                1,
                2048,
                8800f,
                0.04f,
                52),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongSink,
                RetroSfxCategory.Bar,
                0.42f,
                0.48f,
                0f,
                2,
                0.12f,
                2,
                2048,
                7600f,
                0.02f,
                42),
            new RetroSfxDefinition(
                RetroSfxId.DrinkGulp,
                RetroSfxCategory.Bar,
                0.32f,
                0.35f,
                0f,
                1,
                0.22f,
                3,
                1024,
                4200f,
                0.035f,
                42)
        };

        public static int Count => definitions.Length - 1;

        public static RetroSfxDefinition GetDefinition(RetroSfxId id)
        {
            int index = (int)id;
            if (index <= 0 || index >= definitions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return definitions[index];
        }

        public static float[] GenerateSamples(RetroSfxId id)
        {
            RetroSfxDefinition definition = GetDefinition(id);
            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(definition.Duration * SampleRate));
            var samples = new float[sampleCount];
            uint noiseState =
                0x9E3779B9u ^ ((uint)id * 0x85EBCA6Bu);
            int holdLength = Mathf.Max(1, definition.SampleHold);
            float quantizationScale =
                Mathf.Max(2, definition.QuantizationSteps);
            float lowPassAmount = 1f - Mathf.Exp(
                -2f *
                Mathf.PI *
                Mathf.Min(
                    definition.LowPassFrequency,
                    SampleRate * 0.45f) /
                SampleRate);
            float heldSample = 0f;
            float filteredSample = 0f;

            for (int index = 0; index < sampleCount; index++)
            {
                if (index % holdLength == 0)
                {
                    float time = index / (float)SampleRate;
                    heldSample = GenerateRawSample(
                        id,
                        time,
                        definition.Duration,
                        ref noiseState);
                    heldSample =
                        Mathf.Round(heldSample * quantizationScale) /
                        quantizationScale;
                }

                filteredSample +=
                    (heldSample - filteredSample) * lowPassAmount;
                samples[index] = Mathf.Clamp(
                    filteredSample,
                    -0.98f,
                    0.98f);
            }

            return samples;
        }

        internal static AudioClip CreateRuntimeClip(RetroSfxId id)
        {
            float[] samples = GenerateSamples(id);
            AudioClip clip = AudioClip.Create(
                "RetroSfx_" + id,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float GenerateRawSample(
            RetroSfxId id,
            float time,
            float duration,
            ref uint noiseState)
        {
            switch (id)
            {
                case RetroSfxId.UiMove:
                    return GenerateUiMove(time, duration);
                case RetroSfxId.UiConfirm:
                    return GenerateUiConfirm(time, duration);
                case RetroSfxId.UiCancel:
                    return GenerateUiCancel(time, duration);
                case RetroSfxId.MapOpen:
                    return GenerateMapOpen(time, duration);
                case RetroSfxId.Footstep:
                    return GenerateFootstep(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Door:
                    return GenerateDoor(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Pour:
                    return GeneratePour(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Clink:
                    return GenerateClink(time, duration);
                case RetroSfxId.Shake:
                    return GenerateShake(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Good:
                    return GenerateGood(time, duration);
                case RetroSfxId.Bad:
                    return GenerateBad(time, duration);
                case RetroSfxId.BeerPongThrow:
                    return GenerateBeerPongThrow(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BeerPongBounce:
                    return GenerateBeerPongBounce(time, duration);
                case RetroSfxId.BeerPongRim:
                    return GenerateBeerPongRim(time, duration);
                case RetroSfxId.BeerPongSink:
                    return GenerateBeerPongSink(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.DrinkGulp:
                    return GenerateDrinkGulp(
                        time,
                        duration,
                        ref noiseState);
                default:
                    return 0f;
            }
        }

        private static float GenerateUiMove(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.004f, 2.2f);
            return GlideSine(time, duration, 820f, 650f) *
                   envelope *
                   0.62f;
        }

        private static float GenerateUiConfirm(float time, float duration)
        {
            float split = duration * 0.45f;
            float localTime = time < split ? time : time - split;
            float localDuration =
                time < split ? split : duration - split;
            float frequency = time < split ? 520f : 780f;
            float envelope = Envelope(
                localTime,
                localDuration,
                0.006f,
                1.8f);
            return (
                       Mathf.Sin(2f * Mathf.PI * frequency * localTime) +
                       Triangle(frequency * 0.5f, localTime) * 0.18f) *
                   envelope *
                   0.48f;
        }

        private static float GenerateUiCancel(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.004f, 1.6f);
            return (
                       GlideSine(time, duration, 430f, 190f) * 0.72f +
                       Triangle(215f, time) * 0.16f) *
                   envelope;
        }

        private static float GenerateMapOpen(float time, float duration)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.012f, 1.45f);
            float arpeggio = normalized < 0.34f
                ? 220f
                : normalized < 0.67f
                    ? 330f
                    : 440f;
            return (
                       Mathf.Sin(2f * Mathf.PI * arpeggio * time) * 0.52f +
                       Mathf.Sin(2f * Mathf.PI * 110f * time) * 0.18f) *
                   envelope;
        }

        private static float GenerateFootstep(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.002f, 3.1f);
            float thump = GlideSine(
                time,
                duration,
                105f,
                52f);
            return (
                       thump * 0.68f +
                       NextNoise(ref noiseState) * 0.32f) *
                   envelope *
                   0.78f;
        }

        private static float GenerateDoor(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.004f, 1.3f);
            float creak = GlideSine(
                time,
                duration,
                148f,
                72f);
            float grain =
                NextNoise(ref noiseState) *
                (0.16f + Mathf.Abs(Mathf.Sin(time * 34f)) * 0.12f);
            float latch = time < 0.045f
                ? Mathf.Sin(2f * Mathf.PI * 1280f * time) *
                  (1f - time / 0.045f) *
                  0.38f
                : 0f;
            return (creak * 0.43f + grain + latch) * envelope;
        }

        private static float GeneratePour(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.035f, 1.15f);
            float stream = NextNoise(ref noiseState) * 0.34f;
            float bubbleGate =
                Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 15f * time));
            float bubble = Mathf.Sin(
                2f *
                Mathf.PI *
                (420f + bubbleGate * 190f) *
                time) *
                bubbleGate *
                0.16f;
            return (stream + bubble) * envelope;
        }

        private static float GenerateClink(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 4.2f);
            return (
                       Mathf.Sin(2f * Mathf.PI * 2380f * time) * 0.54f +
                       Mathf.Sin(2f * Mathf.PI * 3570f * time) * 0.28f +
                       Mathf.Sin(2f * Mathf.PI * 1190f * time) * 0.12f) *
                   envelope;
        }

        private static float GenerateShake(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.018f, 1.1f);
            float pulse =
                0.28f +
                Mathf.Pow(
                    Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 10.5f * time)),
                    2f) *
                0.72f;
            return (
                       NextNoise(ref noiseState) * 0.48f +
                       Mathf.Sin(2f * Mathf.PI * 185f * time) * 0.10f) *
                   pulse *
                   envelope;
        }

        private static float GenerateGood(float time, float duration)
        {
            float segmentDuration = duration / 3f;
            int segment = Mathf.Min(
                2,
                Mathf.FloorToInt(time / segmentDuration));
            float localTime = time - segment * segmentDuration;
            float frequency = segment == 0
                ? 523.25f
                : segment == 1
                    ? 659.25f
                    : 783.99f;
            float envelope = Envelope(
                localTime,
                segmentDuration,
                0.004f,
                1.35f);
            return (
                       Mathf.Sin(2f * Mathf.PI * frequency * localTime) *
                       0.56f +
                       Mathf.Sin(
                           2f *
                           Mathf.PI *
                           frequency *
                           1.5f *
                           localTime) *
                       0.13f) *
                   envelope;
        }

        private static float GenerateBad(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.006f, 1.2f);
            float first = GlideSine(
                time,
                duration,
                330f,
                138f);
            float second = GlideSine(
                time,
                duration,
                278f,
                116f);
            return (first * 0.46f + second * 0.34f) * envelope;
        }

        private static float GenerateBeerPongThrow(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.003f, 2.2f);
            float sweep = GlideSine(time, duration, 360f, 105f);
            float air = NextNoise(ref noiseState) * 0.18f;
            return (sweep * 0.52f + air) * envelope;
        }

        private static float GenerateBeerPongBounce(
            float time,
            float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 3.6f);
            float body = GlideSine(time, duration, 235f, 86f);
            float tick = Mathf.Sin(2f * Mathf.PI * 1180f * time);
            return (body * 0.62f + tick * 0.18f) * envelope;
        }

        private static float GenerateBeerPongRim(
            float time,
            float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 4.4f);
            return (
                       Mathf.Sin(2f * Mathf.PI * 1820f * time) * 0.48f +
                       Mathf.Sin(2f * Mathf.PI * 2690f * time) * 0.31f +
                       Mathf.Sin(2f * Mathf.PI * 910f * time) * 0.14f) *
                   envelope;
        }

        private static float GenerateBeerPongSink(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.005f, 1.45f);
            float splash =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.2f) *
                0.36f;
            float tone = normalized < 0.34f
                ? 392f
                : normalized < 0.67f
                    ? 523.25f
                    : 659.25f;
            float chime =
                Mathf.Sin(2f * Mathf.PI * tone * time) * 0.52f +
                Triangle(tone * 0.5f, time) * 0.11f;
            return (splash + chime) * envelope;
        }

        private static float GenerateDrinkGulp(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(
                time,
                duration,
                0.018f,
                1.05f);
            float throatPulse =
                Mathf.Max(
                    0f,
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (7.5f + normalized * 2f) *
                        time));
            float body = GlideSine(
                time,
                duration,
                145f,
                82f);
            float liquid =
                NextNoise(ref noiseState) *
                (0.14f + throatPulse * 0.18f);
            float bubble =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    (310f + throatPulse * 130f) *
                    time) *
                throatPulse *
                0.16f;
            return (
                       body * 0.28f +
                       liquid +
                       bubble) *
                   envelope;
        }

        private static float GlideSine(
            float time,
            float duration,
            float startFrequency,
            float endFrequency)
        {
            float safeDuration = Mathf.Max(0.0001f, duration);
            float frequencySlope =
                (endFrequency - startFrequency) / safeDuration;
            float phase =
                startFrequency * time +
                0.5f * frequencySlope * time * time;
            return Mathf.Sin(2f * Mathf.PI * phase);
        }

        private static float Triangle(float frequency, float time)
        {
            float phase = Mathf.Repeat(frequency * time, 1f);
            return 1f - 4f * Mathf.Abs(phase - 0.5f);
        }

        private static float Envelope(
            float time,
            float duration,
            float attack,
            float releasePower)
        {
            float attackAmount = attack <= 0f
                ? 1f
                : Mathf.Clamp01(time / attack);
            float remaining = Mathf.Clamp01(
                1f - time / Mathf.Max(0.0001f, duration));
            return attackAmount * Mathf.Pow(remaining, releasePower);
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
