using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One filtered house environment using the shared capture lifecycle.</summary>
    public sealed class MothersHouseReflectionZone : SceneReflectionCapture
    {
        private MothersHouseReflectionController owner;
        private int index;
        private Vector3 position;
        protected override SceneLightingPresentation Presentation => owner != null ? owner.Presentation : null;
        protected override bool CapturePermitted => owner != null && owner.CanCapture(this);
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;
        internal Texture Texture => CapturedTexture;
        internal Vector4 Decode => CapturedDecode;
        internal Vector3 Position => position;

        internal void Initialize(MothersHouseReflectionController controller, int zoneIndex,
            Bounds bounds, Vector3 capturePosition)
        {
            owner = controller;
            index = zoneIndex;
            position = capturePosition;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 20f, 14f,
                Color.black, ReflectionProbeTimeSlicingMode.AllFacesAtOnce);
        }

        protected override uint ReadState() => owner != null ? owner.ReadLightingState(index) : 0;
        protected override void Bind(bool requested) => CurrentGain = requested && IsReady ? .32f : 0f;
        protected override void ClearBinding() => CurrentGain = 0f;
    }
}
