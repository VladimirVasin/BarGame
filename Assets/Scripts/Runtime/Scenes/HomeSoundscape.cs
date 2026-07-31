using System;
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
        public const int OwnedSourceCount = 4;
        public const int RuntimeClipCount = 7;

        public const float ClosedRefrigeratorVolume = 0.095f;
        public const float OpenRefrigeratorVolume = 0.122f;
        public const float ClosedRefrigeratorCutoff = 2600f;
        public const float OpenRefrigeratorCutoff = 3850f;

        private const float BalconyVolume = 0.080f;
        private const float CueVolume = 0.105f;
        private const float RefrigeratorRadius = 9f;
        private const float BalconyRadius = 11f;
        private const float CueRadius = 8f;
        private const float HalfPi = Mathf.PI * 0.5f;
        private const double ScheduledLoopLeadSeconds = 0.02d;

        [SerializeField] private AudioSource refrigeratorSource;
        [SerializeField] private AudioSource openRefrigeratorSource;
        [SerializeField] private AudioSource balconySource;
        [SerializeField] private AudioSource rareCueSource;
        [SerializeField] private AudioLowPassFilter refrigeratorFilter;
        [SerializeField]
        private AudioLowPassFilter openRefrigeratorFilter;
        [SerializeField] private AudioLowPassFilter balconyFilter;
        [SerializeField] private AudioLowPassFilter rareCueFilter;

        private AudioClip refrigeratorClip;
        private AudioClip openRefrigeratorClip;
        private AudioClip balconyClip;
        private AudioClip softWoodClip;
        private AudioClip radiatorTickClip;
        private AudioClip radioMurmurClip;
        private AudioClip bathroomDetailClip;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private float refrigeratorDoorOpenAmount;
        private HomeSoundscapeAnchors anchors;

        public bool IsInitialized { get; private set; }
        public bool HasPlayedCue { get; private set; }
        public HomeSoundscapeCue LastPlayedCue { get; private set; }
        public Vector3 LastPlayedPosition { get; private set; }
        public int DeterministicSeed => deterministicSeed;
        public int CueSequence => cueSequence;
        public float SecondsUntilNextCue => secondsUntilNextCue;
        public HomeSoundscapeAnchors Anchors => anchors;
        public AudioSource ClosedRefrigeratorSource =>
            refrigeratorSource;
        public AudioSource OpenRefrigeratorSource =>
            openRefrigeratorSource;
        public AudioSource RefrigeratorSource => refrigeratorSource;
        public AudioSource BalconySource => balconySource;
        public AudioSource RareCueSource => rareCueSource;
        public AudioClip ClosedRefrigeratorClip => refrigeratorClip;
        public AudioClip OpenRefrigeratorClip =>
            openRefrigeratorClip;
        public AudioClip RefrigeratorClip => refrigeratorClip;
        public AudioClip BalconyClip => balconyClip;
        public AudioClip SoftWoodClip => softWoodClip;
        public AudioClip RadiatorTickClip => radiatorTickClip;
        public AudioClip RadioMurmurClip => radioMurmurClip;
        public AudioClip BathroomDetailClip => bathroomDetailClip;
        public float RefrigeratorDoorOpenAmount =>
            refrigeratorDoorOpenAmount;
        public float ClosedRefrigeratorMixWeight =>
            Mathf.Clamp01(
                Mathf.Cos(refrigeratorDoorOpenAmount * HalfPi));
        public float OpenRefrigeratorMixWeight =>
            Mathf.Clamp01(
                Mathf.Sin(refrigeratorDoorOpenAmount * HalfPi));

        public void SetRefrigeratorDoorOpenAmount(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "Refrigerator door openness must be finite.");
            }

            refrigeratorDoorOpenAmount = Mathf.Clamp01(amount);
            ApplyRefrigeratorDoorAudio();
        }

        public void Initialize(
            int seed,
            HomeSoundscapeAnchors worldAnchors)
        {
            refrigeratorDoorOpenAmount = 0f;
            EnsureRuntimeObjects();
            deterministicSeed = seed;
            anchors = worldAnchors;
            refrigeratorSource.transform.position =
                anchors.Refrigerator;
            openRefrigeratorSource.transform.position =
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
            openRefrigeratorSource.Stop();
            balconySource.Stop();
            rareCueSource.Stop();
            rareCueSource.clip = null;
            if (isActiveAndEnabled)
            {
                StartSynchronizedRefrigeratorLoops();
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
                openRefrigeratorSource != null &&
                (!refrigeratorSource.isPlaying ||
                 !openRefrigeratorSource.isPlaying))
            {
                StartSynchronizedRefrigeratorLoops();
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
            openRefrigeratorSource?.Stop();
            balconySource?.Stop();
            rareCueSource?.Stop();
        }

        private void OnDestroy()
        {
            StopAndClear(refrigeratorSource);
            StopAndClear(openRefrigeratorSource);
            StopAndClear(balconySource);
            StopAndClear(rareCueSource);

            DestroyRuntimeClip(ref refrigeratorClip);
            DestroyRuntimeClip(ref openRefrigeratorClip);
            DestroyRuntimeClip(ref balconyClip);
            DestroyRuntimeClip(ref softWoodClip);
            DestroyRuntimeClip(ref radiatorTickClip);
            DestroyRuntimeClip(ref radioMurmurClip);
            DestroyRuntimeClip(ref bathroomDetailClip);
            DestroyOwnedSource(refrigeratorSource);
            DestroyOwnedSource(openRefrigeratorSource);
            DestroyOwnedSource(balconySource);
            DestroyOwnedSource(rareCueSource);

            refrigeratorSource = null;
            openRefrigeratorSource = null;
            balconySource = null;
            rareCueSource = null;
            refrigeratorFilter = null;
            openRefrigeratorFilter = null;
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
                    "Spatial Refrigerator Closed",
                    out refrigeratorFilter);
            }

            if (openRefrigeratorSource == null)
            {
                openRefrigeratorSource = CreateOwnedSource(
                    "Spatial Refrigerator Open",
                    out openRefrigeratorFilter);
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
            ConfigureOpenRefrigeratorSource();
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
                ClosedRefrigeratorVolume,
                1.35f,
                RefrigeratorRadius,
                175,
                30f);
            refrigeratorFilter = EnsureFilter(
                refrigeratorSource,
                refrigeratorFilter);
            refrigeratorFilter.cutoffFrequency =
                ClosedRefrigeratorCutoff;
            refrigeratorFilter.lowpassResonanceQ = 1f;
            ApplyRefrigeratorDoorAudio();
        }

        private void ConfigureOpenRefrigeratorSource()
        {
            ConfigureSpatialSource(
                openRefrigeratorSource,
                true,
                0f,
                1.35f,
                RefrigeratorRadius,
                175,
                30f);
            openRefrigeratorFilter = EnsureFilter(
                openRefrigeratorSource,
                openRefrigeratorFilter);
            openRefrigeratorFilter.cutoffFrequency =
                OpenRefrigeratorCutoff;
            openRefrigeratorFilter.lowpassResonanceQ = 1f;
            ApplyRefrigeratorDoorAudio();
        }

        private void ApplyRefrigeratorDoorAudio()
        {
            if (refrigeratorSource != null)
            {
                refrigeratorSource.volume =
                    ClosedRefrigeratorVolume *
                    ClosedRefrigeratorMixWeight;
            }

            if (openRefrigeratorSource != null)
            {
                openRefrigeratorSource.volume =
                    OpenRefrigeratorVolume *
                    OpenRefrigeratorMixWeight;
            }
        }

        private void StartSynchronizedRefrigeratorLoops()
        {
            if (refrigeratorSource == null ||
                openRefrigeratorSource == null)
            {
                return;
            }

            refrigeratorSource.Stop();
            openRefrigeratorSource.Stop();
            refrigeratorSource.timeSamples = 0;
            openRefrigeratorSource.timeSamples = 0;
            double startTime =
                AudioSettings.dspTime +
                ScheduledLoopLeadSeconds;
            refrigeratorSource.PlayScheduled(startTime);
            openRefrigeratorSource.PlayScheduled(startTime);
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
                    "HomeSoundscape_RefrigeratorClosed",
                    HomeSoundscapeSynthesis
                        .GenerateClosedRefrigeratorLoopSamples());
            }

            if (openRefrigeratorClip == null)
            {
                openRefrigeratorClip = CreateClip(
                    "HomeSoundscape_RefrigeratorOpen",
                    HomeSoundscapeSynthesis
                        .GenerateOpenRefrigeratorLoopSamples());
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
            openRefrigeratorSource.clip = openRefrigeratorClip;
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
