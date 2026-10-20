using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>One explicitly refreshed, convolved HDR cube of the static warehouse.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseReflectionController : MonoBehaviour
    {
        public const int Resolution = 256;
        public const int CaptureLayer = 31;
        private const float QuietSeconds = .25f;
        private const double CaptureTimeoutSeconds = 10d;
        private const int MaximumCaptureAttempts = 2;
        private static readonly ProfilerMarker SubmitMarker = new ProfilerMarker("Warehouse Reflection.Submit");
        private static readonly ProfilerMarker PollMarker = new ProfilerMarker("Warehouse Reflection.Poll");
        private static int qualityLeaseCount;
        private static bool previousRealtimeProbes;
        private static readonly int CubeId = Shader.PropertyToID("_WarehouseReflectionCube");
        private static readonly int DecodeId = Shader.PropertyToID("_WarehouseReflectionDecode");
        private static readonly int ProbeId = Shader.PropertyToID("_WarehouseReflectionProbe");
        private static readonly int BoxMinId = Shader.PropertyToID("_WarehouseReflectionBoxMin");
        private static readonly int BoxMaxId = Shader.PropertyToID("_WarehouseReflectionBoxMax");
        private WarehouseCleanPresentation presentation;
        private ReflectionProbe probe;
        private Texture capturedTexture;
        private Vector4 capturedDecode;
        private Light[] lights;
        private float[] capturedIntensity;
        private Bounds bounds;
        private Vector3 capturePosition;
        private int captureId = -1, generation, captureGeneration;
        private uint state;
        private double dirtyAt;
        private double captureStartedAt;
        private int captureAttempts;
        private bool settingWasEnabled;
#if UNITY_EDITOR
        private bool forceTimeoutForTests;
        internal void DebugExpireCaptureForTests()
        {
            if (captureId < 0) throw new InvalidOperationException("A capture must be pending.");
            captureStartedAt = Time.realtimeSinceStartupAsDouble - CaptureTimeoutSeconds - 1d;
            forceTimeoutForTests = true;
        }
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
        public float CurrentGain { get; private set; }
        public Bounds Bounds => bounds;

        internal void Initialize(WarehouseCleanPresentation owner, WarehouseArenaPlan plan, Light[] sources)
        {
            presentation = owner;
            lights = sources;
            capturedIntensity = new float[lights.Length];
            bounds = new Bounds(new Vector3(0f, 5.05f, 0f), new Vector3(10.8f, 10.1f, 22f));
            capturePosition = new Vector3(0f, 1.6f, -6f);
            Vector3[] candidates = { capturePosition, new Vector3(0f, 1.6f, -2f),
                new Vector3(0f, 1.6f, 4f), new Vector3(1.6f, 1.6f, -6f) };
            for (int i = 0; i < candidates.Length; i++)
            {
                bool free = true;
                foreach (WarehouseArenaObstacle obstacle in plan.Obstacles)
                    if (obstacle.Bounds.Contains(candidates[i])) { free = false; break; }
                if (free) { capturePosition = candidates[i]; break; }
            }
            initialized = true;
            settingWasEnabled = GraphicsEffectsSettings.CleanReflectionsEnabled;
            state = ReadState();
            dirtyAt = Time.realtimeSinceStartupAsDouble;
        }

        internal void Prepare(bool requested)
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

        private uint ReadState()
        {
            uint bits = 0;
            for (int i = 0; i < lights.Length && i < 24; i++)
                if (Power(lights[i]) > .001f) bits |= 1u << i;
            if (presentation != null && presentation.IsBlackout) bits |= 1u << 30;
            if (presentation != null && presentation.Mechanisms.DoorAmbushOverdriveActive) bits |= 1u << 29;
            return bits;
        }

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
            if (!initialized || presentation == null || !presentation.isActiveAndEnabled) return;
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
                valid = captureGeneration == generation && !presentation.IsBlackout && capturedTexture != null;
                ReleaseQualityLease();
            }
            if (captureId >= 0 && Time.realtimeSinceStartupAsDouble - captureStartedAt >= CaptureTimeoutSeconds)
            {
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
                    Debug.LogWarning("Warehouse reflection capture timed out; its quality lease was released.", this);
                }
            }
            if (!wanted || valid || captureId >= 0 || presentation.IsBlackout ||
                captureAttempts >= MaximumCaptureAttempts ||
                (state & 0x00ffffffu) == 0u || Time.realtimeSinceStartupAsDouble - dirtyAt < QuietSeconds)
                return;
            // Transitional machine power is not a stable environment. One dirty
            // request survives the whole transition instead of queuing captures.
            if (presentation.Mechanisms.FanShutdownProgress > 0f &&
                presentation.Mechanisms.FanShutdownProgress < 1f ||
                presentation.Mechanisms.DoorAmbushOverdriveActive) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EnsureProbe();
            for (int i = 0; i < lights.Length; i++) capturedIntensity[i] = Power(lights[i]);
            captureGeneration = generation;
            captureAttempts++;
            long submitStarted = Stopwatch.GetTimestamp();
            try
            {
                using (SubmitMarker.Auto())
                {
                    AcquireQualityLease();
                    captureStartedAt = Time.realtimeSinceStartupAsDouble;
                    captureId = probe.RenderProbe();
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
                    Debug.LogWarning("Warehouse reflection capture could not start: " + error.Message, this);
                }
            }
            finally
            {
                CaptureSubmitTicks += Stopwatch.GetTimestamp() - submitStarted;
                // A rejected request has no asynchronous work to finish.
                if (captureId < 0) ReleaseQualityLease();
            }
        }

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
            var host = new GameObject("Warehouse static reflection capture");
            host.transform.SetParent(transform, false);
            host.transform.position = capturePosition;
            probe = host.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = Resolution;
            probe.hdr = true;
            probe.boxProjection = true;
            // This probe is a capture service, never a native URP environment.
            // Forward+ can cluster it even for renderers with probe usage Off;
            // keep both its contribution and influence volume negligible.
            // The explicit custom shader retains its independent hall bounds.
            probe.center = Vector3.zero;
            probe.size = Vector3.one * .01f;
            probe.cullingMask = 1 << CaptureLayer;
            probe.nearClipPlane = .1f;
            probe.farClipPlane = 36f;
            probe.shadowDistance = 16f;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = Color.black;
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

        private void Bind(bool requested)
        {
            float gain = requested && IsReady && !presentation.IsBlackout && (state & 0x00ffffffu) != 0u ? 1f : 0f;
            for (int i = 0; i < capturedIntensity.Length && gain > 0f; i++)
                if (capturedIntensity[i] > .001f)
                    gain = Mathf.Min(gain, Power(lights[i]) / capturedIntensity[i]);
            CurrentGain = Mathf.Clamp01(gain);
            if (IsReady)
            {
                Shader.SetGlobalTexture(CubeId, capturedTexture);
                Shader.SetGlobalVector(DecodeId, capturedDecode);
            }
            Shader.SetGlobalVector(ProbeId, new Vector4(capturePosition.x, capturePosition.y,
                capturePosition.z, CurrentGain));
            Shader.SetGlobalVector(BoxMinId, bounds.min);
            Shader.SetGlobalVector(BoxMaxId, bounds.max);
        }

        private static float Power(Light light) => light != null && light.enabled && light.gameObject.activeInHierarchy
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

        private void OnDisable()
        {
            if (captureId >= 0) CaptureCancelledCount++;
            wanted = valid = false;
            CurrentGain = 0f;
            if (WarehouseCleanPresentation.Current == presentation || WarehouseCleanPresentation.Current == null)
            {
                Shader.SetGlobalVector(ProbeId, Vector4.zero);
                Shader.SetGlobalTexture(CubeId, null);
            }
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

        private void OnDestroy() => ReleaseQualityLease();
    }
}
