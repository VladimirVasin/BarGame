using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public readonly struct MothersHouseWoodSettleCue
    {
        internal MothersHouseWoodSettleCue(
            float delaySeconds,
            float pitch,
            float volumeScale)
        {
            DelaySeconds = delaySeconds;
            Pitch = pitch;
            VolumeScale = volumeScale;
        }

        public float DelaySeconds { get; }
        public float Pitch { get; }
        public float VolumeScale { get; }
    }

    /// <summary>
    /// Pure schedule for the one deliberately rare room detail. The old
    /// cupboard owns a short timber settle, never a door movement or a cue
    /// that could imply an unseen resident.
    /// </summary>
    public static class MothersHouseInteriorSoundSchedule
    {
        public const string WoodSettleStableId =
            "mothers-house-old-cupboard-settle";
        public const float MinimumWoodSettleDelaySeconds = 42f;
        public const float MaximumWoodSettleDelaySeconds = 78f;
        public const float MinimumWoodSettlePitch = 0.94f;
        public const float MaximumWoodSettlePitch = 1.03f;
        public const float MinimumWoodSettleVolumeScale = 0.78f;
        public const float MaximumWoodSettleVolumeScale = 1f;

        public static MothersHouseWoodSettleCue GetWoodSettleCue(
            int seed,
            int sequence)
        {
            uint ordinal = unchecked((uint)Mathf.Max(0, sequence));
            uint basis = CitySoundStableHash.SourceEvent(
                seed,
                WoodSettleStableId,
                ordinal);
            float delay = Mathf.Lerp(
                MinimumWoodSettleDelaySeconds,
                MaximumWoodSettleDelaySeconds,
                CitySoundStableHash.ToUnitFloat(basis));
            float pitch = Mathf.Lerp(
                MinimumWoodSettlePitch,
                MaximumWoodSettlePitch,
                CitySoundStableHash.ToUnitFloat(
                    CitySoundStableHash.Combine(basis, 0x50495443u)));
            float volumeScale = Mathf.Lerp(
                MinimumWoodSettleVolumeScale,
                MaximumWoodSettleVolumeScale,
                CitySoundStableHash.ToUnitFloat(
                    CitySoundStableHash.Combine(basis, 0x564F4C55u)));
            return new MothersHouseWoodSettleCue(
                delay,
                pitch,
                volumeScale);
        }
    }

    /// <summary>
    /// Asset-free mono synthesis for the mother's quiet room. Every clip is
    /// built once during installation; playback and scheduling allocate
    /// nothing per frame.
    /// </summary>
    public static class MothersHouseInteriorSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const float WindLoopDuration = 8f;
        public const float ClockBeatInterval = 1.1f;
        public const float ClockLoopDuration = ClockBeatInterval * 2f;
        public const float ClockTickStart = 0.18f;
        public const float ClockTockStart =
            ClockTickStart + ClockBeatInterval;
        public const float ClockPulseDuration = 0.14f;
        public const float WoodSettleDuration = 0.72f;
        public const float MaximumWindSampleAmplitude = 0.26f;
        public const float MaximumClockSampleAmplitude = 0.22f;
        public const float MaximumWoodSettleSampleAmplitude = 0.18f;

        private static readonly int[] WindHarmonics =
        {
            271, 337, 419, 521, 641, 787,
            947, 1123, 1327, 1559, 1811, 2089,
            2389, 2729, 3083, 3469, 3877, 4339
        };

        internal static AudioClip CreateMuffledWindRuntimeClip(int seed)
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * WindLoopDuration);
            var samples = new float[sampleCount];
            float amplitudeSquares = 0f;
            uint seedBits = unchecked((uint)seed) ^ 0x57494E44u;

            for (int partial = 0;
                 partial < WindHarmonics.Length;
                 partial++)
            {
                int harmonic = WindHarmonics[partial];
                uint hash = CitySoundStableHash.Combine(
                    seedBits,
                    unchecked((uint)harmonic));
                double phase = CitySoundStableHash.ToUnitFloat(hash) *
                               Math.PI * 2d;
                float variation = Mathf.Lerp(
                    0.78f,
                    1.18f,
                    CitySoundStableHash.ToUnitFloat(
                        CitySoundStableHash.Combine(hash, 0x41495220u)));
                float amplitude = variation /
                                  Mathf.Sqrt(1f + partial * 0.46f);
                amplitudeSquares += amplitude * amplitude;

                double step = Math.PI * 2d * harmonic / sampleCount;
                double sine = Math.Sin(phase);
                double cosine = Math.Cos(phase);
                double stepSine = Math.Sin(step);
                double stepCosine = Math.Cos(step);
                for (int index = 0; index < sampleCount; index++)
                {
                    samples[index] += (float)sine * amplitude;
                    double nextSine =
                        sine * stepCosine + cosine * stepSine;
                    cosine = cosine * stepCosine - sine * stepSine;
                    sine = nextSine;
                }
            }

            float normalization = Mathf.Max(
                0.0001f,
                Mathf.Sqrt(amplitudeSquares));
            float breathPhase = CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.Combine(seedBits, 0x42524541u)) *
                Mathf.PI * 2f;
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = index / (float)sampleCount * Mathf.PI * 2f;
                float breath =
                    0.54f +
                    Mathf.Sin(phase * 2f + breathPhase) * 0.085f +
                    Mathf.Sin(phase * 5f + breathPhase * 0.37f) * 0.035f;
                samples[index] = Mathf.Clamp(
                    samples[index] / normalization * breath * 0.18f,
                    -MaximumWindSampleAmplitude,
                    MaximumWindSampleAmplitude);
            }

            // The harmonics are periodic already. Matching the stored edge
            // samples as well keeps even naive import/playback loopers silent
            // at the seam.
            samples[sampleCount - 1] = samples[0];
            return CreateRuntimeClip(
                "MothersHouseMuffledWindowWind",
                samples);
        }

        internal static AudioClip CreateClockRuntimeClip()
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * ClockLoopDuration);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float tick = SoftClockPulse(
                    time,
                    ClockTickStart,
                    405f,
                    0.145f,
                    0.18f);
                float tock = SoftClockPulse(
                    time,
                    ClockTockStart,
                    302f,
                    0.165f,
                    0.43f);
                samples[index] = Mathf.Clamp(
                    tick + tock,
                    -MaximumClockSampleAmplitude,
                    MaximumClockSampleAmplitude);
            }

            return CreateRuntimeClip(
                "MothersHouseSoftClockTickTock",
                samples);
        }

        internal static AudioClip CreateWoodSettleRuntimeClip(int seed)
        {
            int sampleCount = Mathf.RoundToInt(
                SampleRate * WoodSettleDuration);
            var samples = new float[sampleCount];
            float phaseOffset = CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.Combine(
                    unchecked((uint)seed),
                    0x54494D42u)) * 0.45f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float local = time - 0.075f;
                if (local < 0f || local > 0.43f)
                {
                    continue;
                }

                float attack = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(local / 0.026f));
                float release = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((0.43f - local) / 0.12f));
                float envelope =
                    attack * release * Mathf.Exp(-local * 5.4f);
                float bend = local * local;
                float body =
                    Mathf.Sin(
                        Mathf.PI * 2f *
                        (96f * local + 17f * bend) + phaseOffset) *
                    0.105f;
                float grain =
                    Mathf.Sin(
                        Mathf.PI * 2f *
                        (163f * local + 9f * bend) +
                        phaseOffset * 0.41f) *
                    0.047f;
                samples[index] = Mathf.Clamp(
                    (body + grain) * envelope,
                    -MaximumWoodSettleSampleAmplitude,
                    MaximumWoodSettleSampleAmplitude);
            }

            samples[sampleCount - 1] = 0f;
            return CreateRuntimeClip(
                "MothersHouseSoftTimberSettle",
                samples);
        }

        private static float SoftClockPulse(
            float time,
            float start,
            float frequency,
            float amplitude,
            float phaseOffset)
        {
            float local = time - start;
            if (local < 0f || local > ClockPulseDuration)
            {
                return 0f;
            }

            float attack = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(local / 0.012f));
            float release = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    (ClockPulseDuration - local) / 0.042f));
            float envelope =
                attack * release * Mathf.Exp(-local * 22f);
            float body =
                Mathf.Sin(
                    Mathf.PI * 2f * frequency * local + phaseOffset) *
                0.68f;
            float wood =
                Mathf.Sin(
                    Mathf.PI * frequency * local + phaseOffset * 0.54f) *
                0.32f;
            return (body + wood) * envelope * amplitude;
        }

        private static AudioClip CreateRuntimeClip(
            string name,
            float[] samples)
        {
            AudioClip clip = AudioClip.Create(
                name,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }

    /// <summary>
    /// Owns only the room's muffled exterior wind, visible clock and one rare
    /// visible-cupboard timber detail. The hearth ambience remains owned by
    /// MothersHouseInteriorAtmosphere; this component adds no music, voices or
    /// kettle sound.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseInteriorSoundscape : MonoBehaviour
    {
        public const string RootName = "Mother's House Soundscape";
        public const string WindSourceName =
            "Muffled Wind at Window Frames";
        public const string ClockSourceName =
            "Visible Wall Clock Soft Tick Tock";
        public const string WoodSettleSourceName =
            "Old Cupboard Soft Timber Settle";
        public const string ClockOwnerRole = "wall_clock";
        public const string WoodSettleOwnerRole = "old_cupboard";

        public const int MaximumOwnedSourceCount = 3;
        public const int LoopingSourceCount = 2;
        public const int ScheduledSourceCount = 1;
        public const float MuffledWindVolume = 0.022f;
        public const float ClockVolume = 0.033f;
        public const float WoodSettleVolume = 0.035f;
        public const float MuffledWindMinimumDistance = 1.4f;
        public const float MuffledWindMaximumDistance = 12f;
        public const float ClockMinimumDistance = 0.7f;
        public const float ClockMaximumDistance = 4.6f;
        public const float WoodSettleMinimumDistance = 0.8f;
        public const float WoodSettleMaximumDistance = 4.2f;
        public const float MuffledWindLowPassCutoff = 620f;
        public const float ClockLowPassCutoff = 1800f;
        public const float WoodSettleLowPassCutoff = 1450f;
        public const GameAudioGroup WindMixerGroup =
            GameAudioGroup.AmbienceBeds;
        public const GameAudioGroup DetailMixerGroup =
            GameAudioGroup.AmbienceDetails;

        public AudioSource MuffledWindSource { get; private set; }
        public AudioSource ClockSource { get; private set; }
        public AudioSource WoodSettleSource { get; private set; }
        public AudioLowPassFilter MuffledWindLowPass { get; private set; }
        public AudioLowPassFilter ClockLowPass { get; private set; }
        public AudioLowPassFilter WoodSettleLowPass { get; private set; }
        public AudioClip MuffledWindClip => windClip;
        public AudioClip ClockClip => clockClip;
        public AudioClip WoodSettleClip => woodSettleClip;
        public bool IsInitialized { get; private set; }
        public int DeterministicSeed { get; private set; }
        public int WoodSettleSequence { get; private set; }
        public float SecondsUntilNextWoodSettle { get; private set; }
        public bool HasPlayedWoodSettle { get; private set; }
        public MothersHouseWoodSettleCue LastWoodSettleCue
        {
            get;
            private set;
        }
        public Vector3 WindowWindPosition { get; private set; }
        public Vector3 ClockPosition { get; private set; }
        public Vector3 WoodSettlePosition { get; private set; }
        public int OwnedSourceCount =>
            (MuffledWindSource != null ? 1 : 0) +
            (ClockSource != null ? 1 : 0) +
            (WoodSettleSource != null ? 1 : 0);
        public int OwnedRuntimeClipCount =>
            (windClip != null ? 1 : 0) +
            (clockClip != null ? 1 : 0) +
            (woodSettleClip != null ? 1 : 0);

        private AudioClip windClip;
        private AudioClip clockClip;
        private AudioClip woodSettleClip;

        public static MothersHouseInteriorSoundscape Install(
            Transform parent,
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorWorldResult world)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            Renderer clock = RequireVisibleOwner(
                world.Registry,
                ClockOwnerRole);
            Renderer cupboard = RequireVisibleOwner(
                world.Registry,
                WoodSettleOwnerRole);
            GameObject holder = new GameObject(RootName);
            holder.transform.SetParent(parent, false);
            var soundscape = holder.AddComponent<
                MothersHouseInteriorSoundscape>();
            soundscape.Initialize(
                parent,
                plan,
                clock.bounds.center,
                cupboard.bounds.center,
                GameSessionState.CitySeed);
            return soundscape;
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

            SecondsUntilNextWoodSettle -= unscaledDeltaTime;
            if (SecondsUntilNextWoodSettle <= 0f)
            {
                PlayNextWoodSettle();
            }
        }

        private void Initialize(
            Transform roomRoot,
            MothersHouseInteriorLayoutPlan plan,
            Vector3 clockPosition,
            Vector3 woodSettlePosition,
            int seed)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mother's-house soundscape is already initialized.");
            }

            DeterministicSeed = seed;
            BuildMuffledWind(roomRoot, plan, seed);
            BuildClock(clockPosition);
            BuildWoodSettle(woodSettlePosition, seed);
            WoodSettleSequence = 0;
            HasPlayedWoodSettle = false;
            LastWoodSettleCue = default;
            SecondsUntilNextWoodSettle =
                MothersHouseInteriorSoundSchedule
                    .GetWoodSettleCue(seed, WoodSettleSequence)
                    .DelaySeconds;
            IsInitialized = true;
            StartLoopingSources();
        }

        private void BuildMuffledWind(
            Transform roomRoot,
            MothersHouseInteriorLayoutPlan plan,
            int seed)
        {
            windClip = MothersHouseInteriorSoundSynthesis
                .CreateMuffledWindRuntimeClip(seed ^ 0x6D6F7468);
            Vector3 centre = Vector3.Lerp(
                plan.WestWindowPosition,
                plan.EastWindowPosition,
                0.5f);
            WindowWindPosition = roomRoot.TransformPoint(centre);
            MuffledWindSource = CreateSpatialSource(
                WindSourceName,
                WindowWindPosition,
                windClip,
                true,
                MuffledWindVolume,
                MuffledWindMinimumDistance,
                MuffledWindMaximumDistance,
                34f,
                190,
                WindMixerGroup,
                MuffledWindLowPassCutoff,
                out AudioLowPassFilter filter);
            MuffledWindLowPass = filter;
        }

        private void BuildClock(Vector3 position)
        {
            ClockPosition = position;
            clockClip = MothersHouseInteriorSoundSynthesis
                .CreateClockRuntimeClip();
            ClockSource = CreateSpatialSource(
                ClockSourceName,
                ClockPosition,
                clockClip,
                true,
                ClockVolume,
                ClockMinimumDistance,
                ClockMaximumDistance,
                0f,
                184,
                DetailMixerGroup,
                ClockLowPassCutoff,
                out AudioLowPassFilter filter);
            ClockLowPass = filter;
        }

        private void BuildWoodSettle(Vector3 position, int seed)
        {
            WoodSettlePosition = position;
            woodSettleClip = MothersHouseInteriorSoundSynthesis
                .CreateWoodSettleRuntimeClip(seed ^ 0x776F6F64);
            WoodSettleSource = CreateSpatialSource(
                WoodSettleSourceName,
                WoodSettlePosition,
                woodSettleClip,
                false,
                WoodSettleVolume,
                WoodSettleMinimumDistance,
                WoodSettleMaximumDistance,
                0f,
                198,
                DetailMixerGroup,
                WoodSettleLowPassCutoff,
                out AudioLowPassFilter filter);
            WoodSettleLowPass = filter;
        }

        private AudioSource CreateSpatialSource(
            string sourceName,
            Vector3 position,
            AudioClip clip,
            bool loop,
            float volume,
            float minimumDistance,
            float maximumDistance,
            float spread,
            int priority,
            GameAudioGroup group,
            float lowPassCutoff,
            out AudioLowPassFilter lowPass)
        {
            GameObject sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform, false);
            sourceObject.transform.position = position;
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = spread;
            source.priority = priority;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
            source.volume = volume;
            GameAudioMixer.Route(source, group);

            lowPass = sourceObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = lowPassCutoff;
            lowPass.lowpassResonanceQ = 1f;
            return source;
        }

        private void PlayNextWoodSettle()
        {
            MothersHouseWoodSettleCue cue =
                MothersHouseInteriorSoundSchedule.GetWoodSettleCue(
                    DeterministicSeed,
                    WoodSettleSequence);
            WoodSettleSequence++;
            LastWoodSettleCue = cue;
            HasPlayedWoodSettle = true;

            if (WoodSettleSource != null)
            {
                WoodSettleSource.Stop();
                WoodSettleSource.pitch = cue.Pitch;
                WoodSettleSource.volume =
                    WoodSettleVolume * cue.VolumeScale;
                WoodSettleSource.Play();
            }

            SecondsUntilNextWoodSettle =
                MothersHouseInteriorSoundSchedule
                    .GetWoodSettleCue(
                        DeterministicSeed,
                        WoodSettleSequence)
                    .DelaySeconds;
        }

        private void StartLoopingSources()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                return;
            }

            PlayLoop(MuffledWindSource);
            PlayLoop(ClockSource);
        }

        private static void PlayLoop(AudioSource source)
        {
            if (source != null && !source.isPlaying)
            {
                source.Play();
            }
        }

        private static Renderer RequireVisibleOwner(
            MothersHouseInteriorAssetRegistry registry,
            string role)
        {
            if (registry != null)
            {
                for (int index = 0;
                     index < registry.Parts.Count;
                     index++)
                {
                    MothersHouseInteriorPartBinding part =
                        registry.Parts[index];
                    if (part != null &&
                        string.Equals(
                            part.Role,
                            role,
                            StringComparison.Ordinal) &&
                        part.Renderer != null)
                    {
                        return part.Renderer;
                    }
                }
            }

            throw new InvalidOperationException(
                $"The mother's-house soundscape needs a visible '{role}' " +
                "owner.");
        }

        private void Update()
        {
            AdvanceSoundscape(Time.unscaledDeltaTime);
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                StartLoopingSources();
            }
        }

        private void OnDisable()
        {
            MuffledWindSource?.Stop();
            ClockSource?.Stop();
            WoodSettleSource?.Stop();
        }

        private void OnDestroy()
        {
            IsInitialized = false;
            StopAndClear(MuffledWindSource);
            StopAndClear(ClockSource);
            StopAndClear(WoodSettleSource);
            DestroyClip(ref windClip);
            DestroyClip(ref clockClip);
            DestroyClip(ref woodSettleClip);
            MuffledWindSource = null;
            ClockSource = null;
            WoodSettleSource = null;
            MuffledWindLowPass = null;
            ClockLowPass = null;
            WoodSettleLowPass = null;
            SecondsUntilNextWoodSettle = 0f;
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

        private static void DestroyClip(ref AudioClip clip)
        {
            AudioClip value = clip;
            clip = null;
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}
