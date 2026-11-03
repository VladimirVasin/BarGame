using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace BarPromenade.Rendering
{
    /// <summary>One held, pre-composite scene image. It never captures its own echo or UI.</summary>
    internal sealed class HawthornAfterimageHistory : IDisposable
    {
        internal const float CaptureIntervalSeconds = .48f;
        private RTHandle texture;
        private Camera owner;
        private uint sequence;
        private bool valid, capturePending, poseValid;
        private float capturedAt, previousFov;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private int sourceWidth, sourceHeight;
        private TextureHandle imported;
        internal int CaptureCount { get; private set; }
        internal float AgeSeconds { get; private set; }
        internal float Strength { get; private set; }
        internal RenderTexture Texture => texture?.rt;
        internal bool HasSnapshot => valid && texture != null;

        internal HawthornAfterimageHistory() => HawthornHealingPulse.Cleared += Clear;

        internal TextureHandle Prepare(RenderGraph graph, TextureHandle source, Camera camera,
            Vector4 pulse, out Vector4 parameters)
        {
            parameters = Vector4.zero;
            imported = default;
            capturePending = false;
            if (pulse.x <= 0f || camera == null) return default;
            if (!GraphicsEffectsSettings.IntoxicationLensFxEnabled ||
                !HawthornHealingPulse.IsPaused && CounterMenuInput.IsBlockedByOtherUi())
            {
                Clear();
                return default;
            }
            TextureDesc descriptor = graph.GetTextureDesc(source);
            if (descriptor.dimension != TextureDimension.Tex2D) return default;
            Vector2Int size = descriptor.CalculateFinalDimensions();
            float scale = Mathf.Min(.5f, 960f / Mathf.Max(1, size.x), 540f / Mathf.Max(1, size.y));
            int width = Mathf.Max(1, Mathf.CeilToInt(size.x * scale));
            int height = Mathf.Max(1, Mathf.CeilToInt(size.y * scale));
            if (texture == null || texture.rt == null || texture.rt.width != width || texture.rt.height != height ||
                texture.rt.graphicsFormat != descriptor.colorFormat)
            {
                Clear();
                texture = RTHandles.Alloc(width, height, colorFormat: descriptor.colorFormat,
                    filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                    name: "Hawthorn Held Scene");
            }
            bool newCue = owner != camera || sequence != HawthornHealingPulse.Sequence;
            bool cut = poseValid && ((camera.transform.position - previousPosition).sqrMagnitude > 4f ||
                Quaternion.Angle(camera.transform.rotation, previousRotation) > 45f ||
                Mathf.Abs(camera.fieldOfView - previousFov) > 12f);
            if (newCue || cut || sourceWidth != size.x || sourceHeight != size.y)
            {
                valid = false;
                capturedAt = pulse.w;
                AgeSeconds = Strength = 0f;
                if (newCue) CaptureCount = 0;
            }
            owner = camera;
            sequence = HawthornHealingPulse.Sequence;
            sourceWidth = size.x;
            sourceHeight = size.y;
            previousPosition = camera.transform.position;
            previousRotation = camera.transform.rotation;
            previousFov = camera.fieldOfView;
            poseValid = true;
            imported = graph.ImportTexture(texture);
            AgeSeconds = valid ? Mathf.Max(0f, pulse.w - capturedAt) : 0f;
            capturePending = !HawthornHealingPulse.IsPaused && (!valid || AgeSeconds >= CaptureIntervalSeconds);
            Strength = valid ? EvaluateStrength(pulse.w, AgeSeconds) : 0f;
            parameters = new Vector4(Strength, AgeSeconds,
                (CaptureCount & 1) == 0 ? -1f : 1f, pulse.w);
            return valid ? imported : default;
        }

        // Record AFTER the scene composite reads the old image. The source is
        // the untouched incoming scene, so repeated snapshots cannot accumulate.
        internal void RecordCapture(RenderGraph graph, TextureHandle source, float elapsed)
        {
            if (!capturePending || !imported.IsValid()) return;
            graph.AddBlitPass(source, imported, Vector2.one, Vector2.zero,
                passName: "Hawthorn Freeze Scene Snapshot");
            capturedAt = elapsed;
            valid = true;
            capturePending = false;
            CaptureCount++;
        }

        internal static float EvaluateStrength(float elapsed, float age)
        {
            if (elapsed < 0f || elapsed >= HawthornHealingPulseTimeline.DurationSeconds ||
                age <= 0f || age >= CaptureIntervalSeconds) return 0f;
            float entry = Smooth((elapsed - .12f) / .28f);
            float lateEmphasis = Mathf.Lerp(.11f, .24f, Smooth((elapsed - 1.45f) / .7f));
            float recovery = 1f - Smooth((elapsed - 2.8f) / 1.2f);
            float shutter = Smooth(age / .09f) * (1f - Smooth((age - .30f) / .18f));
            return entry * lateEmphasis * recovery * shutter;
        }

        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        internal void Clear()
        {
            texture?.Release();
            texture = null;
            owner = null;
            valid = capturePending = poseValid = false;
            imported = default;
            CaptureCount = 0;
            AgeSeconds = Strength = capturedAt = 0f;
            sourceWidth = sourceHeight = 0;
        }

        public void Dispose()
        {
            HawthornHealingPulse.Cleared -= Clear;
            Clear();
        }
    }
}
