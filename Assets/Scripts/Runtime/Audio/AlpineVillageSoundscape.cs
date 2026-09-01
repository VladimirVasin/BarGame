using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum AlpineVillageSoundPlayback
    {
        Loop = 0,
        ScheduledOneShot = 1
    }

    /// <summary>
    /// Gain, reach and synthesis budget for one causal village sound.
    /// </summary>
    public readonly struct AlpineVillageSoundDefinition
    {
        internal AlpineVillageSoundDefinition(
            AlpineVillageSoundKind kind,
            AlpineVillageSoundPlayback playback,
            float duration,
            int variantCount,
            float volume,
            float minimumDistance,
            float audibleRadius,
            float lowPassFrequency,
            CitySoundScheduleInterval scheduleInterval)
        {
            Kind = kind;
            Playback = playback;
            Duration = duration;
            VariantCount = variantCount;
            Volume = volume;
            MinimumDistance = minimumDistance;
            AudibleRadius = audibleRadius;
            LowPassFrequency = lowPassFrequency;
            ScheduleInterval = scheduleInterval;
        }

        public AlpineVillageSoundKind Kind { get; }
        public AlpineVillageSoundPlayback Playback { get; }
        public float Duration { get; }
        public int VariantCount { get; }
        public float Volume { get; }
        public float MinimumDistance { get; }
        public float AudibleRadius { get; }
        public float LowPassFrequency { get; }
        public CitySoundScheduleInterval ScheduleInterval { get; }
        public bool IsLoop => Playback == AlpineVillageSoundPlayback.Loop;
    }

    /// <summary>
    /// Asset-free, mono, quantized village audio. It follows the same runtime
    /// synthesis contract as RetroSfx and the City causal source library: no
    /// imported recordings, no copyrighted melody and no spoken line.
    /// </summary>
    public static class AlpineVillageSoundSynthesis
    {
        public const int SampleRate = CitySourceSoundSynthesis.SampleRate;
        public const int Channels = 1;
        public const float LoopDuration = 4f;
        public const float MaximumAmplitude = 0.72f;

        private const float QuantizationSteps = 127f;

        private static readonly AlpineVillageSoundDefinition[] Definitions =
        {
            default,
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.StationCableMetal,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.070f,
                0.75f,
                9.5f,
                3600f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.GarlandWire,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.046f,
                0.55f,
                7.5f,
                4100f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.DogBehindFence,
                AlpineVillageSoundPlayback.ScheduledOneShot,
                0.72f,
                3,
                0.145f,
                0.85f,
                12f,
                3000f,
                new CitySoundScheduleInterval(21f, 48f)),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.SourceWater,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.074f,
                0.50f,
                7.5f,
                3300f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.SpringCatchWater,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.079f,
                0.52f,
                6.5f,
                3400f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.BrookRiffle,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.068f,
                0.60f,
                9f,
                3600f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.CascadeFall,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                2,
                0.082f,
                0.55f,
                8f,
                4200f,
                CitySoundScheduleInterval.None),
            new AlpineVillageSoundDefinition(
                AlpineVillageSoundKind.WordlessHumBehindWall,
                AlpineVillageSoundPlayback.Loop,
                LoopDuration,
                3,
                0.018f,
                0.45f,
                5.2f,
                1350f,
                CitySoundScheduleInterval.None)
        };

        public static int Count => Definitions.Length - 1;

        /// <summary>
        /// Looked up BY KIND, never by position.
        ///
        /// The table used to be indexed with the enum value, which is exactly
        /// the kind of coupling that survives until the day a row is removed:
        /// taking the firewood cart out of the village shifted every row after
        /// it, and the wordless hum started throwing for its own number. The
        /// numbering carries a hole now and the table does not have to know.
        /// </summary>
        public static AlpineVillageSoundDefinition GetDefinition(
            AlpineVillageSoundKind kind)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Kind == kind &&
                    kind != AlpineVillageSoundKind.None)
                {
                    return Definitions[index];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        public static float[] GenerateSamples(
            AlpineVillageSoundKind kind,
            int variant = 0)
        {
            AlpineVillageSoundDefinition definition = GetDefinition(kind);
            if (variant < 0 || variant >= definition.VariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }

            switch (kind)
            {
                case AlpineVillageSoundKind.StationCableMetal:
                    return GenerateStationCableMetalLoop(variant);
                case AlpineVillageSoundKind.GarlandWire:
                    return GenerateGarlandWireLoop(variant);
                case AlpineVillageSoundKind.DogBehindFence:
                    return GenerateDogOneShot(definition.Duration, variant);
                case AlpineVillageSoundKind.SourceWater:
                case AlpineVillageSoundKind.SpringCatchWater:
                    return GenerateSourceWaterLoop(variant);
                case AlpineVillageSoundKind.BrookRiffle:
                    return GenerateBrookRiffleLoop(variant);
                case AlpineVillageSoundKind.CascadeFall:
                    return GenerateCascadeFallLoop(variant);
                case AlpineVillageSoundKind.WordlessHumBehindWall:
                    return GenerateWordlessHumLoop(variant);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal static AudioClip CreateRuntimeClip(
            AlpineVillageSoundKind kind,
            int variant)
        {
            float[] samples = GenerateSamples(kind, variant);
            AudioClip clip = AudioClip.Create(
                "AlpineVillage_" + kind + "_" + variant,
                samples.Length,
                Channels,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float[] GenerateStationCableMetalLoop(int variant)
        {
            float offset = variant * 0.67f;
            return GenerateLoop(
                phase =>
                {
                    float bearing =
                        Mathf.Sin(
                            phase * (176f + variant * 4f) + offset) *
                        0.048f +
                        Mathf.Sin(
                            phase * (352f + variant * 8f) + 0.32f) *
                        0.021f;
                    float cableStrain =
                        Mathf.Sin(
                            phase * (29f + variant) + 1.1f) *
                        (0.017f +
                         Mathf.Pow(
                             Mathf.Max(0f, Mathf.Sin(phase * 2f + offset)),
                             5f) *
                         0.026f);
                    float dryBearing = PeriodicNoise(
                        phase,
                        offset + 0.2f,
                        233 + variant * 17) *
                        0.026f;
                    float rollerContact =
                        PeriodicImpact(
                            phase,
                            0.84f + variant * 0.11f,
                            0.016f,
                            308f + variant * 5f) *
                        0.075f +
                        PeriodicImpact(
                            phase,
                            4.17f - variant * 0.08f,
                            0.021f,
                            274f + variant * 7f) *
                        0.056f;
                    return bearing + cableStrain + dryBearing + rollerContact;
                });
        }

        private static float[] GenerateGarlandWireLoop(int variant)
        {
            float offset = variant * 1.03f;
            return GenerateLoop(
                phase =>
                {
                    float gust =
                        0.22f +
                        Mathf.Pow(
                            0.5f +
                            Mathf.Sin(
                                phase * (2f + variant) + offset) *
                            0.5f,
                            2f) *
                        0.58f;
                    float air = PeriodicNoise(
                        phase,
                        offset,
                        317 + variant * 19) *
                        0.045f *
                        gust;
                    float tautWire =
                        Mathf.Sin(
                            phase * (421f + variant * 13f) + 0.6f) *
                        0.018f *
                        gust;
                    float tapedJoint =
                        PeriodicImpact(
                            phase,
                            2.36f + offset * 0.08f,
                            0.010f,
                            517f + variant * 11f) *
                        0.033f;
                    return air + tautWire + tapedJoint;
                });
        }

        private static float[] GenerateSourceWaterLoop(int variant)
        {
            float offset = variant * 0.81f;
            return GenerateLoop(
                phase =>
                {
                    float stream = PeriodicNoise(
                        phase,
                        0.4f + offset,
                        197 + variant * 23) *
                        0.092f;
                    float stoneBowl =
                        Mathf.Sin(
                            phase * (73f + variant * 3f) + offset) *
                        0.018f +
                        Mathf.Sin(
                            phase * (117f + variant * 5f) + 1.7f) *
                        0.012f;
                    float burble =
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(
                                    phase * (11f + variant) + 0.9f)),
                            7f) *
                        Mathf.Sin(
                            phase * (263f + variant * 7f) + offset) *
                        0.036f;
                    return stream + stoneBowl + burble;
                });
        }

        /// <summary>
        /// Running water over a shallow stone bed. Broader and steadier than
        /// the catch's burble, which is a basin filling; this one is going
        /// somewhere.
        /// </summary>
        private static float[] GenerateBrookRiffleLoop(int variant)
        {
            float offset = variant * 0.63f;
            return GenerateLoop(
                phase =>
                {
                    float run = PeriodicNoise(
                        phase,
                        0.9f + offset,
                        311 + variant * 29) * 0.104f;
                    // The bed under it: a slower band that keeps the hiss
                    // from reading as static.
                    float bed = PeriodicNoise(
                        phase,
                        0.22f + offset,
                        149 + variant * 17) * 0.052f;
                    float chatter =
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(phase * (7f + variant) + 2.1f)),
                            5f) *
                        Mathf.Sin(phase * (331f + variant * 11f) + offset) *
                        0.028f;
                    return run + bed + chatter;
                });
        }

        /// <summary>
        /// A step the brook falls over: the same water with a body under it,
        /// because a fall has a plunge and a riffle does not.
        /// </summary>
        private static float[] GenerateCascadeFallLoop(int variant)
        {
            float offset = variant * 0.37f;
            return GenerateLoop(
                phase =>
                {
                    float sheet = PeriodicNoise(
                        phase,
                        1.3f + offset,
                        419 + variant * 31) * 0.118f;
                    float plunge =
                        PeriodicNoise(
                            phase,
                            0.16f + offset,
                            97 + variant * 13) * 0.070f;
                    float slap =
                        Mathf.Pow(
                            Mathf.Max(
                                0f,
                                Mathf.Sin(phase * (5f + variant) + 0.4f)),
                            9f) *
                        Mathf.Sin(phase * (89f + variant * 7f) + offset) *
                        0.044f;
                    return sheet + plunge + slap;
                });
        }

        private static float[] GenerateWordlessHumLoop(int variant)
        {
            float offset = variant * 0.47f;
            return GenerateLoop(
                phase =>
                {
                    // A breath and one continuously bending vowel, not a
                    // tune: no notes, words, cadence or borrowed recording.
                    float breath =
                        0.18f +
                        Mathf.Pow(
                            0.5f +
                            Mathf.Sin(phase * 2f + offset) * 0.5f,
                            0.72f) *
                        0.64f;
                    float bend = Mathf.Sin(phase * 3f + offset) * 0.10f;
                    float fundamental =
                        Mathf.Sin(
                            phase * (568f + variant * 8f) + bend) *
                        0.080f;
                    float closedVowel =
                        Mathf.Sin(
                            phase * (1136f + variant * 16f) +
                            0.31f + bend * 0.6f) *
                        0.020f +
                        Mathf.Sin(
                            phase * (1704f + variant * 24f) + 1.2f) *
                        0.008f;
                    float roomAir = PeriodicNoise(
                        phase,
                        offset,
                        83 + variant * 7) *
                        0.006f;
                    return (fundamental + closedVowel) * breath + roomAir;
                });
        }

        private static float[] GenerateDogOneShot(
            float duration,
            int variant)
        {
            uint noiseState = Seed(
                AlpineVillageSoundKind.DogBehindFence,
                variant);
            float throatNoise = 0f;
            return GenerateOneShot(
                duration,
                (time, normalized) =>
                {
                    float white = NextNoise(ref noiseState);
                    throatNoise += (white - throatNoise) * 0.085f;
                    float rough = white * 0.55f + throatNoise * 0.45f;
                    float first = BarkPulse(
                        time,
                        0.035f,
                        0.20f,
                        128f + variant * 9f,
                        rough);
                    float second = variant == 1
                        ? 0f
                        : BarkPulse(
                            time,
                            0.34f + variant * 0.025f,
                            0.16f,
                            143f + variant * 7f,
                            rough) *
                          0.72f;
                    return first + second;
                });
        }

        private static float BarkPulse(
            float time,
            float start,
            float duration,
            float frequency,
            float roughNoise)
        {
            float local = time - start;
            if (local < 0f || local > duration)
            {
                return 0f;
            }

            float normalized = local / duration;
            float envelope =
                Mathf.Min(1f, local * 95f) *
                Mathf.Pow(1f - normalized, 1.8f);
            float pitchDrop = frequency * (1f - normalized * 0.38f);
            float throat =
                Mathf.Sin(Mathf.PI * 2f * pitchDrop * local) * 0.34f +
                Mathf.Sin(
                    Mathf.PI * 2f * pitchDrop * 2.11f * local + 0.6f) *
                0.12f;
            return (throat + roughNoise * 0.17f) * envelope;
        }

        private static float WoodKnock(
            float time,
            float start,
            float frequency,
            float grain,
            float scale)
        {
            float local = time - start;
            if (local < 0f)
            {
                return 0f;
            }

            float attack = Mathf.Min(1f, local * 750f);
            float body =
                Mathf.Sin(Mathf.PI * 2f * frequency * local) *
                Mathf.Exp(-local * 24f) *
                0.31f;
            float split =
                Mathf.Sin(
                    Mathf.PI * 2f * frequency * 2.76f * local + 0.4f) *
                Mathf.Exp(-local * 43f) *
                0.11f;
            float fibre = grain * Mathf.Exp(-local * 58f) * 0.12f;
            return (body + split + fibre) * attack * scale;
        }

        private static float[] GenerateLoop(Func<float, float> generator)
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * LoopDuration);
            var samples = new float[sampleCount];
            float divisor = Mathf.Max(1f, sampleCount - 1f);
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = index / divisor * Mathf.PI * 2f;
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

        private static float PeriodicNoise(
            float phase,
            float phaseOffset,
            int baseCycle)
        {
            return
                Mathf.Sin(phase * baseCycle + phaseOffset) * 0.31f +
                Mathf.Sin(
                    phase * (baseCycle + 89) + phaseOffset * 1.7f) *
                0.24f +
                Mathf.Sin(
                    phase * (baseCycle + 233) + phaseOffset * 2.3f) *
                0.18f +
                Mathf.Sin(
                    phase * (baseCycle + 541) + phaseOffset * 0.8f) *
                0.12f;
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
            float envelope = Mathf.Exp(
                -distance * distance /
                Mathf.Max(0.0001f, width));
            return Mathf.Sin(phase * carrierCycles) * envelope;
        }

        private static uint Seed(
            AlpineVillageSoundKind kind,
            int variant)
        {
            return
                0x9E3779B9u ^
                ((uint)kind * 0x85EBCA6Bu) ^
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
            return Mathf.Round(
                       Mathf.Clamp(
                           sample,
                           -MaximumAmplitude,
                           MaximumAmplitude) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }

    /// <summary>
    /// The one prologue parameter also owns village loudness. Grade zero is
    /// the current warm village; grade one is quieter but never mute.
    /// </summary>
    public static class AlpineVillageSoundscapeRules
    {
        public static float EvaluateGain(
            AlpineVillageSoundKind kind,
            float warmthGrade)
        {
            float grade = Smooth(Mathf.Clamp01(warmthGrade));
            return Mathf.Lerp(1f, GetGainFloor(kind), grade);
        }

        public static float EvaluateCutoff(
            AlpineVillageSoundKind kind,
            float baseCutoff,
            float warmthGrade)
        {
            GetGainFloor(kind);
            float grade = Smooth(Mathf.Clamp01(warmthGrade));
            return Mathf.Clamp(
                baseCutoff * Mathf.Lerp(1f, 0.62f, grade),
                650f,
                22000f);
        }

        private static float GetGainFloor(AlpineVillageSoundKind kind)
        {
            switch (kind)
            {
                case AlpineVillageSoundKind.StationCableMetal:
                    return 0.38f;
                case AlpineVillageSoundKind.GarlandWire:
                    return 0.30f;
                case AlpineVillageSoundKind.DogBehindFence:
                    return 0.18f;
                case AlpineVillageSoundKind.SourceWater:
                    return 0.42f;
                case AlpineVillageSoundKind.BrookRiffle:
                    return 0.40f;
                case AlpineVillageSoundKind.CascadeFall:
                    return 0.46f;
                case AlpineVillageSoundKind.SpringCatchWater:
                    return 0.44f;
                case AlpineVillageSoundKind.WordlessHumBehindWall:
                    return 0.12f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static float Smooth(float value)
        {
            return value * value * (3f - 2f * value);
        }
    }

    /// <summary>
    /// Scene-owned builder/controller for six bounded spatial voices. Four
    /// loops stay on their physical owners; the dog and firewood use pure,
    /// seed-stable autonomous schedules and never accumulate catch-up events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageSoundscape : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "Alpine Village Causal Soundscape";

        private sealed class Voice
        {
            public AlpineVillageSoundAnchorDescriptor Anchor;
            public AlpineVillageSoundDefinition Definition;
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public AudioClip[] Clips;
        }

        private readonly List<Voice> voices = new List<Voice>();
        private readonly List<AudioSource> sources =
            new List<AudioSource>();
        private readonly List<AudioClip> runtimeClips =
            new List<AudioClip>();
        private readonly Dictionary<string, AlpineVillageSoundScheduleCursor>
            schedules =
                new Dictionary<string, AlpineVillageSoundScheduleCursor>(
                    StringComparer.Ordinal);

        private double elapsedSeconds;

        public bool IsInitialized { get; private set; }
        public AlpineVillageSoundscapePlan Plan { get; private set; }
        public float WarmthGrade { get; private set; }
        public int PlayedEventCount { get; private set; }
        public IReadOnlyList<AudioSource> Sources => sources;
        public IReadOnlyList<AudioClip> RuntimeClips => runtimeClips;

        public static AlpineVillageSoundscape Create(
            Transform parent,
            AlpineVillageSoundscapePlan plan,
            IReadOnlyDictionary<string, Transform> semanticOwners,
            float initialWarmthGrade = 0f)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var owner = new GameObject(RuntimeObjectName);
            owner.transform.SetParent(parent, false);
            AlpineVillageSoundscape soundscape =
                owner.AddComponent<AlpineVillageSoundscape>();
            soundscape.Initialize(
                plan,
                semanticOwners,
                initialWarmthGrade);
            return soundscape;
        }

        public void Initialize(
            AlpineVillageSoundscapePlan plan,
            IReadOnlyDictionary<string, Transform> semanticOwners,
            float initialWarmthGrade = 0f)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Alpine Village soundscape is already initialized.");
            }

            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            plan.ValidateOrThrow();
            if (semanticOwners == null)
            {
                throw new ArgumentNullException(nameof(semanticOwners));
            }

            for (int index = 0; index < plan.Anchors.Count; index++)
            {
                AlpineVillageSoundAnchorDescriptor anchor =
                    plan.Anchors[index];
                if (!semanticOwners.TryGetValue(
                        anchor.PhysicalOwnerStableId,
                        out Transform owner) ||
                    owner == null)
                {
                    throw new InvalidOperationException(
                        $"Village sound '{anchor.StableId}' has no visible " +
                        $"semantic owner '{anchor.PhysicalOwnerStableId}'.");
                }
            }

            elapsedSeconds = 0d;
            for (int index = 0; index < plan.Anchors.Count; index++)
            {
                CreateVoice(plan.Anchors[index]);
            }

            SetWarmthGrade(initialWarmthGrade);
            for (int index = 0; index < voices.Count; index++)
            {
                Voice voice = voices[index];
                if (voice.Definition.IsLoop)
                {
                    voice.Source.Play();
                    continue;
                }

                schedules.Add(
                    voice.Anchor.StableId,
                    AlpineVillageSoundSchedulePlanner.Start(
                        plan,
                        voice.Anchor.StableId,
                        elapsedSeconds));
            }

            IsInitialized = true;
        }

        public bool TryGetSource(
            AlpineVillageSoundKind kind,
            out AudioSource source)
        {
            for (int index = 0; index < voices.Count; index++)
            {
                if (voices[index].Anchor.Kind != kind)
                {
                    continue;
                }

                source = voices[index].Source;
                return source != null;
            }

            source = null;
            return false;
        }

        public void SetWarmthGrade(float grade)
        {
            WarmthGrade = Mathf.Clamp01(grade);
            for (int index = 0; index < voices.Count; index++)
            {
                Voice voice = voices[index];
                float gain = AlpineVillageSoundscapeRules.EvaluateGain(
                    voice.Anchor.Kind,
                    WarmthGrade);
                voice.Source.volume = voice.Definition.Volume * gain;
                voice.Filter.cutoffFrequency =
                    AlpineVillageSoundscapeRules.EvaluateCutoff(
                        voice.Anchor.Kind,
                        voice.Definition.LowPassFrequency,
                        WarmthGrade);
            }
        }

        /// <summary>
        /// Explicit advancement exists for focused tests and future authored
        /// prologue time. Normal scene ownership calls it from Update.
        /// </summary>
        public void Advance(float deltaSeconds)
        {
            if (!IsInitialized ||
                float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f)
            {
                return;
            }

            elapsedSeconds += deltaSeconds;
            for (int index = 0; index < voices.Count; index++)
            {
                Voice voice = voices[index];
                if (voice.Definition.IsLoop ||
                    !schedules.TryGetValue(
                        voice.Anchor.StableId,
                        out AlpineVillageSoundScheduleCursor cursor) ||
                    !cursor.IsDue(elapsedSeconds))
                {
                    continue;
                }

                int variant = SelectEventVariant(voice, cursor.EventOrdinal);
                voice.Source.Stop();
                voice.Source.clip = voice.Clips[variant];
                voice.Source.Play();
                PlayedEventCount++;
                schedules[voice.Anchor.StableId] =
                    AlpineVillageSoundSchedulePlanner.AdvanceAfterFiring(
                        Plan,
                        cursor,
                        elapsedSeconds);
            }
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        private void CreateVoice(AlpineVillageSoundAnchorDescriptor anchor)
        {
            AlpineVillageSoundDefinition definition =
                AlpineVillageSoundSynthesis.GetDefinition(anchor.Kind);
            var voiceObject = new GameObject(
                "Source - " + anchor.PhysicalOwnerStableId);
            voiceObject.transform.SetParent(transform, false);
            voiceObject.transform.position = anchor.WorldPosition;

            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = definition.IsLoop;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = definition.MinimumDistance;
            source.maxDistance = definition.AudibleRadius;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = 174;
            source.bypassReverbZones = true;
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);

            AudioLowPassFilter filter =
                voiceObject.AddComponent<AudioLowPassFilter>();
            filter.lowpassResonanceQ = 1f;

            int clipCount = definition.IsLoop
                ? 1
                : definition.VariantCount;
            var clips = new AudioClip[clipCount];
            if (definition.IsLoop)
            {
                int variant = SelectStableVariant(
                    anchor,
                    definition.VariantCount);
                clips[0] = AlpineVillageSoundSynthesis.CreateRuntimeClip(
                    anchor.Kind,
                    variant);
            }
            else
            {
                for (int variant = 0; variant < clips.Length; variant++)
                {
                    clips[variant] =
                        AlpineVillageSoundSynthesis.CreateRuntimeClip(
                            anchor.Kind,
                            variant);
                }
            }

            for (int index = 0; index < clips.Length; index++)
            {
                runtimeClips.Add(clips[index]);
            }

            source.clip = clips[0];
            var voice = new Voice
            {
                Anchor = anchor,
                Definition = definition,
                Source = source,
                Filter = filter,
                Clips = clips
            };
            voices.Add(voice);
            sources.Add(source);
        }

        private int SelectStableVariant(
            AlpineVillageSoundAnchorDescriptor anchor,
            int variantCount)
        {
            uint hash = CitySoundStableHash.SourceEvent(
                Plan.Seed,
                anchor.StableId,
                0u);
            return (int)(hash % (uint)variantCount);
        }

        private int SelectEventVariant(Voice voice, uint eventOrdinal)
        {
            uint hash = CitySoundStableHash.SourceEvent(
                Plan.Seed,
                voice.Anchor.StableId,
                eventOrdinal ^ 0x56494C4Cu);
            return (int)(hash % (uint)voice.Clips.Length);
        }

        private void OnDestroy()
        {
            for (int index = 0; index < runtimeClips.Count; index++)
            {
                AudioClip clip = runtimeClips[index];
                if (clip == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(clip);
                }
                else
                {
                    DestroyImmediate(clip);
                }
            }

            schedules.Clear();
            sources.Clear();
            voices.Clear();
            runtimeClips.Clear();
            IsInitialized = false;
        }
    }
}
