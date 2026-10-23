using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded static pub environment, using the common capture and cleanup lifecycle.</summary>
    [DisallowMultipleComponent]
    public sealed class BarReflectionController : SceneReflectionCapture
    {
        private const int MaximumArchitectureRenderers = 4096;
        private static readonly int CountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int CubeId = Shader.PropertyToID("_SceneSurfaceReflectionCube0");
        private static readonly int OriginsId = Shader.PropertyToID("_SceneSurfaceReflectionOrigins");
        private static readonly int MinimaId = Shader.PropertyToID("_SceneSurfaceReflectionMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_SceneSurfaceReflectionMaxima");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneSurfaceReflectionDecode");
        private static readonly int ContactFloorId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private struct LayerLease { internal GameObject Object; internal int Previous; }
        private struct ReceiverLease { internal Renderer Renderer; internal float Previous; }
        private readonly List<LayerLease> layers = new List<LayerLease>();
        private readonly List<ReceiverLease> receivers = new List<ReceiverLease>();
        private MaterialPropertyBlock properties;
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2], maxima = new Vector4[2], decode = new Vector4[2];
        private BarInteriorRoot root;
        private BarLightingPresentation presentation;
        private Vector3 position;
        private bool initialized;
        public static BarReflectionController Current { get; private set; }
        public int ReceiverCount => receivers.Count;
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => initialized && root != null && root.IsInitialized &&
            root.isActiveAndEnabled && presentation != null && presentation.isActiveAndEnabled;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        internal void Initialize(BarInteriorRoot bar, BarLightingPresentation provider, Bounds bounds)
        {
            if (initialized) throw new InvalidOperationException("Bar reflection is already initialized.");
            root = bar != null && bar.Room != null ? bar : throw new ArgumentNullException(nameof(bar));
            presentation = provider != null ? provider : throw new ArgumentNullException(nameof(provider));
            properties = new MaterialPropertyBlock();
            Renderer[] renderers = root.Room.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > MaximumArchitectureRenderers)
                throw new InvalidOperationException("Bar reflection architecture exceeds its bounded receiver scan.");
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer) || renderer.GetComponentInParent<BarCeilingFan>() != null ||
                    HasWashroomAncestor(renderer.transform, "Bar Washroom Door")) continue;
                layers.Add(new LayerLease { Object = renderer.gameObject, Previous = renderer.gameObject.layer });
                // The one probe describes the hall, not the separate room behind
                // its doorway. Keep the static WC shell in capture for occlusion,
                // but never paint its ceramics/metal with the hall environment.
                if (HasWashroomAncestor(renderer.transform, "Bar Washroom ")) continue;
                if (!BarInteriorSurfaceAppearance.UsesSharedMaterial(renderer)) continue;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                // Only lacquer, brass and ceramics receive a local environment.
                // Matte panels, upholstery and the authored subdued mirror retain their own response.
                if (properties.GetFloat(SmoothnessId) < .3f) continue;
                receivers.Add(new ReceiverLease { Renderer = renderer, Previous = properties.GetFloat(ContactFloorId) });
            }
            position = root.Room.TransformPoint(new Vector3(0f, 1.45f, -1.45f));
            minima[0] = bounds.min; maxima[0] = bounds.max;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 16f, 10f, Color.black);
            initialized = true;
            Current = this;
            ApplyOwnership(true);
        }

        private static bool HasWashroomAncestor(Transform part, string prefix)
        {
            while (part != null)
            {
                if (part.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
                part = part.parent;
            }
            return false;
        }

        protected override uint ReadState()
        {
            uint hash = 2166136261u;
            if (root != null && root.Atmosphere != null)
            {
                IReadOnlyList<Light> lights = root.Atmosphere.PracticalLights;
                for (int i = 0; i < lights.Count; i++)
                {
                    Light light = lights[i];
                    hash = Hash(hash, Power(light), 16f);
                    if (light == null) continue;
                    hash = Hash(hash, light.color.r, 32f); hash = Hash(hash, light.color.g, 32f);
                    hash = Hash(hash, light.color.b, 32f);
                    Vector3 p = light.transform.position;
                    hash = Hash(hash, p.x, 16f); hash = Hash(hash, p.y, 16f); hash = Hash(hash, p.z, 16f);
                    Vector3 direction = light.transform.forward;
                    hash = Hash(hash, direction.x, 32f); hash = Hash(hash, direction.y, 32f);
                    hash = Hash(hash, direction.z, 32f); hash = Hash(hash, light.range, 8f);
                }
            }
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            hash = Hash(hash, ambient.r, 32f); hash = Hash(hash, ambient.g, 32f); hash = Hash(hash, ambient.b, 32f);
            Light sun = RenderSettings.sun;
            hash = Hash(hash, Power(sun), 16f);
            if (sun != null)
            {
                hash = Hash(hash, sun.color.r, 32f); hash = Hash(hash, sun.color.g, 32f);
                hash = Hash(hash, sun.color.b, 32f);
            }
            return hash;
        }

        private static uint Hash(uint hash, float value, float scale) =>
            (hash ^ unchecked((uint)Mathf.RoundToInt(value * scale))) * 16777619u;

        protected override void Bind(bool requested)
        {
            CurrentGain = requested && IsReady && CapturePermitted ? .32f : 0f;
            bool bind = requested && CurrentGain > 0f;
            Shader.SetGlobalInt(CountId, bind ? 1 : 0);
            if (!bind) return;
            origins[0] = new Vector4(position.x, position.y, position.z, CurrentGain);
            decode[0] = CapturedDecode;
            Shader.SetGlobalTexture(CubeId, CapturedTexture);
            Shader.SetGlobalVectorArray(OriginsId, origins);
            Shader.SetGlobalVectorArray(MinimaId, minima);
            Shader.SetGlobalVectorArray(MaximaId, maxima);
            Shader.SetGlobalVectorArray(DecodeId, decode);
        }

        protected override void ClearBinding()
        {
            if (Current != null && Current != this) return;
            Shader.SetGlobalInt(CountId, 0);
            Shader.SetGlobalTexture(CubeId, null);
        }

        private void ApplyOwnership(bool active)
        {
            foreach (LayerLease lease in layers)
                if (lease.Object != null) lease.Object.layer = active ? CaptureLayer : lease.Previous;
            if (properties == null) return;
            foreach (ReceiverLease lease in receivers)
            {
                if (lease.Renderer == null) continue;
                properties.Clear(); lease.Renderer.GetPropertyBlock(properties);
                properties.SetFloat(ContactFloorId, active ? -Mathf.Max(Mathf.Abs(lease.Previous), .001f) : lease.Previous);
                lease.Renderer.SetPropertyBlock(properties);
            }
        }

        private void OnEnable()
        {
            if (!initialized) return;
            Current = this;
            ApplyOwnership(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ApplyOwnership(false);
            if (Current == this) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Current = null;
    }
}
