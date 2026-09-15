using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored clothing and coverage on one rig, with fixed or modular outfits.</summary>
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
        [Serializable] public sealed class ItemBinding
        {
            [SerializeField] private string id;
            [SerializeField] private string slot;
            [SerializeField] private GarmentBinding[] parts;
            [SerializeField] private Renderer[] coveredRenderers;
            public string Id => id;
            public string Slot => slot;
            public IReadOnlyList<GarmentBinding> Parts => parts;
            public IReadOnlyList<Renderer> CoveredRenderers => coveredRenderers;
            public ItemBinding(string id, string slot, GarmentBinding[] parts, Renderer[] coveredRenderers)
            {
                this.id = id; this.slot = slot;
                this.parts = parts == null ? null : (GarmentBinding[])parts.Clone();
                this.coveredRenderers = coveredRenderers == null ? null : (Renderer[])coveredRenderers.Clone();
            }
        }

        [Serializable] public sealed class OutfitPreset
        {
            [SerializeField] private string id;
            [SerializeField] private string[] itemIds;
            public string Id => id;
            public IReadOnlyList<string> ItemIds => itemIds;
            public OutfitPreset(string id, string[] itemIds)
            { this.id = id; this.itemIds = itemIds == null ? null : (string[])itemIds.Clone(); }
        }

        [SerializeField] private string wardrobeId;
        [SerializeField] private Renderer[] bodyRenderers = Array.Empty<Renderer>();
        [SerializeField] private ItemBinding[] items = Array.Empty<ItemBinding>();
        [SerializeField] private OutfitPreset[] presets = Array.Empty<OutfitPreset>();
        [SerializeField] private string[] equippedItemIds = Array.Empty<string>();
        private MaterialPropertyBlock properties;
        public string CurrentOutfitId => outfitId;
        public Texture2D Atlas => atlas;
        public IReadOnlyList<GarmentBinding> Garments => garments;
        public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;
        public IReadOnlyList<ItemBinding> Items => items;
        public IReadOnlyList<OutfitPreset> Presets => presets;
        public IReadOnlyList<string> EquippedItemIds => equippedItemIds;
        public bool IsModular => items != null && items.Length > 0;

        public void Configure(string identity, Texture2D texture, Material sharedMaterial, GarmentBinding[] bindings)
        {
            ValidateOutfit(identity, texture, sharedMaterial, bindings);
            ReleaseReplacedRenderers(bindings, Array.Empty<Renderer>());
            outfitId = identity; atlas = texture; material = sharedMaterial;
            garments = (GarmentBinding[])bindings.Clone();
            wardrobeId = null; bodyRenderers = Array.Empty<Renderer>();
            items = Array.Empty<ItemBinding>(); presets = Array.Empty<OutfitPreset>();
            equippedItemIds = Array.Empty<string>();
            RestoreCurrentAppearance();
        }

        /// <summary>Validate all items and presets before changing renderers or the current selection.</summary>
        public void ConfigureModular(string identity, Texture2D texture, Material sharedMaterial,
            Renderer[] body, ItemBinding[] catalog, OutfitPreset[] outfits, string defaultOutfitId)
        {
            ValidateModular(identity, texture, sharedMaterial, body, catalog, outfits);
            OutfitPreset initial = Array.Find(outfits, preset => preset.Id == defaultOutfitId);
            if (initial == null) throw new ArgumentException("The default outfit must be an authored preset.", nameof(defaultOutfitId));
            var nextParts = new List<GarmentBinding>();
            var nextItems = new ItemBinding[catalog.Length];
            for (int i = 0; i < catalog.Length; i++)
            {
                ItemBinding item = catalog[i];
                var parts = new List<GarmentBinding>(item.Parts).ToArray();
                nextItems[i] = new ItemBinding(item.Id, item.Slot, parts,
                    new List<Renderer>(item.CoveredRenderers).ToArray());
                nextParts.AddRange(parts);
            }
            var nextPresets = new OutfitPreset[outfits.Length];
            for (int i = 0; i < outfits.Length; i++)
                nextPresets[i] = new OutfitPreset(outfits[i].Id, new List<string>(outfits[i].ItemIds).ToArray());
            GarmentBinding[] flattened = nextParts.ToArray();
            ReleaseReplacedRenderers(flattened, body);
            wardrobeId = identity; atlas = texture; material = sharedMaterial;
            bodyRenderers = (Renderer[])body.Clone(); items = nextItems; presets = nextPresets;
            garments = flattened; outfitId = initial.Id;
            equippedItemIds = new List<string>(initial.ItemIds).ToArray();
            RestoreCurrentAppearance();
        }

        private void ReleaseReplacedRenderers(GarmentBinding[] nextParts, Renderer[] nextBody)
        {
            var next = new HashSet<Renderer>(nextBody);
            foreach (GarmentBinding binding in nextParts) next.Add(binding.Renderer);
            foreach (GarmentBinding previous in garments)
                if (previous?.Renderer != null && !next.Contains(previous.Renderer)) previous.Renderer.enabled = false;
            foreach (Renderer previous in bodyRenderers)
                if (previous != null && !next.Contains(previous)) previous.enabled = true;
        }

        public bool Owns(Renderer target)
        {
            foreach (GarmentBinding garment in garments) if (garment.Renderer == target) return true;
            return false;
        }

        public bool IsEquipped(string itemId) => Array.IndexOf(equippedItemIds, itemId) >= 0;

        public void SetAtlas(Texture2D texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            atlas = texture;
            RestoreCurrentAppearance();
        }

        public void ApplyItems(IReadOnlyList<string> selected)
        {
            RequireModular();
            ValidateSelection(items, selected);
            equippedItemIds = new List<string>(selected).ToArray();
            outfitId = MatchingPreset(equippedItemIds) ?? "custom";
            RestoreCurrentAppearance();
        }

        /// <summary>Stable independent mixing for authoring; world characters use DefaultNpcPopulation.</summary>
        public void Randomize(string stableIdentity)
        {
            RequireModular();
            var slots = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (ItemBinding item in items)
            {
                if (!slots.TryGetValue(item.Slot, out List<string> choices))
                    slots.Add(item.Slot, choices = new List<string>());
                choices.Add(item.Id);
            }
            var selected = new List<string>();
            foreach (var pair in slots)
            {
                pair.Value.Sort(StringComparer.Ordinal);
                selected.Add(pair.Value[DefaultNpcAppearanceSelection.Index(stableIdentity, pair.Key, pair.Value.Count)]);
            }
            ApplyItems(selected);
        }

        public string GetEquippedItem(string slot)
        {
            foreach (ItemBinding item in items)
                if (item.Slot == slot && IsEquipped(item.Id)) return item.Id;
            return null;
        }

        public void SetSlot(string slot, string itemId)
        {
            RequireModular();
            bool exists = false;
            ItemBinding replacement = null;
            foreach (ItemBinding item in items)
            {
                exists |= item.Slot == slot;
                if (item.Id == itemId && item.Slot == slot) replacement = item;
            }
            if (string.IsNullOrWhiteSpace(slot) || !exists || itemId != null && replacement == null)
                throw new ArgumentException("The item must be authored for the selected clothing slot.", nameof(itemId));
            var next = new List<string>();
            foreach (ItemBinding item in items)
                if (item.Slot != slot && IsEquipped(item.Id)) next.Add(item.Id);
            if (replacement != null) next.Add(replacement.Id);
            equippedItemIds = next.ToArray();
            outfitId = MatchingPreset(equippedItemIds) ?? "custom";
            RestoreCurrentAppearance();
        }

        public void ApplyOutfit(string presetId)
        {
            RequireModular();
            OutfitPreset preset = Array.Find(presets, candidate => candidate.Id == presetId);
            if (preset == null) throw new ArgumentException("Unknown authored NPC outfit: " + presetId, nameof(presetId));
            ValidateSelection(items, preset.ItemIds);
            equippedItemIds = new List<string>(preset.ItemIds).ToArray();
            outfitId = preset.Id;
            RestoreCurrentAppearance();
        }

        private string MatchingPreset(string[] selected)
        {
            var worn = new HashSet<string>(selected, StringComparer.Ordinal);
            foreach (OutfitPreset preset in presets)
                if (worn.SetEquals(preset.ItemIds)) return preset.Id;
            return null;
        }

        private void RequireModular()
        {
            if (!IsModular) throw new InvalidOperationException("This NPC has a fixed authored outfit.");
        }

        public void ValidateBindings()
        {
            if (!IsModular) { ValidateOutfit(outfitId, atlas, material, garments); return; }
            ValidateModular(wardrobeId, atlas, material, bodyRenderers, items, presets);
            ValidateSelection(items, equippedItemIds);
        }

        private void ValidateModular(string identity, Texture2D texture, Material sharedMaterial,
            Renderer[] body, ItemBinding[] catalog, OutfitPreset[] outfits)
        {
            if (string.IsNullOrWhiteSpace(identity) || texture == null || sharedMaterial == null ||
                body == null || body.Length == 0 || catalog == null || catalog.Length == 0 || outfits == null || outfits.Length == 0)
                throw new InvalidOperationException("A modular wardrobe requires anatomy, items and outfit presets.");
            var anatomy = new HashSet<Renderer>();
            foreach (Renderer renderer in body)
                if (renderer == null || !renderer.transform.IsChildOf(transform) || !anatomy.Add(renderer))
                    throw new InvalidOperationException("Body bindings must be unique renderers on this NPC rig.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var clothing = new Dictionary<Renderer, ItemBinding>();
            foreach (ItemBinding item in catalog)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Slot) || item.Parts == null || item.Parts.Count == 0 || item.CoveredRenderers == null)
                    throw new InvalidOperationException("Each item requires a unique ID, slot, parts and coverage.");
                foreach (GarmentBinding part in item.Parts)
                {
                    if (part == null || part.Slot != item.Slot || part.Renderer == null ||
                        !part.Renderer.transform.IsChildOf(transform) || anatomy.Contains(part.Renderer) || clothing.ContainsKey(part.Renderer))
                        throw new InvalidOperationException("Item parts must have their item's slot and distinct clothing renderers on this rig.");
                    clothing.Add(part.Renderer, item);
                }
            }
            foreach (ItemBinding item in catalog)
            {
                var coverage = new HashSet<Renderer>();
                foreach (Renderer renderer in item.CoveredRenderers)
                {
                    if (renderer == null || !coverage.Add(renderer))
                        throw new InvalidOperationException("Coverage must reference unique body or inner clothing renderers.");
                    if (anatomy.Contains(renderer)) continue;
                    if (!clothing.TryGetValue(renderer, out ItemBinding covered) || covered.Slot == item.Slot)
                        throw new InvalidOperationException("Clothing may cover anatomy or another clothing slot, never itself.");
                }
            }
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (OutfitPreset preset in outfits)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || !presetIds.Add(preset.Id))
                    throw new InvalidOperationException("Outfit presets require unique IDs.");
                ValidateSelection(catalog, preset.ItemIds);
            }
        }

        private static void ValidateSelection(ItemBinding[] catalog, IReadOnlyList<string> selected)
        {
            if (selected == null) throw new ArgumentException("An outfit selection is required.");
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selected)
            {
                ItemBinding item = Array.Find(catalog, candidate => candidate.Id == id);
                if (item == null || !slots.Add(item.Slot))
                    throw new ArgumentException("An outfit selects at most one known item per clothing slot.");
            }
        }

        private static void ValidateOutfit(string outfitId, Texture2D atlas, Material material, GarmentBinding[] garments)
        {
            if (string.IsNullOrWhiteSpace(outfitId) || atlas == null || material == null || garments == null || garments.Length == 0)
                throw new InvalidOperationException("An authored NPC requires one complete outfit.");
            var unique = new HashSet<Renderer>();
            foreach (GarmentBinding garment in garments)
                if (garment == null || string.IsNullOrWhiteSpace(garment.Slot) || garment.Renderer == null || !unique.Add(garment.Renderer))
                    throw new InvalidOperationException("Every garment needs one explicit outfit slot and renderer binding.");
        }

        /// <summary>Restores selected garments and coverage, retaining the permanent body's colors.</summary>
        public void RestoreCurrentAppearance()
        {
            var visible = new HashSet<Renderer>();
            var covered = new HashSet<Renderer>();
            if (IsModular)
            {
                foreach (ItemBinding item in items)
                    if (IsEquipped(item.Id))
                    {
                        foreach (GarmentBinding part in item.Parts) visible.Add(part.Renderer);
                        foreach (Renderer renderer in item.CoveredRenderers) covered.Add(renderer);
                    }
                foreach (Renderer renderer in bodyRenderers)
                    if (renderer != null) renderer.enabled = !covered.Contains(renderer);
            }
            properties ??= new MaterialPropertyBlock();
            foreach (GarmentBinding garment in garments)
            {
                Renderer renderer = garment.Renderer;
                if (renderer == null) continue;
                renderer.enabled = !IsModular || visible.Contains(renderer) && !covered.Contains(renderer);
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
