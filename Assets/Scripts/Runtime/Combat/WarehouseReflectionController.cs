using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Warehouse-specific states and receivers around the shared bounded capture.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseReflectionController : SceneReflectionCapture
    {
        private static readonly int CubeId = Shader.PropertyToID("_WarehouseReflectionCube");
        private static readonly int DecodeId = Shader.PropertyToID("_WarehouseReflectionDecode");
        private static readonly int ProbeId = Shader.PropertyToID("_WarehouseReflectionProbe");
        private static readonly int BoxMinId = Shader.PropertyToID("_WarehouseReflectionBoxMin");
        private static readonly int BoxMaxId = Shader.PropertyToID("_WarehouseReflectionBoxMax");
        private WarehouseCleanPresentation presentation;
        private Light[] lights;
        private float[] capturedIntensity;
        private Bounds bounds;
        private Vector3 capturePosition;
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => presentation != null && !presentation.IsBlackout &&
            (ReadState() & 0x00ffffffu) != 0u;
        protected override bool StableForCapture => presentation != null &&
            !(presentation.Mechanisms.FanShutdownProgress > 0f && presentation.Mechanisms.FanShutdownProgress < 1f) &&
            !presentation.Mechanisms.DoorAmbushOverdriveActive;

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
            InitializeCapture(bounds, capturePosition, 1 << CaptureLayer, 36f, 16f, Color.black);
        }

        protected override uint ReadState()
        {
            uint bits = 0;
            for (int i = 0; i < lights.Length && i < 24; i++)
                if (Power(lights[i]) > .001f) bits |= 1u << i;
            if (presentation != null && presentation.IsBlackout) bits |= 1u << 30;
            if (presentation != null && presentation.Mechanisms.DoorAmbushOverdriveActive) bits |= 1u << 29;
            return bits;
        }

        protected override void BeforeCapture()
        {
            for (int i = 0; i < lights.Length; i++) capturedIntensity[i] = Power(lights[i]);
        }

        protected override void Bind(bool requested)
        {
            float gain = requested && IsReady && !presentation.IsBlackout && (SourceState & 0x00ffffffu) != 0u ? 1f : 0f;
            for (int i = 0; i < capturedIntensity.Length && gain > 0f; i++)
                if (capturedIntensity[i] > .001f)
                    gain = Mathf.Min(gain, Power(lights[i]) / capturedIntensity[i]);
            CurrentGain = Mathf.Clamp01(gain);
            if (IsReady)
            {
                Shader.SetGlobalTexture(CubeId, CapturedTexture);
                Shader.SetGlobalVector(DecodeId, CapturedDecode);
            }
            Shader.SetGlobalVector(ProbeId, new Vector4(capturePosition.x, capturePosition.y,
                capturePosition.z, CurrentGain));
            Shader.SetGlobalVector(BoxMinId, bounds.min);
            Shader.SetGlobalVector(BoxMaxId, bounds.max);
        }

        protected override void ClearBinding()
        {
            if (WarehouseCleanPresentation.Current != null && WarehouseCleanPresentation.Current != presentation) return;
            Shader.SetGlobalVector(ProbeId, Vector4.zero);
            Shader.SetGlobalTexture(CubeId, null);
        }
    }
}
