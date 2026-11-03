using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns one shared dry-pool voice for two active real seconds, independently of intoxication slow time.</summary>
    [DisallowMultipleComponent]
    public sealed class HawthornHealingAudio : MonoBehaviour
    {
        public const float DurationSeconds = 2f;
        public const RetroSfxId CueId = RetroSfxId.HawthornHealingSwell;
        private static HawthornHealingAudio activeOwner;
        private RetroAudioService service;
        private Camera cameraOwner;
        private bool voicePaused;
        private int triggerFrame;

        public bool IsActive { get; private set; }
        public float ElapsedActiveSeconds { get; private set; }
        public int TriggerCount { get; private set; }
        public static HawthornHealingAudio Current => activeOwner;

        /// <summary>Called during item preparation, before the contact frame can swallow.</summary>
        public static void Prepare(Camera camera)
        {
            if (camera == null) return;
            if (!camera.TryGetComponent(out HawthornHealingAudio cue)) cue = camera.gameObject.AddComponent<HawthornHealingAudio>();
            cue.cameraOwner = camera;
            cue.service = RetroAudioService.EnsureInstalled();
            cue.service.GetClip(CueId);
        }

        public static bool Play(Camera camera)
        {
            if (!Application.isPlaying || camera == null || !camera.isActiveAndEnabled ||
                camera.cameraType != CameraType.Game || camera.TryGetComponent(out ScenePresentationExclusion _)) return false;
            if (!camera.TryGetComponent(out HawthornHealingAudio cue))
            {
                Prepare(camera);
                cue = camera.GetComponent<HawthornHealingAudio>();
            }
            if (activeOwner != null) activeOwner.StopCue();
            cue.cameraOwner = camera;
            if (cue.service == null) cue.service = RetroAudioService.EnsureInstalled();
            cue.enabled = true;
            object previous = RetroAudio.SetPresentationCueOwner(cue);
            bool started;
            try { started = cue.service.TryPlay(CueId, Vector3.zero); }
            finally { RetroAudio.SetPresentationCueOwner(previous); }
            if (!started) return false;
            activeOwner = cue;
            cue.IsActive = true;
            cue.ElapsedActiveSeconds = 0f;
            cue.triggerFrame = Time.frameCount;
            cue.voicePaused = false;
            cue.TriggerCount++;
            cue.ApplyPause(GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused);
            return true;
        }

        public static void Clear(Camera camera)
        {
            if (activeOwner != null && activeOwner.cameraOwner == camera) activeOwner.StopCue();
        }

        public static void ResetSession()
        {
            if (activeOwner != null) activeOwner.StopCue();
        }

        private void Update()
        {
            if (!IsActive || Time.frameCount == triggerFrame) return;
            Advance(Time.unscaledDeltaTime, GameTimeScaleRuntime.IsPaused ||
                PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused);
        }

        internal void Advance(float realSeconds, bool paused)
        {
            if (!IsActive) return;
            if (cameraOwner == null || !cameraOwner.isActiveAndEnabled || SceneTransitionService.IsTransitioning)
            { StopCue(); return; }
            ApplyPause(paused);
            if (paused || !(realSeconds > 0f) || float.IsInfinity(realSeconds)) return;
            ElapsedActiveSeconds = Mathf.Min(DurationSeconds, ElapsedActiveSeconds + realSeconds);
            if (ElapsedActiveSeconds >= DurationSeconds) StopCue();
        }

        private void ApplyPause(bool paused)
        {
            if (voicePaused == paused) return;
            voicePaused = paused;
            if (service != null) service.SetPresentationPaused(this, paused);
        }

        private void StopCue()
        {
            if (service != null) service.ReleasePresentation(this);
            IsActive = false;
            voicePaused = false;
            if (activeOwner == this) activeOwner = null;
        }

        private void OnDisable() => StopCue();
        private void OnDestroy() => StopCue();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => activeOwner = null;
    }
}
