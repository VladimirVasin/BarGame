using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CitySoundProvenanceReason
    {
        LoopActivated = 0,
        ScheduledEvent = 1,
        PhysicalAction = 2
    }

    public readonly struct CitySoundProvenanceEntry
    {
        public CitySoundProvenanceEntry(
            string emitterStableId,
            CitySoundPhysicalOwnerKind physicalOwner,
            CitySourceSoundId cue,
            Vector3 worldPosition,
            double absoluteGameTime,
            float listenerDistance,
            int occlusionBlockers,
            int voiceIndex,
            CitySoundProvenanceReason reason)
        {
            EmitterStableId = emitterStableId ?? string.Empty;
            PhysicalOwner = physicalOwner;
            Cue = cue;
            WorldPosition = worldPosition;
            AbsoluteGameTime = absoluteGameTime;
            ListenerDistance = listenerDistance;
            OcclusionBlockers = occlusionBlockers;
            VoiceIndex = voiceIndex;
            Reason = reason;
        }

        public string EmitterStableId { get; }
        public CitySoundPhysicalOwnerKind PhysicalOwner { get; }
        public CitySourceSoundId Cue { get; }
        public Vector3 WorldPosition { get; }
        public double AbsoluteGameTime { get; }
        public float ListenerDistance { get; }
        public int OcclusionBlockers { get; }
        public int VoiceIndex { get; }
        public CitySoundProvenanceReason Reason { get; }
    }

    /// <summary>
    /// Scene-owned runtime for the causal City plan. Five stable loop voices,
    /// three autonomous detail voices and one action voice are the hard
    /// budget. Triggered cues can only enter through a physical owner event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CitySoundscapeDirector : MonoBehaviour
    {
        public const string RuntimeObjectName = "City Source Soundscape";
        public const int LoopVoiceCount = 5;
        public const int ScheduledVoiceCount = 3;
        public const int ActionVoiceCount = 1;
        public const int DetailVoiceCount =
            ScheduledVoiceCount + ActionVoiceCount;
        public const int OwnedSourceCount =
            LoopVoiceCount + DetailVoiceCount;
        public const int ProvenanceCapacity = 32;
        public const float LoopFadeSeconds = 0.75f;
        public const float OcclusionRefreshSeconds = 0.25f;
        public const float ScheduledSilenceSeconds = 2.75f;
        public const float ActionSilenceSeconds = 0.32f;

        private sealed class LoopVoice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public CitySoundSourceDescriptor Descriptor;
            public CitySourceSoundDefinition Definition;
            public float OcclusionVolume = 1f;
            public float OcclusionCutoff = float.MaxValue;
            public int OcclusionBlockers;
        }

        private sealed class DetailVoice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public double StartedAt = double.NegativeInfinity;
        }

        private readonly LoopVoice[] loopVoices =
            new LoopVoice[LoopVoiceCount];
        private readonly DetailVoice[] detailVoices =
            new DetailVoice[DetailVoiceCount];
        private readonly Dictionary<int, AudioClip> runtimeClips =
            new Dictionary<int, AudioClip>();
        private readonly Dictionary<string, CitySoundScheduleCursor>
            scheduleCursors =
                new Dictionary<string, CitySoundScheduleCursor>(
                    StringComparer.Ordinal);
        private readonly List<DryingYardBabushkaPresentation>
            subscribedBabushkas =
                new List<DryingYardBabushkaPresentation>();
        private readonly List<CityPlaygroundSwing> subscribedSwings =
            new List<CityPlaygroundSwing>();
        private readonly CitySoundProvenanceEntry[] provenance =
            new CitySoundProvenanceEntry[ProvenanceCapacity];

        private Transform listener;
        private IReadOnlyList<BuildingLot> buildingLots;
        private CityWeighbridgeNeedleController weighbridgeNeedle;
        private Func<float> nightFactorProvider;
        private float previousWeighbridgeDeflection;
        private float occlusionCountdown;
        private double currentAbsoluteGameTime;
        private double lastScheduledEventTime = double.NegativeInfinity;
        private double lastActionEventTime = double.NegativeInfinity;
        private int provenanceWriteIndex;
        private int provenanceCount;

        public bool IsInitialized { get; private set; }
        public CitySoundscapePlan Plan { get; private set; }
        public int PlayedEventCount { get; private set; }
        public int ActiveLoopCount { get; private set; }
        public int RuntimeClipCount => runtimeClips.Count;
        public int ProvenanceCount => provenanceCount;

        public static CitySoundscapeDirector Create(
            Transform parent,
            CitySoundscapePlan plan,
            Transform listener,
            CityLayout layout,
            IReadOnlyList<DryingYardBabushkaPresentation> babushkas,
            CityWeighbridgeNeedleController weighbridgeNeedle,
            IReadOnlyList<CityPlaygroundSwing> playgroundSwings,
            Func<float> nightFactorProvider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            var director = host.AddComponent<CitySoundscapeDirector>();
            director.Initialize(
                plan,
                listener,
                layout,
                babushkas,
                weighbridgeNeedle,
                playgroundSwings,
                nightFactorProvider);
            return director;
        }

        public void Initialize(
            CitySoundscapePlan plan,
            Transform listenerTransform,
            CityLayout layout,
            IReadOnlyList<DryingYardBabushkaPresentation> babushkas,
            CityWeighbridgeNeedleController needle,
            IReadOnlyList<CityPlaygroundSwing> playgroundSwings,
            Func<float> nightProvider)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The City soundscape is already initialized.");
            }

            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            listener = listenerTransform != null
                ? listenerTransform
                : throw new ArgumentNullException(
                    nameof(listenerTransform));
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan.LoopingSources.Count > LoopVoiceCount)
            {
                throw new InvalidOperationException(
                    $"The City sound plan needs {plan.LoopingSources.Count} " +
                    $"loop voices, but the pool owns {LoopVoiceCount}.");
            }

            buildingLots = layout.BuildingLots;
            weighbridgeNeedle = needle;
            nightFactorProvider = nightProvider;
            previousWeighbridgeDeflection =
                weighbridgeNeedle != null
                    ? weighbridgeNeedle.Deflection01
                    : 0f;
            currentAbsoluteGameTime = ResolveAbsoluteGameTime();

            CreateVoices();
            BindLoopSources(plan.LoopingSources);
            StartSchedules(plan.ScheduledSources);
            SubscribeBabushkas(babushkas);
            SubscribeSwings(playgroundSwings);
            IsInitialized = true;
        }

        public IReadOnlyList<AudioSource> GetLoopSources()
        {
            var result = new AudioSource[loopVoices.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = loopVoices[index].Source;
            }

            return result;
        }

        public IReadOnlyList<AudioSource> GetDetailSources()
        {
            var result = new AudioSource[detailVoices.Length];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = detailVoices[index].Source;
            }

            return result;
        }

        public CitySoundSourceDescriptor GetLoopDescriptor(int voiceIndex)
        {
            if (voiceIndex < 0 || voiceIndex >= loopVoices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(voiceIndex));
            }

            return loopVoices[voiceIndex].Descriptor;
        }

        public bool TryGetLastSound(out CitySoundProvenanceEntry entry)
        {
            if (provenanceCount <= 0)
            {
                entry = default;
                return false;
            }

            int index = provenanceWriteIndex - 1;
            if (index < 0)
            {
                index += provenance.Length;
            }

            entry = provenance[index];
            return true;
        }

        public void Advance(
            float deltaTime,
            double absoluteGameTime,
            bool advanceSchedules,
            WindSample wind,
            float nightFactor)
        {
            if (!IsInitialized ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime < 0f ||
                double.IsNaN(absoluteGameTime) ||
                double.IsInfinity(absoluteGameTime) ||
                absoluteGameTime < 0d)
            {
                return;
            }

            bool movedBackward =
                absoluteGameTime < currentAbsoluteGameTime;
            currentAbsoluteGameTime = absoluteGameTime;
            if (movedBackward)
            {
                RebaseSchedules();
            }

            occlusionCountdown -= deltaTime;
            bool refreshOcclusion = occlusionCountdown <= 0f;
            if (refreshOcclusion)
            {
                occlusionCountdown = OcclusionRefreshSeconds;
            }

            UpdateLoopVoices(
                deltaTime,
                wind,
                Mathf.Clamp01(nightFactor),
                refreshOcclusion);
            UpdateWeighbridgeAction();
            if (advanceSchedules)
            {
                AdvanceSchedules(wind, Mathf.Clamp01(nightFactor));
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            Advance(
                Time.deltaTime,
                ResolveAbsoluteGameTime(),
                GameSessionState.IsGameTimeRunning &&
                Time.timeScale > 0f,
                GameWeatherRules.EvaluateCurrentWind(),
                nightFactorProvider != null
                    ? nightFactorProvider()
                    : 0f);
        }

        private void CreateVoices()
        {
            for (int index = 0; index < loopVoices.Length; index++)
            {
                CreateVoiceObject(
                    $"Loop Voice {index + 1}",
                    out AudioSource source,
                    out AudioLowPassFilter filter);
                source.loop = true;
                source.priority = 180;
                GameAudioMixer.Route(
                    source,
                    GameAudioGroup.AmbienceDetails);
                loopVoices[index] = new LoopVoice
                {
                    Source = source,
                    Filter = filter
                };
            }

            for (int index = 0; index < detailVoices.Length; index++)
            {
                CreateVoiceObject(
                    index < ScheduledVoiceCount
                        ? $"Scheduled Voice {index + 1}"
                        : "Physical Action Voice",
                    out AudioSource source,
                    out AudioLowPassFilter filter);
                source.loop = false;
                source.priority = index < ScheduledVoiceCount
                    ? 164
                    : 118;
                GameAudioMixer.Route(
                    source,
                    index < ScheduledVoiceCount
                        ? GameAudioGroup.AmbienceDetails
                        : GameAudioGroup.SfxWorld);
                detailVoices[index] = new DetailVoice
                {
                    Source = source,
                    Filter = filter
                };
            }
        }

        private void CreateVoiceObject(
            string objectName,
            out AudioSource source,
            out AudioLowPassFilter filter)
        {
            GameObject voice = new GameObject(objectName);
            voice.transform.SetParent(transform, false);
            source = voice.AddComponent<AudioSource>();
            filter = voice.AddComponent<AudioLowPassFilter>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.reverbZoneMix = 0.75f;
            source.volume = 0f;
            filter.cutoffFrequency = 4000f;
            filter.lowpassResonanceQ = 1f;
        }

        private void BindLoopSources(
            IReadOnlyList<CitySoundSourceDescriptor> sources)
        {
            for (int index = 0; index < sources.Count; index++)
            {
                CitySoundSourceDescriptor descriptor = sources[index];
                CitySourceSoundDefinition definition =
                    CitySourceSoundSynthesis.GetDefinition(descriptor.Cue);
                LoopVoice voice = loopVoices[index];
                voice.Descriptor = descriptor;
                voice.Definition = definition;
                voice.Source.transform.position = descriptor.WorldPosition;
                voice.Source.clip = GetRuntimeClip(
                    descriptor,
                    0u);
                ConfigureSpatialDefinition(
                    voice.Source,
                    definition,
                    descriptor.AudibleRadius);
                voice.Filter.cutoffFrequency =
                    definition.LowPassFrequency;
            }
        }

        private void StartSchedules(
            IReadOnlyList<CitySoundSourceDescriptor> scheduled)
        {
            for (int index = 0; index < scheduled.Count; index++)
            {
                CitySoundSourceDescriptor source = scheduled[index];
                scheduleCursors.Add(
                    source.StableId,
                    CitySoundSchedulePlanner.Start(
                        Plan,
                        source.StableId,
                        currentAbsoluteGameTime));
            }
        }

        private void RebaseSchedules()
        {
            scheduleCursors.Clear();
            StartSchedules(Plan.ScheduledSources);
            lastScheduledEventTime = double.NegativeInfinity;
            lastActionEventTime = double.NegativeInfinity;
        }

        private void SubscribeBabushkas(
            IReadOnlyList<DryingYardBabushkaPresentation> babushkas)
        {
            if (babushkas == null)
            {
                return;
            }

            for (int index = 0; index < babushkas.Count; index++)
            {
                DryingYardBabushkaPresentation babushka =
                    babushkas[index];
                if (babushka == null ||
                    babushka.Role !=
                    DryingYardBabushkaRole.CarpetBeater)
                {
                    continue;
                }

                babushka.StrikeOccurred += OnCarpetStrike;
                subscribedBabushkas.Add(babushka);
            }
        }

        private void SubscribeSwings(
            IReadOnlyList<CityPlaygroundSwing> swings)
        {
            if (swings == null)
            {
                return;
            }

            for (int index = 0; index < swings.Count; index++)
            {
                CityPlaygroundSwing swing = swings[index];
                if (swing == null)
                {
                    continue;
                }

                swing.CreakOccurred += OnSwingCreak;
                subscribedSwings.Add(swing);
            }
        }

        private void UpdateLoopVoices(
            float deltaTime,
            WindSample wind,
            float nightFactor,
            bool refreshOcclusion)
        {
            ActiveLoopCount = 0;
            for (int index = 0; index < loopVoices.Length; index++)
            {
                LoopVoice voice = loopVoices[index];
                if (voice.Descriptor == null)
                {
                    continue;
                }

                CitySoundSourceDescriptor descriptor = voice.Descriptor;
                float distance = Vector3.Distance(
                    listener.position,
                    descriptor.WorldPosition);
                float drive = ResolveLoopDrive(
                    descriptor.Cue,
                    wind,
                    nightFactor);
                bool shouldPlay =
                    drive > 0.01f &&
                    distance <= descriptor.AudibleRadius + 1.5f;
                if (refreshOcclusion)
                {
                    CitySoundOcclusionSample occlusion =
                        CitySoundOcclusion.Evaluate(
                            descriptor.WorldPosition,
                            listener.position,
                            buildingLots);
                    voice.OcclusionVolume = occlusion.VolumeMultiplier;
                    voice.OcclusionCutoff =
                        occlusion.MaximumCutoffFrequency;
                    voice.OcclusionBlockers = occlusion.BlockerCount;
                }

                float targetVolume = shouldPlay
                    ? voice.Definition.Volume *
                      drive *
                      voice.OcclusionVolume
                    : 0f;
                float fadeRate = voice.Definition.Volume /
                    Mathf.Max(0.01f, LoopFadeSeconds);
                voice.Source.volume = Mathf.MoveTowards(
                    voice.Source.volume,
                    targetVolume,
                    fadeRate * deltaTime);
                float targetCutoff = Mathf.Min(
                    voice.Definition.LowPassFrequency,
                    voice.OcclusionCutoff);
                voice.Filter.cutoffFrequency = Mathf.MoveTowards(
                    voice.Filter.cutoffFrequency,
                    targetCutoff,
                    6200f * deltaTime);

                if (shouldPlay)
                {
                    ActiveLoopCount++;
                    if (!voice.Source.isPlaying)
                    {
                        voice.Source.Play();
                        Record(
                            descriptor,
                            index,
                            distance,
                            voice.OcclusionBlockers,
                            CitySoundProvenanceReason.LoopActivated);
                    }
                }
                else if (voice.Source.isPlaying &&
                         voice.Source.volume <= 0.0005f)
                {
                    voice.Source.Stop();
                }
            }
        }

        private static float ResolveLoopDrive(
            CitySourceSoundId cue,
            WindSample wind,
            float nightFactor)
        {
            switch (cue)
            {
                case CitySourceSoundId.WaterworksPipeLoop:
                    return 0.72f;
                case CitySourceSoundId.DryingYardClothLoop:
                    return wind.Strength01 < 0.08f
                        ? 0f
                        : Mathf.Lerp(0.18f, 0.90f, wind.Strength01);
                case CitySourceSoundId
                    .IndustrialWeighbridgeMechanismLoop:
                    return Mathf.Lerp(0.72f, 0.46f, nightFactor);
                case CitySourceSoundId.LastRouteRelayLoop:
                    return Mathf.Lerp(0.08f, 0.92f, nightFactor);
                case CitySourceSoundId.ParkFountainLoop:
                    return 0.78f;
                default:
                    return 0f;
            }
        }

        private void AdvanceSchedules(
            WindSample wind,
            float nightFactor)
        {
            for (int index = 0;
                 index < Plan.ScheduledSources.Count;
                 index++)
            {
                CitySoundSourceDescriptor descriptor =
                    Plan.ScheduledSources[index];
                CitySoundScheduleCursor cursor =
                    scheduleCursors[descriptor.StableId];
                if (!cursor.IsDue(currentAbsoluteGameTime))
                {
                    continue;
                }

                bool eligible = IsScheduledSourceEligible(
                    descriptor.Cue,
                    wind,
                    nightFactor);
                float distance = Vector3.Distance(
                    listener.position,
                    descriptor.WorldPosition);
                if (eligible &&
                    distance <= descriptor.AudibleRadius &&
                    currentAbsoluteGameTime - lastScheduledEventTime >=
                    ScheduledSilenceSeconds)
                {
                    if (PlayDetail(
                            descriptor,
                            cursor.EventOrdinal,
                            false,
                            CitySoundProvenanceReason.ScheduledEvent))
                    {
                        lastScheduledEventTime = currentAbsoluteGameTime;
                    }
                }

                scheduleCursors[descriptor.StableId] =
                    CitySoundSchedulePlanner.AdvanceAfterFiring(
                        Plan,
                        cursor,
                        currentAbsoluteGameTime);
            }
        }

        private static bool IsScheduledSourceEligible(
            CitySourceSoundId cue,
            WindSample wind,
            float nightFactor)
        {
            switch (cue)
            {
                case CitySourceSoundId.DryingYardRopeCreak:
                    return wind.Strength01 >= 0.14f;
                case CitySourceSoundId.LastRouteIncompleteChime:
                    return nightFactor >= 0.42f;
                default:
                    return true;
            }
        }

        private void UpdateWeighbridgeAction()
        {
            if (weighbridgeNeedle == null)
            {
                return;
            }

            float current = weighbridgeNeedle.Deflection01;
            bool loaded =
                previousWeighbridgeDeflection < 0.24f &&
                current >= 0.24f;
            previousWeighbridgeDeflection = current;
            if (!loaded)
            {
                return;
            }

            TryPlayPhysicalAction(
                CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                CitySourceSoundId.IndustrialMetalStress);
        }

        private void OnCarpetStrike(
            DryingYardBabushkaPresentation source)
        {
            Vector3? strikePosition = null;
            if (source != null && source.Carpet != null)
            {
                Renderer carpetRenderer =
                    source.Carpet.GetComponent<Renderer>();
                strikePosition = carpetRenderer != null
                    ? carpetRenderer.bounds.center
                    : source.Carpet.transform.position;
            }

            TryPlayPhysicalAction(
                CitySoundPhysicalOwnerKind.ResidentialDryingYard,
                CitySourceSoundId.DryingYardCarpetStrike,
                strikePosition);
        }

        private void OnSwingCreak(CityPlaygroundSwing swing)
        {
            // Played from the plank rather than the beam: the seat is
            // what the listener can see moving, and by the time the
            // creak fires it is at the far end of its arc.
            Vector3? seatPosition = swing != null
                ? swing.SeatCenter
                : (Vector3?)null;

            TryPlayPhysicalAction(
                CitySoundPhysicalOwnerKind.ParkPlayground,
                CitySourceSoundId.ParkSwingCreak,
                seatPosition);
        }

        private void TryPlayPhysicalAction(
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue,
            Vector3? positionOverride = null)
        {
            if (!IsInitialized ||
                currentAbsoluteGameTime - lastActionEventTime <
                ActionSilenceSeconds)
            {
                return;
            }

            CitySoundSourceDescriptor descriptor = null;
            for (int index = 0;
                 index < Plan.TriggeredSources.Count;
                 index++)
            {
                CitySoundSourceDescriptor candidate =
                    Plan.TriggeredSources[index];
                if (candidate.PhysicalOwner == owner &&
                    candidate.Cue == cue)
                {
                    descriptor = candidate;
                    break;
                }
            }

            if (descriptor == null)
            {
                return;
            }

            Vector3 worldPosition =
                positionOverride ?? descriptor.WorldPosition;
            if (Vector3.Distance(
                    listener.position,
                    worldPosition) >
                descriptor.AudibleRadius)
            {
                return;
            }

            uint ordinal = unchecked((uint)PlayedEventCount);
            if (PlayDetail(
                    descriptor,
                    ordinal,
                    true,
                    CitySoundProvenanceReason.PhysicalAction,
                    worldPosition))
            {
                lastActionEventTime = currentAbsoluteGameTime;
                lastScheduledEventTime = currentAbsoluteGameTime;
            }
        }

        private bool PlayDetail(
            CitySoundSourceDescriptor descriptor,
            uint eventOrdinal,
            bool physicalAction,
            CitySoundProvenanceReason reason,
            Vector3? positionOverride = null)
        {
            int voiceIndex = physicalAction
                ? detailVoices.Length - 1
                : FindScheduledVoice();
            if (voiceIndex < 0)
            {
                return false;
            }

            DetailVoice voice = detailVoices[voiceIndex];
            CitySourceSoundDefinition definition =
                CitySourceSoundSynthesis.GetDefinition(descriptor.Cue);
            uint hash = CitySoundStableHash.SourceEvent(
                Plan.CitySeed,
                descriptor.StableId,
                eventOrdinal);
            int variant = (int)(hash % (uint)definition.VariantCount);
            Vector3 worldPosition =
                positionOverride ?? descriptor.WorldPosition;
            CitySoundOcclusionSample occlusion =
                CitySoundOcclusion.Evaluate(
                    worldPosition,
                    listener.position,
                    buildingLots);

            voice.Source.Stop();
            voice.Source.transform.position = worldPosition;
            voice.Source.clip = GetRuntimeClip(descriptor.Cue, variant);
            ConfigureSpatialDefinition(
                voice.Source,
                definition,
                descriptor.AudibleRadius);
            voice.Source.pitch = Mathf.Lerp(
                0.95f,
                1.045f,
                CitySoundStableHash.ToUnitFloat(
                    CitySoundStableHash.Combine(hash, 0x50495443u)));
            float variation = Mathf.Lerp(
                0.88f,
                1.04f,
                CitySoundStableHash.ToUnitFloat(
                    CitySoundStableHash.Combine(hash, 0x564F4C55u)));
            voice.Source.volume =
                definition.Volume *
                variation *
                occlusion.VolumeMultiplier;
            voice.Filter.cutoffFrequency = Mathf.Min(
                definition.LowPassFrequency,
                occlusion.MaximumCutoffFrequency);
            GameAudioMixer.Route(
                voice.Source,
                physicalAction
                    ? GameAudioGroup.SfxWorld
                    : GameAudioGroup.AmbienceDetails);
            voice.Source.Play();
            voice.StartedAt = currentAbsoluteGameTime;
            PlayedEventCount++;

            Record(
                descriptor,
                LoopVoiceCount + voiceIndex,
                Vector3.Distance(
                    listener.position,
                    worldPosition),
                occlusion.BlockerCount,
                reason,
                worldPosition);
            return true;
        }

        private int FindScheduledVoice()
        {
            int oldestIndex = -1;
            double oldestStart = double.PositiveInfinity;
            for (int index = 0; index < ScheduledVoiceCount; index++)
            {
                DetailVoice voice = detailVoices[index];
                if (!voice.Source.isPlaying)
                {
                    return index;
                }

                if (voice.StartedAt < oldestStart)
                {
                    oldestStart = voice.StartedAt;
                    oldestIndex = index;
                }
            }

            return oldestIndex;
        }

        private AudioClip GetRuntimeClip(
            CitySoundSourceDescriptor descriptor,
            uint eventOrdinal)
        {
            CitySourceSoundDefinition definition =
                CitySourceSoundSynthesis.GetDefinition(descriptor.Cue);
            uint hash = CitySoundStableHash.SourceEvent(
                Plan.CitySeed,
                descriptor.StableId,
                eventOrdinal);
            int variant = (int)(hash % (uint)definition.VariantCount);
            return GetRuntimeClip(descriptor.Cue, variant);
        }

        private AudioClip GetRuntimeClip(
            CitySourceSoundId cue,
            int variant)
        {
            int key = ((int)cue << 8) | variant;
            if (!runtimeClips.TryGetValue(key, out AudioClip clip))
            {
                clip = CitySourceSoundSynthesis.CreateRuntimeClip(
                    cue,
                    variant);
                runtimeClips.Add(key, clip);
            }

            return clip;
        }

        private static void ConfigureSpatialDefinition(
            AudioSource source,
            CitySourceSoundDefinition definition,
            float audibleRadius)
        {
            source.loop = definition.IsLoop;
            source.minDistance = Mathf.Min(
                definition.MinDistance,
                audibleRadius);
            source.maxDistance = audibleRadius;
            source.spread = definition.Id ==
                CitySourceSoundId.ParkFountainLoop
                    ? 54f
                    : 12f;
        }

        private void Record(
            CitySoundSourceDescriptor descriptor,
            int voiceIndex,
            float distance,
            int blockers,
            CitySoundProvenanceReason reason,
            Vector3? positionOverride = null)
        {
            Vector3 worldPosition =
                positionOverride ?? descriptor.WorldPosition;
            provenance[provenanceWriteIndex] =
                new CitySoundProvenanceEntry(
                    descriptor.StableId,
                    descriptor.PhysicalOwner,
                    descriptor.Cue,
                    worldPosition,
                    currentAbsoluteGameTime,
                    distance,
                    blockers,
                    voiceIndex,
                    reason);
            provenanceWriteIndex =
                (provenanceWriteIndex + 1) % provenance.Length;
            provenanceCount = Mathf.Min(
                provenanceCount + 1,
                provenance.Length);
        }

        private static double ResolveAbsoluteGameTime()
        {
            return
                GameSessionState.GameDayIndex *
                (double)GameTimeDayNightRules.MinutesPerDay +
                GameSessionState.GameTimeOfDayMinutes;
        }

        private void OnDestroy()
        {
            for (int index = 0;
                 index < subscribedBabushkas.Count;
                 index++)
            {
                DryingYardBabushkaPresentation babushka =
                    subscribedBabushkas[index];
                if (babushka != null)
                {
                    babushka.StrikeOccurred -= OnCarpetStrike;
                }
            }

            subscribedBabushkas.Clear();
            for (int index = 0; index < subscribedSwings.Count; index++)
            {
                CityPlaygroundSwing swing = subscribedSwings[index];
                if (swing != null)
                {
                    swing.CreakOccurred -= OnSwingCreak;
                }
            }

            subscribedSwings.Clear();
            for (int index = 0; index < loopVoices.Length; index++)
            {
                loopVoices[index]?.Source?.Stop();
            }

            for (int index = 0; index < detailVoices.Length; index++)
            {
                detailVoices[index]?.Source?.Stop();
            }

            foreach (AudioClip clip in runtimeClips.Values)
            {
                if (clip == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(clip);
                }
                else
                {
                    DestroyImmediate(clip);
                }
            }

            runtimeClips.Clear();
            IsInitialized = false;
        }
    }
}
