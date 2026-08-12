using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Deterministic broadband rain loop: filtered pseudo-random noise with a
    /// slow patter envelope, crossfaded into a seamless 4 second bed.
    /// </summary>
    public static class CityRainAmbienceSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 4f;
        public const int CrossfadeSamples = 2048;

        private const uint NoiseSeed = 0x5241494Eu;

        public static float[] GenerateSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var samples = new float[sampleCount + CrossfadeSamples];
            uint state = NoiseSeed;
            float body = 0f;
            float patter = 0f;

            for (int index = 0; index < samples.Length; index++)
            {
                float white = NextNoise(ref state);
                body += 0.30f * (white - body);
                float magnitude = white < 0f ? -white : white;
                patter += 0.0018f * (magnitude - patter);
                float envelope = 0.62f + patter * 2.4f;
                samples[index] = Mathf.Clamp(
                    body * envelope,
                    -0.72f,
                    0.72f);
            }

            var looped = new float[sampleCount];
            Array.Copy(samples, looped, sampleCount);
            for (int index = 0; index < CrossfadeSamples; index++)
            {
                float fade = (float)index / CrossfadeSamples;
                looped[index] =
                    looped[index] * fade +
                    samples[sampleCount + index] * (1f - fade);
            }

            return looped;
        }

        internal static AudioClip CreateRuntimeClip()
        {
            float[] samples = GenerateSamples();
            AudioClip clip = AudioClip.Create(
                "CityRainAmbience",
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state * (2f / uint.MaxValue) - 1f;
        }
    }

    /// <summary>
    /// Plays the synthesized rain bed and maps the continuous weather
    /// intensity onto loudness and brightness, so light rain stays a soft
    /// distant hiss while heavy rain moves closer and sharper.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class CityRainSoundPlayer : MonoBehaviour
    {
        public const float MaximumVolume = 0.115f;
        public const float QuietCutoffFrequency = 1500f;
        public const float HeavyCutoffFrequency = 3600f;

        private const float MinimumAudibleIntensity = 0.005f;

        private AudioClip generatedClip;
        private float appliedIntensity = -1f;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public AudioClip ActiveClip => generatedClip;
        public float Intensity =>
            appliedIntensity < 0f ? 0f : appliedIntensity;

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();
            generatedClip =
                CityRainAmbienceSynthesis.CreateRuntimeClip();

            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.priority = 160;
            Source.volume = 0f;
            Source.clip = generatedClip;
            GameAudioMixer.Route(
                Source,
                GameAudioGroup.AmbienceBeds);

            ToneFilter.cutoffFrequency = QuietCutoffFrequency;
            ToneFilter.lowpassResonanceQ = 1f;
            Source.Play();
        }

        public void SetIntensity(float intensity)
        {
            float clamped = Mathf.Clamp01(intensity);
            if (clamped.Equals(appliedIntensity))
            {
                return;
            }

            appliedIntensity = clamped;
            if (Source == null)
            {
                return;
            }

            if (clamped <= MinimumAudibleIntensity)
            {
                Source.volume = 0f;
                return;
            }

            Source.volume =
                MaximumVolume * Mathf.Pow(clamped, 0.85f);
            ToneFilter.cutoffFrequency = Mathf.Lerp(
                QuietCutoffFrequency,
                HeavyCutoffFrequency,
                clamped);
        }

        private void OnDestroy()
        {
            if (generatedClip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedClip);
            }
            else
            {
                DestroyImmediate(generatedClip);
            }

            generatedClip = null;
        }
    }
}
