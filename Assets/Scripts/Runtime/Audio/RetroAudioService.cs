using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class RetroAudioService : MonoBehaviour
    {
        private struct Voice
        {
            public RetroSfxId Id;
            public double EndsAt;
        }

        private sealed class SourcePool
        {
            private readonly AudioSource[] sources;
            private readonly Voice[] voices;
            private readonly object[] presentationOwners;
            private readonly bool[] previousIgnoreListenerPause;
            private readonly bool[] presentationPaused;
            private int presentationVoiceCount;

            public SourcePool(
                Transform parent,
                string name,
                int sourceCount,
                GameAudioGroup outputGroup)
            {
                sources = new AudioSource[sourceCount];
                voices = new Voice[sourceCount];
                presentationOwners = new object[sourceCount];
                previousIgnoreListenerPause = new bool[sourceCount];
                presentationPaused = new bool[sourceCount];

                for (int index = 0; index < sourceCount; index++)
                {
                    GameObject sourceObject = new GameObject(
                        name + " Voice " + (index + 1));
                    sourceObject.transform.SetParent(parent, false);
                    AudioSource source =
                        sourceObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.loop = false;
                    source.dopplerLevel = 0f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = 1.2f;
                    source.maxDistance = 13f;
                    source.bypassReverbZones = true;
                    source.ignoreListenerPause =
                        outputGroup == GameAudioGroup.Ui;
                    GameAudioMixer.Route(source, outputGroup);
                    sources[index] = source;
                }
            }

            public int Count => sources.Length;

            public bool TryPlay(
                RetroSfxDefinition definition,
                AudioClip clip,
                Vector3 position,
                float pitch,
                double dspTime,
                float volumeScale,
                object presentationOwner)
            {
                int activeForEffect = 0;
                int freeIndex = -1;
                int stealIndex = -1;
                double earliestEnd = double.PositiveInfinity;

                for (int index = 0; index < voices.Length; index++)
                {
                    Voice voice = voices[index];
                    bool active = presentationOwners[index] == null
                        ? voice.EndsAt > dspTime
                        : presentationPaused[index] || sources[index].isPlaying;
                    if (active && voice.Id == definition.Id)
                    {
                        activeForEffect++;
                    }

                    if (!active)
                    {
                        if (freeIndex < 0)
                        {
                            freeIndex = index;
                        }

                        continue;
                    }

                    if (voice.EndsAt < earliestEnd)
                    {
                        earliestEnd = voice.EndsAt;
                        stealIndex = index;
                    }
                }

                if (activeForEffect >= definition.MaxVoices)
                {
                    return false;
                }

                int targetIndex =
                    freeIndex >= 0 ? freeIndex : stealIndex;
                if (targetIndex < 0)
                {
                    return false;
                }

                AudioSource source = sources[targetIndex];
                source.Stop();
                RestorePresentationSource(targetIndex);
                if (presentationOwner != null)
                {
                    presentationOwners[targetIndex] = presentationOwner;
                    presentationVoiceCount++;
                    previousIgnoreListenerPause[targetIndex] = source.ignoreListenerPause;
                    source.ignoreListenerPause = true;
                }
                source.transform.position = position;
                source.clip = clip;
                source.volume =
                    definition.Volume * Mathf.Clamp01(volumeScale);
                source.pitch = pitch;
                source.priority = definition.Priority;
                source.spatialBlend = definition.SpatialBlend;
                source.Play();

                voices[targetIndex] = new Voice
                {
                    Id = definition.Id,
                    EndsAt =
                        dspTime +
                        clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch))
                };
                return true;
            }

            public void SetPresentationPaused(object owner, bool paused)
            {
                if (presentationVoiceCount == 0) return;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (!ReferenceEquals(presentationOwners[i], owner) || presentationPaused[i] == paused) continue;
                    presentationPaused[i] = paused;
                    if (paused) sources[i].Pause();
                    else sources[i].UnPause();
                }
            }

            public void ReleasePresentation(object owner)
            {
                if (presentationVoiceCount == 0) return;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (!ReferenceEquals(presentationOwners[i], owner)) continue;
                    sources[i].Stop();
                    RestorePresentationSource(i);
                    voices[i] = default;
                }
            }

            public void RefreshPresentationSources()
            {
                if (presentationVoiceCount == 0) return;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (presentationOwners[i] == null || presentationPaused[i] || sources[i].isPlaying) continue;
                    RestorePresentationSource(i);
                    voices[i] = default;
                }
            }

            private void RestorePresentationSource(int index)
            {
                if (presentationOwners[index] == null) return;
                sources[index].ignoreListenerPause = previousIgnoreListenerPause[index];
                presentationOwners[index] = null;
                presentationVoiceCount--;
                presentationPaused[index] = false;
            }

            public void StopAll()
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    sources[index].Stop();
                    RestorePresentationSource(index);
                    sources[index].clip = null;
                    voices[index] = default;
                }
            }
        }

        public const int UiPoolSize = 4;
        public const int WorldPoolSize = 5;
        public const int BarPoolSize = 5;
        public const int TotalPoolSize =
            UiPoolSize + WorldPoolSize + BarPoolSize;

        private static RetroAudioService instance;

        private AudioClip[] clips;
        // Per id, the bank of differently seeded clips for a cue that has
        // more than one; slot 0 is the same object as `clips[id]`. Null
        // where a cue has a single clip, which is nearly everything.
        private AudioClip[][] variantClips;
        private AudioClip[][] warehouseClips;
        private int[] lastVariants;
        private SourcePool[] pools;
        private double[] nextAllowedTimes;
        private double[] presentationNextAllowedTimes;
        private uint playSequence;
        private bool initialized;

        public static RetroAudioService Instance => instance;
        public bool IsInitialized => initialized;
        public int GeneratedClipCount { get; private set; }
        public int GeneratedVariantClipCount { get; private set; }
        public int GeneratedWarehouseClipCount { get; private set; }
        public int SourceCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            RetroAudio.SetPresentationCueOwner(null);
        }

        public static RetroAudioService EnsureInstalled()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<RetroAudioService>();
            if (instance != null)
            {
                instance.Initialize();
                return instance;
            }

            GameObject serviceObject = new GameObject(
                "[Bar Promenade] Retro Audio");
            instance = serviceObject.AddComponent<RetroAudioService>();
            return instance;
        }

        public int GetPoolSize(RetroSfxCategory category)
        {
            int index = (int)category;
            return pools != null &&
                   index > 0 &&
                   index < pools.Length &&
                   pools[index] != null
                ? pools[index].Count
                : 0;
        }

        public AudioClip GetClip(RetroSfxId id)
        {
            int index = (int)id;
            return clips != null &&
                   index > 0 &&
                   index < clips.Length
                ? clips[index]
                : null;
        }

        /// <summary>
        /// One clip of a cue's bank; variant 0 is <see cref="GetClip"/>.
        /// Null for a variant the cue has not got.
        /// </summary>
        public AudioClip GetClip(RetroSfxId id, int variant)
        {
            if (variant == 0)
            {
                return GetClip(id);
            }

            int index = (int)id;
            if (variantClips == null ||
                index <= 0 ||
                index >= variantClips.Length ||
                variantClips[index] == null ||
                variant < 0 ||
                variant >= variantClips[index].Length)
            {
                return null;
            }

            return variantClips[index][variant];
        }

        /// <summary>
        /// A warehouse-only dry impact; this read does not allocate or change
        /// the canonical clip accessors used elsewhere in the world.
        /// </summary>
        public AudioClip GetWarehouseClip(RetroSfxId id, int variant = 0)
        {
            int index = (int)id;
            if (warehouseClips == null || index <= 0 || index >= warehouseClips.Length ||
                warehouseClips[index] == null || variant < 0 ||
                variant >= warehouseClips[index].Length)
                return null;
            return warehouseClips[index][variant];
        }

        /// <summary>
        /// Called at warehouse construction, before input. Eleven mono clips
        /// are shared by all feet and firearms, including repeated rounds.
        /// </summary>
        public void WarmWarehouseClips()
        {
            Initialize();
            if (warehouseClips != null) return;
            warehouseClips = new AudioClip[clips.Length][];
            for (int index = 1; index < clips.Length; index++)
            {
                var id = (RetroSfxId)index;
                if (!RetroSfxLibrary.HasWarehouseTreatment(id)) continue;
                int variantCount = RetroSfxLibrary.GetDefinition(id).VariantCount;
                var bank = new AudioClip[variantCount];
                for (int variant = 0; variant < variantCount; variant++)
                {
                    bank[variant] = RetroSfxLibrary.CreateWarehouseRuntimeClip(id, variant);
                    GeneratedWarehouseClipCount++;
                }
                warehouseClips[index] = bank;
            }
        }

        /// <summary>The bank slot the last play of this cue used.</summary>
        public int GetLastVariant(RetroSfxId id)
        {
            int index = (int)id;
            return lastVariants != null &&
                   index > 0 &&
                   index < lastVariants.Length
                ? lastVariants[index]
                : 0;
        }

        public bool TryPlay(
            RetroSfxId id,
            Vector3 position)
        {
            return TryPlay(id, position, 1f);
        }

        /// <summary>
        /// Plays a cue at a fraction of its authored volume: another body's
        /// footstep is the same sound as the hero's, further from the ear.
        /// </summary>
        public bool TryPlay(
            RetroSfxId id,
            Vector3 position,
            float volumeScale)
        {
            Initialize();
            int index = (int)id;
            if (index <= 0 ||
                index >= clips.Length ||
                clips[index] == null)
            {
                return false;
            }

            RetroSfxDefinition definition =
                RetroSfxLibrary.GetDefinition(id);
            double dspTime = AudioSettings.dspTime;
            bool presentationCue = RetroAudio.PresentationCueOwner != null;
            double cueTime = presentationCue ? Time.realtimeSinceStartupAsDouble : dspTime;
            double[] cueCooldowns = presentationCue ? presentationNextAllowedTimes : nextAllowedTimes;
            if (cueTime < cueCooldowns[index])
            {
                return false;
            }

            SourcePool pool = pools[(int)definition.Category];
            AudioClip clip = clips[index];
            AudioClip[] bank = variantClips[index];
            bool warehouseTreatment = GameAudioMixer.CurrentProfile == GameAudioProfile.CombatWarehouse &&
                RetroSfxLibrary.HasWarehouseTreatment(id);
            if (warehouseTreatment)
            {
                // Defensive fallback for direct callers; production warms the
                // finite bank when composing the warehouse, before gameplay.
                WarmWarehouseClips();
                bank = warehouseClips[index];
                clip = bank[0];
            }
            if (bank != null && bank.Length > 1)
            {
                int variant = RetroSfxLibrary.NextVariant(
                    lastVariants[index],
                    bank.Length,
                    playSequence);
                lastVariants[index] = variant;
                clip = bank[variant] != null ? bank[variant] : clip;
            }

            float pitch = GetNextPitch(definition);
            if (warehouseTreatment) pitch *= RetroSfxLibrary.GetWarehousePitchScale(id);
            if (!pool.TryPlay(
                    definition,
                    clip,
                    position,
                    pitch,
                    dspTime,
                    volumeScale,
                    RetroAudio.PresentationCueOwner))
            {
                return false;
            }

            cueCooldowns[index] = cueTime + definition.CooldownSeconds;
            return true;
        }

        internal void SetPresentationPaused(object owner, bool paused)
        {
            if (owner == null || pools == null) return;
            for (int i = 1; i < pools.Length; i++) pools[i]?.SetPresentationPaused(owner, paused);
        }

        internal void ReleasePresentation(object owner)
        {
            if (owner == null || pools == null) return;
            for (int i = 1; i < pools.Length; i++) pools[i]?.ReleasePresentation(owner);
        }

        private void Update()
        {
            if (pools == null) return;
            for (int i = 1; i < pools.Length; i++) pools[i]?.RefreshPresentationSources();
        }

        public void StopAll()
        {
            if (pools == null)
            {
                return;
            }

            for (int index = 1; index < pools.Length; index++)
            {
                pools[index]?.StopAll();
            }

            if (nextAllowedTimes != null)
            {
                for (int index = 0;
                     index < nextAllowedTimes.Length;
                     index++)
                {
                    nextAllowedTimes[index] = 0d;
                    presentationNextAllowedTimes[index] = 0d;
                }
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            StopAll();
            DestroyGeneratedClips();
            instance = null;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            clips = new AudioClip[(int)RetroSfxId.Count];
            variantClips = new AudioClip[clips.Length][];
            lastVariants = new int[clips.Length];
            nextAllowedTimes = new double[clips.Length];
            presentationNextAllowedTimes = new double[clips.Length];
            for (int index = 1; index < clips.Length; index++)
            {
                var id = (RetroSfxId)index;
                clips[index] = RetroSfxLibrary.CreateRuntimeClip(id);
                GeneratedClipCount++;

                int variantCount =
                    RetroSfxLibrary.GetDefinition(id).VariantCount;
                if (variantCount <= 1)
                {
                    continue;
                }

                var bank = new AudioClip[variantCount];
                bank[0] = clips[index];
                for (int variant = 1; variant < variantCount; variant++)
                {
                    bank[variant] =
                        RetroSfxLibrary.CreateRuntimeClip(id, variant);
                    GeneratedVariantClipCount++;
                }

                variantClips[index] = bank;
            }

            pools = new SourcePool[(int)RetroSfxCategory.Count];
            pools[(int)RetroSfxCategory.Ui] =
                new SourcePool(
                    transform,
                    "UI",
                    UiPoolSize,
                    GameAudioGroup.Ui);
            pools[(int)RetroSfxCategory.World] =
                new SourcePool(
                    transform,
                    "World",
                    WorldPoolSize,
                    GameAudioGroup.SfxWorld);
            pools[(int)RetroSfxCategory.Bar] =
                new SourcePool(
                    transform,
                    "Bar",
                    BarPoolSize,
                    GameAudioGroup.SfxGameplay);
            SourceCount = TotalPoolSize;
            initialized = true;
        }

        private float GetNextPitch(RetroSfxDefinition definition)
        {
            playSequence++;
            int step = (int)(
                (playSequence * 1664525u +
                 (uint)definition.Id * 1013904223u) %
                5u) - 2;
            return 1f + step * definition.PitchVariation * 0.5f;
        }

        private void DestroyGeneratedClips()
        {
            if (clips == null)
            {
                return;
            }

            for (int index = 1; index < clips.Length; index++)
            {
                DestroyClip(clips[index]);
                AudioClip[] bank =
                    variantClips != null ? variantClips[index] : null;
                if (bank == null)
                {
                    continue;
                }

                // Slot 0 is the clip destroyed just above.
                for (int variant = 1; variant < bank.Length; variant++)
                {
                    DestroyClip(bank[variant]);
                }
            }

            clips = null;
            variantClips = null;
            if (warehouseClips != null)
            {
                for (int index = 1; index < warehouseClips.Length; index++)
                {
                    AudioClip[] bank = warehouseClips[index];
                    if (bank == null) continue;
                    for (int variant = 0; variant < bank.Length; variant++)
                        DestroyClip(bank[variant]);
                }
            }
            warehouseClips = null;
            lastVariants = null;
            GeneratedClipCount = 0;
            GeneratedVariantClipCount = 0;
            GeneratedWarehouseClipCount = 0;
            initialized = false;
        }

        private static void DestroyClip(AudioClip clip)
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
        }
    }

    public static class RetroAudio
    {
        internal static object PresentationCueOwner { get; private set; }
        internal static object SetPresentationCueOwner(object owner)
        {
            object previous = PresentationCueOwner;
            PresentationCueOwner = owner;
            return previous;
        }

        public static bool Play(RetroSfxId id)
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            return RetroAudioService
                .EnsureInstalled()
                .TryPlay(id, Vector3.zero);
        }

        public static bool PlayAt(
            RetroSfxId id,
            Vector3 worldPosition)
        {
            return PlayAt(id, worldPosition, 1f);
        }

        public static bool PlayAt(
            RetroSfxId id,
            Vector3 worldPosition,
            float volumeScale)
        {
            // Outside play mode there is nothing to hear and no scene to
            // keep the service in: EditMode tests now drive the nausea and
            // vomit controllers to their outcomes, and the service's
            // DontDestroyOnLoad is an error there rather than a sound.
            if (!Application.isPlaying)
            {
                return false;
            }

            return RetroAudioService
                .EnsureInstalled()
                .TryPlay(id, worldPosition, volumeScale);
        }
    }
}
