using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Deterministic thunder one-shot: an initial crack over a slow brown
    /// rumble with a delayed secondary roll, all from seeded xorshift noise.
    /// </summary>
    public static class CityThunderSynthesis
    {
        public const int SampleRate = 22050;
        public const float Duration = 3.4f;

        private const uint NoiseSeed = 0x54484E44u;

        public static float[] GenerateSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * Duration);
            var samples = new float[sampleCount];
            uint state = NoiseSeed;
            float rumble = 0f;
            float crack = 0f;
            float inverseRate = 1f / SampleRate;

            for (int index = 0; index < sampleCount; index++)
            {
                float time = index * inverseRate;
                float white = NextNoise(ref state);
                rumble += 0.045f * (white - rumble);
                crack += 0.34f * (white - crack);

                float crackEnvelope =
                    time < 0.24f
                        ? Mathf.Exp(-time * 16f)
                        : 0f;
                float rumbleEnvelope =
                    Mathf.Exp(-time * 1.35f) *
                    (0.72f + 0.28f * Mathf.Cos(time * 5.1f));
                float roll =
                    time > 0.9f
                        ? Mathf.Exp(-(time - 0.9f) * 1.8f) * 0.5f
                        : 0f;

                float sample =
                    crack * crackEnvelope * 0.85f +
                    rumble * (rumbleEnvelope + roll) * 3.6f;
                samples[index] = Mathf.Clamp(sample, -0.85f, 0.85f);
            }

            return samples;
        }

        internal static AudioClip CreateRuntimeClip()
        {
            float[] samples = GenerateSamples();
            AudioClip clip = AudioClip.Create(
                "CityThunder",
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
    /// Plays the synthesized thunder for a strike after its distance delay:
    /// near strikes arrive fast, loud and bright, far ones late, quiet and
    /// muffled. Two rotating sources keep close consecutive strikes intact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityThunderSoundPlayer : MonoBehaviour
    {
        public const float MaximumVolume = 0.30f;
        public const float NearDelaySeconds = 0.6f;
        public const float FarDelaySeconds = 3.0f;
        public const float NearCutoffFrequency = 2600f;
        public const float FarCutoffFrequency = 900f;
        public const float NearSourceDistance = 24f;
        public const float FarSourceDistance = 48f;
        public const float NearSourceHeight = 12f;
        public const float FarSourceHeight = 22f;

        private readonly AudioSource[] sources = new AudioSource[2];
        private readonly AudioLowPassFilter[] filters =
            new AudioLowPassFilter[2];
        private AudioClip generatedClip;
        private int nextSourceIndex;

        public AudioClip ActiveClip => generatedClip;
        public int StrikesPlayed { get; private set; }

        private void Awake()
        {
            generatedClip = CityThunderSynthesis.CreateRuntimeClip();
            for (int index = 0; index < sources.Length; index++)
            {
                // The low-pass filter processes every source on its own
                // GameObject, so each thunder voice owns a child object.
                GameObject voice = new GameObject(
                    "Thunder Voice " + index);
                voice.transform.SetParent(transform, false);
                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 16f;
                source.maxDistance = 96f;
                source.spread = 58f;
                source.priority = 150;
                source.volume = 0f;
                source.clip = generatedClip;
                GameAudioMixer.Route(
                    source,
                    GameAudioGroup.SfxWorld);
                AudioLowPassFilter filter =
                    voice.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = FarCutoffFrequency;
                filter.lowpassResonanceQ = 1f;
                sources[index] = source;
                filters[index] = filter;
            }
        }

        public void PlayStrike(
            float distanceFactor,
            float azimuthDegrees,
            Vector3 listenerPosition)
        {
            float distance = Mathf.Clamp01(distanceFactor);
            AudioSource source = sources[nextSourceIndex];
            AudioLowPassFilter filter = filters[nextSourceIndex];
            nextSourceIndex = (nextSourceIndex + 1) % sources.Length;
            if (source == null)
            {
                return;
            }

            source.Stop();
            Vector3 direction = Quaternion.Euler(
                0f,
                azimuthDegrees,
                0f) * Vector3.forward;
            source.transform.position =
                listenerPosition +
                direction * Mathf.Lerp(
                    NearSourceDistance,
                    FarSourceDistance,
                    distance) +
                Vector3.up * Mathf.Lerp(
                    NearSourceHeight,
                    FarSourceHeight,
                    distance);
            source.volume =
                MaximumVolume * Mathf.Lerp(1f, 0.38f, distance);
            filter.cutoffFrequency = Mathf.Lerp(
                NearCutoffFrequency,
                FarCutoffFrequency,
                distance);
            source.PlayScheduled(
                AudioSettings.dspTime +
                Mathf.Lerp(
                    NearDelaySeconds,
                    FarDelaySeconds,
                    distance));
            StrikesPlayed++;
        }

        public void StopAll()
        {
            for (int index = 0; index < sources.Length; index++)
            {
                sources[index]?.Stop();
            }
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
}
