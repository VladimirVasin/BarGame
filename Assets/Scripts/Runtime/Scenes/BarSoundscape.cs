using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarSoundscape : MonoBehaviour
    {
        public const int SampleRate = 22050;
        public const int OwnedSourceCount = 3;
        public const int CompatibleSceneSourceCount = 6;
        public const int RuntimeClipCount = 6;

        private const float CrowdLoopDuration = 8f;
        private const float GlassClinkDuration = 0.22f;
        private const float ChairScrapeDuration = 0.42f;
        private const float BottleSetDownDuration = 0.3f;
        private const float CrowdReactionDuration = 0.9f;
        private const float FirstCrowdVolume = 0.34f;
        private const float SecondCrowdVolume = 0.3f;
        private const float CueVolume = 0.34f;

        [SerializeField] private AudioSource firstCrowdSource;
        [SerializeField] private AudioSource secondCrowdSource;
        [SerializeField] private AudioSource cueSource;
        [SerializeField] private AudioLowPassFilter firstCrowdFilter;
        [SerializeField] private AudioLowPassFilter secondCrowdFilter;
        [SerializeField] private AudioLowPassFilter cueFilter;

        private AudioClip firstCrowdClip;
        private AudioClip secondCrowdClip;
        private AudioClip glassClinkClip;
        private AudioClip chairScrapeClip;
        private AudioClip bottleSetDownClip;
        private AudioClip crowdReactionClip;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private Vector3 firstCrowdPosition;
        private Vector3 secondCrowdPosition;
        private Vector3 servicePosition;
        private float firstCrowdRadius = 12f;
        private float firstCrowdGain = 1f;
        private float secondCrowdRadius = 12f;
        private float secondCrowdGain = 1f;
        private float cueRadius = 8f;
        private float cueGain = 1f;

        public bool IsInitialized { get; private set; }
        public bool HasPlayedCue { get; private set; }
        public BarSoundscapeCue LastPlayedCue { get; private set; }
        public Vector3 LastCuePosition { get; private set; }
        public AudioSource FirstCrowdSource => firstCrowdSource;
        public AudioSource SecondCrowdSource => secondCrowdSource;
        public AudioSource CueSource => cueSource;
        public AudioClip FirstCrowdClip => firstCrowdClip;
        public AudioClip SecondCrowdClip => secondCrowdClip;
        public AudioClip GlassClinkClip => glassClinkClip;
        public AudioClip ChairScrapeClip => chairScrapeClip;
        public AudioClip BottleSetDownClip => bottleSetDownClip;
        public AudioClip CrowdReactionClip => crowdReactionClip;
        public int CueSequence => cueSequence;
        public float SecondsUntilNextCue => secondsUntilNextCue;
        public float FirstCrowdRadius => firstCrowdRadius;
        public float FirstCrowdGain => firstCrowdGain;
        public float SecondCrowdRadius => secondCrowdRadius;
        public float SecondCrowdGain => secondCrowdGain;
        public float CueRadius => cueRadius;
        public float CueGain => cueGain;

        // Kept for source compatibility with the original single-pocket API.
        public AudioSource CrowdSource => firstCrowdSource;
        public AudioClip CrowdClip => firstCrowdClip;
        public float CrowdRadius => firstCrowdRadius;
        public float CrowdGain => firstCrowdGain;

        public void Initialize(
            int seed,
            Vector3 crowdPosition,
            Vector3 rareCuePosition)
        {
            Initialize(
                seed,
                crowdPosition,
                crowdPosition,
                rareCuePosition,
                12f,
                1f,
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
            Initialize(
                seed,
                crowdPosition,
                crowdPosition,
                rareCuePosition,
                crowdMaxDistance,
                crowdVolumeScale,
                crowdMaxDistance,
                crowdVolumeScale,
                cueMaxDistance,
                cueVolumeScale);
        }

        public void Initialize(
            int seed,
            Vector3 firstCrowdWorldPosition,
            Vector3 secondCrowdWorldPosition,
            Vector3 rareCuePosition,
            float firstCrowdMaxDistance,
            float firstCrowdVolumeScale,
            float secondCrowdMaxDistance,
            float secondCrowdVolumeScale,
            float cueMaxDistance,
            float cueVolumeScale)
        {
            firstCrowdPosition = firstCrowdWorldPosition;
            secondCrowdPosition = secondCrowdWorldPosition;
            servicePosition = rareCuePosition;
            firstCrowdRadius = Mathf.Max(
                0.1f,
                firstCrowdMaxDistance);
            firstCrowdGain = Mathf.Clamp01(
                firstCrowdVolumeScale);
            secondCrowdRadius = Mathf.Max(
                0.1f,
                secondCrowdMaxDistance);
            secondCrowdGain = Mathf.Clamp01(
                secondCrowdVolumeScale);
            cueRadius = Mathf.Max(0.1f, cueMaxDistance);
            cueGain = Mathf.Clamp01(cueVolumeScale);
            EnsureRuntimeObjects();
            deterministicSeed = seed;
            firstCrowdSource.transform.position =
                firstCrowdPosition;
            secondCrowdSource.transform.position =
                secondCrowdPosition;
            cueSource.transform.position = servicePosition;
            cueSequence = 0;
            HasPlayedCue = false;
            LastPlayedCue = default;
            LastCuePosition = default;
            secondsUntilNextCue =
                BarSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
            IsInitialized = true;

            StopAllSources();
            cueSource.clip = null;
            if (isActiveAndEnabled)
            {
                StartCrowdLoops();
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
            if (!IsInitialized)
            {
                return;
            }

            if (firstCrowdSource == null ||
                secondCrowdSource == null)
            {
                return;
            }

            if (!firstCrowdSource.isPlaying ||
                !secondCrowdSource.isPlaying)
            {
                StartCrowdLoops();
            }
        }

        private void OnDisable()
        {
            StopAllSources();
        }

        private void OnDestroy()
        {
            StopAllSources();
            ClearSourceClip(firstCrowdSource);
            ClearSourceClip(secondCrowdSource);
            ClearSourceClip(cueSource);

            DestroyRuntimeClip(ref firstCrowdClip);
            DestroyRuntimeClip(ref secondCrowdClip);
            DestroyRuntimeClip(ref glassClinkClip);
            DestroyRuntimeClip(ref chairScrapeClip);
            DestroyRuntimeClip(ref bottleSetDownClip);
            DestroyRuntimeClip(ref crowdReactionClip);
            DestroyOwnedSource(firstCrowdSource);
            DestroyOwnedSource(secondCrowdSource);
            DestroyOwnedSource(cueSource);
            firstCrowdSource = null;
            secondCrowdSource = null;
            cueSource = null;
            firstCrowdFilter = null;
            secondCrowdFilter = null;
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
            LastCuePosition = ResolveCuePosition(cue.Kind);
            cueSource.transform.position = LastCuePosition;
            cueSource.clip = ResolveCueClip(cue.Kind);
            cueSource.pitch = cue.Pitch;
            cueSource.volume =
                CueVolume * cueGain * cue.VolumeScale;
            cueSource.Play();

            secondsUntilNextCue =
                BarSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
        }

        private Vector3 ResolveCuePosition(
            BarSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case BarSoundscapeCueKind.ChairScrape:
                    return firstCrowdPosition;
                case BarSoundscapeCueKind.CrowdReaction:
                    return secondCrowdPosition;
                default:
                    return servicePosition;
            }
        }

        private AudioClip ResolveCueClip(
            BarSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case BarSoundscapeCueKind.GlassClink:
                    return glassClinkClip;
                case BarSoundscapeCueKind.ChairScrape:
                    return chairScrapeClip;
                case BarSoundscapeCueKind.BottleSetDown:
                    return bottleSetDownClip;
                case BarSoundscapeCueKind.CrowdReaction:
                    return crowdReactionClip;
                default:
                    return glassClinkClip;
            }
        }

        private void EnsureRuntimeObjects()
        {
            EnsureCrowdSource(
                ref firstCrowdSource,
                ref firstCrowdFilter,
                "Crowd Pocket A");
            EnsureCrowdSource(
                ref secondCrowdSource,
                ref secondCrowdFilter,
                "Crowd Pocket B");

            if (cueSource == null)
            {
                GameObject cueObject =
                    new GameObject("Rare Bar Cues");
                cueObject.transform.SetParent(transform, false);
                cueSource = cueObject.AddComponent<AudioSource>();
                cueFilter =
                    cueObject.AddComponent<AudioLowPassFilter>();
            }

            ConfigureCrowdSource(
                firstCrowdSource,
                ref firstCrowdFilter,
                firstCrowdRadius,
                FirstCrowdVolume * firstCrowdGain,
                3400f);
            ConfigureCrowdSource(
                secondCrowdSource,
                ref secondCrowdFilter,
                secondCrowdRadius,
                SecondCrowdVolume * secondCrowdGain,
                3100f);
            ConfigureCueSource();
            EnsureRuntimeClips();
        }

        private void EnsureCrowdSource(
            ref AudioSource source,
            ref AudioLowPassFilter filter,
            string objectName)
        {
            if (source != null)
            {
                return;
            }

            GameObject crowdObject = new GameObject(objectName);
            crowdObject.transform.SetParent(transform, false);
            source = crowdObject.AddComponent<AudioSource>();
            filter =
                crowdObject.AddComponent<AudioLowPassFilter>();
        }

        private void ConfigureCrowdSource(
            AudioSource source,
            ref AudioLowPassFilter filter,
            float maxDistance,
            float volume,
            float cutoffFrequency)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = 48f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = maxDistance;
            source.volume = volume;
            source.priority = 176;
            source.reverbZoneMix = 0.45f;
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);

            filter = filter != null
                ? filter
                : source.GetComponent<AudioLowPassFilter>();
            if (filter == null)
            {
                filter =
                    source.gameObject.AddComponent<
                        AudioLowPassFilter>();
            }

            filter.cutoffFrequency = cutoffFrequency;
            filter.lowpassResonanceQ = 1f;
        }

        private void ConfigureCueSource()
        {
            cueSource.playOnAwake = false;
            cueSource.loop = false;
            cueSource.spatialBlend = 1f;
            cueSource.dopplerLevel = 0f;
            cueSource.spread = 24f;
            cueSource.rolloffMode = AudioRolloffMode.Linear;
            cueSource.minDistance = 1.2f;
            cueSource.maxDistance = cueRadius;
            cueSource.volume = CueVolume * cueGain;
            cueSource.priority = 168;
            cueSource.reverbZoneMix = 0.58f;
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

            cueFilter.cutoffFrequency = 5600f;
            cueFilter.lowpassResonanceQ = 1f;
        }

        private void EnsureRuntimeClips()
        {
            if (firstCrowdClip == null)
            {
                firstCrowdClip = CreateClip(
                    "BarSoundscape_Crowd_A",
                    GenerateCrowdSamples(0));
            }

            if (secondCrowdClip == null)
            {
                secondCrowdClip = CreateClip(
                    "BarSoundscape_Crowd_B",
                    GenerateCrowdSamples(1));
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

            if (bottleSetDownClip == null)
            {
                bottleSetDownClip = CreateClip(
                    "BarSoundscape_BottleSetDown",
                    GenerateBottleSetDownSamples());
            }

            if (crowdReactionClip == null)
            {
                crowdReactionClip = CreateClip(
                    "BarSoundscape_CrowdReaction",
                    GenerateCrowdReactionSamples());
            }

            firstCrowdSource.clip = firstCrowdClip;
            secondCrowdSource.clip = secondCrowdClip;
        }

        private void StartCrowdLoops()
        {
            firstCrowdSource.Stop();
            secondCrowdSource.Stop();
            firstCrowdSource.timeSamples = 0;
            secondCrowdSource.timeSamples =
                secondCrowdClip.samples / 3;
            firstCrowdSource.Play();
            secondCrowdSource.Play();
        }

        private void StopAllSources()
        {
            firstCrowdSource?.Stop();
            secondCrowdSource?.Stop();
            cueSource?.Stop();
        }

        private static void ClearSourceClip(AudioSource source)
        {
            if (source != null)
            {
                source.clip = null;
            }
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

        private static float[] GenerateCrowdSamples(int variant)
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * CrowdLoopDuration);
            var samples = new float[sampleCount];
            bool firstVariant = variant == 0;
            float phaseOffset = firstVariant ? 0.31f : 2.17f;
            float lowFrequency = firstVariant ? 118f : 131f;
            float lowMidFrequency = firstVariant ? 164f : 177f;
            float midFrequency = firstVariant ? 221f : 239f;
            float upperMidFrequency = firstVariant ? 286f : 314f;
            float highFrequency = firstVariant ? 474f : 539f;
            float airFrequency = firstVariant ? 803f : 887f;
            float twoPi = Mathf.PI * 2f;

            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float loopPhase =
                    index / (float)sampleCount * twoPi;
                float slowBreath =
                    0.66f +
                    Mathf.Sin(
                        loopPhase *
                        (firstVariant ? 3f : 5f) +
                        phaseOffset) *
                    0.18f +
                    Mathf.Sin(
                        loopPhase *
                        (firstVariant ? 7f : 4f) +
                        phaseOffset * 0.47f) *
                    0.1f;
                float lowVoices =
                    Mathf.Sin(
                        twoPi * lowFrequency * time +
                        phaseOffset) *
                    0.28f +
                    Mathf.Sin(
                        twoPi * lowMidFrequency * time +
                        phaseOffset * 1.7f) *
                    0.22f;
                float middleVoices =
                    Mathf.Sin(
                        twoPi * midFrequency * time +
                        phaseOffset * 2.3f) *
                    0.2f +
                    Mathf.Sin(
                        twoPi * upperMidFrequency * time +
                        phaseOffset * 3.1f) *
                    0.15f;
                float upperMurmur =
                    Mathf.Sin(
                        twoPi * highFrequency * time +
                        phaseOffset * 0.8f) *
                    0.095f +
                    Mathf.Sin(
                        twoPi * airFrequency * time +
                        phaseOffset * 1.3f) *
                    0.055f;
                float conversationalPulse =
                    0.72f +
                    Mathf.Sin(
                        loopPhase *
                        (firstVariant ? 11f : 13f) +
                        phaseOffset) *
                    0.19f;
                float sample =
                    lowVoices * slowBreath +
                    middleVoices * conversationalPulse +
                    upperMurmur *
                    (0.78f + slowBreath * 0.22f);
                samples[index] = Quantize(sample * 0.31f);
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

        private static float[] GenerateBottleSetDownSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * BottleSetDownDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x424F5454u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float impactEnvelope = Mathf.Exp(-normalized * 18f);
                float ringEnvelope = Mathf.Exp(-normalized * 7f);
                float noise = NextNoise(ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.24f;
                float body =
                    Mathf.Sin(2f * Mathf.PI * 108f * time) * 0.54f +
                    Mathf.Sin(2f * Mathf.PI * 184f * time) * 0.24f;
                float glassRing =
                    Mathf.Sin(2f * Mathf.PI * 936f * time) *
                    ringEnvelope *
                    0.16f;
                samples[index] = Quantize(
                    (body + filteredNoise * 0.32f) *
                    impactEnvelope +
                    glassRing);
            }

            return samples;
        }

        private static float[] GenerateCrowdReactionSamples()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * CrowdReactionDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x43524F57u;
            float filteredNoise = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float normalized = index / (float)sampleCount;
                float riseAndFall = Mathf.Pow(
                    Mathf.Sin(Mathf.PI * normalized),
                    0.72f);
                float laughPulse =
                    0.62f +
                    Mathf.Abs(
                        Mathf.Sin(
                            2f * Mathf.PI * 5.2f * time)) *
                    0.38f;
                float voices =
                    Mathf.Sin(2f * Mathf.PI * 132f * time) * 0.24f +
                    Mathf.Sin(2f * Mathf.PI * 187f * time + 0.8f) *
                    0.21f +
                    Mathf.Sin(2f * Mathf.PI * 251f * time + 2.1f) *
                    0.17f +
                    Mathf.Sin(2f * Mathf.PI * 347f * time + 1.4f) *
                    0.12f +
                    Mathf.Sin(2f * Mathf.PI * 518f * time + 2.8f) *
                    0.065f;
                float noise = NextNoise(ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.08f;
                samples[index] = Quantize(
                    (voices * laughPulse +
                     filteredNoise * 0.075f) *
                    riseAndFall *
                    0.58f);
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
