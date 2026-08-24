using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeSoundKind
    {
        Refrigerator = 0,
        FluorescentFixture = 1,
        CoffeeBoiler = 2
    }

    /// <summary>
    /// Short mono loops for three visible cafe mechanisms. The cafe owns no
    /// music or room-tone bed: every generated sample is heard at the object
    /// that visibly explains it.
    /// </summary>
    public static class MountainRoadCafeSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 4f;

        private const int CrossfadeSamples = 1536;
        private const float QuantizationSteps = 127f;

        public static float[] GenerateSamples(
            MountainRoadCafeSoundKind kind,
            int seed)
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var extended = new float[sampleCount + CrossfadeSamples];
            uint noiseState = CreateSeed(seed, kind);
            float lowNoise = 0f;
            float slowNoise = 0f;
            for (int index = 0; index < extended.Length; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                lowNoise += (white - lowNoise) * 0.042f;
                slowNoise += (white - slowNoise) * 0.0012f;
                extended[index] = Quantize(Evaluate(
                    kind,
                    time,
                    white,
                    lowNoise,
                    slowNoise));
            }

            var samples = new float[sampleCount];
            Array.Copy(extended, samples, sampleCount);
            for (int index = 0; index < CrossfadeSamples; index++)
            {
                float blend = index / (float)CrossfadeSamples;
                samples[index] = Quantize(Mathf.Lerp(
                    extended[sampleCount + index],
                    samples[index],
                    blend));
            }

            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        internal static AudioClip CreateRuntimeClip(
            MountainRoadCafeSoundKind kind,
            int seed)
        {
            float[] samples = GenerateSamples(kind, seed);
            AudioClip clip = AudioClip.Create(
                "MountainRoadCafe_" + kind,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float Evaluate(
            MountainRoadCafeSoundKind kind,
            float time,
            float white,
            float lowNoise,
            float slowNoise)
        {
            switch (kind)
            {
                case MountainRoadCafeSoundKind.Refrigerator:
                    return Refrigerator(time, lowNoise, slowNoise);
                case MountainRoadCafeSoundKind.FluorescentFixture:
                    return FluorescentFixture(time, lowNoise, slowNoise);
                case MountainRoadCafeSoundKind.CoffeeBoiler:
                    return CoffeeBoiler(time, white, lowNoise, slowNoise);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown cafe sound owner.");
            }
        }

        private static float Refrigerator(
            float time,
            float lowNoise,
            float slowNoise)
        {
            float compressor =
                Mathf.Sin(time * Mathf.PI * 2f * 48f) * 0.070f +
                Mathf.Sin(time * Mathf.PI * 2f * 96f + 0.31f) * 0.023f;
            float chassis =
                Mathf.Sin(time * Mathf.PI * 2f * 17f + slowNoise) *
                0.018f;
            return compressor + chassis + lowNoise * 0.017f;
        }

        private static float FluorescentFixture(
            float time,
            float lowNoise,
            float slowNoise)
        {
            float mains =
                Mathf.Sin(time * Mathf.PI * 2f * 100f) * 0.052f +
                Mathf.Sin(time * Mathf.PI * 2f * 200f + 0.18f) *
                0.016f;
            float transformer =
                Mathf.Sin(time * Mathf.PI * 2f * 37f + slowNoise * 0.2f) *
                0.014f;
            return mains + transformer + lowNoise * 0.008f;
        }

        private static float CoffeeBoiler(
            float time,
            float white,
            float lowNoise,
            float slowNoise)
        {
            float first = SteamEnvelope(time, 0.72f, 1.18f);
            float second = SteamEnvelope(time, 2.64f, 0.74f);
            float pressure = first + second;
            float hiss =
                (white * 0.060f + lowNoise * 0.13f) * pressure;
            float pipe = Mathf.Sin(time * Mathf.PI * 2f * 241f) *
                         pressure * 0.018f;
            return hiss + pipe + slowNoise * 0.007f;
        }

        private static float SteamEnvelope(
            float loopTime,
            float start,
            float duration)
        {
            float elapsed = loopTime - start;
            if (elapsed < 0f || elapsed > duration)
            {
                return 0f;
            }

            float attack = Mathf.Clamp01(elapsed * 9f);
            float release = Mathf.Clamp01((duration - elapsed) * 5f);
            return attack * release;
        }

        private static uint CreateSeed(
            int seed,
            MountainRoadCafeSoundKind kind)
        {
            uint value = unchecked((uint)seed) ^
                         ((uint)kind + 1u) * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return value == 0u ? 0xA341316Cu : value;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return ((state & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Round(
                       Mathf.Clamp(sample, -0.72f, 0.72f) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }

    /// <summary>
    /// Owns the cafe's three causal 3D voices and destroys their generated
    /// clips with the cafe root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeSoundscape : MonoBehaviour
    {
        public const string RefrigeratorAnchorId =
            "terminal-cafe-fridge";
        public const string FixtureAnchorId =
            "terminal-cafe-ceiling-fixture";
        public const string BoilerAnchorId =
            "terminal-cafe-coffee-boiler";

        private readonly List<AudioSource> sources =
            new List<AudioSource>();
        private readonly List<AudioClip> runtimeClips =
            new List<AudioClip>();

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<AudioSource> Sources => sources;
        public IReadOnlyList<AudioClip> RuntimeClips => runtimeClips;

        public static MountainRoadCafeSoundscape Create(
            Transform parent,
            IReadOnlyDictionary<string, Transform> semanticAnchors,
            int seed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var owner = new GameObject("Nighthawks Causal Soundscape");
            owner.transform.SetParent(parent, false);
            MountainRoadCafeSoundscape soundscape =
                owner.AddComponent<MountainRoadCafeSoundscape>();
            soundscape.Initialize(semanticAnchors, seed);
            return soundscape;
        }

        public void Initialize(
            IReadOnlyDictionary<string, Transform> semanticAnchors,
            int seed)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mountain cafe soundscape is already initialized.");
            }

            if (semanticAnchors == null)
            {
                throw new ArgumentNullException(nameof(semanticAnchors));
            }

            CreateVoice(
                semanticAnchors,
                RefrigeratorAnchorId,
                MountainRoadCafeSoundKind.Refrigerator,
                seed + 17,
                0.105f,
                0.55f,
                4.8f,
                2500f);
            CreateVoice(
                semanticAnchors,
                FixtureAnchorId,
                MountainRoadCafeSoundKind.FluorescentFixture,
                seed + 53,
                0.080f,
                0.45f,
                3.8f,
                3300f);
            CreateVoice(
                semanticAnchors,
                BoilerAnchorId,
                MountainRoadCafeSoundKind.CoffeeBoiler,
                seed + 91,
                0.12f,
                0.55f,
                4.2f,
                4600f);
            IsInitialized = true;
        }

        private void CreateVoice(
            IReadOnlyDictionary<string, Transform> semanticAnchors,
            string anchorId,
            MountainRoadCafeSoundKind kind,
            int seed,
            float volume,
            float minimumDistance,
            float maximumDistance,
            float cutoff)
        {
            if (!semanticAnchors.TryGetValue(
                    anchorId,
                    out Transform anchor) ||
                anchor == null)
            {
                throw new InvalidOperationException(
                    $"Cafe sound '{kind}' has no visible '{anchorId}' " +
                    "semantic owner.");
            }

            var voice = new GameObject("Source - " + anchorId);
            voice.transform.SetParent(transform, false);
            voice.transform.position = anchor.position;

            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = 164;
            source.volume = volume;
            source.clip = MountainRoadCafeSoundSynthesis.CreateRuntimeClip(
                kind,
                seed);
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);

            AudioLowPassFilter filter =
                voice.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = cutoff;
            filter.lowpassResonanceQ = 1f;
            sources.Add(source);
            runtimeClips.Add(source.clip);
            source.Play();
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

            sources.Clear();
            runtimeClips.Clear();
            IsInitialized = false;
        }
    }
}
