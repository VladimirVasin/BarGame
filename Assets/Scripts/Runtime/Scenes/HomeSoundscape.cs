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
            Vector3 bathroom,
            Vector3 bathroomLight)
        {
            Refrigerator = refrigerator;
            BalconyNightAir = balconyNightAir;
            SoftWood = softWood;
            Radiator = radiator;
            Radio = radio;
            Bathroom = bathroom;
            BathroomLight = bathroomLight;
        }

        public Vector3 Refrigerator { get; }
        public Vector3 BalconyNightAir { get; }
        public Vector3 SoftWood { get; }
        public Vector3 Radiator { get; }
        public Vector3 Radio { get; }
        public Vector3 Bathroom { get; }
        public Vector3 BathroomLight { get; }
    }

    [DisallowMultipleComponent]
    public sealed class HomeSoundscape : MonoBehaviour
    {
        public const int SampleRate =
            HomeSoundscapeSynthesis.SampleRate;
        public const int OwnedSourceCount = 6;
        public const int RuntimeClipCount = 9;

        public const float RefrigeratorGain = 1.5848932f;
        public const float ClosedRefrigeratorVolume =
            0.095f * RefrigeratorGain;
        public const float OpenRefrigeratorVolume =
            0.122f * RefrigeratorGain;
        public const float ClosedRefrigeratorCutoff = 2600f;
        public const float OpenRefrigeratorCutoff = 3850f;
        public const float BathroomLightCrackleVolume = 0.18f;
        public const float BathroomLightCrackleCutoff = 5200f;
        public const float ShowerWaterVolume = 0.145f;
        public const float ShowerWaterClosedCutoff = 1400f;
        public const float ShowerWaterOpenCutoff = 4600f;

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
        [SerializeField] private AudioSource bathroomLightCrackleSource;
        [SerializeField] private AudioSource showerWaterSource;
        [SerializeField] private AudioLowPassFilter refrigeratorFilter;
        [SerializeField]
        private AudioLowPassFilter openRefrigeratorFilter;
        [SerializeField] private AudioLowPassFilter balconyFilter;
        [SerializeField] private AudioLowPassFilter rareCueFilter;
        [SerializeField]
        private AudioLowPassFilter bathroomLightCrackleFilter;
        [SerializeField] private AudioLowPassFilter showerWaterFilter;

        private AudioClip refrigeratorClip;
        private AudioClip openRefrigeratorClip;
        private AudioClip balconyClip;
        private AudioClip softWoodClip;
        private AudioClip radiatorTickClip;
        private AudioClip radioMurmurClip;
        private AudioClip bathroomDetailClip;
        private AudioClip bathroomLightCrackleClip;
        private AudioClip showerWaterClip;
        private HomeBathroomLightFlicker bathroomFlicker;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private float refrigeratorDoorOpenAmount;
        private float showerWaterAmount;
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
        public AudioSource BathroomLightCrackleSource =>
            bathroomLightCrackleSource;
        public AudioSource ShowerWaterSource => showerWaterSource;
        public AudioClip ClosedRefrigeratorClip => refrigeratorClip;
        public AudioClip OpenRefrigeratorClip =>
            openRefrigeratorClip;
        public AudioClip RefrigeratorClip => refrigeratorClip;
        public AudioClip BalconyClip => balconyClip;
        public AudioClip SoftWoodClip => softWoodClip;
        public AudioClip RadiatorTickClip => radiatorTickClip;
        public AudioClip RadioMurmurClip => radioMurmurClip;
        public AudioClip BathroomDetailClip => bathroomDetailClip;
        public AudioClip BathroomLightCrackleClip =>
            bathroomLightCrackleClip;
        public AudioClip ShowerWaterClip => showerWaterClip;
        public float ShowerWaterAmount => showerWaterAmount;
        public HomeBathroomLightFlicker BoundBathroomFlicker =>
            bathroomFlicker;
        public int BathroomLightCracklePlayCount { get; private set; }
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

        /// <summary>
        /// The running-shower loop. Zero keeps the source silent and
        /// stopped; anything above fades the hiss in and opens its
        /// low-pass, the refrigerator-door crossfade idiom.
        /// </summary>
        public void SetShowerWaterAmount(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "Shower water amount must be finite.");
            }

            showerWaterAmount = Mathf.Clamp01(amount);
            ApplyShowerWaterAudio();
        }

        public void Initialize(
            int seed,
            HomeSoundscapeAnchors worldAnchors)
        {
            refrigeratorDoorOpenAmount = 0f;
            showerWaterAmount = 0f;
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
            bathroomLightCrackleSource.transform.position =
                anchors.BathroomLight;
            showerWaterSource.transform.position = anchors.Bathroom;
            cueSequence = 0;
            HasPlayedCue = false;
            LastPlayedCue = default;
            LastPlayedPosition = rareCueSource.transform.position;
            BathroomLightCracklePlayCount = 0;
            secondsUntilNextCue =
                HomeSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
            IsInitialized = true;

            refrigeratorSource.Stop();
            openRefrigeratorSource.Stop();
            balconySource.Stop();
            rareCueSource.Stop();
            bathroomLightCrackleSource.Stop();
            showerWaterSource.Stop();
            ApplyShowerWaterAudio();
            rareCueSource.clip = null;
            if (isActiveAndEnabled)
            {
                StartSynchronizedRefrigeratorLoops();
                balconySource.Play();
            }
        }

        public void BindBathroomFlicker(
            HomeBathroomLightFlicker flicker)
        {
            if (flicker == null)
            {
                throw new ArgumentNullException(nameof(flicker));
            }

            if (bathroomFlicker == flicker)
            {
                return;
            }

            UnbindBathroomFlicker();
            bathroomFlicker = flicker;
            bathroomFlicker.AppliedFactorChanged +=
                HandleBathroomFlickerFactorChanged;
        }

        public void UnbindBathroomFlicker()
        {
            if (bathroomFlicker != null)
            {
                bathroomFlicker.AppliedFactorChanged -=
                    HandleBathroomFlickerFactorChanged;
            }

            bathroomFlicker = null;
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
            showerWaterSource?.Stop();
            balconySource?.Stop();
            rareCueSource?.Stop();
            bathroomLightCrackleSource?.Stop();
        }

        private void OnDestroy()
        {
            UnbindBathroomFlicker();
            StopAndClear(refrigeratorSource);
            StopAndClear(openRefrigeratorSource);
            StopAndClear(balconySource);
            StopAndClear(rareCueSource);
            StopAndClear(bathroomLightCrackleSource);
            StopAndClear(showerWaterSource);

            DestroyRuntimeClip(ref refrigeratorClip);
            DestroyRuntimeClip(ref openRefrigeratorClip);
            DestroyRuntimeClip(ref balconyClip);
            DestroyRuntimeClip(ref softWoodClip);
            DestroyRuntimeClip(ref radiatorTickClip);
            DestroyRuntimeClip(ref radioMurmurClip);
            DestroyRuntimeClip(ref bathroomDetailClip);
            DestroyRuntimeClip(ref bathroomLightCrackleClip);
            DestroyRuntimeClip(ref showerWaterClip);
            DestroyOwnedSource(refrigeratorSource);
            DestroyOwnedSource(openRefrigeratorSource);
            DestroyOwnedSource(balconySource);
            DestroyOwnedSource(rareCueSource);
            DestroyOwnedSource(bathroomLightCrackleSource);
            DestroyOwnedSource(showerWaterSource);

            refrigeratorSource = null;
            openRefrigeratorSource = null;
            balconySource = null;
            rareCueSource = null;
            bathroomLightCrackleSource = null;
            showerWaterSource = null;
            refrigeratorFilter = null;
            openRefrigeratorFilter = null;
            balconyFilter = null;
            rareCueFilter = null;
            bathroomLightCrackleFilter = null;
            showerWaterFilter = null;
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

            if (bathroomLightCrackleSource == null)
            {
                bathroomLightCrackleSource = CreateOwnedSource(
                    "Spatial Bathroom Light Crackle",
                    out bathroomLightCrackleFilter);
            }

            if (showerWaterSource == null)
            {
                showerWaterSource = CreateOwnedSource(
                    "Spatial Shower Water",
                    out showerWaterFilter);
            }

            ConfigureRefrigeratorSource();
            ConfigureOpenRefrigeratorSource();
            ConfigureBalconySource();
            ConfigureRareCueSource();
            ConfigureBathroomLightCrackleSource();
            ConfigureShowerWaterSource();
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

        private void ConfigureBathroomLightCrackleSource()
        {
            ConfigureSpatialSource(
                bathroomLightCrackleSource,
                false,
                BathroomLightCrackleVolume,
                1.05f,
                8f,
                168,
                0f);
            bathroomLightCrackleFilter = EnsureFilter(
                bathroomLightCrackleSource,
                bathroomLightCrackleFilter);
            bathroomLightCrackleFilter.cutoffFrequency =
                BathroomLightCrackleCutoff;
            bathroomLightCrackleFilter.lowpassResonanceQ = 1f;
        }

        private void ConfigureShowerWaterSource()
        {
            ConfigureSpatialSource(
                showerWaterSource,
                true,
                0f,
                1.20f,
                8f,
                172,
                20f);
            showerWaterFilter = EnsureFilter(
                showerWaterSource,
                showerWaterFilter);
            showerWaterFilter.cutoffFrequency =
                ShowerWaterClosedCutoff;
            showerWaterFilter.lowpassResonanceQ = 1f;
            ApplyShowerWaterAudio();
        }

        private void ApplyShowerWaterAudio()
        {
            if (showerWaterSource == null)
            {
                return;
            }

            showerWaterSource.volume =
                ShowerWaterVolume * showerWaterAmount;
            if (showerWaterFilter != null)
            {
                showerWaterFilter.cutoffFrequency = Mathf.Lerp(
                    ShowerWaterClosedCutoff,
                    ShowerWaterOpenCutoff,
                    showerWaterAmount);
            }

            bool shouldPlay =
                showerWaterAmount > 0.0001f &&
                IsInitialized &&
                isActiveAndEnabled;
            if (shouldPlay && !showerWaterSource.isPlaying)
            {
                showerWaterSource.Play();
            }
            else if (!shouldPlay && showerWaterSource.isPlaying)
            {
                showerWaterSource.Stop();
            }
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

            if (bathroomLightCrackleClip == null)
            {
                bathroomLightCrackleClip = CreateClip(
                    "HomeSoundscape_BathroomLightCrackle",
                    HomeSoundscapeSynthesis
                        .GenerateBathroomLightCrackleSamples());
            }

            if (showerWaterClip == null)
            {
                showerWaterClip = CreateClip(
                    "HomeSoundscape_ShowerWater",
                    HomeSoundscapeSynthesis
                        .GenerateShowerWaterLoopSamples());
            }

            refrigeratorSource.clip = refrigeratorClip;
            openRefrigeratorSource.clip = openRefrigeratorClip;
            balconySource.clip = balconyClip;
            showerWaterSource.clip = showerWaterClip;
        }

        private void HandleBathroomFlickerFactorChanged(
            float appliedFactor)
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                bathroomLightCrackleSource == null ||
                bathroomLightCrackleClip == null)
            {
                return;
            }

            bathroomLightCrackleSource.PlayOneShot(
                bathroomLightCrackleClip);
            BathroomLightCracklePlayCount++;
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
