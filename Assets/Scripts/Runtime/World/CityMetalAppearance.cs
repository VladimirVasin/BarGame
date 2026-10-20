using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Opt-in response on existing authored metal; pigment, wetness and wind remain owned by their presenters.</summary>
    internal sealed class CityMetalAppearance : IDisposable
    {
        private const int MaximumMetalReceivers = 16384;
        private static readonly int DirectionalId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int ContactId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private readonly List<Entry> entries = new List<Entry>(256);
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private bool initialized, applied, disposed;

        internal int ReceiverCount => entries.Count;

        internal void Initialize(CityGameRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            InitializeExterior(root.transform);
        }

        internal void InitializeExterior(Transform root)
        {
            if (initialized) throw new InvalidOperationException("City metal appearance is already initialized.");
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    int index = CitySurfaceLibrary.PropertyIndex(renderer, materials.Length, slot, properties);
                    if (!TryGetResponse(renderer, material, properties, index, out Vector2 response)) continue;
                    if (entries.Count >= MaximumMetalReceivers)
                        throw new InvalidOperationException("City metal receivers exceed their bounded budget.");
                    CitySurfaceLibrary.ReadProperties(renderer, index, properties);
                    entries.Add(new Entry
                    {
                        Renderer = renderer, MaterialIndex = index, Response = response,
                        Directional = properties.HasProperty(DirectionalId) ? properties.GetFloat(DirectionalId) : material.GetFloat(DirectionalId),
                        Contact = properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : material.GetFloat(ContactId)
                    });
                }
            }
            initialized = true;
        }

        internal void Prepare(bool enabled)
        {
            if (!initialized || disposed || applied == enabled) return;
            foreach (Entry entry in entries)
            {
                if (entry.Renderer == null) continue;
                CitySurfaceLibrary.ReadProperties(entry.Renderer, entry.MaterialIndex, properties);
                // Reflection owns the sign of this reserved slot; toggling highlights must preserve it.
                float current = properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : entry.Contact;
                float contact = enabled ? entry.Response.y : Mathf.Abs(entry.Contact);
                properties.SetFloat(DirectionalId, enabled ? entry.Response.x : entry.Directional);
                properties.SetFloat(ContactId, current < 0f ? -Mathf.Max(contact, .001f) : contact);
                GroundSurfaceAppearance.WriteProperties(entry.Renderer, properties, entry.MaterialIndex);
            }
            applied = enabled;
        }

        internal void Restore() => Prepare(false);
        public void Dispose()
        {
            if (disposed) return;
            Restore(); disposed = true; entries.Clear();
        }

        internal static bool Supports(Renderer renderer) => CitySurfaceLibrary.SupportsOpaque(renderer);

        internal static bool TryGetResponse(Renderer renderer, MaterialPropertyBlock scratch, out Vector2 response)
            => TryGetResponse(renderer, renderer != null ? renderer.sharedMaterial : null, scratch, -1, out response);

        internal static bool TryGetResponse(Renderer renderer, Material material, MaterialPropertyBlock scratch, int materialIndex, out Vector2 response)
        {
            response = default;
            if (renderer == null || !CitySurfaceLibrary.SupportsOpaque(material)) return false;
            string name = renderer.name;
            // Authored surface-role suffixes, shared by the building, misc, port and cannery catalogs.
            bool bare = name.EndsWith("__SteelLight", StringComparison.Ordinal) ||
                name.EndsWith("__Chrome", StringComparison.Ordinal) || name.EndsWith("__Stainless", StringComparison.Ordinal);
            bool painted = name.EndsWith("_PaintedMetal", StringComparison.Ordinal) ||
                name.EndsWith("__Metal", StringComparison.Ordinal) || name.EndsWith("_Metal", StringComparison.Ordinal) ||
                name.EndsWith("__WindowFrame", StringComparison.Ordinal) ||
                name.EndsWith("__Steel", StringComparison.Ordinal) || name.EndsWith("__SteelDark", StringComparison.Ordinal) ||
                name.EndsWith("__SteelRust", StringComparison.Ordinal);
            switch (name)
            {
                case "Metal": case "WindowFrame": case "Home Balcony Rail":
                case "Street Lamp Pole": case "Street Lamp Arm":
                case "Boarding Gate": case "Cemetery Fence Rail":
                case "GEO_Bumpers": case "GEO_HeadlightRims":
                case "GEO_DoorDriverHandle": case "GEO_DoorPassengerHandle":
                case "GEO_Grille": case "GEO_HubcapFL": case "GEO_HubcapFR":
                case "GEO_HubcapRL": case "GEO_HubcapRR": case "GEO_MirrorArms":
                case "GEO_ExteriorTrim": case "INT_Handrails": case "INT_SeatFrames":
                case "GEO_CabinLampCollars": case "GEO_CabinLampStems":
                case "Bar Door Furniture": case "Bar Entrance Canopy Inset":
                case "Pub Cellar Hatch": case "Pub Gutters And Downpipes": case "Bar Sign Bracket":
                    painted = true; break;
            }
            if (!bare && !painted)
            {
                // Combined detail meshes retain their authored surface sheet even when their source names are batched away.
                CitySurfaceLibrary.ReadProperties(renderer, materialIndex, scratch);
                Texture texture = scratch.GetTexture(BaseMapId) ?? material.GetTexture(BaseMapId);
                if (texture != null)
                {
                    painted = texture.name.IndexOf("PaintedMetal", StringComparison.Ordinal) >= 0;
                    bare = texture.name.IndexOf("Stainless", StringComparison.Ordinal) >= 0;
                }
            }
            if (!bare && !painted) return false;
            response = bare ? new Vector2(.23f, .27f) : new Vector2(.14f, .21f);
            return true;
        }

        private struct Entry
        {
            internal Renderer Renderer;
            internal int MaterialIndex;
            internal Vector2 Response;
            internal float Directional, Contact;
        }
    }
}
