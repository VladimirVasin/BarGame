using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Opt-in response on existing steel parts, retaining aged maps,
    /// tints and metalness. Reuses their property blocks and shared Lit material.</summary>
    internal static class AlpineVillageMetalAppearance
    {
        private static readonly int DirectionalAmountId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int ContactSmoothnessId = Shader.PropertyToID("_ClearCoatSmoothness");

        internal static IDisposable Apply(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var lease = new AppearanceLease();
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in renderers)
            {
                if (!TryGetResponse(renderer.name, out Vector2 response)) continue;
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null ||
                    (material.shader.name != "Bar Promenade/Lit" &&
                     material.shader.name != VillageSurfaceAppearance.ShaderName)) continue;
                // Only single-slot semantic parts are selected. No imported
                // multi-material shell, snow, wood or facade enters this path.
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                lease.Remember(renderer,
                    properties.HasProperty(DirectionalAmountId) ? properties.GetFloat(DirectionalAmountId) : material.GetFloat(DirectionalAmountId),
                    properties.HasProperty(ContactSmoothnessId) ? properties.GetFloat(ContactSmoothnessId) : material.GetFloat(ContactSmoothnessId));
                properties.SetFloat(DirectionalAmountId, response.x);
                properties.SetFloat(ContactSmoothnessId, response.y);
                renderer.SetPropertyBlock(properties);
            }
            return lease;
        }

        private static bool TryGetResponse(string name, out Vector2 response)
        {
            // Exact authored semantic names, never a broad "metal"/rust match:
            // rust remains predominantly dielectric, snow and paint stay old.
            switch (name)
            {
                case "Cabin Grab Rail":
                case "Cabin Safety Bar":
                    response = new Vector2(.3f, .28f);
                    return true;
                case "Bullwheel Disc":
                case "Bullwheel Bearing Housing":
                case "Visible Lower Guide Roller":
                case "Visible Cable Roller":
                case "Tension Rail":
                    response = new Vector2(.25f, .26f);
                    return true;
                case "Cabin Hanger":
                case "Cabin Cable Grip":
                case "Tension Hanger":
                case "Visible Support Roller Crossbeam":
                    response = new Vector2(.2f, .24f);
                    return true;
                case "CableGate Cable":
                case "RailBridge Rails":
                    response = new Vector2(.16f, .22f);
                    return true;
                default:
                    response = default;
                    return false;
            }
        }

        private sealed class AppearanceLease : IDisposable
        {
            private readonly List<Entry> entries = new List<Entry>(96);
            private bool disposed;

            internal void Remember(Renderer renderer, float directional, float contact)
            {
                entries.Add(new Entry(renderer, directional, contact));
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                var properties = new MaterialPropertyBlock();
                foreach (Entry entry in entries)
                {
                    if (entry.Renderer != null)
                    {
                        // Other owners may have refreshed texture/tint/response since
                        // this lease began. Restore only the two values we own.
                        properties.Clear();
                        entry.Renderer.GetPropertyBlock(properties);
                        properties.SetFloat(DirectionalAmountId, entry.Directional);
                        properties.SetFloat(ContactSmoothnessId, entry.Contact);
                        entry.Renderer.SetPropertyBlock(properties);
                    }
                }
                entries.Clear();
            }

            private readonly struct Entry
            {
                internal Entry(Renderer renderer, float directional, float contact)
                {
                    Renderer = renderer;
                    Directional = directional; Contact = contact;
                }

                internal Renderer Renderer { get; }
                internal float Directional { get; }
                internal float Contact { get; }
            }
        }
    }
}
