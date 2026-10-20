using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>A reusable finite City receiver slot, with the shared asynchronous filtered capture lifecycle.</summary>
    public sealed class CityReflectionZone : SceneReflectionCapture
    {
        private CityReflectionController owner;
        protected override SceneLightingPresentation Presentation => owner != null ? owner.Presentation : null;
        protected override bool CapturePermitted => owner != null && owner.CanCapture(this);
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused &&
            owner != null && owner.CanSubmit(this);
        internal Texture Texture => CapturedTexture;
        internal Vector4 Decode => CapturedDecode;
        public Vector3 Position { get; private set; }
        public int RegionIndex { get; private set; } = -1;

        internal void Assign(CityReflectionController controller, int region, Bounds bounds, Vector3 position)
        {
            enabled = false;
            owner = controller; RegionIndex = region; Position = position;
            InitializeCapture(bounds, position, 1 << CaptureLayer, CityReflectionController.CaptureRadius,
                18f, RenderSettings.fogColor, ReflectionProbeTimeSlicingMode.AllFacesAtOnce);
            enabled = true;
        }

        protected override uint ReadState() => owner != null ? owner.ReadLightingState() : 0;
        protected override void BeforeCapture()
        {
            SetCaptureBackground(RenderSettings.fogColor);
            owner.MarkCaptureSubmission();
        }
        protected override void Bind(bool requested) => CurrentGain = requested && IsReady ? .23f : 0f;
        protected override void ClearBinding() => CurrentGain = 0f;
    }
}
