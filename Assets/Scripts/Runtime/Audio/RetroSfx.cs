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
        ShotSwap,
        ShotMatch,
        MoonshineBurst,
        DoorCreak,
        RefrigeratorSeal,
        RefrigeratorHinge,
        RefrigeratorThunk,
        ToiletFlush,
        TeethBrushScrub,
        BoardPiecePlace,
        BoardPieceTake,
        SpadeBite,
        SpadeGlance,
        SpadeToss,
        RopeCreak,
        CoffinSettle,
        StoneTamp,
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
                42),
            new RetroSfxDefinition(
                RetroSfxId.ShotSwap,
                RetroSfxCategory.Bar,
                0.18f,
                0.38f,
                0f,
                2,
                0.045f,
                2,
                2048,
                8200f,
                0.035f,
                58),
            new RetroSfxDefinition(
                RetroSfxId.ShotMatch,
                RetroSfxCategory.Bar,
                0.30f,
                0.44f,
                0f,
                3,
                0.060f,
                2,
                2048,
                8400f,
                0.025f,
                48),
            new RetroSfxDefinition(
                RetroSfxId.MoonshineBurst,
                RetroSfxCategory.Bar,
                0.46f,
                0.50f,
                0f,
                2,
                0.18f,
                2,
                2048,
                7600f,
                0.02f,
                36),
            new RetroSfxDefinition(
                RetroSfxId.DoorCreak,
                RetroSfxCategory.World,
                0.48f,
                0.52f,
                0.72f,
                1,
                0.24f,
                3,
                1024,
                4800f,
                0.018f,
                78),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorSeal,
                RetroSfxCategory.World,
                0.22f,
                0.46f,
                0.92f,
                2,
                0.14f,
                2,
                1024,
                5200f,
                0.018f,
                72),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorHinge,
                RetroSfxCategory.World,
                0.48f,
                0.38f,
                0.90f,
                1,
                0.24f,
                3,
                1024,
                3900f,
                0.016f,
                82),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorThunk,
                RetroSfxCategory.World,
                0.30f,
                0.50f,
                0.94f,
                2,
                0.18f,
                2,
                1024,
                4600f,
                0.015f,
                68),
            new RetroSfxDefinition(
                RetroSfxId.ToiletFlush,
                RetroSfxCategory.World,
                2.60f,
                0.50f,
                0.94f,
                1,
                1.00f,
                3,
                1024,
                3600f,
                0.012f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.TeethBrushScrub,
                RetroSfxCategory.World,
                0.45f,
                0.30f,
                0.85f,
                2,
                0.30f,
                3,
                1024,
                5200f,
                0.030f,
                58),
            new RetroSfxDefinition(
                RetroSfxId.BoardPiecePlace,
                RetroSfxCategory.World,
                0.16f,
                0.34f,
                0f,
                2,
                0.04f,
                2,
                1024,
                6800f,
                0.055f,
                62),
            new RetroSfxDefinition(
                RetroSfxId.BoardPieceTake,
                RetroSfxCategory.World,
                0.22f,
                0.38f,
                0f,
                2,
                0.05f,
                2,
                1024,
                6200f,
                0.045f,
                60),
            new RetroSfxDefinition(
                RetroSfxId.SpadeBite,
                RetroSfxCategory.World,
                0.26f,
                0.42f,
                1f,
                3,
                0.05f,
                2,
                1024,
                5400f,
                0.070f,
                64),
            new RetroSfxDefinition(
                RetroSfxId.SpadeGlance,
                RetroSfxCategory.World,
                0.20f,
                0.34f,
                1f,
                3,
                0.05f,
                2,
                1024,
                7200f,
                0.080f,
                60),
            new RetroSfxDefinition(
                RetroSfxId.SpadeToss,
                RetroSfxCategory.World,
                0.30f,
                0.30f,
                1f,
                3,
                0.06f,
                2,
                1024,
                4200f,
                0.075f,
                56),
            new RetroSfxDefinition(
                RetroSfxId.RopeCreak,
                RetroSfxCategory.World,
                0.34f,
                0.30f,
                1f,
                2,
                0.22f,
                2,
                1024,
                3600f,
                0.090f,
                54),
            new RetroSfxDefinition(
                RetroSfxId.CoffinSettle,
                RetroSfxCategory.World,
                0.44f,
                0.46f,
                1f,
                2,
                0.10f,
                2,
                1024,
                3000f,
                0.035f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.StoneTamp,
                RetroSfxCategory.World,
                0.24f,
                0.38f,
                1f,
                3,
                0.07f,
                2,
                1024,
                4800f,
                0.060f,
                62)
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
                case RetroSfxId.DoorCreak:
                    return GenerateDoorCreak(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorSeal:
                    return GenerateRefrigeratorSeal(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorHinge:
                    return GenerateRefrigeratorHinge(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorThunk:
                    return GenerateRefrigeratorThunk(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.ToiletFlush:
                    return GenerateToiletFlush(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.TeethBrushScrub:
                    return GenerateTeethBrushScrub(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BoardPiecePlace:
                    return GenerateBoardPiecePlace(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BoardPieceTake:
                    return GenerateBoardPieceTake(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeBite:
                    return GenerateSpadeBite(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeGlance:
                    return GenerateSpadeGlance(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeToss:
                    return GenerateSpadeToss(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RopeCreak:
                    return GenerateRopeCreak(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.CoffinSettle:
                    return GenerateCoffinSettle(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.StoneTamp:
                    return GenerateStoneTamp(
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
                case RetroSfxId.ShotSwap:
                    return GenerateShotSwap(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.ShotMatch:
                    return GenerateShotMatch(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.MoonshineBurst:
                    return GenerateMoonshineBurst(
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

        private static float GenerateDoorCreak(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.012f, 0.92f);
            float stickSlip =
                0.52f +
                Mathf.Pow(
                    Mathf.Abs(
                        Mathf.Sin(
                            2f *
                            Mathf.PI *
                            (5.2f + normalized * 1.4f) *
                            time)),
                    3f) *
                0.48f;
            float hinge =
                GlideSine(time, duration, 118f, 63f) * 0.42f +
                GlideSine(time, duration, 196f, 112f) * 0.19f;
            float wood =
                Triangle(72f + normalized * 18f, time) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.13f;
            float grain =
                NextNoise(ref noiseState) *
                (0.10f + stickSlip * 0.17f);
            return (hinge + wood + grain) *
                   stickSlip *
                   envelope;
        }

        private static float GenerateRefrigeratorSeal(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.003f, 2.6f);
            float suction = GlideSine(
                time,
                duration,
                92f,
                38f) * 0.58f;
            float rubber =
                Triangle(185f - normalized * 54f, time) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.20f;
            float snap = time < 0.038f
                ? NextNoise(ref noiseState) *
                  (1f - time / 0.038f) *
                  0.34f
                : 0f;
            return (suction + rubber + snap) * envelope;
        }

        private static float GenerateRefrigeratorHinge(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.018f, 0.94f);
            float pulse =
                0.42f +
                Mathf.Pow(
                    Mathf.Abs(
                        Mathf.Sin(
                            2f *
                            Mathf.PI *
                            (4.1f + normalized) *
                            time)),
                    4f) *
                0.58f;
            float metal =
                GlideSine(time, duration, 154f, 71f) * 0.32f +
                Triangle(93f + normalized * 21f, time) * 0.16f;
            float grain =
                NextNoise(ref noiseState) *
                (0.08f + pulse * 0.13f);
            return (metal + grain) * pulse * envelope;
        }

        private static float GenerateRefrigeratorThunk(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 3.4f);
            float body = GlideSine(
                time,
                duration,
                116f,
                43f) * 0.72f;
            float rattle =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.2f) *
                0.24f;
            float canRing =
                Mathf.Sin(2f * Mathf.PI * 690f * time) *
                Mathf.Exp(-time * 18f) *
                0.16f;
            return (body + rattle + canRing) * envelope;
        }

        /// <summary>
        /// Turned wood set down on a stone slab: a short low knock with
        /// a bright tick off the polish and almost no tail, because the
        /// slab does not ring.
        /// </summary>
        private static float GenerateBoardPiecePlace(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.001f, 5f);
            float knock = GlideSine(time, duration, 210f, 150f) * 0.70f;
            float tick =
                Mathf.Sin(2f * Mathf.PI * 1450f * time) *
                Mathf.Exp(-time * 70f) *
                0.26f;
            float grain =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 95f) *
                0.16f;
            return (knock + tick + grain) * envelope;
        }

        /// <summary>
        /// The same knock with the scrape of a man being dragged off
        /// the board in front of it.
        /// </summary>
        private static float GenerateBoardPieceTake(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 3.6f);
            float knock = GlideSine(time, duration, 260f, 170f) * 0.52f;
            float scrape =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 1.4f) *
                0.28f;
            float clack =
                Mathf.Sin(2f * Mathf.PI * 980f * time) *
                Mathf.Exp(-time * 40f) *
                0.20f;
            return (knock + scrape + clack) * envelope;
        }

        /// <summary>
        /// Steel into wet ground: a short crunch with the low thud of
        /// the tread taking a boot behind it. No ring — the earth
        /// swallows everything above the crunch.
        /// </summary>
        private static float GenerateSpadeBite(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.004f, 2.4f);
            float crunch =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - (normalized * 1.7f)) *
                0.52f;
            float thud = GlideSine(time, duration, 128f, 74f) * 0.44f;
            float grit =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 26f) *
                0.20f;
            return (crunch + thud + grit) * envelope;
        }

        /// <summary>
        /// The blade skidding off the face instead of into it: bright,
        /// thin and over at once, with the handle knocking once in the
        /// hands.
        /// </summary>
        private static float GenerateSpadeGlance(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.002f, 4.2f);
            float scrape =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 15f) *
                0.46f;
            float ring =
                Mathf.Sin(2f * Mathf.PI * 1720f * time) *
                Mathf.Exp(-time * 46f) *
                0.22f;
            float knock = GlideSine(time, duration, 240f, 190f) * 0.18f;
            return (scrape + ring + knock) * envelope;
        }

        /// <summary>
        /// A spadeful thrown onto the heap. It lands loose, so it is
        /// all scatter and no impact.
        /// </summary>
        private static float GenerateSpadeToss(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.030f, 1.9f);
            float scatter =
                NextNoise(ref noiseState) *
                Mathf.Lerp(0.16f, 0.50f, normalized) *
                0.72f;
            float body = GlideSine(time, duration, 96f, 58f) * 0.24f;
            return (scatter + body) * envelope;
        }

        /// <summary>
        /// Hemp under load, running a little and catching again. The
        /// triangle is the fibres, not a tone.
        /// </summary>
        private static float GenerateRopeCreak(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.040f, 1.6f);
            float groan =
                Triangle(Mathf.Lerp(58f, 41f, time / duration), time) *
                0.34f;
            float fibre =
                NextNoise(ref noiseState) *
                Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 9f * time)) *
                0.26f;
            return (groan + fibre) * envelope;
        }

        /// <summary>
        /// Boards taking their own weight at the bottom of a hole: one
        /// deep knock and the earth under it, with a long tail because
        /// there are walls either side of it now.
        /// </summary>
        private static float GenerateCoffinSettle(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.003f, 1.5f);
            float knock = GlideSine(time, duration, 112f, 52f) * 0.68f;
            float boards =
                Mathf.Sin(2f * Mathf.PI * 320f * time) *
                Mathf.Exp(-time * 18f) *
                0.24f;
            float earth =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 11f) *
                0.22f;
            return (knock + boards + earth) * envelope;
        }

        /// <summary>
        /// A boot treading loose earth down round the foot of a stone:
        /// dull, packed, and a shade more solid every time.
        /// </summary>
        private static float GenerateStoneTamp(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.005f, 2.8f);
            float pack = GlideSine(time, duration, 150f, 88f) * 0.50f;
            float scuff =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 22f) *
                0.34f;
            float grit =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 60f) *
                0.14f;
            return (pack + scuff + grit) * envelope;
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

        private static float GenerateToiletFlush(
            float time,
            float duration,
            ref uint noiseState)
        {
            // A hard rush that decays into a descending gurgle, then a
            // thin cistern-refill hiss rides out the tail.
            float rush = NextNoise(ref noiseState) *
                0.55f *
                Envelope(time, duration * 0.62f, 0.03f, 1.6f);
            float gurgleGate = Mathf.Max(
                0f,
                Mathf.Sin(2f * Mathf.PI * 9f * time));
            float gurgle = Mathf.Sin(
                2f *
                Mathf.PI *
                (260f - Mathf.Min(time, duration) * 55f) *
                time) *
                gurgleGate *
                0.20f *
                Envelope(time, duration * 0.75f, 0.05f, 1.2f);
            float refillStart = duration * 0.55f;
            float refill = time > refillStart
                ? NextNoise(ref noiseState) *
                  0.10f *
                  Envelope(
                      time - refillStart,
                      duration - refillStart,
                      0.30f,
                      0.8f)
                : 0f;
            return rush + gurgle + refill;
        }

        private static float GenerateTeethBrushScrub(
            float time,
            float duration,
            ref uint noiseState)
        {
            // Band-limited scrub noise pulsing as two brush strokes.
            float stroke = Mathf.Abs(
                Mathf.Sin(2f * Mathf.PI * 2.2f * time));
            float scrub = NextNoise(ref noiseState) *
                (0.22f + stroke * 0.30f);
            return scrub * Envelope(time, duration, 0.02f, 1.1f);
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

        private static float GenerateShotSwap(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 2.3f);
            float glide = GlideSine(
                time,
                duration,
                510f,
                980f);
            float glass =
                Mathf.Sin(2f * Mathf.PI * 2420f * time) *
                Mathf.Max(0f, 1f - normalized * 3.6f);
            float slide =
                NextNoise(ref noiseState) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.12f;
            return (
                       glide * 0.36f +
                       glass * 0.38f +
                       slide) *
                   envelope;
        }

        private static float GenerateShotMatch(
            float time,
            float duration,
            ref uint noiseState)
        {
            float segmentDuration = duration / 3f;
            int segment = Mathf.Min(
                2,
                Mathf.FloorToInt(time / segmentDuration));
            float localTime = time - segment * segmentDuration;
            float frequency = segment == 0
                ? 784f
                : segment == 1
                    ? 988f
                    : 1175f;
            float localEnvelope = Envelope(
                localTime,
                segmentDuration,
                0.001f,
                2.8f);
            float sparkle =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    frequency *
                    localTime) *
                0.52f;
            float glass =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    frequency *
                    2.46f *
                    localTime) *
                0.23f;
            float crackle =
                NextNoise(ref noiseState) *
                localEnvelope *
                0.07f;
            return (sparkle + glass) * localEnvelope + crackle;
        }

        private static float GenerateMoonshineBurst(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.003f, 1.25f);
            float lowBody = GlideSine(
                time,
                duration,
                128f,
                54f);
            float icyRise = GlideSine(
                time,
                duration,
                420f,
                1320f);
            float triplePulse =
                0.42f +
                Mathf.Max(
                    0f,
                    Mathf.Sin(2f * Mathf.PI * 7.4f * time)) *
                0.58f;
            float burstNoise =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.7f) *
                0.28f;
            return (
                       lowBody * 0.34f +
                       icyRise * triplePulse * 0.32f +
                       burstNoise) *
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
