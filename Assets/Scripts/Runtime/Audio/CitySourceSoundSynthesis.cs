using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CitySourceSoundId
    {
        None = 0,
        WaterworksPipeLoop,
        WaterworksDrip,
        DryingYardClothLoop,
        DryingYardRopeCreak,
        DryingYardCarpetStrike,
        IndustrialWeighbridgeMechanismLoop,
        IndustrialMetalStress,
        LastRouteRelayLoop,
        LastRouteIncompleteChime,
        ParkFountainLoop,
        ParkSwingCreak,
        Count
    }

    public enum CitySourceSoundPlayback
    {
        Loop = 0,
        OneShot
    }

    public readonly struct CitySourceSoundDefinition
    {
        internal CitySourceSoundDefinition(
            CitySourceSoundId id,
            CitySourceSoundPlayback playback,
            float duration,
            int variantCount,
            float volume,
            float minDistance,
            float maxDistance,
            float lowPassFrequency)
        {
            Id = id;
            Playback = playback;
            Duration = duration;
            VariantCount = variantCount;
            Volume = volume;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            LowPassFrequency = lowPassFrequency;
        }

        public CitySourceSoundId Id { get; }
        public CitySourceSoundPlayback Playback { get; }
        public float Duration { get; }
        public int VariantCount { get; }
        public float Volume { get; }
        public float MinDistance { get; }
        public float MaxDistance { get; }
        public float LowPassFrequency { get; }
        public bool IsLoop => Playback == CitySourceSoundPlayback.Loop;
    }

    /// <summary>
    /// Deterministic, asset-free mono clips for visible City mechanisms.
    /// Every loop completes whole oscillator cycles and duplicates its first
    /// endpoint; every transient fades to silence at both buffer edges.
    /// </summary>
    public static class CitySourceSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const int Channels = 1;
        public const float LoopDuration = 4f;
        public const float MaximumDuration = LoopDuration;
        public const int MaximumSampleCount =
            (int)(SampleRate * MaximumDuration);

        private const float QuantizationSteps = 127f;
        private const float MaximumAmplitude = 0.82f;

        private static readonly CitySourceSoundDefinition[] definitions =
        {
            default,
            new CitySourceSoundDefinition(
                CitySourceSoundId.WaterworksPipeLoop,
                CitySourceSoundPlayback.Loop,
                LoopDuration,
                3,
                0.20f,
                0.9f,
                14f,
                3300f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.WaterworksDrip,
                CitySourceSoundPlayback.OneShot,
                0.68f,
                4,
                0.30f,
                0.7f,
                10f,
                4400f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.DryingYardClothLoop,
                CitySourceSoundPlayback.Loop,
                LoopDuration,
                3,
                0.17f,
                1f,
                12f,
                5100f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.DryingYardRopeCreak,
                CitySourceSoundPlayback.OneShot,
                1.08f,
                4,
                0.24f,
                0.7f,
                10f,
                3900f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.DryingYardCarpetStrike,
                CitySourceSoundPlayback.OneShot,
                0.52f,
                4,
                0.32f,
                0.7f,
                12f,
                5200f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.IndustrialWeighbridgeMechanismLoop,
                CitySourceSoundPlayback.Loop,
                LoopDuration,
                3,
                0.18f,
                1.4f,
                18f,
                2800f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.IndustrialMetalStress,
                CitySourceSoundPlayback.OneShot,
                1.62f,
                4,
                0.28f,
                1f,
                17f,
                3600f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.LastRouteRelayLoop,
                CitySourceSoundPlayback.Loop,
                LoopDuration,
                3,
                0.15f,
                0.7f,
                11f,
                4000f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.LastRouteIncompleteChime,
                CitySourceSoundPlayback.OneShot,
                1.74f,
                3,
                0.25f,
                0.8f,
                14f,
                4700f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.ParkFountainLoop,
                CitySourceSoundPlayback.Loop,
                LoopDuration,
                3,
                0.18f,
                1f,
                15f,
                5900f),
            new CitySourceSoundDefinition(
                CitySourceSoundId.ParkSwingCreak,
                CitySourceSoundPlayback.OneShot,
                1.26f,
                4,
                0.22f,
                0.8f,
                12f,
                3700f)
        };

        public static int Count => definitions.Length - 1;

        public static CitySourceSoundDefinition GetDefinition(
            CitySourceSoundId id)
        {
            int index = (int)id;
            if (index <= 0 || index >= definitions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return definitions[index];
        }

        public static float[] GenerateSamples(
            CitySourceSoundId id,
            int variant = 0)
        {
            CitySourceSoundDefinition definition =
                GetDefinition(id);
            if (variant < 0 || variant >= definition.VariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }

            switch (id)
            {
                case CitySourceSoundId.WaterworksPipeLoop:
                    return GenerateWaterworksPipeLoop(variant);
                case CitySourceSoundId.WaterworksDrip:
                    return GenerateWaterworksDrip(
                        definition.Duration,
                        variant);
                case CitySourceSoundId.DryingYardClothLoop:
                    return GenerateDryingYardClothLoop(variant);
                case CitySourceSoundId.DryingYardRopeCreak:
                    return GenerateDryingYardRopeCreak(
                        definition.Duration,
                        variant);
                case CitySourceSoundId.DryingYardCarpetStrike:
                    return GenerateDryingYardCarpetStrike(
                        definition.Duration,
                        variant);
                case CitySourceSoundId
                    .IndustrialWeighbridgeMechanismLoop:
                    return GenerateIndustrialWeighbridgeMechanismLoop(
                        variant);
                case CitySourceSoundId.IndustrialMetalStress:
                    return GenerateIndustrialMetalStress(
                        definition.Duration,
                        variant);
                case CitySourceSoundId.LastRouteRelayLoop:
                    return GenerateLastRouteRelayLoop(variant);
                case CitySourceSoundId.LastRouteIncompleteChime:
                    return GenerateLastRouteIncompleteChime(
                        definition.Duration,
                        variant);
                case CitySourceSoundId.ParkFountainLoop:
                    return GenerateParkFountainLoop(variant);
                case CitySourceSoundId.ParkSwingCreak:
                    return GenerateParkSwingCreak(
                        definition.Duration,
                        variant);
                default:
                    throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        internal static AudioClip CreateRuntimeClip(
            CitySourceSoundId id,
            int variant = 0)
        {
            float[] samples = GenerateSamples(id, variant);
            AudioClip clip = AudioClip.Create(
                "CitySource_" + id + "_" + variant,
                samples.Length,
                Channels,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float[] GenerateWaterworksPipeLoop(int variant)
        {
            float phaseOffset = variant * 0.73f;
            return GenerateLoop(
                phase =>
                {
                    float pressure =
                        0.74f +
                        Mathf.Sin(phase * 2f + phaseOffset) * 0.14f +
                        Mathf.Sin(phase * 5f + 1.1f) * 0.05f;
                    float pipeBody =
                        Mathf.Sin(
                            phase * (372f + variant * 7f) + 0.4f) *
                        0.105f +
                        Mathf.Sin(
                            phase * (548f + variant * 9f) + 2.2f) *
                        0.054f +
                        Mathf.Sin(
                            phase * (781f + variant * 11f) + 4.3f) *
                        0.025f;
                    float water =
                        PeriodicNoise(
                            phase,
                            1.7f + phaseOffset,
                            239 + variant * 13) *
                        0.10f;
                    float lowKnock =
                        Mathf.Sin(phase * 47f + phaseOffset) *
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(phase * 3f + 0.5f)),
                            8f) *
                        0.055f;
                    return
                        pipeBody * pressure +
                        water * (0.62f + pressure * 0.38f) +
                        lowKnock;
                });
        }

        private static float[] GenerateDryingYardClothLoop(int variant)
        {
            float phaseOffset = variant * 1.17f;
            return GenerateLoop(
                phase =>
                {
                    float gust =
                        0.42f +
                        Mathf.Pow(
                            0.5f +
                            Mathf.Sin(
                                phase * (2f + variant) +
                                phaseOffset) *
                            0.5f,
                            2f) *
                        0.52f;
                    float cloth =
                        PeriodicNoise(
                            phase,
                            0.9f + phaseOffset,
                            421 + variant * 17) *
                        0.12f;
                    float looseHem =
                        Mathf.Sin(
                            phase * (927f + variant * 23f) +
                            2.4f) *
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(phase * 5f + phaseOffset)),
                            10f) *
                        0.048f;
                    float rope =
                        Mathf.Sin(
                            phase * (233f + variant * 5f) + 0.6f) *
                        (0.012f + gust * 0.016f);
                    return cloth * gust + looseHem + rope;
                });
        }

        private static float[] GenerateIndustrialWeighbridgeMechanismLoop(
            int variant)
        {
            float phaseOffset = variant * 0.81f;
            return GenerateLoop(
                phase =>
                {
                    int mainsCycles = 200 + variant * 4;
                    float load =
                        0.82f +
                        Mathf.Sin(phase * 2f + phaseOffset) * 0.07f +
                        Mathf.Sin(phase * 7f + 2.5f) * 0.025f;
                    float mains =
                        Mathf.Sin(phase * mainsCycles) * 0.14f +
                        Mathf.Sin(
                            phase * mainsCycles * 2f + 0.18f) *
                        0.061f +
                        Mathf.Sin(
                            phase * mainsCycles * 3f + 1.4f) *
                        0.024f;
                    float steelCabinet =
                        Mathf.Sin(
                            phase * (117f + variant * 3f) + 0.4f) *
                        0.026f +
                        Mathf.Sin(
                            phase * (353f + variant * 5f) + 3.2f) *
                        0.018f;
                    float grain =
                        PeriodicNoise(
                            phase,
                            2.6f + phaseOffset,
                            73 + variant * 11) *
                        0.018f;
                    return mains * load + steelCabinet + grain;
                });
        }

        private static float[] GenerateLastRouteRelayLoop(int variant)
        {
            float phaseOffset = variant * 0.59f;
            return GenerateLoop(
                phase =>
                {
                    float coil =
                        Mathf.Sin(
                            phase * (392f + variant * 5f) + 0.3f) *
                        0.047f +
                        Mathf.Sin(
                            phase * (784f + variant * 10f) + 1.1f) *
                        0.019f;
                    float tiredSpeaker =
                        Mathf.Sin(
                            phase * (119f + variant * 4f) +
                            phaseOffset) *
                        0.016f;
                    float firstRelay =
                        PeriodicImpact(
                            phase,
                            0.42f + phaseOffset * 0.2f,
                            0.034f,
                            1260f + variant * 31f) *
                        0.17f;
                    float secondRelay =
                        PeriodicImpact(
                            phase,
                            3.77f + phaseOffset * 0.15f,
                            0.052f,
                            870f + variant * 23f) *
                        0.11f;
                    return
                        coil *
                        (0.82f +
                         Mathf.Sin(phase * 3f + phaseOffset) * 0.12f) +
                        tiredSpeaker +
                        firstRelay +
                        secondRelay;
                });
        }

        private static float[] GenerateParkFountainLoop(int variant)
        {
            float phaseOffset = variant * 1.03f;
            return GenerateLoop(
                phase =>
                {
                    float flow =
                        0.74f +
                        Mathf.Sin(phase * 3f + phaseOffset) * 0.11f +
                        Mathf.Sin(phase * 7f + 1.8f) * 0.045f;
                    float spray =
                        PeriodicNoise(
                            phase,
                            3.1f + phaseOffset,
                            733 + variant * 19) *
                        0.13f;
                    float basin =
                        PeriodicNoise(
                            phase,
                            0.6f + phaseOffset,
                            151 + variant * 11) *
                        0.075f;
                    float burble =
                        Mathf.Sin(
                            phase * (83f + variant * 3f) + 0.7f) *
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(phase * 6f + phaseOffset)),
                            6f) *
                        0.038f;
                    return spray * flow + basin + burble;
                });
        }

        private static float[] GenerateWaterworksDrip(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                CitySourceSoundId.WaterworksDrip,
                variant);
            float pitch = 1f + variant * 0.055f;
            float filteredNoise = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float noise = NextNoise(ref noiseState);
                    filteredNoise +=
                        (noise - filteredNoise) * 0.16f;
                    float drop =
                        DecayingTone(
                            time,
                            0.018f,
                            34f,
                            680f * pitch,
                            0.20f) +
                        DecayingTone(
                            time,
                            0.024f,
                            18f,
                            326f * pitch,
                            0.15f);
                    float basin =
                        DecayingTone(
                            time,
                            0.105f + variant * 0.006f,
                            8.5f,
                            176f * pitch,
                            0.18f) +
                        DecayingTone(
                            time,
                            0.118f + variant * 0.006f,
                            11f,
                            453f * pitch,
                            0.09f);
                    float splashNoise =
                        (noise - filteredNoise) *
                        Mathf.Exp(
                            -Mathf.Max(0f, time - 0.10f) * 29f) *
                        StepWindow(time, 0.10f, 0.24f) *
                        0.055f;
                    return drop + basin + splashNoise;
                });
        }

        private static float[] GenerateDryingYardRopeCreak(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                CitySourceSoundId.DryingYardRopeCreak,
                variant);
            float filteredNoise = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float envelope =
                        Mathf.Sin(Mathf.PI * normalized) *
                        Mathf.Min(1f, normalized * 15f);
                    float baseFrequency = 91f + variant * 8f;
                    float bend =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (baseFrequency * time +
                             18f * time * time)) *
                        0.17f;
                    float upperFiber =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (baseFrequency * 2.37f * time +
                             31f * time * time) +
                            0.8f) *
                        0.064f;
                    float noise = NextNoise(ref noiseState);
                    filteredNoise +=
                        (noise - filteredNoise) * 0.055f;
                    float fibers =
                        (noise - filteredNoise) * 0.038f;
                    float peg =
                        DecayingTone(
                            time,
                            0.12f + variant * 0.018f,
                            16f,
                            620f + variant * 27f,
                            0.07f);
                    return
                        (bend + upperFiber + fibers) * envelope +
                        peg;
                });
        }

        private static float[] GenerateDryingYardCarpetStrike(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                CitySourceSoundId.DryingYardCarpetStrike,
                variant);
            float clothBody = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float noise = NextNoise(ref noiseState);
                    clothBody += (noise - clothBody) *
                        (0.09f + variant * 0.008f);
                    float dryCloth = noise - clothBody;
                    float contactTime = time - 0.016f;
                    float contact = contactTime < 0f
                        ? 0f
                        : Mathf.Exp(-contactTime * 34f) *
                          (dryCloth * 0.23f +
                           Mathf.Sin(
                               Mathf.PI * 2f *
                               (104f + variant * 9f) * contactTime) *
                           0.21f);
                    float heavyFold = DecayingTone(
                        time,
                        0.022f,
                        21f,
                        72f + variant * 5f,
                        0.22f);
                    float rackAnswer = DecayingTone(
                        time,
                        0.055f + variant * 0.004f,
                        15f,
                        286f + variant * 24f,
                        0.08f);
                    float flutterStart = time - 0.085f;
                    float flutter = flutterStart < 0f
                        ? 0f
                        : dryCloth *
                          Mathf.Exp(-flutterStart * 11f) *
                          (0.055f +
                           0.025f * Mathf.Sin(flutterStart * 43f));
                    return contact + heavyFold + rackAnswer + flutter;
                });
        }

        private static float[] GenerateIndustrialMetalStress(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                CitySourceSoundId.IndustrialMetalStress,
                variant);
            float filteredNoise = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float rise =
                        Mathf.Sin(Mathf.PI * normalized) *
                        Mathf.Min(1f, normalized * 9f);
                    float startFrequency = 63f + variant * 6f;
                    float groan =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (startFrequency * time +
                             15f * time * time) +
                            Mathf.Sin(time * 17f) * 0.8f) *
                        0.18f;
                    float plate =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (startFrequency * 2.61f * time -
                             9f * time * time) +
                            1.9f) *
                        0.073f;
                    float noise = NextNoise(ref noiseState);
                    filteredNoise +=
                        (noise - filteredNoise) * 0.035f;
                    float scrape =
                        (noise - filteredNoise) *
                        (0.026f + normalized * 0.022f);
                    return (groan + plate + scrape) * rise;
                });
        }

        private static float[] GenerateLastRouteIncompleteChime(
            float duration,
            int variant)
        {
            float pitch = 1f + variant * 0.047f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float relay =
                        DecayingTone(
                            time,
                            0.018f,
                            52f,
                            1180f + variant * 47f,
                            0.17f);
                    float firstNote =
                        WarpedChime(
                            time,
                            0.12f,
                            311f * pitch,
                            3.7f,
                            0.19f);
                    float secondNote =
                        WarpedChime(
                            time,
                            0.57f,
                            277f * pitch,
                            3.9f,
                            0.16f);
                    float failedThird =
                        DecayingTone(
                            time,
                            1.04f,
                            31f,
                            196f * pitch,
                            0.055f);
                    return relay + firstNote + secondNote + failedThird;
                });
        }

        private static float[] GenerateParkSwingCreak(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                CitySourceSoundId.ParkSwingCreak,
                variant);
            float filteredNoise = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float arc =
                        Mathf.Sin(Mathf.PI * normalized) *
                        Mathf.Min(1f, normalized * 12f);
                    float pivotFrequency = 74f + variant * 5f;
                    float pivot =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (pivotFrequency * time +
                             Mathf.Sin(time * 6.2f) * 1.7f)) *
                        0.14f;
                    float chain =
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            (pivotFrequency * 3.18f * time +
                             8f * time * time) +
                            1.2f) *
                        0.052f;
                    float noise = NextNoise(ref noiseState);
                    filteredNoise +=
                        (noise - filteredNoise) * 0.045f;
                    float dryBearing =
                        (noise - filteredNoise) * 0.031f;
                    float returnKnock =
                        DecayingTone(
                            time,
                            0.78f + variant * 0.025f,
                            13f,
                            438f + variant * 21f,
                            0.085f);
                    return
                        (pivot + chain + dryBearing) * arc +
                        returnKnock;
                });
        }

        private static float[] GenerateLoop(Func<float, float> generator)
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
                samples[index] = Quantize(generator(phase));
            }

            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        private static float[] GenerateOneShot(
            float duration,
            Func<float, float, float> generator)
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[sampleCount];
            float inverseCount = 1f / Mathf.Max(1f, sampleCount - 1f);
            for (int index = 0; index < sampleCount; index++)
            {
                float normalized = index * inverseCount;
                float edge =
                    Mathf.Min(1f, normalized * 160f) *
                    Mathf.Min(1f, (1f - normalized) * 80f);
                samples[index] =
                    Quantize(generator(
                        index / (float)SampleRate,
                        normalized) * edge);
            }

            samples[0] = 0f;
            samples[sampleCount - 1] = 0f;
            return samples;
        }

        private static float PeriodicNoise(
            float phase,
            float phaseOffset,
            int baseCycle)
        {
            return
                Mathf.Sin(phase * baseCycle + phaseOffset) * 0.31f +
                Mathf.Sin(
                    phase * (baseCycle + 89) +
                    phaseOffset * 1.7f) *
                0.24f +
                Mathf.Sin(
                    phase * (baseCycle + 233) +
                    phaseOffset * 2.3f) *
                0.18f +
                Mathf.Sin(
                    phase * (baseCycle + 541) +
                    phaseOffset * 0.8f) *
                0.12f +
                Mathf.Sin(
                    phase * (baseCycle + 887) +
                    phaseOffset * 3.1f) *
                0.075f;
        }

        private static float PeriodicImpact(
            float phase,
            float centre,
            float width,
            float carrierCycles)
        {
            float distance = Mathf.Abs(
                Mathf.DeltaAngle(
                    phase * Mathf.Rad2Deg,
                    centre * Mathf.Rad2Deg) *
                Mathf.Deg2Rad);
            float envelope =
                Mathf.Exp(
                    -distance * distance /
                    Mathf.Max(0.0001f, width));
            return
                Mathf.Sin(phase * carrierCycles) * envelope;
        }

        private static float DecayingTone(
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

            float attack = Mathf.Min(1f, localTime * 850f);
            return
                Mathf.Sin(
                    Mathf.PI *
                    2f *
                    frequency *
                    localTime) *
                Mathf.Exp(-localTime * decay) *
                attack *
                amplitude;
        }

        private static float WarpedChime(
            float time,
            float startTime,
            float frequency,
            float decay,
            float amplitude)
        {
            float localTime = time - startTime;
            if (localTime < 0f)
            {
                return 0f;
            }

            float attack = Mathf.Min(1f, localTime * 180f);
            float wobble = Mathf.Sin(localTime * 18f) * 0.028f;
            float envelope =
                Mathf.Exp(-localTime * decay) * attack;
            return
                (Mathf.Sin(
                     Mathf.PI *
                     2f *
                     frequency *
                     localTime +
                     wobble) *
                 amplitude +
                 Mathf.Sin(
                     Mathf.PI *
                     2f *
                     frequency *
                     2.03f *
                     localTime +
                     0.6f) *
                 amplitude *
                 0.31f) *
                envelope;
        }

        private static float StepWindow(
            float time,
            float start,
            float end)
        {
            return time >= start && time <= end ? 1f : 0f;
        }

        private static uint Seed(
            CitySourceSoundId id,
            int variant)
        {
            return
                0x9E3779B9u ^
                ((uint)id * 0x85EBCA6Bu) ^
                ((uint)(variant + 1) * 0xC2B2AE35u);
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
