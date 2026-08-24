using System;
using UnityEngine;

namespace BarPromenade
{
    public enum SceneMusicPlaybackState
    {
        Unavailable = 0,
        Loading,
        FadingIn,
        Playing,
        FadingOut,
        Paused,
        Silent,
        WaitingForMix
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public abstract class SceneMusicPlayer :
        MonoBehaviour,
        IMusicMixSource
    {
        private const float SilentGainThreshold = 0.0001f;

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioLowPassFilter toneFilter;

        private float fadeStartGain;
        private float fadeTargetGain;
        private float fadeElapsedSeconds;
        private float fadeDurationSeconds;
        private bool fadeActive;
        private bool pauseWhenFadeCompletes;
        private bool sourcePaused;
        private bool waitingForClipLoad;
        private bool resumeWhenClipLoads;
        private bool detachedForSceneExit;
        private bool fadeInDeferred;
        private float deferredFadeInDurationSeconds =
            MusicMix.FadeInSeconds;
        private float pendingFadeInDurationSeconds =
            MusicMix.FadeInSeconds;

        public AudioSource Source => audioSource;
        public AudioLowPassFilter ToneFilter => toneFilter;
        public AudioClip ActiveClip => audioSource != null
            ? audioSource.clip
            : null;
        public float NormalizedGain { get; private set; }
        public SceneMusicPlaybackState PlaybackState { get; private set; } =
            SceneMusicPlaybackState.Unavailable;
        public bool IsPaused =>
            PlaybackState == SceneMusicPlaybackState.Paused;
        public bool IsFadeActive => fadeActive;

        /// <summary>
        /// True while this theme is held silent because an earlier one is
        /// still fading out. The mixing rule never lets the two overlap.
        /// </summary>
        public bool IsFadeInDeferred => fadeInDeferred;

        /// <summary>
        /// True once the theme has left its scene to finish its fade-out in
        /// the persistent mix.
        /// </summary>
        public bool IsDetachedForSceneExit => detachedForSceneExit;
        public bool IsSceneExitFadeRequested { get; private set; }
        public bool IsSceneExitFadeComplete =>
            IsSceneExitFadeRequested &&
            (ActiveClip == null ||
             (!fadeActive &&
              NormalizedGain <= SilentGainThreshold));

        protected abstract string TrackResourcePath { get; }
        protected virtual float OutputVolume =>
            MusicMix.DefaultOutputVolume;

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            toneFilter = GetComponent<AudioLowPassFilter>();
            ConfigureSource();
            ConfigureTone();
            LoadTheme();
        }

        protected virtual void Update()
        {
            RefreshClipLoadState();
            RefreshDeferredFadeIn();
            AdvanceFade(Time.unscaledDeltaTime);
        }

        protected virtual void OnDisable()
        {
            if (IsSceneExitFadeRequested &&
                !IsSceneExitFadeComplete)
            {
                CompleteSceneExitFadeImmediately();
            }
        }

        public bool BeginSceneExitFadeOut()
        {
            return RequestSceneExitFade();
        }

        public bool RequestSceneExitFade()
        {
            return RequestSceneExitFade(MusicMix.FadeOutSeconds);
        }

        public bool RequestSceneExitFade(float durationSeconds)
        {
            ValidateDuration(durationSeconds);
            if (IsSceneExitFadeRequested)
            {
                return !IsSceneExitFadeComplete;
            }

            IsSceneExitFadeRequested = true;
            fadeInDeferred = false;
            if (ActiveClip == null ||
                NormalizedGain <= SilentGainThreshold)
            {
                CancelFade();
                resumeWhenClipLoads = false;
                ApplyNormalizedGain(0f);
                PlaybackState = ActiveClip == null
                    ? SceneMusicPlaybackState.Unavailable
                    : SceneMusicPlaybackState.Silent;
                return false;
            }

            // The tail outlives the scene that owns it, so a location change
            // hears the whole fade-out instead of standing still for it.
            detachedForSceneExit =
                MusicMix.BeginDetachedFadeOut(audioSource);
            BeginFade(0f, durationSeconds, false);
            return fadeActive;
        }

        public void FadeOutAndPause()
        {
            FadeOutAndPause(MusicMix.FadeOutSeconds);
        }

        public void FadeOutAndPause(float durationSeconds)
        {
            ValidateDuration(durationSeconds);
            IsSceneExitFadeRequested = false;
            fadeInDeferred = false;
            if (ActiveClip == null)
            {
                CancelFade();
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Unavailable;
                return;
            }

            if (waitingForClipLoad)
            {
                CancelFade();
                resumeWhenClipLoads = false;
                sourcePaused = true;
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Paused;
                return;
            }

            MusicMix.BeginFadeOut(audioSource);
            BeginFade(0f, durationSeconds, true);
        }

        public void ResumeWithFadeIn()
        {
            ResumeWithFadeIn(MusicMix.FadeInSeconds);
        }

        public void ResumeWithFadeIn(float durationSeconds)
        {
            ValidateDuration(durationSeconds);
            IsSceneExitFadeRequested = false;
            if (ActiveClip == null)
            {
                CancelFade();
                fadeInDeferred = false;
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Unavailable;
                return;
            }

            if (waitingForClipLoad)
            {
                CancelFade();
                fadeInDeferred = false;
                resumeWhenClipLoads = true;
                sourcePaused = false;
                pendingFadeInDurationSeconds = durationSeconds;
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Loading;
                return;
            }

            BeginFadeInThroughRule(durationSeconds);
        }

        public void CompleteSceneExitFadeImmediately()
        {
            IsSceneExitFadeRequested = true;
            CancelFade();
            fadeInDeferred = false;
            resumeWhenClipLoads = false;
            sourcePaused = false;
            if (audioSource != null)
            {
                audioSource.Stop();
            }

            ApplyNormalizedGain(0f);
            PlaybackState = ActiveClip == null
                ? SceneMusicPlaybackState.Unavailable
                : SceneMusicPlaybackState.Silent;
            ReleaseDetachedSceneExit();
        }

        public void AdvanceFade(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime),
                    unscaledDeltaTime,
                    "Music fade delta time must be finite and " +
                    "non-negative.");
            }

            if (!fadeActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            fadeElapsedSeconds = Mathf.Min(
                fadeElapsedSeconds + unscaledDeltaTime,
                fadeDurationSeconds);
            float normalizedTime = fadeDurationSeconds > 0f
                ? Mathf.Clamp01(
                    fadeElapsedSeconds / fadeDurationSeconds)
                : 1f;
            float smoothTime =
                normalizedTime *
                normalizedTime *
                (3f - 2f * normalizedTime);
            ApplyNormalizedGain(
                Mathf.Lerp(
                    fadeStartGain,
                    fadeTargetGain,
                    smoothTime));
            if (normalizedTime >= 1f)
            {
                CompleteFade();
            }
        }

        private void ConfigureSource()
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.volume = 0f;
            audioSource.priority = 64;
            GameAudioMixer.Route(
                audioSource,
                GameAudioGroup.Music);
        }

        private void ConfigureTone()
        {
            // The music keeps its dynamics and timing. This only rounds the
            // brittle top octave, unlike the stronger baked treatment on SFX.
            toneFilter.cutoffFrequency =
                MusicMix.ToneCutoffFrequency;
            toneFilter.lowpassResonanceQ = 1f;
        }

        private void LoadTheme()
        {
            AudioClip clip = Resources.Load<AudioClip>(TrackResourcePath);
            audioSource.clip = clip;
            ApplyNormalizedGain(0f);
            if (clip == null)
            {
                PlaybackState = SceneMusicPlaybackState.Unavailable;
                return;
            }

            resumeWhenClipLoads = true;
            pendingFadeInDurationSeconds = MusicMix.FadeInSeconds;
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                CompleteClipLoad();
                return;
            }

            waitingForClipLoad = true;
            PlaybackState = SceneMusicPlaybackState.Loading;
            bool loadRequested = clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                CompleteClipLoad();
            }
            else if (!loadRequested ||
                     clip.loadState == AudioDataLoadState.Failed)
            {
                FailClipLoad();
            }
        }

        private void RefreshClipLoadState()
        {
            if (!waitingForClipLoad || ActiveClip == null)
            {
                return;
            }

            if (ActiveClip.loadState == AudioDataLoadState.Loaded)
            {
                CompleteClipLoad();
            }
            else if (ActiveClip.loadState == AudioDataLoadState.Failed)
            {
                FailClipLoad();
            }
        }

        private void CompleteClipLoad()
        {
            waitingForClipLoad = false;
            if (IsSceneExitFadeRequested)
            {
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Silent;
                return;
            }

            if (!resumeWhenClipLoads || sourcePaused)
            {
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Paused;
                return;
            }

            BeginFadeInThroughRule(pendingFadeInDurationSeconds);
        }

        private void FailClipLoad()
        {
            waitingForClipLoad = false;
            resumeWhenClipLoads = false;
            sourcePaused = false;
            fadeInDeferred = false;
            CancelFade();
            audioSource.Stop();
            audioSource.clip = null;
            ApplyNormalizedGain(0f);
            PlaybackState = SceneMusicPlaybackState.Unavailable;
        }

        /// <summary>
        /// The only way a theme is allowed to start. It never plays a note
        /// while an earlier theme is still fading out; it waits silently and
        /// begins the moment the mix is clear.
        /// </summary>
        private void BeginFadeInThroughRule(float durationSeconds)
        {
            MusicMix.ReleaseFadeOut(audioSource);
            if (MusicMix.IsFadeOutActive)
            {
                CancelFade();
                fadeInDeferred = true;
                deferredFadeInDurationSeconds = durationSeconds;
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.WaitingForMix;
                return;
            }

            fadeInDeferred = false;
            EnsurePlaybackRunning();
            BeginFade(1f, durationSeconds, false);
        }

        private void RefreshDeferredFadeIn()
        {
            if (!fadeInDeferred || MusicMix.IsFadeOutActive)
            {
                return;
            }

            BeginFadeInThroughRule(deferredFadeInDurationSeconds);
        }

        private void BeginFade(
            float targetGain,
            float durationSeconds,
            bool pauseAfterFade)
        {
            fadeStartGain = NormalizedGain;
            fadeTargetGain = Mathf.Clamp01(targetGain);
            fadeElapsedSeconds = 0f;
            fadeDurationSeconds = durationSeconds;
            pauseWhenFadeCompletes = pauseAfterFade;

            if (durationSeconds <= 0f ||
                Mathf.Abs(fadeTargetGain - fadeStartGain) <=
                SilentGainThreshold)
            {
                ApplyNormalizedGain(fadeTargetGain);
                CompleteFade();
                return;
            }

            fadeActive = true;
            PlaybackState = fadeTargetGain > fadeStartGain
                ? SceneMusicPlaybackState.FadingIn
                : SceneMusicPlaybackState.FadingOut;
        }

        private void CompleteFade()
        {
            fadeActive = false;
            ApplyNormalizedGain(fadeTargetGain);
            if (NormalizedGain <= SilentGainThreshold)
            {
                if (pauseWhenFadeCompletes &&
                    audioSource != null &&
                    ActiveClip != null &&
                    !sourcePaused)
                {
                    audioSource.Pause();
                    sourcePaused = true;
                }

                PlaybackState = sourcePaused
                    ? SceneMusicPlaybackState.Paused
                    : SceneMusicPlaybackState.Silent;
                ReleaseDetachedSceneExit();
            }
            else
            {
                PlaybackState = SceneMusicPlaybackState.Playing;
            }

            pauseWhenFadeCompletes = false;
        }

        /// <summary>
        /// A theme that left its scene has nothing to come back to once it
        /// reaches zero, so it takes its carrier object with it.
        /// </summary>
        private void ReleaseDetachedSceneExit()
        {
            MusicMix.ReleaseFadeOut(audioSource);
            if (!detachedForSceneExit)
            {
                return;
            }

            detachedForSceneExit = false;
            if (audioSource != null)
            {
                audioSource.Stop();
            }

            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
        }

        private void CancelFade()
        {
            fadeActive = false;
            fadeElapsedSeconds = 0f;
            fadeDurationSeconds = 0f;
            pauseWhenFadeCompletes = false;
        }

        private void EnsurePlaybackRunning()
        {
            if (audioSource == null || ActiveClip == null)
            {
                return;
            }

            if (sourcePaused)
            {
                audioSource.UnPause();
                sourcePaused = false;
                if (audioSource.isPlaying)
                {
                    return;
                }
            }

            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        private void ApplyNormalizedGain(float normalizedGain)
        {
            NormalizedGain = Mathf.Clamp01(normalizedGain);
            if (audioSource != null)
            {
                audioSource.volume =
                    Mathf.Max(0f, OutputVolume) * NormalizedGain;
            }
        }

        private static void ValidateDuration(float durationSeconds)
        {
            if (float.IsNaN(durationSeconds) ||
                float.IsInfinity(durationSeconds) ||
                durationSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    durationSeconds,
                    "Music fade duration must be finite and " +
                    "non-negative.");
            }
        }
    }
}
