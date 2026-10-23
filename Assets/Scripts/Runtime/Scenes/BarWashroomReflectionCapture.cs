using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A static WC environment; the real mirror remains geometric and is never captured.</summary>
    [DisallowMultipleComponent]
    public sealed class BarWashroomReflectionCapture : SceneReflectionCapture
    {
        private BarInteriorRoot owner;
        private BarLightingPresentation presentation;
        private Light source;
        public Vector3 Position { get; private set; }
        public Texture Environment => CapturedTexture;
        public Vector4 Decode => CapturedDecode;
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => owner != null && owner.IsInitialized &&
            owner.isActiveAndEnabled && source != null && source.isActiveAndEnabled && source.intensity > .001f;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        internal void Initialize(BarInteriorRoot bar, BarLightingPresentation provider)
        {
            owner = bar;
            presentation = provider;
            foreach (Light candidate in bar.Atmosphere.PracticalLights)
                if (candidate != null && candidate.name == "washroom-ceiling") { source = candidate; break; }
            Bounds bounds = new Bounds();
            bounds.SetMinMax(bar.Room.TransformPoint(new Vector3(-5.4f, -.02f, 4.15f)),
                bar.Room.TransformPoint(new Vector3(-2.4f, 3f, 7.15f)));
            Position = bar.Room.TransformPoint(new Vector3(-3.8f, 1.45f, 5.35f));
            InitializeCapture(bounds, Position, 1 << CaptureLayer, 6f, 4f, Color.black);
        }

        protected override uint ReadState()
        {
            if (source == null || !source.isActiveAndEnabled) return 0;
            // Only semantic source changes re-capture this static room.
            return (uint)Mathf.RoundToInt(source.intensity * 32f) ^
                ((uint)Mathf.RoundToInt(source.color.r * 31f) << 12) ^
                ((uint)Mathf.RoundToInt(source.color.g * 31f) << 17) ^
                ((uint)Mathf.RoundToInt(source.color.b * 31f) << 22);
        }

        protected override void Bind(bool requested) => CurrentGain =
            requested && IsReady && CapturePermitted ? .42f : 0f;
        protected override void ClearBinding() => CurrentGain = 0f;
    }
}
