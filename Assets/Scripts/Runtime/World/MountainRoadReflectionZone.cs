using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One finite static road environment using the shared filtered capture lifecycle.</summary>
    public sealed class MountainRoadReflectionZone : SceneReflectionCapture
    {
        private MountainRoadReflectionController owner;
        private Vector3 position;
        protected override SceneLightingPresentation Presentation => owner != null ? owner.Presentation : null;
        protected override bool CapturePermitted => owner != null && owner.CanCapture(this);
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;
        internal Texture Texture => CapturedTexture;
        internal Vector4 Decode => CapturedDecode;
        internal Vector3 Position => position;

        internal void Initialize(MountainRoadReflectionController controller, Bounds bounds, Vector3 capturePosition)
        {
            owner = controller;
            position = capturePosition;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 32f, 18f,
                RenderSettings.fogColor, ReflectionProbeTimeSlicingMode.AllFacesAtOnce);
        }

        protected override uint ReadState() => owner != null ? owner.ReadLightingState() : 0;
        protected override void BeforeCapture()
        {
            SetCaptureBackground(RenderSettings.fogColor);
            owner.MarkCaptureSubmission();
        }
        protected override void Bind(bool requested) => CurrentGain = requested && IsReady ? .30f : 0f;
        protected override void ClearBinding() => CurrentGain = 0f;
    }
}
