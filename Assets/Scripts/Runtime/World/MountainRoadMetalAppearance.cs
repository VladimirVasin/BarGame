using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored steel parts opt in without replacing their material, pigment or live property block.</summary>
    internal static class MountainRoadMetalAppearance
    {
        private static readonly int DirectionalId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int ContactId = Shader.PropertyToID("_ClearCoatSmoothness");

        internal static IDisposable Apply(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var lease = new AppearanceLease();
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!TryGetResponse(renderer.name, out Vector2 response) || !Supports(renderer)) continue;
                Material material = renderer.sharedMaterial;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                lease.Remember(renderer,
                    properties.HasProperty(DirectionalId) ? properties.GetFloat(DirectionalId) : material.GetFloat(DirectionalId),
                    properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : material.GetFloat(ContactId));
                properties.SetFloat(DirectionalId, response.x);
                properties.SetFloat(ContactId, response.y);
                renderer.SetPropertyBlock(properties);
            }
            return lease;
        }

        internal static bool Supports(Renderer renderer) => renderer != null &&
            renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null &&
            (renderer.sharedMaterial.shader.name == "Bar Promenade/Lit" ||
             renderer.sharedMaterial.shader.name == MountainRoadSurfaceLibrary.ShaderName);

        private static bool TryGetResponse(string name, out Vector2 response)
        {
            // Exact source names preserve paint, rust and each unrelated surface's response.
            switch (name)
            {
                case "Cafe_CoffeeUrns":
                case "Cafe_KitchenHandles":
                case "Cafe_CounterFootRail":
                    response = new Vector2(.30f, .28f); return true;
                case "Cafe_StoolMetal":
                case "Cafe_Stove":
                case "Cafe_FryingPan":
                case "Cafe_RefrigeratorBody":
                case "Cafe_FridgeDoor":
                case "Cafe_StoveTaskFixture":
                    response = new Vector2(.20f, .23f); return true;
                case "Bullwheel Bearing Housing":
                case "Visible Lower Guide Roller":
                case "Visible Drive Shaft":
                case "Boarding Gate Leaf Standing Open":
                case "Boarding Dock Lamp Housing":
                case "Boarding Dock Lamp Boom":
                    response = new Vector2(.20f, .24f); return true;
                case "Rusty Service Hut Door":
                case "Site Slack Chains":
                    response = new Vector2(.12f, .20f); return true;
                default:
                    response = default; return false;
            }
        }

        private sealed class AppearanceLease : IDisposable
        {
            private readonly List<Entry> entries = new List<Entry>(32);
            private bool disposed;
            internal void Remember(Renderer renderer, float directional, float contact) =>
                entries.Add(new Entry { Renderer = renderer, Directional = directional, Contact = contact });

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                var properties = new MaterialPropertyBlock();
                foreach (Entry entry in entries)
                {
                    if (entry.Renderer == null) continue;
                    // Keep newer texture/tint/animation values from other presentation owners.
                    properties.Clear(); entry.Renderer.GetPropertyBlock(properties);
                    properties.SetFloat(DirectionalId, entry.Directional);
                    properties.SetFloat(ContactId, entry.Contact);
                    entry.Renderer.SetPropertyBlock(properties);
                }
                entries.Clear();
            }
        }

        private struct Entry { internal Renderer Renderer; internal float Directional, Contact; }
    }
}
