using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One filtered static room environment; captures and cleanup use the shared service.</summary>
    [DisallowMultipleComponent]
    public sealed class ChurchReflectionController : SceneReflectionCapture
    {
        private static readonly int CountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int CubeId = Shader.PropertyToID("_SceneSurfaceReflectionCube0");
        private static readonly int OriginsId = Shader.PropertyToID("_SceneSurfaceReflectionOrigins");
        private static readonly int MinimaId = Shader.PropertyToID("_SceneSurfaceReflectionMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_SceneSurfaceReflectionMaxima");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneSurfaceReflectionDecode");
        private static readonly int ContactId = Shader.PropertyToID("_ClearCoatSmoothness");
        private struct LayerLease { internal GameObject Object; internal int Previous; }
        private struct ReceiverLease { internal Renderer Renderer; internal float Previous; }
        private readonly List<LayerLease> layers = new List<LayerLease>(24);
        private readonly List<ReceiverLease> receivers = new List<ReceiverLease>(8);
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2], maxima = new Vector4[2], decode = new Vector4[2];
        private ChurchInteriorAtmosphere owner;
        private MaterialPropertyBlock properties;
        private Vector3 position;
        private bool initialized;
        public int ReceiverCount => receivers.Count;
        protected override SceneLightingPresentation Presentation => owner;
        protected override bool CapturePermitted => initialized && owner != null && owner.isActiveAndEnabled && isActiveAndEnabled;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        internal void Initialize(ChurchInteriorAtmosphere atmosphere, ChurchAssetRegistry model, Bounds bounds)
        {
            if (initialized) throw new InvalidOperationException("Church environment is already initialized.");
            owner = atmosphere != null ? atmosphere : throw new ArgumentNullException(nameof(atmosphere));
            properties = new MaterialPropertyBlock();
            if (model.RendererBindings.Count > 24) throw new InvalidOperationException("Church receiver budget exceeded.");
            foreach (ChurchRendererBinding binding in model.RendererBindings)
            {
                Renderer renderer = binding.Renderer;
                if (renderer == null) continue;
                layers.Add(new LayerLease { Object = renderer.gameObject, Previous = renderer.gameObject.layer });
                if (binding.MaterialSlot != ChurchMaterialSlot.Floor && binding.MaterialSlot != ChurchMaterialSlot.Gold &&
                    binding.MaterialSlot != ChurchMaterialSlot.Iron) continue;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                receivers.Add(new ReceiverLease { Renderer = renderer, Previous = properties.GetFloat(ContactId) });
            }
            position = owner.transform.TransformPoint(new Vector3(0f, 1.6f, -1f));
            minima[0] = bounds.min; maxima[0] = bounds.max;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 27f, 24f, Color.black);
            initialized = true;
            ApplyOwnership(true);
        }

        protected override uint ReadState()
        {
            if (owner == null) return 0;
            uint state = 2166136261u;
            foreach (ChurchCandleFlame flame in owner.CandleFlames)
                state = Hash(state, flame != null ? flame.BaseIntensity : 0f, 8f);
            foreach (Light light in owner.DaylightGlows)
            {
                state = Hash(state, Power(light), 8f);
                if (light != null)
                { state = Hash(state, light.color.r, 16f); state = Hash(state, light.color.g, 16f); state = Hash(state, light.color.b, 16f); }
            }
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            state = Hash(state, ambient.r, 16f); state = Hash(state, ambient.g, 16f); state = Hash(state, ambient.b, 16f);
            Light directional = RenderSettings.sun;
            state = Hash(state, Power(directional), 16f);
            if (directional != null)
            { state = Hash(state, directional.color.r, 16f); state = Hash(state, directional.color.g, 16f); state = Hash(state, directional.color.b, 16f); }
            return state;
        }
        private static uint Hash(uint hash, float value, float scale) =>
            (hash ^ unchecked((uint)Mathf.RoundToInt(value * scale))) * 16777619u;

        protected override void Bind(bool requested)
        {
            CurrentGain = requested && IsReady && CapturePermitted ? .24f : 0f;
            Shader.SetGlobalInt(CountId, CurrentGain > 0f ? 1 : 0);
            if (CurrentGain <= 0f) return;
            origins[0] = new Vector4(position.x, position.y, position.z, CurrentGain);
            decode[0] = CapturedDecode;
            Shader.SetGlobalTexture(CubeId, CapturedTexture);
            Shader.SetGlobalVectorArray(OriginsId, origins); Shader.SetGlobalVectorArray(MinimaId, minima);
            Shader.SetGlobalVectorArray(MaximaId, maxima); Shader.SetGlobalVectorArray(DecodeId, decode);
        }
        protected override void ClearBinding()
        { Shader.SetGlobalInt(CountId, 0); Shader.SetGlobalTexture(CubeId, null); }
        private void ApplyOwnership(bool active)
        {
            foreach (LayerLease lease in layers)
                if (lease.Object != null) lease.Object.layer = active ? CaptureLayer : lease.Previous;
            if (properties == null) return;
            foreach (ReceiverLease lease in receivers)
            {
                if (lease.Renderer == null) continue;
                properties.Clear(); lease.Renderer.GetPropertyBlock(properties);
                properties.SetFloat(ContactId, active ? -Mathf.Max(.001f, Mathf.Abs(lease.Previous)) : lease.Previous);
                lease.Renderer.SetPropertyBlock(properties);
            }
        }
        private void OnEnable() { if (initialized) ApplyOwnership(true); }
        protected override void OnDisable() { base.OnDisable(); ApplyOwnership(false); }
    }
}
