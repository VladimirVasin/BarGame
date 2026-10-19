using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two optional warehouse recordings at fresh nearby world positions after the fan has stopped.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatWarehouseAmbiencePlayer : MonoBehaviour
    {
        public const string ResourceFolder = "Audio/CombatWarehouseAmbience";
        public const string ClicksResourcePath = ResourceFolder + "/warehouse_clicks";
        public const string WhispersResourcePath = ResourceFolder + "/warehouse_whispers";
        public const float MinimumClicksDelaySeconds = 12f, MaximumClicksDelaySeconds = 25f;
        public const float MinimumWhispersDelaySeconds = 25f, MaximumWhispersDelaySeconds = 50f;
        public const float MinimumSpawnRadius = 3f, MaximumSpawnRadius = 7f;
        public const float MinimumAudibleDistance = 2f, MaximumAudibleDistance = 16f;
        public const float PlaybackSpeed = .8f, ToneCutoffFrequency = 2800f;
        private const int MaximumPlacementAttempts = 24;
        private const float SourceClearance = .15f;

        // Quiet room textures, with the same muffled tone and shared warehouse
        // returns as local machinery. Retain the supplied masters' relative trim.
        [SerializeField, Range(0f, 1f)] private float clicksVolume = .30f;
        [SerializeField, Range(0f, 1f)] private float whispersVolume = .15f;

        private sealed class Channel
        {
            public AudioSource Source;
            public ResourceRequest Request;
            public System.Random Random;
            public System.Random PositionRandom;
            public float MinimumDelay, MaximumDelay, Delay;
            public bool Playing;
            public int PlayCount;
            public string Cue;

            public void Schedule() => Delay = Mathf.Lerp(MinimumDelay, MaximumDelay, (float)Random.NextDouble());
        }

        private readonly Channel clicks = new Channel(), whispers = new Channel();
        private CombatTestRoot warehouse;
        private bool initialized, paused;
        public bool FanStopped { get; private set; }
        public AudioSource ClicksSource => clicks.Source;
        public AudioSource WhispersSource => whispers.Source;
        public int ClicksPlayCount => clicks.PlayCount;
        public int WhispersPlayCount => whispers.PlayCount;
        public float ClicksDelayRemaining => clicks.Delay;
        public float WhispersDelayRemaining => whispers.Delay;
        internal bool AutomaticPlayback { get; set; } = true;

        public void Initialize(CombatTestRoot owner)
        {
            if (initialized) throw new InvalidOperationException("Warehouse ambience already initialized.");
            if (owner == null || owner.WarehousePlan == null) throw new ArgumentNullException(nameof(owner));
            warehouse = owner;
            int seed = owner.WarehousePlan.Seed;
            InitializeChannel(clicks, "Warehouse insect clicks", ClicksResourcePath, clicksVolume,
                MinimumClicksDelaySeconds, MaximumClicksDelaySeconds, unchecked(seed ^ 0x434C4943));
            InitializeChannel(whispers, "Warehouse indistinct whispers", WhispersResourcePath, whispersVolume,
                MinimumWhispersDelaySeconds, MaximumWhispersDelaySeconds, unchecked(seed ^ 0x57484953));
            initialized = true;
            // The first layout precedes this retained carrier; later layouts push their state directly.
            WarehouseArenaMechanisms mechanisms = owner.GetComponentInChildren<WarehouseArenaMechanisms>(true);
            ApplyFanShutdown(mechanisms != null ? mechanisms.FanShutdownProgress : 0f);
            Advance(0f);
        }

        private void InitializeChannel(Channel channel, string name, string resourcePath, float volume,
            float minimum, float maximum, int seed)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            channel.Source = host.AddComponent<AudioSource>();
            channel.Cue = resourcePath == ClicksResourcePath ? "clicks" : "whispers";
            channel.Source.playOnAwake = channel.Source.loop = false;
            CityWorkAudio.Configure(channel.Source, true, ToneCutoffFrequency, MaximumAudibleDistance, false);
            channel.Source.minDistance = MinimumAudibleDistance;
            channel.Source.volume = volume;
            channel.Source.pitch = PlaybackSpeed;
            channel.Source.priority = 160;
            channel.MinimumDelay = minimum;
            channel.MaximumDelay = maximum;
            channel.Random = new System.Random(seed);
            channel.PositionRandom = new System.Random(unchecked(seed ^ 0x504F5349));
            channel.Schedule();
            channel.Request = Resources.LoadAsync<AudioClip>(resourcePath);
        }

        public void ApplyFanShutdown(float progress)
        {
            if (!initialized || !float.IsFinite(progress)) return;
            bool stopped = progress >= 1f;
            if (FanStopped == stopped) return;
            FanStopped = stopped;
            // Restoring an already stopped warehouse arms a fresh wait, never an immediate replay.
            StopChannel(clicks);
            StopChannel(whispers);
        }

        /// <summary>Bind supplied recordings without synthesizing a substitute for an empty slot.</summary>
        internal void SetClips(AudioClip clicksClip, AudioClip whispersClip)
        {
            if (!initialized) throw new InvalidOperationException("Warehouse ambience is not initialized.");
            BindClip(clicks, clicksClip);
            BindClip(whispers, whispersClip);
        }

        private static void BindClip(Channel channel, AudioClip clip)
        {
            channel.Request = null;
            StopChannel(channel);
            channel.Source.clip = clip;
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        private static void ResolveClip(Channel channel)
        {
            if (channel.Request == null || !channel.Request.isDone) return;
            AudioClip clip = channel.Request.asset as AudioClip;
            channel.Request = null;
            channel.Source.clip = clip;
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        public void Advance(float seconds)
        {
            if (!initialized) return;
            ResolveClip(clicks);
            ResolveClip(whispers);
            bool suspended = !isActiveAndEnabled || warehouse == null || !warehouse.isActiveAndEnabled ||
                GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning || !GameInput.MovementFocused;
            if (suspended != paused)
            {
                paused = suspended;
                SynchronizePause(clicks, paused);
                SynchronizePause(whispers, paused);
            }
            if (suspended || !FanStopped || !float.IsFinite(seconds) || seconds <= 0f) return;
            AdvanceChannel(clicks, seconds);
            AdvanceChannel(whispers, seconds);
        }

        private static void SynchronizePause(Channel channel, bool value)
        {
            if (value) channel.Source.Pause();
            else if (channel.Playing) channel.Source.UnPause();
        }

        private void AdvanceChannel(Channel channel, float seconds)
        {
            AudioClip clip = channel.Source.clip;
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded) return;
            if (channel.Playing)
            {
                if (channel.Source.isPlaying) return;
                channel.Playing = false;
                channel.Schedule();
                return;
            }
            channel.Delay = Mathf.Max(0f, channel.Delay - seconds);
            if (channel.Delay > 0f) return;
            if (!TryPlaceSource(channel))
            {
                // A temporarily obstructed neighbourhood retries at a bounded rate without emitting inside a solid.
                channel.Delay = 1f;
                return;
            }
            channel.Source.Play();
            channel.Playing = true;
            channel.PlayCount++;
            GameLog.Info("audio", "warehouse_ambience_started", GameLog.Field("cue", channel.Cue),
                GameLog.Field("gain", channel.Source.volume),
                GameLog.Field("distance_from_hero", Vector3.Distance(channel.Source.transform.position, warehouse.Hero.transform.position)));
            // Deliberately discard overshoot: a long frame never queues missed repetitions.
        }

        private bool TryPlaceSource(Channel channel)
        {
            if (warehouse.Hero == null) return false;
            Vector3 centre = warehouse.Hero.transform.position + Vector3.up * 1.35f;
            for (int attempt = 0; attempt < MaximumPlacementAttempts; attempt++)
            {
                float angle = (float)channel.PositionRandom.NextDouble() * Mathf.PI * 2f;
                // Square-root sampling gives the annulus uniform area density.
                float radius = Mathf.Sqrt(Mathf.Lerp(MinimumSpawnRadius * MinimumSpawnRadius,
                    MaximumSpawnRadius * MaximumSpawnRadius, (float)channel.PositionRandom.NextDouble()));
                Vector3 point = centre + new Vector3(Mathf.Cos(angle) * radius,
                    (float)channel.PositionRandom.NextDouble() * .8f - .4f, Mathf.Sin(angle) * radius);
                if (Mathf.Abs(point.x) > WarehouseArenaPlan.HalfWidth - SourceClearance ||
                    Mathf.Abs(point.z) > WarehouseArenaPlan.HalfDepth - SourceClearance ||
                    point.y < SourceClearance || point.y > WarehouseArenaPlan.CeilingHeight - SourceClearance ||
                    channel.PlayCount > 0 && (point - channel.Source.transform.position).sqrMagnitude < 1f ||
                    Physics.CheckSphere(point, SourceClearance, ~0, QueryTriggerInteraction.Ignore)) continue;
                channel.Source.transform.position = point;
                return true;
            }
            return false;
        }

        private static void StopChannel(Channel channel)
        {
            if (channel.Source != null) channel.Source.Stop();
            channel.Playing = false;
            if (channel.Random != null) channel.Schedule();
        }

        private void Update() { if (AutomaticPlayback) Advance(Time.unscaledDeltaTime); }

        private void OnDisable()
        {
            StopChannel(clicks);
            StopChannel(whispers);
            paused = false;
        }

        private void OnDestroy()
        {
            clicks.Request = whispers.Request = null;
            if (clicks.Source != null) clicks.Source.clip = null;
            if (whispers.Source != null) whispers.Source.clip = null;
            warehouse = null;
        }
    }
}
