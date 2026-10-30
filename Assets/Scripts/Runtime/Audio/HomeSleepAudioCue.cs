using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A finite owned transformation on the existing Gameplay SFX pool. The sleep
    /// director pauses this voice with its presentation and releases it on every
    /// cancellation/disable/transition; no source, clip or replay timer lives here.
    /// </summary>
    public static class HomeSleepAudioCue
    {
        public const RetroSfxId CueId = RetroSfxId.HomeSleepStartle;
        public const float DurationSeconds = RetroSfxLibrary.HomeSleepStartleDurationSeconds;

        public static bool Trigger(RetroAudioService audio, object owner)
        {
            if (audio == null || owner == null || !Application.isPlaying)
                return false;
            object previous = RetroAudio.SetPresentationCueOwner(owner);
            try { return audio.TryPlay(CueId, Vector3.zero); }
            finally { RetroAudio.SetPresentationCueOwner(previous); }
        }

        public static void SetPaused(RetroAudioService audio, object owner, bool paused)
        {
            if (audio != null && owner != null) audio.SetPresentationPaused(owner, paused);
        }

        public static void Stop(RetroAudioService audio, object owner)
        {
            if (audio != null && owner != null) audio.ReleasePresentation(owner);
        }
    }
}
