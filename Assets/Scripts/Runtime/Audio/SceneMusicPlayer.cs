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
        // The clip the pending background load belongs to. Whoever hands the
        // source another clip mid-load must not inherit a wait for data that
        // clip already has; the old synchronous import never left one behind.
        private AudioClip loadingClip;
        private bool resumeWhenClipLoads;
        private bool detachedForSceneExit;
        private bool fadeInDeferred;
        private bool playbackRequested;
        private bool playbackSuppressed;

        /// <summary>
        /// True while a theme born suppressed has not loaded its clip yet.
        /// A city score under a switched-on radio, or a radio under a
        /// switched-off one, may never be heard in this scene, so it must
        /// not pay the decode at Awake; the load happens the first time the
        /// gate opens. Until then the player reads as Unavailable.
        /// </summary>
        private bool themeDeferred;
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
        public bool IsPlaybackSuppressed => playbackSuppressed;

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
        protected virtual AudioClip LoadThemeClip() => Resources.Load<AudioClip>(TrackResourcePath);
        protected virtual bool InitiallySuppressed => false;
        protected virtual float SuppressionFadeOutSeconds => MusicMix.FadeOutSeconds;
        protected virtual float OutputVolume =>
            MusicMix.DefaultOutputVolume;

        protected virtual void PrepareForPlayback()
        {
        }

        /// <summary>
        /// Lets a scene-specific carrier prepare its sound before the
        /// shared music tail leaves the scene. Ordinary score sources need
        /// no preparation; a diegetic source can preserve its audible level
        /// when the next scene moves the listener elsewhere.
        /// </summary>
        protected virtual void PrepareForSceneExitFade()
        {
        }

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            toneFilter = GetComponent<AudioLowPassFilter>();
            ConfigureSource();
            ConfigureTone();
            playbackSuppressed = InitiallySuppressed;
            if (playbackSuppressed)
            {
                // Requested at birth exactly as a loaded theme would be, so
                // a later FadeOutAndPause/Resume pair still decides whether
                // the deferred load starts playing or parks.
                themeDeferred = true;
                playbackRequested = true;
                ApplyNormalizedGain(0f);
                PlaybackState = SceneMusicPlaybackState.Unavailable;
                return;
            }

            LoadTheme(true);
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
            PrepareForSceneExitFade();
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
            DropStaleClipWait();
            playbackRequested = false;
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
            DropStaleClipWait();
            playbackRequested = true;
            if (playbackSuppressed)
            {
                return;
            }
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

        /// <summary>
        /// Holds a theme without losing the location director's desired state.
        /// Suppression is established before loading can call Play, and later
        /// Resume requests cannot bypass it. Outgoing sources must be suppressed
        /// before incoming ones are released, to preserve the shared mix rule.
        /// </summary>
        public void SetPlaybackSuppressed(bool suppressed)
        {
            if (playbackSuppressed == suppressed || IsSceneExitFadeRequested)
            {
                return;
            }

            playbackSuppressed = suppressed;
            bool requested = playbackRequested;
            if (suppressed)
            {
                FadeOutAndPause(SuppressionFadeOutSeconds);
                playbackRequested = requested;
                return;
            }

            // The gate opened for the first time: pay the load now. A clip
            // that reached the source while the load was deferred is the
            // theme already and only needs the ordinary resume.
            bool loadDeferred = themeDeferred && ActiveClip == null;
            themeDeferred = false;
            if (loadDeferred)
            {
                LoadTheme(requested);
            }
            else if (requested)
            {
                ResumeWithFadeIn();
            }
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

        /// <summary>Replace this source's track while retaining its suppression gate.</summary>
        protected void ReloadTheme()
        {
            if (IsSceneExitFadeRequested) return;
            // A deferred player has nothing to replace; the load that the
            // open gate triggers reads the track path current at that time.
            if (themeDeferred) return;
            CancelFade();
            MusicMix.ReleaseFadeOut(audioSource);
            audioSource.Stop();
            waitingForClipLoad = false;
            resumeWhenClipLoads = false;
            sourcePaused = false;
            fadeInDeferred = false;
            LoadTheme(true);
        }

        /// <summary>
        /// Loads the track and, when playback is requested, starts it through
        /// the mixing rule once the data is in; otherwise it parks loaded and
        /// paused, the way a director-held theme waits.
        /// </summary>
        private void LoadTheme(bool requestPlayback)
        {
            playbackRequested = requestPlayback;
            AudioClip clip = LoadThemeClip();
            audioSource.clip = clip;
            ApplyNormalizedGain(0f);
            if (clip == null)
            {
                PlaybackState = SceneMusicPlaybackState.Unavailable;
                return;
            }

            resumeWhenClipLoads = requestPlayback;
            pendingFadeInDurationSeconds = MusicMix.FadeInSeconds;
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                CompleteClipLoad();
                return;
            }

            waitingForClipLoad = true;
            loadingClip = clip;
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

        private void DropStaleClipWait()
        {
            if (waitingForClipLoad && ActiveClip != loadingClip)
            {
                waitingForClipLoad = false;
                loadingClip = null;
            }
        }

        private void RefreshClipLoadState()
        {
            DropStaleClipWait();
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
            loadingClip = null;
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
            loadingClip = null;
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
            if (playbackSuppressed)
            {
                bool requested = playbackRequested;
                FadeOutAndPause(0f);
                playbackRequested = requested;
                return;
            }
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
                PrepareForPlayback();
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
