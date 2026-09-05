using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Three bounded voices belong to the passing hulls: two soft motor loops
    /// and one globally spaced horn. All source positions are physical anchors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityOffshoreBoatSound : MonoBehaviour
    {
        public const int EngineSourceCount = 2;
        public const int HornSourceCount = 1;
        public const int OwnedSourceCount = EngineSourceCount + HornSourceCount;
        public const float MaximumEngineVolume = 0.26f;
        public const float MaximumHornVolume = 0.28f;
        public const float MinimumDistance = 18f;
        public const float MaximumDistance = RuntimeSceneSetup.CityFarClipPlane;
        public const float EngineCutoffFrequency = 760f;
        public const float HornCutoffFrequency = 1250f;
        public const float MinimumHornInterval = 90f;
        public const float MaximumHornInterval = 150f;
        public const float MinimumHornPresence = 0.3f;
        public const float OcclusionRefreshSeconds = 0.25f;

        private readonly AudioSource[] engines = new AudioSource[EngineSourceCount];
        private readonly AudioLowPassFilter[] engineFilters = new AudioLowPassFilter[EngineSourceCount];
        private readonly AudioClip[] engineClips = new AudioClip[EngineSourceCount];
        private readonly AudioClip[] hornClips = new AudioClip[EngineSourceCount];
        private readonly float[] presence = new float[EngineSourceCount];
        private readonly float[] engineGain = new float[EngineSourceCount];
        private readonly bool[] engineStarted = new bool[EngineSourceCount];
        private readonly CitySoundOcclusionSample[] occlusion = new CitySoundOcclusionSample[EngineSourceCount];
        private Transform[] boats;
        private Transform[] engineAnchors;
        private Transform[] hornAnchors;
        private Transform listener;
        private IReadOnlyList<BuildingLot> buildingLots;
        private AudioLowPassFilter hornFilter;
        private System.Random schedule;
        private float occlusionCountdown;
        private float hornRemaining;
        private bool voicesPaused;

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<AudioSource> EngineSources => engines;
        public AudioSource HornSource { get; private set; }
        public int HornBoatIndex { get; private set; } = -1;
        public int HornsPlayed { get; private set; }
        public float SecondsUntilHorn { get; private set; }

        public void Initialize(int seed, Transform[] boatTransforms,
            Transform[] engineTransforms, Transform[] hornTransforms)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("Offshore boat sound is already initialized.");
            }

            ValidateAnchors(boatTransforms, nameof(boatTransforms));
            ValidateAnchors(engineTransforms, nameof(engineTransforms));
            ValidateAnchors(hornTransforms, nameof(hornTransforms));
            if (engineTransforms.Length != boatTransforms.Length ||
                hornTransforms.Length != boatTransforms.Length)
            {
                throw new ArgumentException(
                    "Each offshore boat requires one engine and one horn anchor.");
            }

            boats = PadAnchors(boatTransforms);
            engineAnchors = PadAnchors(engineTransforms);
            hornAnchors = PadAnchors(hornTransforms);
            schedule = new System.Random(seed ^ 0x424F4154);
            for (int index = 0; index < EngineSourceCount; index++)
            {
                engines[index] = CreateVoice("Fishing Boat Motor " + index,
                    true, GameAudioGroup.AmbienceBeds, out engineFilters[index]);
                engines[index].priority = 185;
                engines[index].spread = 18f;
                engineFilters[index].cutoffFrequency = EngineCutoffFrequency;
                engineClips[index] = CityOffshoreBoatSynthesis.CreateEngineClip(seed, index);
                engines[index].clip = engineClips[index];
                hornClips[index] = CityOffshoreBoatSynthesis.CreateHornClip(seed, index);
                occlusion[index] = new CitySoundOcclusionSample(0, 1f, float.MaxValue);
            }

            HornSource = CreateVoice("Fishing Boat Horn", false,
                GameAudioGroup.AmbienceDetails, out hornFilter);
            HornSource.priority = 172;
            HornSource.spread = 22f;
            hornFilter.cutoffFrequency = HornCutoffFrequency;
            SecondsUntilHorn = NextHornInterval();
            IsInitialized = true;
            SyncAnchors();
        }

        public void SetPresence(int index, float amount)
        {
            if (index < 0 || index >= EngineSourceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            presence[index] = float.IsNaN(amount) || float.IsInfinity(amount)
                ? 0f : Mathf.Clamp01(amount);
        }

        public void SetOcclusionContext(Transform listenerTransform,
            IReadOnlyList<BuildingLot> lots)
        {
            listener = listenerTransform;
            buildingLots = lots;
            occlusionCountdown = 0f;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime, GameSessionState.IsGameTimeRunning &&
                !GameTimeScaleRuntime.IsPaused);
        }

        /// <summary>Advances at most one horn decision; an elapsed gap never catches up.</summary>
        public void Advance(float deltaTime, bool timeRunning)
        {
            if (!IsInitialized)
            {
                return;
            }

            SyncAnchors();
            SetPaused(!timeRunning);
            if (!timeRunning)
            {
                return;
            }

            float delta = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                ? 0f : Mathf.Max(0f, deltaTime);
            occlusionCountdown -= delta;
            if (occlusionCountdown <= 0f)
            {
                occlusionCountdown = OcclusionRefreshSeconds;
                RefreshOcclusion();
            }

            for (int index = 0; index < engines.Length; index++)
            {
                float amount = EffectivePresence(index);
                // Both approach and withdrawal ease the shore gate; an
                // entirely hidden/recycled hull cannot leave a motor behind.
                engineGain[index] = amount <= 0.001f ? 0f : Mathf.MoveTowards(
                    engineGain[index], amount, delta / 1.6f);
                AudioSource source = engines[index];
                source.volume = MaximumEngineVolume * engineGain[index] *
                    occlusion[index].VolumeMultiplier;
                engineFilters[index].cutoffFrequency = Mathf.Min(
                    EngineCutoffFrequency, occlusion[index].MaximumCutoffFrequency);
                if (source.volume > 0.0001f && !engineStarted[index])
                {
                    source.Play();
                    engineStarted[index] = true;
                }
                else if (source.volume <= 0.0001f && engineStarted[index])
                {
                    source.Stop();
                    engineStarted[index] = false;
                }
            }

            if (HornBoatIndex >= 0)
            {
                hornRemaining -= delta;
                float amount = EffectivePresence(HornBoatIndex);
                if (hornRemaining <= 0f || amount <= 0.001f)
                {
                    StopHorn();
                }
                else
                {
                    ApplyHornMix();
                }
            }

            SecondsUntilHorn -= delta;
            if (SecondsUntilHorn <= 0f)
            {
                SecondsUntilHorn = NextHornInterval();
                if (HornBoatIndex < 0)
                {
                    TryPlayHorn();
                }
            }
        }

        private void TryPlayHorn()
        {
            int first = schedule.Next(EngineSourceCount);
            for (int offset = 0; offset < EngineSourceCount; offset++)
            {
                int index = (first + offset) % EngineSourceCount;
                if (EffectivePresence(index) < MinimumHornPresence)
                {
                    continue;
                }

                HornBoatIndex = index;
                HornSource.transform.position = hornAnchors[index].position;
                HornSource.clip = hornClips[index];
                hornRemaining = CityOffshoreBoatSynthesis.GetHornDuration(index);
                ApplyHornMix();
                HornSource.Play();
                HornsPlayed++;
                return;
            }
        }

        private void ApplyHornMix()
        {
            CitySoundOcclusionSample sample = occlusion[HornBoatIndex];
            HornSource.volume = MaximumHornVolume * EffectivePresence(HornBoatIndex) *
                sample.VolumeMultiplier;
            hornFilter.cutoffFrequency = Mathf.Min(
                HornCutoffFrequency, sample.MaximumCutoffFrequency);
        }

        private float EffectivePresence(int index) =>
            boats[index] != null && boats[index].gameObject.activeInHierarchy &&
            engineAnchors[index] != null && hornAnchors[index] != null
                ? presence[index] : 0f;

        private void SyncAnchors()
        {
            for (int index = 0; index < engines.Length; index++)
            {
                if (engines[index] != null && engineAnchors[index] != null)
                {
                    engines[index].transform.position = engineAnchors[index].position;
                }
            }

            if (HornBoatIndex >= 0 && hornAnchors[HornBoatIndex] != null)
            {
                HornSource.transform.position = hornAnchors[HornBoatIndex].position;
            }
        }

        private void RefreshOcclusion()
        {
            // The coast is composed before PlayerFactory creates its camera.
            // Resolve once that physical listener becomes available.
            if (listener == null)
            {
                Camera camera = Camera.main;
                if (camera != null) listener = camera.transform;
            }

            for (int index = 0; index < EngineSourceCount; index++)
            {
                occlusion[index] = listener != null && buildingLots != null &&
                    engineAnchors[index] != null
                    ? CitySoundOcclusion.Evaluate(engineAnchors[index].position,
                        listener.position, buildingLots)
                    : new CitySoundOcclusionSample(0, 1f, float.MaxValue);
            }
        }

        private void SetPaused(bool paused)
        {
            if (voicesPaused == paused)
            {
                return;
            }

            voicesPaused = paused;
            for (int index = 0; index < engines.Length; index++)
            {
                if (engineStarted[index])
                {
                    if (paused) engines[index].Pause();
                    else engines[index].UnPause();
                }
            }

            if (HornBoatIndex >= 0)
            {
                if (paused) HornSource.Pause();
                else HornSource.UnPause();
            }
        }

        private AudioSource CreateVoice(string name, bool loop,
            GameAudioGroup group, out AudioLowPassFilter filter)
        {
            GameObject voice = new GameObject(name);
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0f;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinimumDistance;
            source.maxDistance = MaximumDistance;
            source.reverbZoneMix = 0.35f;
            GameAudioMixer.Route(source, group);
            filter = voice.AddComponent<AudioLowPassFilter>();
            filter.lowpassResonanceQ = 1f;
            return source;
        }

        private float NextHornInterval() => Mathf.Lerp(
            MinimumHornInterval, MaximumHornInterval, (float)schedule.NextDouble());

        private static void ValidateAnchors(Transform[] anchors, string parameter)
        {
            if (anchors == null || anchors.Length < 1 ||
                anchors.Length > EngineSourceCount)
            {
                throw new ArgumentException(
                    "One or two offshore boat anchors are required.", parameter);
            }

            for (int index = 0; index < anchors.Length; index++)
            {
                if (anchors[index] == null)
                {
                    throw new ArgumentException("Offshore boat anchors cannot be null.", parameter);
                }
            }
        }

        private static Transform[] PadAnchors(Transform[] anchors)
        {
            var padded = new Transform[EngineSourceCount];
            Array.Copy(anchors, padded, anchors.Length);
            return padded;
        }

        private void StopHorn()
        {
            HornSource?.Stop();
            if (HornSource != null) HornSource.volume = 0f;
            HornBoatIndex = -1;
            hornRemaining = 0f;
        }

        private void OnDisable()
        {
            for (int index = 0; index < engines.Length; index++)
            {
                engines[index]?.Stop();
                if (engines[index] != null) engines[index].volume = 0f;
                engineStarted[index] = false;
                engineGain[index] = 0f;
            }

            StopHorn();
            voicesPaused = false;
        }

        private void OnDestroy()
        {
            OnDisable();
            for (int index = 0; index < EngineSourceCount; index++)
            {
                // Sources are children owned by this component even when only
                // the component, rather than its parent world, is removed.
                DestroyOwned(engines[index] != null ? engines[index].gameObject : null);
                DestroyOwned(engineClips[index]);
                DestroyOwned(hornClips[index]);
            }

            DestroyOwned(HornSource != null ? HornSource.gameObject : null);
            IsInitialized = false;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
