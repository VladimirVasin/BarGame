using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional warehouse cassette; its final motor rundown follows the fan's clock.</summary>
    public sealed class CombatWarehouseMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/CombatWarehouseMusic";
        public const string TrackName = "warehouse_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        public const float PlaybackSpeed = .8f;

        private CombatTestRoot warehouse;
        private float tapeGain = 1f;
        private float mechanicalPitch = PlaybackSpeed;
        private bool pausedForGameplay;
        public float TapeStopProgress { get; private set; }
        public bool TapeStopped => TapeStopProgress >= 1f;

        protected override string TrackResourcePath => ResourcePath;
        protected override bool InitiallySuppressed => true;
        protected override float SuppressionFadeOutSeconds => 0f;
        protected override float OutputVolume => base.OutputVolume * tapeGain;
        protected override float AuthoredPlaybackPitch => mechanicalPitch;

        protected override void PrepareForPlayback() => ApplyTapeProperties();

        public void Initialize(CombatTestRoot owner)
        {
            warehouse = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            // The first layout exists before this retained music carrier. Later
            // layouts push their initial state explicitly from ApplyFanPower.
            WarehouseArenaMechanisms mechanisms = owner.GetComponentInChildren<WarehouseArenaMechanisms>(true);
            ApplyFanShutdown(mechanisms != null ? mechanisms.FanShutdownProgress : 0f);
        }

        public void ApplyFanShutdown(float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return;
            TapeStopProgress = Mathf.Clamp01(progress);
            ApplyTapeProperties();
            RefreshOwnerGate();
            SynchronizePause();
            if (TapeStopped) Source.Stop();
        }

        private void ApplyTapeProperties()
        {
            float progress = TapeStopProgress;
            float speed = 1f - progress;
            // All modulation is a pure function of the same five-second
            // mechanism envelope, so pause and manual stepping cannot drift.
            float time = progress * WarehouseArenaMechanisms.FanShutdownDuration;
            float chewing = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .10f));
            float wow = Mathf.Sin(time * Mathf.PI * 2f * .73f) * .055f;
            float flutter = Mathf.Sin(time * Mathf.PI * 2f * 7.3f) * .022f +
                Mathf.Sin(time * Mathf.PI * 2f * 12.7f) * .010f;
            float dip = Mathf.Pow(.5f + .5f * Mathf.Sin(time * Mathf.PI * 2f * 3.1f), 8f);
            tapeGain = speed * (1f - .55f * chewing * dip);
            mechanicalPitch = TapeStopped ? 0f : PlaybackSpeed * Mathf.Max(.025f,
                speed * (1f + (wow + flutter) * chewing));
            RefreshPlaybackRate();
            Source.volume = OutputVolume * NormalizedGain;
            ToneFilter.cutoffFrequency = Mathf.Lerp(420f, MusicMix.ToneCutoffFrequency,
                speed * speed) * (1f - .32f * chewing * dip);
        }

        private void SynchronizePause()
        {
            bool suspended = !IsDetachedForSceneExit &&
                (GameTimeScaleRuntime.IsPaused || warehouse == null || !warehouse.isActiveAndEnabled);
            if (suspended && !pausedForGameplay)
            {
                Source.Pause();
                pausedForGameplay = true;
            }
            else if (!suspended && pausedForGameplay)
            {
                if (!TapeStopped && (!IsPlaybackSuppressed || IsDetachedForSceneExit)) Source.UnPause();
                pausedForGameplay = false;
            }
        }

        private void RefreshOwnerGate()
        {
            // A scene-exit tail has already detached from the owner's hierarchy.
            // The shared player owns that lifetime and ignores suppression then.
            bool suppress = warehouse == null || !warehouse.isActiveAndEnabled || TapeStopped;
            // Opening a retained/deferred source during a modal pause must not
            // call Play before this local pause owner can hold its playhead.
            if (!suppress && GameTimeScaleRuntime.IsPaused) return;
            SetPlaybackSuppressed(suppress);
        }

        protected override void Update()
        {
            RefreshOwnerGate();
            SynchronizePause();
            if (!GameTimeScaleRuntime.IsPaused || IsDetachedForSceneExit) base.Update();
            if (TapeStopped && Source.isPlaying) Source.Stop();
        }
    }
}
