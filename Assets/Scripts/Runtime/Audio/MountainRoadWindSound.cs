using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Deterministic wind bed for the mountain road: filtered noise with a
    /// slow formant that wanders, crossfaded into a seamless 4 second loop.
    ///
    /// It is built like the rain bed rather than like
    /// <c>MountainRoadSoundSynthesis</c>, and the difference matters. That
    /// file's whole contract is that nothing in it is anonymous filler —
    /// every clip belongs to a visible object and plays from where that
    /// object stands, the snow pole's whine included. This is air, it comes
    /// from nowhere in particular, and it is the exact thing that replaced
    /// the rain hiss when the mountain's rain became snow. So it belongs
    /// beside <see cref="CityRainAmbienceSynthesis"/>.
    ///
    /// Snow itself is silent, which is the point: what you hear on the climb
    /// is the wind that is driving it sideways.
    /// </summary>
    public static class MountainRoadWindAmbienceSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 4f;
        public const int CrossfadeSamples = 2048;

        private const uint NoiseSeed = 0x57494E44u;

        public static float[] GenerateSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var samples = new float[sampleCount + CrossfadeSamples];
            uint state = NoiseSeed;
            float body = 0f;
            float lower = 0f;
            float swell = 0f;

            for (int index = 0; index < samples.Length; index++)
            {
                float white = NextNoise(ref state);

                // Two poles rather than the rain's one. Rain is broadband
                // patter; wind is a band that moves, and a single lowpass
                // gives a hiss that never breathes.
                body += 0.16f * (white - body);
                lower += 0.035f * (body - lower);
                float voiced = body - lower * 0.72f;

                float magnitude = voiced < 0f ? -voiced : voiced;
                swell += 0.0009f * (magnitude - swell);
                float envelope = 0.48f + swell * 3.6f;
                samples[index] = Mathf.Clamp(
                    voiced * envelope * 3.1f,
                    -0.74f,
                    0.74f);
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
                "MountainRoadWindAmbience",
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
    /// Plays the wind bed and maps the shaped mountain wind onto loudness and
    /// brightness, so the tunnel mouth is a low moan and the terrace is a
    /// hard sheeting roar.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class MountainRoadWindSoundPlayer : MonoBehaviour
    {
        public const float MaximumVolume = 0.14f;
        public const float CalmCutoffFrequency = 700f;
        public const float GaleCutoffFrequency = 2600f;

        /// <summary>
        /// One source, on this player's own GameObject. Scene audio budgets
        /// are asserted against owner constants rather than a hand-counted
        /// total, so this is the number they read.
        /// </summary>
        public const int OwnedSourceCount = 1;

        private const float MinimumAudibleStrength = 0.005f;

        /// <summary>
        /// What a shut car takes off the wind: most of the level and most
        /// of the top. The bed is still there under the engine - a gale
        /// through a door seal is a real sound - but it is behind glass.
        /// </summary>
        public const float EnclosedVolumeMultiplier = 0.42f;

        public const float EnclosedCutoffMultiplier = 0.45f;

        private AudioClip generatedClip;
        private float appliedStrength = -1f;
        private float enclosure;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public AudioClip ActiveClip => generatedClip;
        public float Strength =>
            appliedStrength < 0f ? 0f : appliedStrength;

        /// <summary>`0` in the open, `1` shut inside a car.</summary>
        public float Enclosure => enclosure;

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();
            generatedClip =
                MountainRoadWindAmbienceSynthesis.CreateRuntimeClip();

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

            ToneFilter.cutoffFrequency = CalmCutoffFrequency;
            ToneFilter.lowpassResonanceQ = 1f;
            Source.Play();
        }

        /// <summary>
        /// Takes the UNCLAMPED sway amplitude, so a storm at the summit is
        /// audibly worse than the same storm at the tunnel instead of both
        /// pinning at full.
        /// </summary>
        public void SetStrength(float strength)
        {
            SetNormalizedStrength(
                strength / MountainRoadWeatherRules.MaximumSwayAmplitude);
        }

        /// <summary>
        /// Drives the same air bed from an already normalized exterior wind.
        /// The Alpine Village uses this path because its weather sample is a
        /// permanent gale and has no tree-sway headroom above one to divide
        /// back out. Sharing the clip keeps both mountain exteriors sonically
        /// coherent without sharing their strength rules.
        /// </summary>
        public void SetNormalizedStrength(float strength01)
        {
            float clamped = Mathf.Clamp01(strength01);
            if (clamped.Equals(appliedStrength))
            {
                return;
            }

            appliedStrength = clamped;
            Apply();
        }

        /// <summary>
        /// How shut in the listener is, `0` open to `1` inside a car with
        /// the doors closed. The wind driver goes on writing the strength
        /// every frame; this is a second, independent factor on top of it,
        /// so the car can muffle the bed without becoming a second writer
        /// of the wind.
        /// </summary>
        public void SetEnclosure(float enclosure01)
        {
            float clamped = Mathf.Clamp01(enclosure01);
            if (clamped.Equals(enclosure))
            {
                return;
            }

            enclosure = clamped;
            Apply();
        }

        private void Apply()
        {
            if (Source == null)
            {
                return;
            }

            float strength = Strength;
            if (strength <= MinimumAudibleStrength)
            {
                Source.volume = 0f;
                return;
            }

            Source.volume =
                MaximumVolume *
                Mathf.Pow(strength, 0.85f) *
                Mathf.Lerp(1f, EnclosedVolumeMultiplier, enclosure);
            ToneFilter.cutoffFrequency =
                Mathf.Lerp(
                    CalmCutoffFrequency,
                    GaleCutoffFrequency,
                    strength) *
                Mathf.Lerp(1f, EnclosedCutoffMultiplier, enclosure);
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
