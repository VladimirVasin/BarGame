using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One authored outfit, with explicit garment bindings separate from skin, hair and face.</summary>
    [DisallowMultipleComponent]
    public class NpcWardrobe : MonoBehaviour
    {
        [Serializable] public sealed class GarmentBinding
        {
            [SerializeField] private string slot;
            [SerializeField] private Renderer renderer;
            [SerializeField] private Color color;
            public string Slot => slot;
            public Renderer Renderer => renderer;
            public Color Color => color;
            public GarmentBinding(string part, Renderer target, Color tint)
            { slot = part; renderer = target; color = tint; }
        }

        [SerializeField] private string outfitId;
        [SerializeField] private Texture2D atlas;
        [SerializeField] private Material material;
        [SerializeField] private GarmentBinding[] garments = Array.Empty<GarmentBinding>();
        private MaterialPropertyBlock properties;
        public string CurrentOutfitId => outfitId;
        public Texture2D Atlas => atlas;
        public IReadOnlyList<GarmentBinding> Garments => garments;

        public void Configure(string identity, Texture2D texture, Material sharedMaterial, GarmentBinding[] bindings)
        {
            ValidateOutfit(identity, texture, sharedMaterial, bindings);
            // A later authored outfit can replace these bindings on the same rig.
            // Validate first; a failed replacement must leave the worn set intact.
            var next = new HashSet<Renderer>();
            foreach (GarmentBinding binding in bindings) next.Add(binding.Renderer);
            foreach (GarmentBinding previous in garments)
                if (previous?.Renderer != null && !next.Contains(previous.Renderer)) previous.Renderer.enabled = false;
            outfitId = identity; atlas = texture; material = sharedMaterial; garments = bindings;
            RestoreCurrentAppearance();
        }

        public bool Owns(Renderer target)
        {
            foreach (GarmentBinding garment in garments) if (garment.Renderer == target) return true;
            return false;
        }

        public void ValidateBindings() => ValidateOutfit(outfitId, atlas, material, garments);

        private static void ValidateOutfit(string outfitId, Texture2D atlas, Material material, GarmentBinding[] garments)
        {
            if (string.IsNullOrWhiteSpace(outfitId) || atlas == null || material == null || garments == null || garments.Length == 0)
                throw new InvalidOperationException("An authored NPC requires one complete outfit.");
            var unique = new HashSet<Renderer>();
            foreach (GarmentBinding garment in garments)
                if (garment == null || string.IsNullOrWhiteSpace(garment.Slot) || garment.Renderer == null || !unique.Add(garment.Renderer))
                    throw new InvalidOperationException("Every garment needs one explicit outfit slot and renderer binding.");
        }

        /// <summary>Re-enable restores only clothing; permanent appearance owners retain their own bindings.</summary>
        public void RestoreCurrentAppearance()
        {
            properties ??= new MaterialPropertyBlock();
            foreach (GarmentBinding garment in garments)
            {
                Renderer renderer = garment.Renderer;
                if (renderer == null) continue;
                renderer.enabled = true;
                renderer.sharedMaterial = material;
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", garment.Color); properties.SetColor("_Color", garment.Color);
                properties.SetTexture("_BaseMap", atlas); properties.SetTexture("_MainTex", atlas);
                properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
                properties.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
        }

        protected virtual void OnEnable() { if (!string.IsNullOrEmpty(outfitId)) RestoreCurrentAppearance(); }
    }
}
