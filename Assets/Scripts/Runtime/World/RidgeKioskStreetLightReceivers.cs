using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The strong window beam illuminates static surfaces; ordinary light owns people.</summary>
    [DisallowMultipleComponent]
    internal sealed class RidgeKioskStreetLightReceivers : MonoBehaviour
    {
        internal const uint StreetLayer = 1u << 3;
        private const int MaximumReceivers = 128;
        private readonly List<Receiver> receivers = new List<Receiver>(16);
        private Transform model;
        private bool initialized;

        internal void Configure(Transform kioskModel) => model = kioskModel;

        internal void Initialize(Transform world, Vector3 source, float range)
        {
            if (initialized) return;
            initialized = true;
            var bounds = new Bounds(source, Vector3.one * (range * 2f));
            var materials = new List<Material>(8);
            foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.bounds.Intersects(bounds)) continue;
                bool receives = model != null && renderer.transform.IsChildOf(model);
                if (!receives)
                {
                    materials.Clear();
                    renderer.GetSharedMaterials(materials);
                    foreach (var material in materials)
                        if (material != null && material.shader == GroundSurfaceAppearance.SharedMaterial.shader)
                        { receives = true; break; }
                }
                if (!receives) continue;
                if (receivers.Count == MaximumReceivers)
                    throw new InvalidOperationException("Kiosk street-light receiver budget exceeded.");
                receivers.Add(new Receiver(renderer, (renderer.renderingLayerMask & StreetLayer) != 0));
                renderer.renderingLayerMask |= StreetLayer;
            }
        }

        private void OnEnable()
        {
            foreach (var receiver in receivers)
                if (receiver.Renderer != null) receiver.Renderer.renderingLayerMask |= StreetLayer;
        }

        private void OnDisable() => ReleaseLayer();
        private void OnDestroy() => ReleaseLayer();

        private void ReleaseLayer()
        {
            foreach (var receiver in receivers)
                if (receiver.Renderer != null && !receiver.HadLayer)
                    receiver.Renderer.renderingLayerMask &= ~StreetLayer;
        }

        private readonly struct Receiver
        {
            internal readonly Renderer Renderer;
            internal readonly bool HadLayer;
            internal Receiver(Renderer renderer, bool hadLayer) { Renderer = renderer; HadLayer = hadLayer; }
        }
    }
}
