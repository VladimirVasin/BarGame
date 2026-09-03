using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Anything that can hold a music theme while a scene is alive. The
    /// scene transition asks every one of them to leave through the mixing
    /// rule, so a location change never cuts a theme dead.
    /// </summary>
    public interface IMusicMixSource
    {
        /// <summary>
        /// True once this source has no audible tail left to hand over.
        /// </summary>
        bool IsSceneExitFadeComplete { get; }

        /// <summary>
        /// Hands any audible tail to the mix. Returns true while the tail is
        /// still fading.
        /// </summary>
        bool BeginSceneExitFadeOut();

        /// <summary>
        /// Drops the tail without a fade, for teardown that cannot wait.
        /// </summary>
        void CompleteSceneExitFadeImmediately();
    }

    /// <summary>
    /// The one mixing rule every music change obeys. A theme always leaves
    /// through the same four-second fade-out, and no other theme is allowed
    /// to start while that tail is still audible, so themes hand over
    /// instead of overlapping. A departing theme is detached from its scene
    /// first, which lets the tail finish while the destination streams in
    /// rather than forcing a location change to stand still for it.
    /// </summary>
    public static class MusicMix
    {
        public const float FadeOutSeconds = 4f;
        public const float FadeInSeconds = 1f;
        public const float SilentVolumeThreshold = 0.0001f;

        // Calibrated against the current imported masters. Their raw
        // integrated loudness spans roughly eight LUFS; these source trims
        // place all six present themes near -30.5 LUFS after Music and Master
        // bus gains, before scene acoustics. Music remains a location colour,
        // while actions and short details keep the foreground.
        public const float CalibratedIntegratedTargetLufs = -30.5f;
        public const float ToneCutoffFrequency = 12000f;
        public const float DefaultOutputVolume = 0.40f;
        public const float CityOutputVolume = 0.92f;
        public const float BarOutputVolume = 0.36f;
        public const float HomeOutputVolume = 0.47f;
        public const float SmokingOutputVolume = 0.58f;
        public const float StairwellOutputVolume = 0.38f;
        public const float SupermarketOutputVolume = 0.52f;

        // The two themes whose masters do not exist yet keep the default
        // trim. There is nothing to measure against the calibration above
        // until a file lands, and guessing a number would only look like
        // it had been measured. Re-trim each one the day its track
        // arrives.
        public const float CemeteryOutputVolume = DefaultOutputVolume;
        public const float ChurchOutputVolume = DefaultOutputVolume;

        private static readonly List<AudioSource> FadingOut =
            new List<AudioSource>();

        /// <summary>
        /// True while an outgoing theme is still audible. Every music start
        /// waits for this to clear.
        /// </summary>
        public static bool IsFadeOutActive => AudibleFadeOutCount > 0;

        public static int AudibleFadeOutCount
        {
            get
            {
                Prune();
                return FadingOut.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FadingOut.Clear();
        }

        /// <summary>
        /// Claims an audible source as outgoing. Returns false when there is
        /// nothing left to fade, which callers treat as an immediate finish.
        /// </summary>
        public static bool BeginFadeOut(AudioSource source)
        {
            if (!IsAudible(source))
            {
                return false;
            }

            if (!FadingOut.Contains(source))
            {
                FadingOut.Add(source);
            }

            return true;
        }

        /// <summary>
        /// Same claim, but the source also leaves its scene so an unload
        /// cannot cut the tail short mid-fade.
        /// </summary>
        public static bool BeginDetachedFadeOut(AudioSource source)
        {
            if (!BeginFadeOut(source))
            {
                return false;
            }

            Detach(source.gameObject);
            return true;
        }

        /// <summary>
        /// Releases a source that is no longer outgoing, so a theme never
        /// waits on its own tail when it restarts or resumes.
        /// </summary>
        public static void ReleaseFadeOut(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            FadingOut.Remove(source);
        }

        public static void ClearFadeOuts()
        {
            FadingOut.Clear();
        }

        private static void Detach(GameObject owner)
        {
            if (owner == null || !Application.isPlaying)
            {
                return;
            }

            owner.transform.SetParent(null, true);
            Object.DontDestroyOnLoad(owner);
        }

        private static bool IsAudible(AudioSource source)
        {
            return source != null &&
                   source.isPlaying &&
                   source.volume > SilentVolumeThreshold;
        }

        private static void Prune()
        {
            for (int index = FadingOut.Count - 1; index >= 0; index--)
            {
                if (!IsAudible(FadingOut[index]))
                {
                    FadingOut.RemoveAt(index);
                }
            }
        }
    }
}
