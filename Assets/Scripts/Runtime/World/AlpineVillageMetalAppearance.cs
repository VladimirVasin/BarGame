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
                    material.shader.name != "Bar Promenade/Lit") continue;
                // Only single-slot semantic parts are selected. No imported
                // multi-material shell, snow, wood or facade enters this path.
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                lease.Remember(renderer, original);
                properties.Clear();
                renderer.GetPropertyBlock(properties);
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

            internal void Remember(Renderer renderer, MaterialPropertyBlock original)
            {
                entries.Add(new Entry(renderer, original));
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (Entry entry in entries)
                    if (entry.Renderer != null)
                        entry.Renderer.SetPropertyBlock(entry.Original.isEmpty ? null : entry.Original);
                entries.Clear();
            }

            private readonly struct Entry
            {
                internal Entry(Renderer renderer, MaterialPropertyBlock original)
                {
                    Renderer = renderer;
                    Original = original;
                }

                internal Renderer Renderer { get; }
                internal MaterialPropertyBlock Original { get; }
            }
        }
    }
}
