using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Rendering
{
    /// <summary>A camera-owned, finite swallow cue; ordinary intoxication keeps its own clock.</summary>
    public static class HawthornHealingPulse
    {
        private static Camera owner;
        private static HawthornHealingPulseTimeline timeline;
        private static int lastFrame = -1;
        internal static event System.Action Cleared;
        internal static uint Sequence { get; private set; }
        internal static bool IsPaused => GameTimeScaleRuntime.IsPaused ||
            PauseMenuController.IsAnyPaused || !GameCursor.ApplicationFocused;

        public static void Trigger(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled ||
                camera.cameraType != CameraType.Game ||
                camera.TryGetComponent(out ScenePresentationExclusion _)) return;
            if (owner != camera) timeline = default;
            owner = camera;
            timeline.Trigger();
            unchecked { Sequence++; }
            // The frame before the swallow does not belong to its visual cue.
            lastFrame = Time.frameCount;
        }

        public static void Clear(Camera camera)
        {
            if (owner == camera) Clear();
        }

        public static void Clear()
        {
            owner = null;
            timeline = default;
            lastFrame = -1;
            Cleared?.Invoke();
        }

        internal static void AdvanceFrame()
        {
            if (!timeline.Active || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Advance(Time.unscaledDeltaTime, IsPaused);
        }

        internal static void Advance(float unscaledSeconds, bool paused)
        {
            if (!timeline.Active) return;
            if (owner == null || !owner.isActiveAndEnabled ||
                SceneTransitionService.IsTransitioning)
            {
                Clear();
                return;
            }
            timeline.Advance(unscaledSeconds, paused);
            if (!timeline.Active) Clear();
        }

        internal static Vector4 ForCamera(Camera camera) => owner != null && camera == owner && camera.isActiveAndEnabled &&
            !camera.TryGetComponent(out ScenePresentationExclusion _)
                ? timeline.Sample(GraphicsEffectsSettings.IntoxicationLensFxEnabled)
                : Vector4.zero;

        private static void OnSceneUnloaded(Scene scene)
        {
            if (owner == null || owner.gameObject.scene == scene) Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clear();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
    }

    internal struct HawthornHealingPulseTimeline
    {
        internal const float DurationSeconds = 4f;
        internal float Elapsed { get; private set; }
        internal bool Active { get; private set; }
        private Vector3 entryStrength;

        internal void Trigger()
        {
            Vector4 live = Sample(true);
            entryStrength = new Vector3(live.x, live.y, live.z);
            Elapsed = 0f;
            Active = true;
        }

        internal void Advance(float seconds, bool paused)
        {
            if (!Active || paused || !(seconds > 0f) || float.IsInfinity(seconds)) return;
            Elapsed = Mathf.Min(DurationSeconds, Elapsed + seconds);
            if (Elapsed >= DurationSeconds) Active = false;
        }

        // Colour, edge pressure, lens separation and the independent cue clock.
        internal Vector4 Sample(bool lensEnabled)
        {
            if (!Active) return Vector4.zero;
            float attack = Smooth(Elapsed / .18f);
            float colour = Mathf.Lerp(entryStrength.x, 1f, attack) *
                (1f - Smooth((Elapsed - .55f) / 3.45f));
            float edges = Mathf.Lerp(entryStrength.y, 1f, attack) *
                (1f - Smooth((Elapsed - .35f) / .95f));
            float lens = lensEnabled ? Mathf.Lerp(entryStrength.z, 1f, attack) *
                (1f - Smooth((Elapsed - .28f) / .72f)) : 0f;
            return new Vector4(colour, edges, lens, Elapsed);
        }

        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
