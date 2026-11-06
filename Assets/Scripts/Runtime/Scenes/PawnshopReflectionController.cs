using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One filtered static room capture; the moving door and the clerk are excluded.</summary>
    [DisallowMultipleComponent]
    public sealed class PawnshopReflectionController : SceneReflectionCapture
    {
        private struct Lease { internal Renderer Renderer; internal int Layer; internal float Receiver; }
        private readonly List<Lease> leases = new List<Lease>(64);
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2], maxima = new Vector4[2], decode = new Vector4[2];
        private static readonly int ReceiverId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private MaterialPropertyBlock properties;
        private PawnshopInteriorRoot root;
        private PawnshopLightingPresentation presentation;
        private Vector3 position;
        private bool initialized;
        public int ReceiverCount { get; private set; }
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => initialized && root != null && root.IsInitialized && root.isActiveAndEnabled;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        internal void Initialize(PawnshopInteriorRoot owner, PawnshopLightingPresentation provider)
        {
            root = owner; presentation = provider; properties = new MaterialPropertyBlock();
            Renderer[] renderers = root.Model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 256) throw new InvalidOperationException("Pawnshop static reflection renderer budget exceeded.");
            Shader physical = SupermarketSurfaceAppearance.SharedMaterial.shader;
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer)) continue;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                Material material = renderer.sharedMaterial;
                float smoothness = properties.HasFloat(SmoothnessId) ? properties.GetFloat(SmoothnessId) :
                    material != null && material.HasProperty(SmoothnessId) ? material.GetFloat(SmoothnessId) : 0f;
                bool receives = material != null && material.shader == physical && smoothness >= .18f;
                leases.Add(new Lease { Renderer = renderer, Layer = renderer.gameObject.layer,
                    Receiver = receives ? properties.GetFloat(ReceiverId) : float.NaN });
                if (receives) ReceiverCount++;
            }
            position = root.World.Root.TransformPoint(new Vector3(0f, 1.5f, -.3f));
            var bounds = new Bounds(root.World.Root.TransformPoint(new Vector3(0f, 1.5f, 0f)), new Vector3(7.4f, 3f, 8.4f));
            minima[0] = bounds.min; maxima[0] = bounds.max;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 12f, 8f, Color.black);
            initialized = true; ApplyOwnership(true);
        }

        protected override uint ReadState() => 1u;
        protected override void Bind(bool requested)
        {
            CurrentGain = requested && IsReady && CapturePermitted ? .18f : 0f;
            Shader.SetGlobalInt("_SceneSurfaceReflectionCount", CurrentGain > 0f ? 1 : 0);
            if (CurrentGain <= 0f) return;
            origins[0] = new Vector4(position.x, position.y, position.z, CurrentGain); decode[0] = CapturedDecode;
            Shader.SetGlobalTexture("_SceneSurfaceReflectionCube0", CapturedTexture);
            Shader.SetGlobalVectorArray("_SceneSurfaceReflectionOrigins", origins);
            Shader.SetGlobalVectorArray("_SceneSurfaceReflectionMinima", minima);
            Shader.SetGlobalVectorArray("_SceneSurfaceReflectionMaxima", maxima);
            Shader.SetGlobalVectorArray("_SceneSurfaceReflectionDecode", decode);
        }

        protected override void ClearBinding()
        {
            Shader.SetGlobalInt("_SceneSurfaceReflectionCount", 0);
            Shader.SetGlobalTexture("_SceneSurfaceReflectionCube0", null);
        }

        private void ApplyOwnership(bool active)
        {
            foreach (Lease lease in leases)
            {
                if (lease.Renderer == null) continue;
                lease.Renderer.gameObject.layer = active ? CaptureLayer : lease.Layer;
                if (float.IsNaN(lease.Receiver)) continue;
                properties.Clear(); lease.Renderer.GetPropertyBlock(properties);
                properties.SetFloat(ReceiverId, active ? -Mathf.Max(Mathf.Abs(lease.Receiver), .001f) : lease.Receiver);
                lease.Renderer.SetPropertyBlock(properties);
            }
        }

        private void OnEnable() { if (initialized) ApplyOwnership(true); }
        protected override void OnDisable() { base.OnDisable(); ApplyOwnership(false); }
    }
}
