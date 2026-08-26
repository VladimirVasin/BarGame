using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Short, deliberately rough mono loops for the five physical things
    /// that can be heard along the mountain road. Nothing in this synthesis
    /// is an anonymous forest filler: every clip is attached to the matching
    /// visible object from <see cref="MountainRoadPlan.SoundAnchors"/>.
    /// </summary>
    public static class MountainRoadSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 4f;

        private const int CrossfadeSamples = 1536;
        private const float QuantizationSteps = 127f;

        public static float[] GenerateSamples(
            MountainRoadSoundAnchorKind kind,
            int seed)
        {
            if (kind == MountainRoadSoundAnchorKind.TunnelLampBallast)
            {
                return CityTunnelLampSoundSynthesis
                    .GenerateBallastBuzzSamples();
            }

            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var extended = new float[sampleCount + CrossfadeSamples];
            uint noiseState = CreateSeed(seed, kind);
            float lowNoise = 0f;
            float slowNoise = 0f;
            for (int index = 0; index < extended.Length; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                lowNoise += (white - lowNoise) * 0.055f;
                slowNoise += (white - slowNoise) * 0.0018f;
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
                samples[index] = Quantize(
                    Mathf.Lerp(
                        extended[sampleCount + index],
                        samples[index],
                        blend));
            }

            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        internal static AudioClip CreateRuntimeClip(
            MountainRoadSoundAnchorKind kind,
            int seed)
        {
            if (kind == MountainRoadSoundAnchorKind.TunnelLampBallast)
            {
                return CityTunnelLampSoundSynthesis
                    .CreateBallastRuntimeClip();
            }

            float[] samples = GenerateSamples(kind, seed);
            AudioClip clip = AudioClip.Create(
                "MountainRoad_" + kind,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float Evaluate(
            MountainRoadSoundAnchorKind kind,
            float time,
            float white,
            float lowNoise,
            float slowNoise)
        {
            switch (kind)
            {
                case MountainRoadSoundAnchorKind.CulvertWater:
                    return CulvertWater(time, lowNoise, slowNoise);
                case MountainRoadSoundAnchorKind.LooseGuardRail:
                    return LooseGuardRail(time, white, slowNoise);
                case MountainRoadSoundAnchorKind.UtilityCable:
                    return UtilityCable(time, lowNoise, slowNoise);
                case MountainRoadSoundAnchorKind.SnowPole:
                    return SnowPole(time, white, slowNoise);
                case MountainRoadSoundAnchorKind.WindsockHalyard:
                    return WindsockHalyard(time, white, slowNoise);
                case MountainRoadSoundAnchorKind.LoadTarp:
                    return LoadTarp(time, white, lowNoise, slowNoise);
                case MountainRoadSoundAnchorKind.TunnelLampBallast:
                    return 0f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown mountain-road sound owner.");
            }
        }

        private static float CulvertWater(
            float time,
            float lowNoise,
            float slowNoise)
        {
            float burble =
                Mathf.Sin(time * Mathf.PI * 2f * 5.0f) * 0.030f +
                Mathf.Sin(time * Mathf.PI * 2f * 8.0f + 1.7f) * 0.018f;
            float body = lowNoise * (0.19f + slowNoise * 0.035f);
            return body + burble;
        }

        private static float LooseGuardRail(
            float time,
            float white,
            float slowNoise)
        {
            float first = MetallicStrike(time, 0.72f, 410f, 19f);
            float second = MetallicStrike(time, 2.63f, 337f, 24f);
            float strainedMetal =
                Mathf.Sin(time * Mathf.PI * 2f * 71f) *
                Mathf.Max(0f, slowNoise - 0.09f) *
                0.035f;
            return first + second + strainedMetal + white * 0.004f;
        }

        private static float UtilityCable(
            float time,
            float lowNoise,
            float slowNoise)
        {
            float mains =
                Mathf.Sin(time * Mathf.PI * 2f * 100f) * 0.055f +
                Mathf.Sin(time * Mathf.PI * 2f * 200f + 0.2f) * 0.019f;
            float wire =
                Mathf.Sin(time * Mathf.PI * 2f * 37f + 1.1f) *
                (0.018f + Mathf.Abs(slowNoise) * 0.025f);
            return mains + wire + lowNoise * 0.015f;
        }

        private static float SnowPole(
            float time,
            float white,
            float slowNoise)
        {
            float first = MetallicStrike(time, 1.31f, 690f, 31f);
            float second = MetallicStrike(time, 3.44f, 832f, 38f) * 0.72f;
            float windContact = Mathf.Max(0f, slowNoise - 0.12f) *
                                white * 0.035f;
            return first + second + windContact;
        }

        /// <summary>
        /// A rope clip against a hollow mast. Lower and slacker than a
        /// snow pole because there is a metre of loose line above it, and
        /// the strikes come in pairs - the clip hits going out and again
        /// coming back.
        /// </summary>
        private static float WindsockHalyard(
            float time,
            float white,
            float slowNoise)
        {
            float first = MetallicStrike(time, 0.41f, 246f, 15f);
            float echo = MetallicStrike(time, 0.58f, 231f, 21f) * 0.55f;
            float second = MetallicStrike(time, 2.24f, 268f, 17f) * 0.86f;
            float secondEcho =
                MetallicStrike(time, 2.39f, 252f, 23f) * 0.48f;
            float rope = Mathf.Max(0f, slowNoise - 0.05f) * white * 0.028f;
            return first + echo + second + secondEcho + rope;
        }

        /// <summary>
        /// Canvas. No pitch at all - a band of noise that opens and shuts
        /// with the gust, and one hard snap where the rope has gone slack.
        /// </summary>
        private static float LoadTarp(
            float time,
            float white,
            float lowNoise,
            float slowNoise)
        {
            float gust = Mathf.Max(0f, slowNoise + 0.18f);
            float body = white * gust * 0.052f + lowNoise * 0.021f;
            float snap = 0f;
            float elapsed = time - 1.86f;
            if (elapsed >= 0f && elapsed < 0.12f)
            {
                snap = white * Mathf.Exp(-elapsed * 42f) * 0.19f;
            }

            return body + snap;
        }

        private static float MetallicStrike(
            float loopTime,
            float strikeTime,
            float frequency,
            float decay)
        {
            float elapsed = loopTime - strikeTime;
            if (elapsed < 0f || elapsed > 0.34f)
            {
                return 0f;
            }

            float attack = Mathf.Min(1f, elapsed * 500f);
            return Mathf.Sin(
                       elapsed * Mathf.PI * 2f * frequency) *
                   Mathf.Exp(-elapsed * decay) *
                   attack *
                   0.22f;
        }

        private static uint CreateSeed(
            int seed,
            MountainRoadSoundAnchorKind kind)
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

    [DisallowMultipleComponent]
    public sealed class MountainRoadSoundscape : MonoBehaviour
    {
        private readonly List<AudioSource> sources =
            new List<AudioSource>();
        private readonly List<AudioClip> generatedClips =
            new List<AudioClip>();

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<AudioSource> Sources => sources;

        public static MountainRoadSoundscape Create(
            Transform parent,
            MountainRoadPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            GameObject owner = new GameObject("Mountain Road Soundscape");
            owner.transform.SetParent(parent, false);
            MountainRoadSoundscape soundscape =
                owner.AddComponent<MountainRoadSoundscape>();
            soundscape.Initialize(plan);
            return soundscape;
        }

        public void Initialize(MountainRoadPlan plan)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mountain-road soundscape is already initialized.");
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            for (int index = 0; index < plan.SoundAnchors.Count; index++)
            {
                CreateVoice(plan.SoundAnchors[index], plan.Seed + index * 97);
            }

            IsInitialized = true;
        }

        private void CreateVoice(
            MountainRoadSoundAnchor anchor,
            int seed)
        {
            GameObject voiceObject = new GameObject(
                "Source - " + anchor.SourceObjectStableId);
            voiceObject.transform.SetParent(transform, false);
            voiceObject.transform.position = anchor.Position;

            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.65f;
            source.maxDistance = anchor.AudibleRadius;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = 170;
            source.volume = GetVolume(anchor.Kind);
            source.clip = MountainRoadSoundSynthesis.CreateRuntimeClip(
                anchor.Kind,
                seed);
            GameAudioMixer.Route(
                source,
                anchor.Kind == MountainRoadSoundAnchorKind.TunnelLampBallast
                    ? GameAudioGroup.AmbienceDetails
                    : GameAudioGroup.SfxWorld);

            AudioLowPassFilter filter =
                voiceObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = GetCutoff(anchor.Kind);
            filter.lowpassResonanceQ = 1f;
            sources.Add(source);
            generatedClips.Add(source.clip);
            source.Play();
        }

        private static float GetVolume(MountainRoadSoundAnchorKind kind)
        {
            switch (kind)
            {
                case MountainRoadSoundAnchorKind.TunnelLampBallast:
                    return 0.11f;
                case MountainRoadSoundAnchorKind.CulvertWater:
                    return 0.12f;
                case MountainRoadSoundAnchorKind.LooseGuardRail:
                    return 0.10f;
                case MountainRoadSoundAnchorKind.UtilityCable:
                    return 0.075f;
                case MountainRoadSoundAnchorKind.SnowPole:
                    return 0.10f;
                default:
                    return 0.08f;
            }
        }

        private static float GetCutoff(MountainRoadSoundAnchorKind kind)
        {
            switch (kind)
            {
                case MountainRoadSoundAnchorKind.CulvertWater:
                    return 3900f;
                case MountainRoadSoundAnchorKind.LooseGuardRail:
                case MountainRoadSoundAnchorKind.SnowPole:
                    return 5100f;
                case MountainRoadSoundAnchorKind.WindsockHalyard:
                    return 4400f;
                case MountainRoadSoundAnchorKind.LoadTarp:
                    return 2100f;
                case MountainRoadSoundAnchorKind.UtilityCable:
                    return 2800f;
                default:
                    return 3400f;
            }
        }

        private void OnDestroy()
        {
            for (int index = 0; index < generatedClips.Count; index++)
            {
                AudioClip clip = generatedClips[index];
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
            generatedClips.Clear();
        }
    }
}
