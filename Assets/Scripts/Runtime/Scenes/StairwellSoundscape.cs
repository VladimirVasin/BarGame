using UnityEngine;

namespace BarPromenade
{
    public readonly struct StairwellSoundscapeAnchors
    {
        public StairwellSoundscapeAnchors(
            Vector3 ventilation,
            Vector3 electrical,
            Vector3 pipeKnock,
            Vector3 metalStress,
            Vector3 distantWater,
            Vector3 distantMovement)
        {
            Ventilation = ventilation;
            Electrical = electrical;
            PipeKnock = pipeKnock;
            MetalStress = metalStress;
            DistantWater = distantWater;
            DistantMovement = distantMovement;
        }

        public Vector3 Ventilation { get; }
        public Vector3 Electrical { get; }
        public Vector3 PipeKnock { get; }
        public Vector3 MetalStress { get; }
        public Vector3 DistantWater { get; }
        public Vector3 DistantMovement { get; }
    }

    [DisallowMultipleComponent]
    public sealed class StairwellSoundscape : MonoBehaviour
    {
        public const int SampleRate =
            StairwellSoundscapeSynthesis.SampleRate;
        public const int OwnedSourceCount = 3;
        public const int RuntimeClipCount = 6;
        public const int MaximumLabyrinthSwitchSources = 2;

        private const float VentilationVolume = 0.13f;
        private const float ElectricalVolume = 0.075f;
        private const float CueVolume = 0.17f;
        private const float VentilationRadius = 13f;
        private const float ElectricalRadius = 8.5f;
        private const float CueRadius = 15f;

        [SerializeField] private AudioSource ventilationSource;
        [SerializeField] private AudioSource electricalSource;
        [SerializeField] private AudioSource rareCueSource;
        [SerializeField] private AudioLowPassFilter ventilationFilter;
        [SerializeField] private AudioLowPassFilter electricalFilter;
        [SerializeField] private AudioLowPassFilter rareCueFilter;

        private AudioClip ventilationClip;
        private AudioClip electricalClip;
        private AudioClip pipeKnockClip;
        private AudioClip metalStressClip;
        private AudioClip distantWaterClip;
        private AudioClip distantMovementClip;
        private int deterministicSeed;
        private int cueSequence;
        private float secondsUntilNextCue;
        private StairwellSoundscapeAnchors anchors;
        private Transform mechanismFan, mechanismLamp, mechanismLift, mechanismFlap;
        private float nextMechanismCue;
        private bool hasMechanismAnchors, ownsMechanismCue;
        private Vector3 previousVentilationPosition, previousElectricalPosition;
        private float previousVentilationRange, previousElectricalRange;
        private AudioSource lampOnSource, lampOffSource;
        private AudioClip lampOnClip, lampOffClip;
        private Transform poweredLamp;
        private bool ownsLabyrinthLighting;
        public AudioSource LampOnSource => lampOnSource;
        public AudioSource LampOffSource => lampOffSource;
        public Transform PoweredLamp => poweredLamp;

        public bool IsInitialized { get; private set; }
        public bool HasPlayedCue { get; private set; }
        public StairwellSoundscapeCue LastPlayedCue { get; private set; }
        public Vector3 LastPlayedPosition { get; private set; }
        public int DeterministicSeed => deterministicSeed;
        public int CueSequence => cueSequence;
        public float SecondsUntilNextCue => secondsUntilNextCue;
        public StairwellSoundscapeAnchors Anchors => anchors;
        public AudioSource VentilationSource => ventilationSource;
        public AudioSource ElectricalSource => electricalSource;
        public AudioSource RareCueSource => rareCueSource;
        public AudioClip VentilationClip => ventilationClip;
        public AudioClip ElectricalClip => electricalClip;
        public AudioClip PipeKnockClip => pipeKnockClip;
        public AudioClip MetalStressClip => metalStressClip;
        public AudioClip DistantWaterClip => distantWaterClip;
        public AudioClip DistantMovementClip => distantMovementClip;

        public void PrepareLabyrinthLighting()
        {
            if (lampOnSource != null) return;
            lampOnSource = CreateOwnedSource("Industrial Lamp Contactor On", out AudioLowPassFilter onFilter);
            lampOffSource = CreateOwnedSource("Industrial Lamp Contactor Off", out AudioLowPassFilter offFilter);
            ConfigureSpatialSource(lampOnSource, false, .34f, 1.4f, 21f, 150, 0f);
            ConfigureSpatialSource(lampOffSource, false, .24f, 1.4f, 18f, 151, 0f);
            onFilter.cutoffFrequency = offFilter.cutoffFrequency = 4800f;
            lampOnClip = CreateClip("Stairwell_IndustrialLampOn", BuildLampContactorSamples(true));
            lampOffClip = CreateClip("Stairwell_IndustrialLampOff", BuildLampContactorSamples(false));
            lampOnSource.clip = lampOnClip;
            lampOffSource.clip = lampOffClip;
        }

        public void PlayLabyrinthLampCue(Transform lamp, bool turningOn)
        {
            if (lamp == null || !IsInitialized || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused) return;
            AudioSource source = turningOn ? lampOnSource : lampOffSource;
            if (source == null) return;
            source.transform.position = lamp.position;
            source.Play();
        }

        public void SetLabyrinthLighting(bool owned, Transform source, bool playCue, bool preserveContactorTail = false)
        {
            if (!IsInitialized || lampOnSource == null) return;
            bool changed = source != poweredLamp;
            if (changed && playCue && isActiveAndEnabled && !GameTimeScaleRuntime.IsPaused)
            {
                if (poweredLamp != null)
                {
                    lampOffSource.transform.position = poweredLamp.position;
                    lampOffSource.Play();
                }
                if (source != null)
                {
                    lampOnSource.transform.position = source.position;
                    lampOnSource.Play();
                }
            }
            ownsLabyrinthLighting = owned;
            poweredLamp = source;
            if (!playCue && source == null && !preserveContactorTail)
            {
                lampOnSource.Stop();
                lampOffSource.Stop();
            }
            if (!owned)
            {
                if (!playCue && !preserveContactorTail)
                {
                    lampOnSource.Stop();
                    lampOffSource.Stop();
                }
                electricalSource.transform.position = mechanismLamp != null ? mechanismLamp.position : anchors.Electrical;
                electricalSource.maxDistance = hasMechanismAnchors ? 15f : ElectricalRadius;
                if (isActiveAndEnabled && !electricalSource.isPlaying) electricalSource.Play();
                return;
            }
            if (source == null) electricalSource.Stop();
            else
            {
                electricalSource.transform.position = source.position;
                electricalSource.maxDistance = 12f;
                if (isActiveAndEnabled && !electricalSource.isPlaying) electricalSource.Play();
            }
        }

        private static float[] BuildLampContactorSamples(bool turningOn)
        {
            float duration = turningOn ? .82f : .48f;
            var samples = new float[Mathf.CeilToInt(SampleRate * duration)];
            uint noise = turningOn ? 0x4C414D50u : 0x4F46464Cu;
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                noise = unchecked(noise * 1664525u + 1013904223u);
                float random = (noise >> 8) / 8388607.5f - 1f;
                filtered += (random - filtered) * .28f;
                float attack = Mathf.Clamp01(t / .0025f);
                float impact = (filtered * .72f + Mathf.Sin(t * Mathf.PI * 2f * 83f) * .46f) * Mathf.Exp(-t * 38f);
                float body = (Mathf.Sin(t * Mathf.PI * 2f * 57f) * .28f +
                    Mathf.Sin(t * Mathf.PI * 2f * 171f) * .13f) * Mathf.Exp(-t * (turningOn ? 6f : 11f));
                float catchTime = Mathf.Max(0f, t - .055f);
                float latch = t < .055f ? 0f : filtered * .30f * Mathf.Exp(-catchTime * 65f);
                float tail = Mathf.Clamp01((duration - t) / .06f);
                samples[i] = Mathf.Clamp((impact + body + latch) * attack * tail, -.98f, .98f);
            }
            return samples;
        }

        public void ConfigureLabyrinthMechanisms(Transform fan, Transform lamp, Transform lift, Transform flap)
        {
            if (!hasMechanismAnchors)
            {
                previousVentilationPosition = ventilationSource.transform.position;
                previousElectricalPosition = electricalSource.transform.position;
                previousVentilationRange = ventilationSource.maxDistance;
                previousElectricalRange = electricalSource.maxDistance;
                hasMechanismAnchors = true;
            }
            mechanismFan = fan; mechanismLamp = lamp; mechanismLift = lift; mechanismFlap = flap;
            UpdateMechanismAnchors();
            ventilationSource.maxDistance = 42f;
            if (!ownsLabyrinthLighting) electricalSource.maxDistance = 15f;
        }

        public void ReleaseLabyrinthMechanisms()
        {
            if (!hasMechanismAnchors) return;
            hasMechanismAnchors = false;
            mechanismFan = mechanismLamp = mechanismLift = mechanismFlap = null;
            if (ventilationSource != null)
            {
                ventilationSource.transform.position = previousVentilationPosition;
                ventilationSource.maxDistance = previousVentilationRange;
            }
            if (electricalSource != null && !ownsLabyrinthLighting)
            {
                electricalSource.transform.position = previousElectricalPosition;
                electricalSource.maxDistance = previousElectricalRange;
            }
            if (ownsMechanismCue && rareCueSource != null) rareCueSource.Stop();
            ownsMechanismCue = false;
        }

        public void UpdateMechanismAnchors()
        {
            if (!IsInitialized) return;
            if (mechanismFan != null) ventilationSource.transform.position = mechanismFan.position;
            if (ownsLabyrinthLighting)
            {
                if (poweredLamp != null) electricalSource.transform.position = poweredLamp.position;
            }
            else if (mechanismLamp != null) electricalSource.transform.position = mechanismLamp.position;
        }

        public void TryPlayMechanismCue(Vector3 position, StairwellSoundscapeCueKind kind)
        {
            if (!isActiveAndEnabled || !IsInitialized || GameTimeScaleRuntime.IsPaused ||
                Time.time < nextMechanismCue || rareCueSource.isPlaying) return;
            nextMechanismCue = Time.time + 8f;
            rareCueSource.transform.position = position;
            rareCueSource.clip = GetCueClip(kind);
            rareCueSource.pitch = kind == StairwellSoundscapeCueKind.MetalStress ? .72f : .85f;
            rareCueSource.volume = CueVolume * .65f;
            rareCueSource.Play();
            ownsMechanismCue = true;
            secondsUntilNextCue = Mathf.Max(secondsUntilNextCue, 8f);
        }

        public void Initialize(
            int seed,
            StairwellSoundscapeAnchors worldAnchors)
        {
            EnsureRuntimeObjects();
            deterministicSeed = seed;
            anchors = worldAnchors;
            ventilationSource.transform.position =
                anchors.Ventilation;
            electricalSource.transform.position =
                anchors.Electrical;
            rareCueSource.transform.position =
                anchors.PipeKnock;
            cueSequence = 0;
            HasPlayedCue = false;
            LastPlayedCue = default;
            LastPlayedPosition = rareCueSource.transform.position;
            secondsUntilNextCue =
                StairwellSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
            IsInitialized = true;

            ventilationSource.Stop();
            electricalSource.Stop();
            rareCueSource.Stop();
            rareCueSource.clip = null;
            if (isActiveAndEnabled)
            {
                ventilationSource.Play();
                electricalSource.Play();
            }
        }

        public Vector3 GetCuePosition(
            StairwellSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case StairwellSoundscapeCueKind.PipeKnock:
                    return anchors.PipeKnock;
                case StairwellSoundscapeCueKind.MetalStress:
                    return mechanismLift != null ? mechanismLift.position : anchors.MetalStress;
                case StairwellSoundscapeCueKind.DistantWater:
                    return anchors.DistantWater;
                default:
                    return mechanismFlap != null ? mechanismFlap.position : anchors.DistantMovement;
            }
        }

        public void AdvanceSoundscape(float unscaledDeltaTime)
        {
            if (!IsInitialized || GameTimeScaleRuntime.IsPaused ||
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

            if (ventilationSource != null &&
                !ventilationSource.isPlaying)
            {
                ventilationSource.Play();
            }

            if ((!ownsLabyrinthLighting || poweredLamp != null) && electricalSource != null &&
                !electricalSource.isPlaying)
            {
                electricalSource.Play();
            }
        }

        private void OnDisable()
        {
            ventilationSource?.Stop();
            electricalSource?.Stop();
            rareCueSource?.Stop();
            lampOnSource?.Stop();
            lampOffSource?.Stop();
        }

        private void OnDestroy()
        {
            StopAndClear(ventilationSource);
            StopAndClear(electricalSource);
            StopAndClear(rareCueSource);
            StopAndClear(lampOnSource);
            StopAndClear(lampOffSource);

            DestroyRuntimeClip(ref ventilationClip);
            DestroyRuntimeClip(ref electricalClip);
            DestroyRuntimeClip(ref pipeKnockClip);
            DestroyRuntimeClip(ref metalStressClip);
            DestroyRuntimeClip(ref distantWaterClip);
            DestroyRuntimeClip(ref distantMovementClip);
            DestroyRuntimeClip(ref lampOnClip);
            DestroyRuntimeClip(ref lampOffClip);
            DestroyOwnedSource(ventilationSource);
            DestroyOwnedSource(electricalSource);
            DestroyOwnedSource(rareCueSource);
            DestroyOwnedSource(lampOnSource);
            DestroyOwnedSource(lampOffSource);

            ventilationSource = null;
            electricalSource = null;
            rareCueSource = null;
            ventilationFilter = null;
            electricalFilter = null;
            rareCueFilter = null;
            IsInitialized = false;
        }

        private void PlayNextCue()
        {
            ownsMechanismCue = false;
            StairwellSoundscapeCue cue =
                StairwellSoundscapeSchedule.GetCue(
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
                StairwellSoundscapeSchedule
                    .GetCue(deterministicSeed, cueSequence)
                    .DelaySeconds;
        }

        private AudioClip GetCueClip(
            StairwellSoundscapeCueKind kind)
        {
            switch (kind)
            {
                case StairwellSoundscapeCueKind.PipeKnock:
                    return pipeKnockClip;
                case StairwellSoundscapeCueKind.MetalStress:
                    return metalStressClip;
                case StairwellSoundscapeCueKind.DistantWater:
                    return distantWaterClip;
                default:
                    return distantMovementClip;
            }
        }

        private void EnsureRuntimeObjects()
        {
            if (ventilationSource == null)
            {
                ventilationSource = CreateOwnedSource(
                    "Spatial Ventilation",
                    out ventilationFilter);
            }

            if (electricalSource == null)
            {
                electricalSource = CreateOwnedSource(
                    "Spatial Electrical Buzz",
                    out electricalFilter);
            }

            if (rareCueSource == null)
            {
                rareCueSource = CreateOwnedSource(
                    "Rare Stairwell Cues",
                    out rareCueFilter);
            }

            ConfigureVentilationSource();
            ConfigureElectricalSource();
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

        private void ConfigureVentilationSource()
        {
            ConfigureSpatialSource(
                ventilationSource,
                true,
                VentilationVolume,
                1.5f,
                VentilationRadius,
                174,
                42f);
            ventilationFilter = EnsureFilter(
                ventilationSource,
                ventilationFilter);
            ventilationFilter.cutoffFrequency = 2350f;
            ventilationFilter.lowpassResonanceQ = 1.1f;
        }

        private void ConfigureElectricalSource()
        {
            ConfigureSpatialSource(
                electricalSource,
                true,
                ElectricalVolume,
                1.1f,
                ElectricalRadius,
                172,
                16f);
            electricalFilter = EnsureFilter(
                electricalSource,
                electricalFilter);
            electricalFilter.cutoffFrequency = 3900f;
            electricalFilter.lowpassResonanceQ = 1.25f;
        }

        private void ConfigureRareCueSource()
        {
            ConfigureSpatialSource(
                rareCueSource,
                false,
                CueVolume,
                1.2f,
                CueRadius,
                166,
                0f);
            rareCueFilter = EnsureFilter(
                rareCueSource,
                rareCueFilter);
            rareCueFilter.cutoffFrequency = 2850f;
            rareCueFilter.lowpassResonanceQ = 1.05f;
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
            source.reverbZoneMix = 1f;
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
            if (ventilationClip == null)
            {
                ventilationClip = CreateClip(
                    "StairwellSoundscape_Ventilation",
                    StairwellSoundscapeSynthesis
                        .GenerateVentilationLoopSamples());
            }

            if (electricalClip == null)
            {
                electricalClip = CreateClip(
                    "StairwellSoundscape_ElectricalBuzz",
                    StairwellSoundscapeSynthesis
                        .GenerateElectricalBuzzLoopSamples());
            }

            if (pipeKnockClip == null)
            {
                pipeKnockClip = CreateCueClip(
                    StairwellSoundscapeCueKind.PipeKnock,
                    "StairwellSoundscape_PipeKnock");
            }

            if (metalStressClip == null)
            {
                metalStressClip = CreateCueClip(
                    StairwellSoundscapeCueKind.MetalStress,
                    "StairwellSoundscape_MetalStress");
            }

            if (distantWaterClip == null)
            {
                distantWaterClip = CreateCueClip(
                    StairwellSoundscapeCueKind.DistantWater,
                    "StairwellSoundscape_DistantWater");
            }

            if (distantMovementClip == null)
            {
                distantMovementClip = CreateCueClip(
                    StairwellSoundscapeCueKind.DistantMovement,
                    "StairwellSoundscape_DistantMovement");
            }

            ventilationSource.clip = ventilationClip;
            electricalSource.clip = electricalClip;
        }

        private static AudioClip CreateCueClip(
            StairwellSoundscapeCueKind kind,
            string clipName)
        {
            return CreateClip(
                clipName,
                StairwellSoundscapeSynthesis
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
