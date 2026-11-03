using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded filtered environment of static fixtures/stock; actors and the moving door stay out.</summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketReflectionController : SceneReflectionCapture
    {
        private const int MaximumRenderers = 4096;
        private struct Lease { public Renderer Renderer; public int Layer; public float Receiver; }
        private readonly List<Lease> leases = new List<Lease>();
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2], maxima = new Vector4[2], decode = new Vector4[2];
        private static readonly int ReceiverId = Shader.PropertyToID("_ClearCoatSmoothness");
        private MaterialPropertyBlock properties;
        private SupermarketInteriorRoot root;
        private SupermarketLightingPresentation presentation;
        private Vector3 position;
        private bool initialized;
        public int ReceiverCount { get; private set; }
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => initialized && root != null && root.IsInitialized && root.isActiveAndEnabled;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        internal void Initialize(SupermarketInteriorRoot owner, SupermarketLightingPresentation provider)
        {
            root = owner; presentation = provider;
            properties = new MaterialPropertyBlock();
            Renderer[] renderers = root.World.Root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > MaximumRenderers) throw new InvalidOperationException("Supermarket reflection exceeded static renderer budget.");
            Transform movingDoor = root.World.AssetRegistry.GetAnchor("door_hinge");
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer) || renderer.transform.IsChildOf(movingDoor) ||
                    renderer.GetComponentInParent<SupermarketProductView>() != null ||
                    renderer.name.StartsWith("CCTV Head", StringComparison.Ordinal)) continue;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                bool receives = renderer.sharedMaterial == SupermarketSurfaceAppearance.SharedMaterial &&
                    properties.GetFloat("_Smoothness") >= .20f;
                leases.Add(new Lease { Renderer = renderer, Layer = renderer.gameObject.layer,
                    Receiver = receives ? properties.GetFloat(ReceiverId) : float.NaN });
                if (receives) ReceiverCount++;
            }
            position = root.World.Root.TransformPoint(new Vector3(-1.15f, 1.55f, -.15f));
            Bounds bounds = new Bounds(root.World.Root.TransformPoint(new Vector3(0f, 1.8f, 0f)), new Vector3(16f, 3.6f, 11f));
            minima[0] = bounds.min; maxima[0] = bounds.max;
            InitializeCapture(bounds, position, 1 << CaptureLayer, 18f, 12f, Color.black);
            initialized = true;
            ApplyOwnership(true);
        }

        // This hall has fixed source colours and no day/night state. The brief ballast
        // dips modulate the existing light, not the static environment's capture lifetime.
        protected override uint ReadState() => 1u;
        protected override void Bind(bool requested)
        {
            CurrentGain = requested && IsReady && CapturePermitted ? .22f : 0f;
            Shader.SetGlobalInt("_SceneSurfaceReflectionCount", CurrentGain > 0f ? 1 : 0);
            if (CurrentGain <= 0f) return;
            origins[0] = new Vector4(position.x, position.y, position.z, CurrentGain);
            decode[0] = CapturedDecode;
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
