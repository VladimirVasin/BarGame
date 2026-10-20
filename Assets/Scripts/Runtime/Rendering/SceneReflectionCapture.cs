using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Rendering
{
    /// <summary>Bounded asynchronous HDR capture shared by scene-specific reflection receivers.</summary>
    [DisallowMultipleComponent]
    public abstract class SceneReflectionCapture : MonoBehaviour
    {
        public const int Resolution = 256;
        public const int CaptureLayer = 31;
        private const float QuietSeconds = .25f;
        private const double CaptureTimeoutSeconds = 10d;
        // IndividualFaces needs fourteen engine frames for faces/filtering.
        // Cold shader/asset stalls can spend ten wall seconds before that cycle
        // gets to run. Give it one cycle, with a hard bound if frames stay scarce.
        private const int MinimumCaptureFrames = 16;
        private const double MaximumCaptureWallSeconds = 30d;
        private const int MaximumCaptureAttempts = 2;
        private static readonly ProfilerMarker SubmitMarker = new ProfilerMarker("Scene Reflection.Submit");
        private static readonly ProfilerMarker PollMarker = new ProfilerMarker("Scene Reflection.Poll");
        private static int qualityLeaseCount;
        private static bool previousRealtimeProbes;
        protected abstract SceneLightingPresentation Presentation { get; }
        protected abstract bool CapturePermitted { get; }
        protected virtual bool StableForCapture => true;
        protected Texture CapturedTexture => capturedTexture;
        protected Vector4 CapturedDecode => capturedDecode;
        protected uint SourceState => state;
        private int captureMask;
        private float farPlane, shadowRange;
        private Color background;
        private ReflectionProbeTimeSlicingMode slicing;
        private ReflectionProbe probe;
        private Texture capturedTexture;
        private Vector4 capturedDecode;
        private Bounds bounds;
        private Vector3 capturePosition;
        private int captureId = -1, generation, captureGeneration;
        private uint state;
        private double dirtyAt;
        private double captureStartedAt;
        private int captureStartedFrame;
        private int captureAttempts;
        private bool settingWasEnabled;
#if UNITY_EDITOR
        private bool forceTimeoutForTests;
        internal void DebugExpireCaptureForTests()
        {
            DebugHoldCaptureForTests(CaptureTimeoutSeconds + 1d, MinimumCaptureFrames);
        }
        internal void DebugHoldCaptureForTests(double elapsedSeconds, int elapsedFrames)
        {
            if (captureId < 0) throw new InvalidOperationException("A capture must be pending.");
            captureStartedAt = Time.realtimeSinceStartupAsDouble - elapsedSeconds;
            captureStartedFrame = Time.frameCount - elapsedFrames;
            forceTimeoutForTests = true;
        }
        internal void DebugResumeCaptureForTests() => forceTimeoutForTests = false;
#endif
        private bool initialized, wanted, valid, qualityLease, captureFailureReported;

        public bool IsReady => valid && probe != null && capturedTexture != null;
        public bool CaptureInProgress => captureId >= 0;
        public int CaptureCount { get; private set; }
        public int CaptureCompletedCount { get; private set; }
        public int CaptureFailureCount { get; private set; }
        public int CaptureCancelledCount { get; private set; }
        public int CaptureTimeoutCount { get; private set; }
        // Request/poll CPU time and asynchronous wall latency are distinct:
        // neither CPU counter represents the cubemap faces/filter GPU cost.
        public long CaptureSubmitTicks { get; private set; }
        public long CapturePollTicks { get; private set; }
        public double TotalCaptureLatencySeconds { get; private set; }
        public double LastCaptureLatencySeconds { get; private set; }
        public float CurrentGain { get; protected set; }
        public Bounds Bounds => bounds;

        protected void InitializeCapture(Bounds area, Vector3 position, int mask,
            float far, float shadows, Color clearColor,
            ReflectionProbeTimeSlicingMode timeSlicing = ReflectionProbeTimeSlicingMode.IndividualFaces)
        {
            bounds = area; capturePosition = position; captureMask = mask;
            farPlane = far; shadowRange = shadows; background = clearColor; slicing = timeSlicing;
            initialized = true;
            settingWasEnabled = GraphicsEffectsSettings.CleanReflectionsEnabled;
            state = ReadState();
            dirtyAt = Time.realtimeSinceStartupAsDouble;
        }

        public void Prepare(bool requested)
        {
            bool settingEnabled = GraphicsEffectsSettings.CleanReflectionsEnabled;
            if (settingEnabled && !settingWasEnabled)
            {
                captureAttempts = 0;
                captureFailureReported = false;
                dirtyAt = Time.realtimeSinceStartupAsDouble;
            }
            settingWasEnabled = settingEnabled;
            wanted = requested;
            RefreshState();
            Bind(requested);
        }

        protected abstract uint ReadState();
        protected virtual void BeforeCapture() { }
        protected void SetCaptureBackground(Color color)
        {
            background = color;
            if (probe != null) probe.backgroundColor = color;
        }
        protected abstract void Bind(bool requested);
        protected abstract void ClearBinding();

        private void RefreshState()
        {
            if (!initialized) return;
            uint next = ReadState();
            if (next == state) return;
            state = next;
            generation++;
            captureAttempts = 0;
            captureFailureReported = false;
            valid = false;
            CurrentGain = 0f;
            dirtyAt = Time.realtimeSinceStartupAsDouble;
        }

        private void LateUpdate()
        {
            if (!initialized || Presentation == null || !Presentation.isActiveAndEnabled) return;
            RefreshState();
            if (captureId >= 0 && (probe == null || !probe.isActiveAndEnabled))
            {
                CaptureCancelledCount++;
                captureId = -1;
                valid = false;
                ReleaseQualityLease();
                dirtyAt = Time.realtimeSinceStartupAsDouble + .75d;
            }
            if (probe != null && !probe.isActiveAndEnabled) return;
            bool forcedTimeout = false;
#if UNITY_EDITOR
            forcedTimeout = forceTimeoutForTests;
#endif
            if (captureId >= 0 && probe != null && !forcedTimeout && PollCapture())
            {
                CaptureCompletedCount++;
                LastCaptureLatencySeconds = Time.realtimeSinceStartupAsDouble - captureStartedAt;
                TotalCaptureLatencySeconds += LastCaptureLatencySeconds;
                captureId = -1;
                // Retain the completed filtered result before returning the
                // global baking permission to its original quality setting.
                capturedTexture = probe.texture;
                // Native probe HDR data also carries its receiver intensity.
                // Our capture-only probe has intensity zero, while its floating
                // HDR pixels still contain the unscaled rendered radiance.
                capturedDecode = RawTextureDecode(capturedTexture, probe.textureHDRDecodeValues);
                valid = captureGeneration == generation && CapturePermitted && capturedTexture != null;
                ReleaseQualityLease();
            }
            if (captureId >= 0 && CaptureDeadlineReached(Time.realtimeSinceStartupAsDouble - captureStartedAt,
                Time.frameCount - captureStartedFrame))
            {
                double pendingSeconds = Time.realtimeSinceStartupAsDouble - captureStartedAt;
                int pendingFrames = Time.frameCount - captureStartedFrame;
                CaptureTimeoutCount++;
                CaptureFailureCount++;
                captureId = -1;
                valid = false;
                capturedTexture = null;
                CurrentGain = 0f;
                if (probe != null) Destroy(probe.gameObject);
                probe = null;
                ReleaseQualityLease();
                dirtyAt = Time.realtimeSinceStartupAsDouble + .75d;
#if UNITY_EDITOR
                forceTimeoutForTests = false;
#endif
                if (!captureFailureReported)
                {
                    captureFailureReported = true;
                    Debug.LogWarning("Scene reflection capture timed out; its quality lease was released. " +
                        gameObject.scene.name + "/" + GetType().Name + ": " + pendingSeconds.ToString("F1") +
                        " seconds, " + pendingFrames + " frames.", this);
                }
            }
            if (!wanted || valid || captureId >= 0 || !CapturePermitted ||
                captureAttempts >= MaximumCaptureAttempts ||
                !StableForCapture || Time.realtimeSinceStartupAsDouble - dirtyAt < QuietSeconds)
                return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EnsureProbe();
            BeforeCapture();
            captureGeneration = generation;
            captureAttempts++;
            long submitStarted = Stopwatch.GetTimestamp();
            try
            {
                using (SubmitMarker.Auto())
                {
                    AcquireQualityLease();
                    captureId = probe.RenderProbe();
                    // Submission can synchronously compile shaders/upload assets.
                    // Only its returned asynchronous request owns this watchdog.
                    captureStartedAt = Time.realtimeSinceStartupAsDouble;
                    captureStartedFrame = Time.frameCount;
                }
                if (captureId >= 0) CaptureCount++;
                else
                {
                    CaptureFailureCount++;
                    dirtyAt = Time.realtimeSinceStartupAsDouble + .75d;
                }
            }
            catch (Exception error)
            {
                CaptureFailureCount++;
                captureId = -1;
                dirtyAt = Time.realtimeSinceStartupAsDouble + .75d;
                if (!captureFailureReported)
                {
                    captureFailureReported = true;
                    Debug.LogWarning("Scene reflection capture could not start: " + error.Message, this);
                }
            }
            finally
            {
                CaptureSubmitTicks += Stopwatch.GetTimestamp() - submitStarted;
                // A rejected request has no asynchronous work to finish.
                if (captureId < 0) ReleaseQualityLease();
            }
        }

        internal static bool CaptureDeadlineReached(double elapsedSeconds, int elapsedFrames) =>
            elapsedSeconds >= MaximumCaptureWallSeconds ||
            elapsedSeconds >= CaptureTimeoutSeconds && elapsedFrames >= MinimumCaptureFrames;

        private bool PollCapture()
        {
            long started = Stopwatch.GetTimestamp();
            using (PollMarker.Auto())
            {
                try { return probe.IsFinishedRendering(captureId); }
                finally { CapturePollTicks += Stopwatch.GetTimestamp() - started; }
            }
        }

        private void EnsureProbe()
        {
            if (probe != null) return;
            var host = new GameObject(gameObject.scene.name + " static reflection capture");
            host.transform.SetParent(transform, false);
            host.transform.position = capturePosition;
            probe = host.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = slicing;
            probe.resolution = Resolution;
            probe.hdr = true;
            probe.boxProjection = true;
            // This probe is a capture service, never a native URP environment.
            // Forward+ can cluster it even for renderers with probe usage Off;
            // keep both its contribution and influence volume negligible.
            // The explicit custom shader retains its independent hall bounds.
            probe.center = Vector3.zero;
            probe.size = Vector3.one * .01f;
            probe.cullingMask = captureMask;
            probe.nearClipPlane = .1f;
            probe.farClipPlane = farPlane;
            probe.shadowDistance = shadowRange;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = background;
            // URP's reflection-probe render request filters the cube for glossy
            // roughness after its faces finish. A plain mip generation is not used.
            probe.intensity = 0f;
        }

        private static Vector4 RawTextureDecode(Texture texture, Vector4 decode)
        {
            if (texture == null) return decode;
            switch (texture.graphicsFormat)
            {
                case GraphicsFormat.R16G16B16A16_SFloat:
                case GraphicsFormat.R32G32B32A32_SFloat:
                case GraphicsFormat.B10G11R11_UFloatPack32:
                    return new Vector4(1f, 1f, 0f, 0f);
                default:
                    return decode;
            }
        }

        protected static float Power(Light light) => light != null && light.enabled && light.gameObject.activeInHierarchy
            ? Mathf.Max(0f, light.intensity) : 0f;

        private void AcquireQualityLease()
        {
            if (qualityLease) return;
            // Outgoing and incoming layouts may overlap while a time-sliced
            // render is pending. Only the last owner restores the baseline.
            if (qualityLeaseCount == 0)
            {
                previousRealtimeProbes = QualitySettings.realtimeReflectionProbes;
                QualitySettings.realtimeReflectionProbes = true;
            }
            qualityLeaseCount++;
            qualityLease = true;
        }

        private void ReleaseQualityLease()
        {
            if (!qualityLease) return;
            qualityLease = false;
            qualityLeaseCount = Mathf.Max(0, qualityLeaseCount - 1);
            if (qualityLeaseCount == 0)
            {
                // Preserve a concurrent external switch to false. Otherwise
                // put back precisely the value captured by the first owner.
                if (QualitySettings.realtimeReflectionProbes)
                    QualitySettings.realtimeReflectionProbes = previousRealtimeProbes;
            }
        }

        protected virtual void OnDisable()
        {
            if (captureId >= 0) CaptureCancelledCount++;
            wanted = valid = false;
            CurrentGain = 0f;
            ClearBinding();
            if (probe != null) Destroy(probe.gameObject);
            probe = null;
            capturedTexture = null;
            captureId = -1;
            captureAttempts = 0;
#if UNITY_EDITOR
            forceTimeoutForTests = false;
#endif
            ReleaseQualityLease();
        }

        protected virtual void OnDestroy() => ReleaseQualityLease();
    }
}
