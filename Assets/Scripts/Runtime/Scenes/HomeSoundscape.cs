using UnityEngine;

namespace BarPromenade
{
    public readonly struct HomeSoundscapeAnchors
    {
        public HomeSoundscapeAnchors(
            Vector3 refrigerator,
            Vector3 balconyNightAir,
            Vector3 softWood,
            Vector3 radiator,
            Vector3 radio,
            Vector3 bathroom)
        {
            Refrigerator = refrigerator;
            BalconyNightAir = balconyNightAir;
            SoftWood = softWood;
            Radiator = radiator;
            Radio = radio;
            Bathroom = bathroom;
        }

        public Vector3 Refrigerator { get; }
        public Vector3 BalconyNightAir { get; }
        public Vector3 SoftWood { get; }
        public Vector3 Radiator { get; }
        public Vector3 Radio { get; }
        public Vector3 Bathroom { get; }
    }

    [DisallowMultipleComponent]
    public sealed class HomeSoundscape : MonoBehaviour
    {
        public const int SampleRate =
            HomeSoundscapeSynthesis.SampleRate;
        public const int OwnedSourceCount = 3;
        public const int RuntimeClipCount = 6;

        private const float RefrigeratorVolume = 0.095f;
        private const float BalconyVolume = 0.080f;
        private const float CueVolume = 0.105f;
        private const float RefrigeratorRadius = 9f;
        private const float BalconyRadius = 11f;
        private const float CueRadius = 8f;

        [SerializeField] private AudioSource refrigeratorSource;
        [SerializeField] private AudioSource balconySource;
        [SerializeField] private AudioSource rareCueSource;
        [SerializeField] private AudioLowPassFilter refrigeratorFilter;
        [SerializeField] private AudioLowPassFilter balconyFilter;
        [SerializeField] private AudioLowPassFilter rareCueFilter;

        private AudioClip refrigeratorClip;
        private AudioClip balconyClip;
        private AudioClip softWoodClip;
        private AudioClip radiatorTickClip;
        private AudioClip radioMurmurClip;
        private AudioClip bathroomDetailClip;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private HomeSoundscapeAnchors anchors;

        public bool IsInitialized { get; private set; }
        public bool HasPlayedCue { get; private set; }
        public HomeSoundscapeCue LastPlayedCue { get; private set; }
        public Vector3 LastPlayedPosition { get; private set; }
        public int DeterministicSeed => deterministicSeed;
        public int CueSequence => cueSequence;
        public float SecondsUntilNextCue => secondsUntilNextCue;
        public HomeSoundscapeAnchors Anchors => anchors;
        public AudioSource RefrigeratorSource => refrigeratorSource;
        public AudioSource BalconySource => balconySource;
        public AudioSource RareCueSource => rareCueSource;
        public AudioClip RefrigeratorClip => refrigeratorClip;
        public AudioClip BalconyClip => balconyClip;
        public AudioClip SoftWoodClip => softWoodClip;
        public AudioClip RadiatorTickClip => radiatorTickClip;
        public AudioClip RadioMurmurClip => radioMurmurClip;
        public AudioClip BathroomDetailClip => bathroomDetailClip;

        public void Initialize(
            int seed,
            HomeSoundscapeAnchors worldAnchors)
        {
            EnsureRuntimeObjects();
            deterministicSeed = seed;
            anchors = worldAnchors;
            refrigeratorSource.transform.position =
                anchors.Refrigerator;
            balconySource.transform.position =
                anchors.BalconyNightAir;
            rareCueSource.transform.position = anchors.SoftWood;
            cueSequence = 0;
            HasPlayedCue = false;
            LastPlayedCue = default;
            LastPlayedPosition = rareCueSource.transform.position;
            secondsUntilNextCue =
                HomeSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
            IsInitialized = true;

            refrigeratorSource.Stop();
            balconySource.Stop();
            rareCueSource.Stop();
            rareCueSource.clip = null;
            if (isActiveAndEnabled)
            {
                refrigeratorSource.Play();
                balconySource.Play();
            }
        }

        public Vector3 GetCuePosition(HomeSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case HomeSoundscapeCueKind.SoftWood:
                    return anchors.SoftWood;
                case HomeSoundscapeCueKind.RadiatorTick:
                    return anchors.Radiator;
                case HomeSoundscapeCueKind.RadioMurmur:
                    return anchors.Radio;
                default:
                    return anchors.Bathroom;
            }
        }

        public void AdvanceSoundscape(float unscaledDeltaTime)
        {
            if (!IsInitialized ||
                float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
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

            if (refrigeratorSource != null &&
                !refrigeratorSource.isPlaying)
            {
                refrigeratorSource.Play();
            }

            if (balconySource != null &&
                !balconySource.isPlaying)
            {
                balconySource.Play();
            }
        }

        private void OnDisable()
        {
            refrigeratorSource?.Stop();
            balconySource?.Stop();
            rareCueSource?.Stop();
        }

        private void OnDestroy()
        {
            StopAndClear(refrigeratorSource);
            StopAndClear(balconySource);
            StopAndClear(rareCueSource);

            DestroyRuntimeClip(ref refrigeratorClip);
            DestroyRuntimeClip(ref balconyClip);
            DestroyRuntimeClip(ref softWoodClip);
            DestroyRuntimeClip(ref radiatorTickClip);
            DestroyRuntimeClip(ref radioMurmurClip);
            DestroyRuntimeClip(ref bathroomDetailClip);
            DestroyOwnedSource(refrigeratorSource);
            DestroyOwnedSource(balconySource);
            DestroyOwnedSource(rareCueSource);

            refrigeratorSource = null;
            balconySource = null;
            rareCueSource = null;
            refrigeratorFilter = null;
            balconyFilter = null;
            rareCueFilter = null;
            IsInitialized = false;
        }

        private void PlayNextCue()
        {
            HomeSoundscapeCue cue =
                HomeSoundscapeSchedule.GetCue(
                    deterministicSeed,
                    cueSequence);
            cueSequence++;
            HasPlayedCue = true;
            LastPlayedCue = cue;
            LastPlayedPosition = GetCuePosition(cue.Kind);

            rareCueSource.Stop();
            rareCueSource.transform.position = LastPlayedPosition;
            rareCueSource.clip = GetCueClip(cue.Kind);
            rareCueSource.pitch = cue.Pitch;
            rareCueSource.volume =
                CueVolume * cue.VolumeScale;
            rareCueSource.Play();

            secondsUntilNextCue =
                HomeSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
        }

        private AudioClip GetCueClip(HomeSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case HomeSoundscapeCueKind.SoftWood:
                    return softWoodClip;
                case HomeSoundscapeCueKind.RadiatorTick:
                    return radiatorTickClip;
                case HomeSoundscapeCueKind.RadioMurmur:
                    return radioMurmurClip;
                default:
                    return bathroomDetailClip;
            }
        }

        private void EnsureRuntimeObjects()
        {
            if (refrigeratorSource == null)
            {
                refrigeratorSource = CreateOwnedSource(
                    "Spatial Refrigerator",
                    out refrigeratorFilter);
            }

            if (balconySource == null)
            {
                balconySource = CreateOwnedSource(
                    "Spatial Balcony Night Air",
                    out balconyFilter);
            }

            if (rareCueSource == null)
            {
                rareCueSource = CreateOwnedSource(
                    "Rare Home Cues",
                    out rareCueFilter);
            }

            ConfigureRefrigeratorSource();
            ConfigureBalconySource();
            ConfigureRareCueSource();
            EnsureRuntimeClips();
        }

        private AudioSource CreateOwnedSource(
            string objectName,
            out AudioLowPassFilter filter)
        {
            var sourceObject = new GameObject(objectName);
            sourceObject.transform.SetParent(transform, false);
            AudioSource source =
                sourceObject.AddComponent<AudioSource>();
            filter =
                sourceObject.AddComponent<AudioLowPassFilter>();
            return source;
        }

        private void ConfigureRefrigeratorSource()
        {
            ConfigureSpatialSource(
                refrigeratorSource,
                true,
                RefrigeratorVolume,
                1.35f,
                RefrigeratorRadius,
                175,
                30f);
            refrigeratorFilter = EnsureFilter(
                refrigeratorSource,
                refrigeratorFilter);
            refrigeratorFilter.cutoffFrequency = 2600f;
            refrigeratorFilter.lowpassResonanceQ = 1f;
        }

        private void ConfigureBalconySource()
        {
            ConfigureSpatialSource(
                balconySource,
                true,
                BalconyVolume,
                1.5f,
                BalconyRadius,
                178,
                54f);
            balconyFilter = EnsureFilter(
                balconySource,
                balconyFilter);
            balconyFilter.cutoffFrequency = 3450f;
            balconyFilter.lowpassResonanceQ = 1f;
        }

        private void ConfigureRareCueSource()
        {
            ConfigureSpatialSource(
                rareCueSource,
                false,
                CueVolume,
                1.15f,
                CueRadius,
                170,
                0f);
            rareCueFilter = EnsureFilter(
                rareCueSource,
                rareCueFilter);
            rareCueFilter.cutoffFrequency = 3150f;
            rareCueFilter.lowpassResonanceQ = 1f;
        }

        private static void ConfigureSpatialSource(
            AudioSource source,
            bool loop,
            float volume,
            float minDistance,
            float maxDistance,
            int priority,
            float spread)
        {
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.volume = volume;
            source.priority = priority;
            source.spread = spread;
            source.reverbZoneMix = 0.65f;
            GameAudioMixer.Route(
                source,
                GameAudioGroup.AmbienceDetails);
        }

        private static AudioLowPassFilter EnsureFilter(
            AudioSource source,
            AudioLowPassFilter current)
        {
            if (current != null)
            {
                return current;
            }

            AudioLowPassFilter filter =
                source.GetComponent<AudioLowPassFilter>();
            return filter != null
                ? filter
                : source.gameObject.AddComponent<
                    AudioLowPassFilter>();
        }

        private void EnsureRuntimeClips()
        {
            if (refrigeratorClip == null)
            {
                refrigeratorClip = CreateClip(
                    "HomeSoundscape_Refrigerator",
                    HomeSoundscapeSynthesis
                        .GenerateRefrigeratorLoopSamples());
            }

            if (balconyClip == null)
            {
                balconyClip = CreateClip(
                    "HomeSoundscape_BalconyNightAir",
                    HomeSoundscapeSynthesis
                        .GenerateBalconyNightAirLoopSamples());
            }

            if (softWoodClip == null)
            {
                softWoodClip = CreateCueClip(
                    HomeSoundscapeCueKind.SoftWood,
                    "HomeSoundscape_SoftWood");
            }

            if (radiatorTickClip == null)
            {
                radiatorTickClip = CreateCueClip(
                    HomeSoundscapeCueKind.RadiatorTick,
                    "HomeSoundscape_RadiatorTick");
            }

            if (radioMurmurClip == null)
            {
                radioMurmurClip = CreateCueClip(
                    HomeSoundscapeCueKind.RadioMurmur,
                    "HomeSoundscape_RadioMurmur");
            }

            if (bathroomDetailClip == null)
            {
                bathroomDetailClip = CreateCueClip(
                    HomeSoundscapeCueKind.BathroomDetail,
                    "HomeSoundscape_BathroomDetail");
            }

            refrigeratorSource.clip = refrigeratorClip;
            balconySource.clip = balconyClip;
        }

        private static AudioClip CreateCueClip(
            HomeSoundscapeCueKind kind,
            string clipName)
        {
            return CreateClip(
                clipName,
                HomeSoundscapeSynthesis
                    .GenerateCueSamples(kind));
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

        private static void StopAndClear(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
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
