using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Deterministic surf loop: a dark low-passed rumble that swells
    /// and eases on two slow envelopes, with a brighter breaker hiss
    /// gated onto each crest — crossfaded into a seamless nine second
    /// bed, the rain synthesis's shape slowed to the sea's clock.
    ///
    /// The envelopes complete whole cycles inside the loop (two and
    /// three per bed), so the swell itself is seam-free by
    /// construction and only the noise needs the crossfade.
    /// </summary>
    public static class CitySurfAmbienceSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 9f;
        public const int CrossfadeSamples = 4096;

        private const uint NoiseSeed = 0x53555246u; // "SURF"

        public static float[] GenerateSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var samples = new float[sampleCount + CrossfadeSamples];
            uint state = NoiseSeed;
            float rumble = 0f;
            float hiss = 0f;

            for (int index = 0; index < samples.Length; index++)
            {
                float seconds = (float)index / SampleRate;
                float white = NextNoise(ref state);

                // The body: noise filtered far darker than rain's, the
                // weight of water rather than the patter of it.
                rumble += 0.085f * (white - rumble);

                // Two swell envelopes, two and three cycles per bed,
                // never cresting together.
                float swell =
                    0.5f +
                    0.30f * Mathf.Sin(
                        2f * Mathf.PI * seconds * (2f / Duration)) +
                    0.20f * Mathf.Sin(
                        2f * Mathf.PI * seconds * (3f / Duration) +
                        1.9f);

                // The breaker: a brighter band that only exists near
                // the crest of the swell, the wave actually falling.
                hiss += 0.42f * (white - hiss);
                float breaker = Mathf.Clamp01((swell - 0.62f) * 3.2f);

                samples[index] = Mathf.Clamp(
                    rumble * (0.55f + swell * 1.05f) * 2.4f +
                    (hiss - rumble) * breaker * 0.34f,
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
                "CitySurfAmbience",
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
    /// Plays the synthesized surf bed and maps nearness to the
    /// waterline onto loudness and brightness: far off it is a low
    /// pressure under the city's sound, at the sand it opens up.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class CitySurfSoundPlayer : MonoBehaviour
    {
        public const float MaximumVolume = 0.10f;
        public const float FarCutoffFrequency = 900f;
        public const float ShoreCutoffFrequency = 2600f;

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
                CitySurfAmbienceSynthesis.CreateRuntimeClip();

            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.priority = 168;
            Source.volume = 0f;
            Source.clip = generatedClip;
            GameAudioMixer.Route(
                Source,
                GameAudioGroup.AmbienceBeds);

            ToneFilter.cutoffFrequency = FarCutoffFrequency;
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
                FarCutoffFrequency,
                ShoreCutoffFrequency,
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

    /// <summary>
    /// Feeds the surf player from the hero's distance to the
    /// waterline: full within twenty metres of the sand's edge, gone
    /// by ninety, and a little louder in wind — the deterministic
    /// weather schedule's wind, so every scene hears the same sea.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CitySurfSoundController : MonoBehaviour
    {
        public const float FullDistance = 20f;
        public const float SilentDistance = 90f;

        private CitySurfSoundPlayer player;
        private Transform listener;
        private Rect shoreline;
        private float waterlineZ;
        private bool ready;

        public void Initialize(
            CitySurfSoundPlayer surfPlayer,
            Transform listenerTransform,
            CitySeacoastPlan seacoastPlan)
        {
            player = surfPlayer != null
                ? surfPlayer
                : throw new ArgumentNullException(nameof(surfPlayer));
            listener = listenerTransform != null
                ? listenerTransform
                : throw new ArgumentNullException(
                    nameof(listenerTransform));
            if (seacoastPlan == null)
            {
                ready = false;
                player.SetIntensity(0f);
                return;
            }

            shoreline = seacoastPlan.Frame.BeachRowBounds;
            waterlineZ = seacoastPlan.Frame.WaterlineZ;
            ready = true;
        }

        private void Update()
        {
            if (!ready || player == null || listener == null)
            {
                return;
            }

            Vector3 position = listener.position;
            float alongX = Mathf.Clamp(
                position.x,
                shoreline.xMin,
                shoreline.xMax);
            float acrossZ = position.z >= waterlineZ
                ? 0f
                : waterlineZ - position.z;
            float lateral = Mathf.Abs(position.x - alongX);
            float distance = Mathf.Sqrt(
                lateral * lateral + acrossZ * acrossZ);
            float nearness = 1f - Mathf.Clamp01(
                (distance - FullDistance) /
                (SilentDistance - FullDistance));

            WindSample wind = GameWeatherRules.EvaluateCurrentWind();
            player.SetIntensity(Mathf.Clamp01(
                nearness * (0.72f + 0.38f * wind.Strength01)));
        }
    }
}
