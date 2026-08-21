using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public sealed class HomeSmokingMusicPlayer :
        MonoBehaviour,
        IMusicMixSource
    {
        public const string ResourceFolder = "Audio/SmokingMusic";
        public const string TrackName = "smoking_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float TargetVolume = 0.50f;
        public const float CutoffFrequency = 15500f;

        private bool startDeferred;
        private bool entryRampActive;
        private float entryScale = 1f;
        private float entryElapsedSeconds;
        private bool ruleFadeOutActive;
        private float ruleFadeOutStartVolume;
        private float ruleFadeOutElapsedSeconds;
        private bool detachedForSceneExit;

        public AudioSource Source { get; private set; }
        public AudioLowPassFilter ToneFilter { get; private set; }
        public AudioClip ActiveClip => Source != null
            ? Source.clip
            : null;
        public float NormalizedGain { get; private set; }

        /// <summary>
        /// True while the vignette theme is held silent because an earlier
        /// theme has not finished its fade-out yet.
        /// </summary>
        public bool IsStartDeferred => startDeferred;

        /// <summary>
        /// True while the theme is leaving through the shared four-second
        /// fade-out instead of being cut.
        /// </summary>
        public bool IsRuleFadeOutActive => ruleFadeOutActive;

        public bool IsSceneExitFadeComplete => !ruleFadeOutActive;

        private void Awake()
        {
            Source = GetComponent<AudioSource>();
            ToneFilter = GetComponent<AudioLowPassFilter>();

            ConfigureSource();
            ConfigureTone();
            Source.clip = Resources.Load<AudioClip>(ResourcePath);
            ApplyNormalizedGain(0f);
        }

        private void Update()
        {
            AdvanceMix(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Starts the vignette theme under the mixing rule: it restarts its
        /// own tail freely, but waits in silence for any other theme that is
        /// still fading out and then eases in.
        /// </summary>
        public void BeginFromStart()
        {
            if (Source == null)
            {
                return;
            }

            CancelRuleFadeOut();
            MusicMix.ReleaseFadeOut(Source);
            Source.Stop();
            if (Source.clip != null)
            {
                Source.timeSamples = 0;
            }

            NormalizedGain = 0f;
            if (MusicMix.IsFadeOutActive)
            {
                startDeferred = true;
                entryRampActive = false;
                entryScale = 0f;
                ApplyVolume();
                return;
            }

            startDeferred = false;
            entryRampActive = false;
            entryScale = 1f;
            ApplyVolume();
            if (Source.clip != null)
            {
                Source.Play();
            }
        }

        public void ApplyNormalizedGain(float normalizedGain)
        {
            NormalizedGain = Mathf.Clamp01(normalizedGain);
            if (ruleFadeOutActive)
            {
                return;
            }

            ApplyVolume();
        }

        /// <summary>
        /// Hands the theme to the mix so it leaves through the shared
        /// four-second fade-out. Repeat calls do not restart the fade.
        /// </summary>
        public bool BeginRuleFadeOut()
        {
            if (ruleFadeOutActive)
            {
                return true;
            }

            startDeferred = false;
            entryRampActive = false;
            if (Source == null ||
                !Source.isPlaying ||
                Source.volume <= MusicMix.SilentVolumeThreshold)
            {
                StopImmediate();
                return false;
            }

            MusicMix.BeginFadeOut(Source);
            ruleFadeOutActive = true;
            ruleFadeOutStartVolume = Source.volume;
            ruleFadeOutElapsedSeconds = 0f;
            return true;
        }

        public bool BeginSceneExitFadeOut()
        {
            if (!BeginRuleFadeOut())
            {
                return false;
            }

            detachedForSceneExit = MusicMix.BeginDetachedFadeOut(Source);
            return true;
        }

        public void CompleteSceneExitFadeImmediately()
        {
            StopImmediate();
            ReleaseDetachedSceneExit();
        }

        /// <summary>
        /// A vignette theme that left its scene has nothing to come back
        /// to once it reaches zero, so it takes its carrier object with it.
        /// </summary>
        private void ReleaseDetachedSceneExit()
        {
            if (!detachedForSceneExit)
            {
                return;
            }

            detachedForSceneExit = false;
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
        }

        public void StopImmediate()
        {
            CancelRuleFadeOut();
            startDeferred = false;
            entryRampActive = false;
            entryScale = 1f;
            if (Source != null)
            {
                MusicMix.ReleaseFadeOut(Source);
                Source.Stop();
                if (Source.clip != null)
                {
                    Source.timeSamples = 0;
                }
            }

            ApplyNormalizedGain(0f);
        }

        /// <summary>
        /// Drives the deferred start, the eased entry and the rule fade-out
        /// on unscaled time, so a paused game never strands the mix.
        /// </summary>
        public void AdvanceMix(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime < 0f ||
                float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime))
            {
                return;
            }

            AdvanceRuleFadeOut(unscaledDeltaTime);
            ResolveDeferredStart();
            AdvanceEntryRamp(unscaledDeltaTime);
        }

        private void ResolveDeferredStart()
        {
            if (!startDeferred || MusicMix.IsFadeOutActive)
            {
                return;
            }

            startDeferred = false;
            entryRampActive = true;
            entryElapsedSeconds = 0f;
            entryScale = 0f;
            ApplyVolume();
            if (Source != null && Source.clip != null)
            {
                Source.timeSamples = 0;
                Source.Play();
            }
        }

        private void AdvanceEntryRamp(float unscaledDeltaTime)
        {
            if (!entryRampActive)
            {
                return;
            }

            entryElapsedSeconds = Mathf.Min(
                entryElapsedSeconds + unscaledDeltaTime,
                MusicMix.FadeInSeconds);
            float normalizedTime = MusicMix.FadeInSeconds > 0f
                ? Mathf.Clamp01(
                    entryElapsedSeconds / MusicMix.FadeInSeconds)
                : 1f;
            entryScale = Smooth01(normalizedTime);
            ApplyVolume();
            if (normalizedTime >= 1f)
            {
                entryRampActive = false;
                entryScale = 1f;
                ApplyVolume();
            }
        }

        private void AdvanceRuleFadeOut(float unscaledDeltaTime)
        {
            if (!ruleFadeOutActive)
            {
                return;
            }

            ruleFadeOutElapsedSeconds = Mathf.Min(
                ruleFadeOutElapsedSeconds + unscaledDeltaTime,
                MusicMix.FadeOutSeconds);
            float normalizedTime = MusicMix.FadeOutSeconds > 0f
                ? Mathf.Clamp01(
                    ruleFadeOutElapsedSeconds / MusicMix.FadeOutSeconds)
                : 1f;
            if (Source != null)
            {
                Source.volume = Mathf.Lerp(
                    ruleFadeOutStartVolume,
                    0f,
                    Smooth01(normalizedTime));
            }

            if (normalizedTime >= 1f)
            {
                StopImmediate();
                ReleaseDetachedSceneExit();
            }
        }

        private void CancelRuleFadeOut()
        {
            ruleFadeOutActive = false;
            ruleFadeOutStartVolume = 0f;
            ruleFadeOutElapsedSeconds = 0f;
        }

        private void ApplyVolume()
        {
            if (Source != null)
            {
                Source.volume =
                    TargetVolume * NormalizedGain * entryScale;
            }
        }

        private void OnDisable()
        {
            StopImmediate();
        }

        private void OnDestroy()
        {
            StopImmediate();
        }

        private void ConfigureSource()
        {
            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0f;
            Source.dopplerLevel = 0f;
            Source.priority = 64;
            GameAudioMixer.Route(Source, GameAudioGroup.Music);
        }

        private void ConfigureTone()
        {
            ToneFilter.cutoffFrequency = CutoffFrequency;
            ToneFilter.lowpassResonanceQ = 1f;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
