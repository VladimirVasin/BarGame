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

            public SourcePool(
                Transform parent,
                string name,
                int sourceCount,
                GameAudioGroup outputGroup)
            {
                sources = new AudioSource[sourceCount];
                voices = new Voice[sourceCount];

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
                double dspTime)
            {
                int activeForEffect = 0;
                int freeIndex = -1;
                int stealIndex = -1;
                double earliestEnd = double.PositiveInfinity;

                for (int index = 0; index < voices.Length; index++)
                {
                    Voice voice = voices[index];
                    bool active = voice.EndsAt > dspTime;
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
                source.transform.position = position;
                source.clip = clip;
                source.volume = definition.Volume;
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

            public void StopAll()
            {
                for (int index = 0; index < sources.Length; index++)
                {
                    sources[index].Stop();
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
        private SourcePool[] pools;
        private double[] nextAllowedTimes;
        private uint playSequence;
        private bool initialized;

        public static RetroAudioService Instance => instance;
        public bool IsInitialized => initialized;
        public int GeneratedClipCount { get; private set; }
        public int SourceCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
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

        public bool TryPlay(
            RetroSfxId id,
            Vector3 position)
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
            if (dspTime < nextAllowedTimes[index])
            {
                return false;
            }

            SourcePool pool = pools[(int)definition.Category];
            float pitch = GetNextPitch(definition);
            if (!pool.TryPlay(
                    definition,
                    clips[index],
                    position,
                    pitch,
                    dspTime))
            {
                return false;
            }

            nextAllowedTimes[index] =
                dspTime + definition.CooldownSeconds;
            return true;
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
            nextAllowedTimes = new double[clips.Length];
            for (int index = 1; index < clips.Length; index++)
            {
                clips[index] =
                    RetroSfxLibrary.CreateRuntimeClip((RetroSfxId)index);
                GeneratedClipCount++;
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
                AudioClip clip = clips[index];
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

            clips = null;
            GeneratedClipCount = 0;
            initialized = false;
        }
    }

    public static class RetroAudio
    {
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
                .TryPlay(id, worldPosition);
        }
    }
}
