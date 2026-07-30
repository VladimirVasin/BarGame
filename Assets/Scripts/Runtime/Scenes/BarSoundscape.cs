using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarSoundscape : MonoBehaviour
    {
        public const int SampleRate = 22050;
        public const int OwnedSourceCount = 2;
        public const int CompatibleSceneSourceCount = 4;
        public const int RuntimeClipCount = 3;

        private const float CrowdLoopDuration = 4f;
        private const float GlassClinkDuration = 0.22f;
        private const float ChairScrapeDuration = 0.42f;
        private const float CrowdVolume = 0.24f;
        private const float CueVolume = 0.32f;

        [SerializeField] private AudioSource crowdSource;
        [SerializeField] private AudioSource cueSource;
        [SerializeField] private AudioLowPassFilter crowdFilter;
        [SerializeField] private AudioLowPassFilter cueFilter;

        private AudioClip crowdClip;
        private AudioClip glassClinkClip;
        private AudioClip chairScrapeClip;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private Vector3 eventPosition;
        private float crowdRadius = 12f;
        private float crowdGain = 1f;
        private float cueRadius = 8f;
        private float cueGain = 1f;

        public bool IsInitialized { get; private set; }
        public bool HasPlayedCue { get; private set; }
        public BarSoundscapeCue LastPlayedCue { get; private set; }
        public AudioSource CrowdSource => crowdSource;
        public AudioSource CueSource => cueSource;
        public AudioClip CrowdClip => crowdClip;
        public AudioClip GlassClinkClip => glassClinkClip;
        public AudioClip ChairScrapeClip => chairScrapeClip;
        public int CueSequence => cueSequence;
        public float SecondsUntilNextCue => secondsUntilNextCue;
        public float CrowdRadius => crowdRadius;
        public float CrowdGain => crowdGain;
        public float CueRadius => cueRadius;
        public float CueGain => cueGain;

        public void Initialize(
            int seed,
            Vector3 crowdPosition,
            Vector3 rareCuePosition)
        {
            Initialize(
                seed,
                crowdPosition,
                rareCuePosition,
                12f,
                1f,
                8f,
                1f);
        }

        public void Initialize(
            int seed,
            Vector3 crowdPosition,
            Vector3 rareCuePosition,
            float crowdMaxDistance,
            float crowdVolumeScale,
            float cueMaxDistance,
            float cueVolumeScale)
        {
            crowdRadius = Mathf.Max(0.1f, crowdMaxDistance);
            crowdGain = Mathf.Clamp01(crowdVolumeScale);
            cueRadius = Mathf.Max(0.1f, cueMaxDistance);
            cueGain = Mathf.Clamp01(cueVolumeScale);
            EnsureRuntimeObjects();
            deterministicSeed = seed;
            eventPosition = rareCuePosition;
            crowdSource.transform.position = crowdPosition;
            cueSource.transform.position = rareCuePosition;
            cueSequence = 0;
            HasPlayedCue = false;
            LastPlayedCue = default;
            secondsUntilNextCue =
                BarSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
            IsInitialized = true;

            crowdSource.Stop();
            cueSource.Stop();
            cueSource.clip = null;
            if (isActiveAndEnabled)
            {
                crowdSource.Play();
            }
        }

        public void AdvanceSoundscape(float unscaledDeltaTime)
        {
            if (!IsInitialized ||
                float.IsNaN(unscaledDeltaTime) ||
                unscaledDeltaTime <= 0f)
            {
                return;
            }

            secondsUntilNextCue -= unscaledDeltaTime;
            if (secondsUntilNextCue <= 0f)
            {
                PlayNextCue();
            }
        }

        private void Update()
        {
            AdvanceSoundscape(Time.unscaledDeltaTime);
        }

        private void OnEnable()
        {
            if (IsInitialized &&
                crowdSource != null &&
                !crowdSource.isPlaying)
            {
                crowdSource.Play();
            }
        }

        private void OnDisable()
        {
            crowdSource?.Stop();
            cueSource?.Stop();
        }

        private void OnDestroy()
        {
            crowdSource?.Stop();
            cueSource?.Stop();
            if (crowdSource != null)
            {
                crowdSource.clip = null;
            }

            if (cueSource != null)
            {
                cueSource.clip = null;
            }

            DestroyRuntimeClip(ref crowdClip);
            DestroyRuntimeClip(ref glassClinkClip);
            DestroyRuntimeClip(ref chairScrapeClip);
            DestroyOwnedSource(crowdSource);
            DestroyOwnedSource(cueSource);
            crowdSource = null;
            cueSource = null;
            crowdFilter = null;
            cueFilter = null;
            IsInitialized = false;
        }

        private void PlayNextCue()
        {
            BarSoundscapeCue cue =
                BarSoundscapeSchedule.GetCue(
                    deterministicSeed,
                    cueSequence);
            cueSequence++;
            HasPlayedCue = true;
            LastPlayedCue = cue;

            cueSource.Stop();
            cueSource.transform.position = eventPosition;
            cueSource.clip = cue.Kind ==
                             BarSoundscapeCueKind.GlassClink
                ? glassClinkClip
                : chairScrapeClip;
            cueSource.pitch = cue.Pitch;
            cueSource.volume =
                CueVolume * cueGain * cue.VolumeScale;
            cueSource.Play();

            secondsUntilNextCue =
                BarSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
        }

        private void EnsureRuntimeObjects()
        {
            if (crowdSource == null)
            {
                GameObject crowdObject =
                    new GameObject("Crowd Bed");
                crowdObject.transform.SetParent(transform, false);
                crowdSource =
                    crowdObject.AddComponent<AudioSource>();
                crowdFilter =
                    crowdObject.AddComponent<AudioLowPassFilter>();
            }

            if (cueSource == null)
            {
                GameObject cueObject =
                    new GameObject("Rare Bar Cues");
                cueObject.transform.SetParent(transform, false);
                cueSource = cueObject.AddComponent<AudioSource>();
                cueFilter =
                    cueObject.AddComponent<AudioLowPassFilter>();
            }

            ConfigureCrowdSource();
            ConfigureCueSource();
            EnsureRuntimeClips();
        }

        private void ConfigureCrowdSource()
        {
            crowdSource.playOnAwake = false;
            crowdSource.loop = true;
            crowdSource.spatialBlend = 0.55f;
            crowdSource.dopplerLevel = 0f;
            crowdSource.rolloffMode = AudioRolloffMode.Linear;
            crowdSource.minDistance = 1.5f;
            crowdSource.maxDistance = crowdRadius;
            crowdSource.volume = CrowdVolume * crowdGain;
            crowdSource.priority = 176;
            GameAudioMixer.Route(
                crowdSource,
                GameAudioGroup.AmbienceDetails);

            crowdFilter = crowdFilter != null
                ? crowdFilter
                : crowdSource.GetComponent<AudioLowPassFilter>();
            if (crowdFilter == null)
            {
                crowdFilter =
                    crowdSource.gameObject.AddComponent<
                        AudioLowPassFilter>();
            }

            crowdFilter.cutoffFrequency = 3400f;
            crowdFilter.lowpassResonanceQ = 1f;
        }

        private void ConfigureCueSource()
        {
            cueSource.playOnAwake = false;
            cueSource.loop = false;
            cueSource.spatialBlend = 0.72f;
            cueSource.dopplerLevel = 0f;
            cueSource.rolloffMode = AudioRolloffMode.Linear;
            cueSource.minDistance = 1.2f;
            cueSource.maxDistance = cueRadius;
            cueSource.volume = CueVolume * cueGain;
            cueSource.priority = 168;
            GameAudioMixer.Route(
                cueSource,
                GameAudioGroup.SfxWorld);

            cueFilter = cueFilter != null
                ? cueFilter
                : cueSource.GetComponent<AudioLowPassFilter>();
            if (cueFilter == null)
            {
                cueFilter =
                    cueSource.gameObject.AddComponent<
                        AudioLowPassFilter>();
            }

            cueFilter.cutoffFrequency = 5200f;
            cueFilter.lowpassResonanceQ = 1f;
        }

        private void EnsureRuntimeClips()
        {
            if (crowdClip == null)
            {
                crowdClip = CreateClip(
                    "BarSoundscape_Crowd",
                    GenerateCrowdSamples());
            }

            if (glassClinkClip == null)
            {
                glassClinkClip = CreateClip(
                    "BarSoundscape_GlassClink",
                    GenerateGlassClinkSamples());
            }

            if (chairScrapeClip == null)
            {
                chairScrapeClip = CreateClip(
                    "BarSoundscape_ChairScrape",
                    GenerateChairScrapeSamples());
            }

            crowdSource.clip = crowdClip;
        }

        private static AudioClip CreateClip(
            string clipName,
            float[] samples)
        {
            AudioClip clip = AudioClip.Create(
                clipName,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float[] GenerateCrowdSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * CrowdLoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase =
                    index /
                    (float)sampleCount *
                    Mathf.PI *
                    2f;
                float roomBreath =
                    0.68f +
                    Mathf.Sin(phase * 2f + 0.7f) * 0.17f +
                    Mathf.Sin(phase * 3f + 2.3f) * 0.09f;
                float voices =
                    Mathf.Sin(phase * 83f + 0.2f) * 0.17f +
                    Mathf.Sin(phase * 109f + 1.6f) * 0.13f +
                    Mathf.Sin(phase * 137f + 3.1f) * 0.10f +
                    Mathf.Sin(phase * 191f + 4.4f) * 0.055f;
                float consonants =
                    Mathf.Sin(phase * 263f + 0.8f) *
                    Mathf.Sin(phase * 7f + 1.1f) *
                    0.035f;
                samples[index] = Quantize(
                    (voices * roomBreath + consonants) * 0.42f);
            }

            return samples;
        }

        private static float[] GenerateGlassClinkSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * GlassClinkDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float envelope = Mathf.Exp(-normalized * 8.5f);
                float sample =
                    Mathf.Sin(2f * Mathf.PI * 1760f * time) * 0.54f +
                    Mathf.Sin(2f * Mathf.PI * 2470f * time) * 0.29f +
                    Mathf.Sin(2f * Mathf.PI * 1120f * time) * 0.14f;
                samples[index] = Quantize(sample * envelope * 0.72f);
            }

            return samples;
        }

        private static float[] GenerateChairScrapeSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * ChairScrapeDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x43484149u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float envelope =
                    Mathf.Sin(Mathf.PI * normalized) *
                    (1f - normalized * 0.28f);
                float noise = NextNoise(ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.18f;
                float woodTone =
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (94f + normalized * 42f) *
                        time) *
                    0.18f;
                float pulse =
                    0.55f +
                    Mathf.Abs(
                        Mathf.Sin(
                            2f *
                            Mathf.PI *
                            8.5f *
                            time)) *
                    0.45f;
                samples[index] = Quantize(
                    (filteredNoise * 0.45f + woodTone) *
                    envelope *
                    pulse);
            }

            return samples;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Clamp(
                Mathf.Round(sample * 63f) / 63f,
                -0.95f,
                0.95f);
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

        private static void DestroyRuntimeClip(ref AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }

            clip = null;
        }

        private void DestroyOwnedSource(AudioSource source)
        {
            if (source == null ||
                source.gameObject == gameObject)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(source.gameObject);
            }
            else
            {
                DestroyImmediate(source.gameObject);
            }
        }
    }
}
