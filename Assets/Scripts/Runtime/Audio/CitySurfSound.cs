using System;
using System.Collections.Generic;
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
    /// Plays one synthesized surf voice at the nearest point of the real,
    /// finite waterline. Spatial rolloff makes the sea disappear with
    /// distance while retaining an unambiguous shoreward direction.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class CitySurfSoundPlayer : MonoBehaviour
    {
        public const int OwnedSourceCount = 1;
        public const float MaximumVolume = 0.10f;
        public const float FarCutoffFrequency = 900f;
        public const float ShoreCutoffFrequency = 2600f;
        public const float MinimumDistance = 4f;
        public const float MaximumDistance = 44f;
        public const float ActivationMargin = 4f;

        private const float MinimumAudibleIntensity = 0.005f;

        private AudioClip generatedClip;
        private float appliedIntensity = -1f;
        private bool hasShoreline;
        private Rect shoreline;
        private float waterlineZ;
        private float sourceHeight;
        private float occlusionVolume = 1f;
        private float occlusionCutoff = float.MaxValue;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public Vector3 Anchor =>
            Source != null ? Source.transform.position : Vector3.zero;
        public AudioClip ActiveClip => generatedClip;
        public float Intensity =>
            appliedIntensity < 0f ? 0f : appliedIntensity;

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();
            generatedClip =
                CitySurfAmbienceSynthesis.CreateRuntimeClip();
            ConfigureSource(Source, ToneFilter);
        }

        public void SetShoreline(CitySeacoastPlan plan)
        {
            hasShoreline = plan != null;
            if (!hasShoreline)
            {
                StopAll();
                return;
            }

            CitySeacoastFrame frame = plan.Frame;
            shoreline = frame.BeachRowBounds;
            waterlineZ = frame.WaterlineZ + 0.35f;
            sourceHeight = frame.SeaTopY + 0.18f;
            Source.transform.position = new Vector3(
                shoreline.center.x,
                sourceHeight,
                waterlineZ);
        }

        public void SetIntensity(float intensity)
        {
            float clamped = Mathf.Clamp01(intensity);
            if (clamped.Equals(appliedIntensity))
            {
                return;
            }

            appliedIntensity = clamped;
            ApplyMix();
        }

        public void SetOcclusion(CitySoundOcclusionSample occlusion)
        {
            if (occlusionVolume.Equals(occlusion.VolumeMultiplier) &&
                occlusionCutoff.Equals(
                    occlusion.MaximumCutoffFrequency))
            {
                return;
            }

            occlusionVolume = occlusion.VolumeMultiplier;
            occlusionCutoff = occlusion.MaximumCutoffFrequency;
            ApplyMix();
        }

        private void ApplyMix()
        {
            float intensity = Mathf.Max(0f, appliedIntensity);
            float volume = intensity <= MinimumAudibleIntensity
                ? 0f
                : MaximumVolume *
                  Mathf.Pow(intensity, 0.85f) *
                  occlusionVolume;
            float directCutoff = Mathf.Lerp(
                FarCutoffFrequency,
                ShoreCutoffFrequency,
                intensity);
            Source.volume = volume;
            ToneFilter.cutoffFrequency = Mathf.Min(
                directCutoff,
                occlusionCutoff);
            if (volume <= 0f && Source.isPlaying)
            {
                Source.Stop();
            }
        }

        public void SetListenerPosition(Vector3 listenerPosition)
        {
            if (!hasShoreline)
            {
                return;
            }

            // One voice follows the nearest point of the finite waterline.
            // It remains visibly explainable as surf, avoids four copies of
            // the same loop and cannot produce phase seams along the beach.
            Source.transform.position = new Vector3(
                Mathf.Clamp(
                    listenerPosition.x,
                    shoreline.xMin,
                    shoreline.xMax),
                sourceHeight,
                waterlineZ);
            float activeDistance = MaximumDistance + ActivationMargin;
            Vector3 delta = Source.transform.position - listenerPosition;
            delta.y = 0f;
            bool shouldRun =
                appliedIntensity > MinimumAudibleIntensity &&
                delta.sqrMagnitude <= activeDistance * activeDistance;
            if (shouldRun && !Source.isPlaying)
            {
                Source.Play();
            }
            else if (!shouldRun && Source.isPlaying)
            {
                Source.Stop();
            }
        }

        private void ConfigureSource(
            AudioSource source,
            AudioLowPassFilter filter)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinimumDistance;
            source.maxDistance = MaximumDistance;
            source.spread = 72f;
            source.priority = 184;
            source.volume = 0f;
            source.clip = generatedClip;
            source.reverbZoneMix = 0.7f;
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);

            filter.cutoffFrequency = FarCutoffFrequency;
            filter.lowpassResonanceQ = 1f;
        }

        private void StopAll()
        {
            Source?.Stop();
        }

        private void OnDestroy()
        {
            StopAll();
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
    /// Keeps the surf source on the nearest physical waterline point and
    /// drives its breaker strength from deterministic weather wind.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CitySurfSoundController : MonoBehaviour
    {
        public const float OcclusionRefreshSeconds = 0.25f;

        private CitySurfSoundPlayer player;
        private Transform listener;
        private IReadOnlyList<BuildingLot> buildingLots;
        private float occlusionCountdown;
        private bool ready;

        public void Initialize(
            CitySurfSoundPlayer surfPlayer,
            Transform listenerTransform,
            CitySeacoastPlan seacoastPlan,
            IReadOnlyList<BuildingLot> occlusionLots)
        {
            player = surfPlayer != null
                ? surfPlayer
                : throw new ArgumentNullException(nameof(surfPlayer));
            listener = listenerTransform != null
                ? listenerTransform
                : throw new ArgumentNullException(
                    nameof(listenerTransform));
            buildingLots = occlusionLots ??
                throw new ArgumentNullException(nameof(occlusionLots));
            if (seacoastPlan == null)
            {
                ready = false;
                player.SetIntensity(0f);
                return;
            }

            player.SetShoreline(seacoastPlan);
            ready = true;
        }

        private void Update()
        {
            if (!ready || player == null || listener == null)
            {
                return;
            }

            WindSample wind = GameWeatherRules.EvaluateCurrentWind();
            player.SetIntensity(Mathf.Clamp01(
                0.72f + 0.28f * wind.Strength01));
            player.SetListenerPosition(listener.position);
            occlusionCountdown -= Time.deltaTime;
            if (occlusionCountdown <= 0f)
            {
                occlusionCountdown = OcclusionRefreshSeconds;
                player.SetOcclusion(CitySoundOcclusion.Evaluate(
                    player.Anchor,
                    listener.position,
                    buildingLots));
            }
        }
    }
}
