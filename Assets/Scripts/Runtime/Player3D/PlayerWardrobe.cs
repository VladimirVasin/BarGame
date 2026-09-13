using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored clothing on the production rig, separate from its bare anatomy.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWardrobe : MonoBehaviour
    {
        [Serializable]
        public sealed class GarmentBinding
        {
            [SerializeField] private string id;
            [SerializeField] private string slot;
            [SerializeField] private Renderer[] renderers;
            [SerializeField] private Renderer[] coveredBodyRenderers;

            public GarmentBinding(string id, string slot, Renderer[] renderers, Renderer[] coveredBodyRenderers)
            {
                this.id = id; this.slot = slot;
                this.renderers = renderers == null ? null : (Renderer[])renderers.Clone();
                this.coveredBodyRenderers = coveredBodyRenderers == null ? null : (Renderer[])coveredBodyRenderers.Clone();
            }

            public string Id => id;
            public string Slot => slot;
            public IReadOnlyList<Renderer> Renderers => renderers;
            public IReadOnlyList<Renderer> CoveredBodyRenderers => coveredBodyRenderers;
        }

        public sealed class OutfitSnapshot
        {
            internal readonly string[] Items;
            internal OutfitSnapshot(string[] items) { Items = (string[])items.Clone(); }
        }

        [SerializeField] private string outfitId;
        [SerializeField] private Renderer[] bodyRenderers = Array.Empty<Renderer>();
        [SerializeField] private GarmentBinding[] garments = Array.Empty<GarmentBinding>();
        [SerializeField] private string[] equippedItems = Array.Empty<string>();
        private readonly Dictionary<Renderer, bool> appliedVisibility = new Dictionary<Renderer, bool>();
        private AppearanceLease appearanceLease;
        private int visibilityLocks;

        public string CurrentOutfitId => outfitId;
        public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;
        public IReadOnlyList<GarmentBinding> Garments => garments;
        public bool IsConfigured => !string.IsNullOrEmpty(outfitId) && garments.Length > 0;
        public bool HasAppearanceLease => appearanceLease != null;
        public bool IsVisibilityLocked => visibilityLocks > 0;

        /// <summary>Validate the complete replacement before changing any renderer or selection.</summary>
        public void Configure(string identity, Renderer[] body, GarmentBinding[] items)
        {
            RequireAvailable();
            ValidateConfiguration(identity, body, items);
            var defaults = new List<string>();
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (GarmentBinding item in items)
                if (slots.Add(item.Slot)) defaults.Add(item.Id);
            var nextRenderers = new HashSet<Renderer>(body);
            foreach (GarmentBinding item in items)
                foreach (Renderer renderer in item.Renderers) nextRenderers.Add(renderer);
            foreach (GarmentBinding previous in garments)
                foreach (Renderer renderer in previous.Renderers)
                    if (renderer != null && !nextRenderers.Contains(renderer)) renderer.enabled = false;
            outfitId = identity;
            bodyRenderers = (Renderer[])body.Clone();
            garments = (GarmentBinding[])items.Clone();
            equippedItems = defaults.ToArray();
            appliedVisibility.Clear();
            ApplyVisibility();
        }

        public void ValidateBindings()
        {
            ValidateConfiguration(outfitId, bodyRenderers, garments);
            ValidateSelection(equippedItems);
        }

        public bool IsEquipped(string itemId) => Array.IndexOf(equippedItems, itemId) >= 0;

        public string GetEquippedItem(string slot)
        {
            foreach (GarmentBinding item in garments)
                if (item.Slot == slot && IsEquipped(item.Id)) return item.Id;
            return null;
        }

        /// <summary>Null removes a slot; all items remain on this same rig for later restoration.</summary>
        public void SetSlot(string slot, string itemId)
        {
            RequireAvailable();
            if (string.IsNullOrEmpty(slot)) throw new ArgumentException("A clothing slot is required.", nameof(slot));
            bool slotExists = false;
            GarmentBinding replacement = null;
            foreach (GarmentBinding item in garments)
            {
                slotExists |= item.Slot == slot;
                if (item.Id == itemId && item.Slot == slot) replacement = item;
            }
            if (!slotExists || itemId != null && replacement == null)
                throw new ArgumentException("The item must be authored for the selected clothing slot.", nameof(itemId));
            var selected = new List<string>();
            foreach (GarmentBinding item in garments)
                if (item.Slot != slot && IsEquipped(item.Id)) selected.Add(item.Id);
            if (replacement != null) selected.Add(replacement.Id);
            equippedItems = selected.ToArray();
            ApplyVisibility();
        }

        public OutfitSnapshot CaptureOutfit() => new OutfitSnapshot(equippedItems);

        public void RestoreOutfit(OutfitSnapshot snapshot)
        {
            RequireAvailable();
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ValidateSelection(snapshot.Items);
            equippedItems = (string[])snapshot.Items.Clone();
            ApplyVisibility();
        }

        /// <summary>Logical visibility, independent of a camera temporarily hiding the world renderer.</summary>
        public bool IsRendererWorn(Renderer renderer)
        {
            foreach (GarmentBinding item in garments)
                foreach (Renderer garment in item.Renderers)
                    if (garment == renderer) return IsEquipped(item.Id);
            if (Array.IndexOf(bodyRenderers, renderer) < 0) return renderer != null && renderer.enabled;
            foreach (GarmentBinding item in garments)
                if (IsEquipped(item.Id))
                    foreach (Renderer covered in item.CoveredBodyRenderers)
                        if (covered == renderer) return false;
            return true;
        }

        /// <summary>Freezes the selection while a contextual owner borrows the appearance.</summary>
        public AppearanceLease CaptureAppearance()
        {
            RequireAvailable();
            ValidateBindings();
            appearanceLease = new AppearanceLease(this);
            return appearanceLease;
        }

        public AppearanceLease Undress()
        {
            AppearanceLease lease = CaptureAppearance();
            foreach (Renderer renderer in bodyRenderers)
                // A body part which was visible to the wardrobe but hidden by
                // another owner stays hidden. Covered anatomy becomes visible.
                if (renderer != null && !IsRendererWorn(renderer)) renderer.enabled = true;
            foreach (GarmentBinding item in garments)
                foreach (Renderer renderer in item.Renderers) renderer.enabled = false;
            return lease;
        }

        private void ApplyVisibility()
        {
            foreach (Renderer renderer in bodyRenderers) ApplyRenderer(renderer);
            foreach (GarmentBinding item in garments)
                foreach (Renderer renderer in item.Renderers) ApplyRenderer(renderer);
        }

        private void ApplyRenderer(Renderer renderer)
        {
            bool desired = IsRendererWorn(renderer);
            // Do not undo a camera/action owner's hidden flag on an unchanged item.
            if (!appliedVisibility.TryGetValue(renderer, out bool previous) ||
                previous != desired && renderer.enabled == previous)
                renderer.enabled = desired;
            appliedVisibility[renderer] = desired;
        }

        private void RequireAvailable()
        {
            if (appearanceLease != null || visibilityLocks > 0)
                throw new InvalidOperationException("Clothing is temporarily owned by a contextual appearance.");
        }

        internal IDisposable LockVisibility()
        {
            visibilityLocks++;
            return new VisibilityLock(this);
        }

        private sealed class VisibilityLock : IDisposable
        {
            private PlayerWardrobe owner;
            public VisibilityLock(PlayerWardrobe owner) { this.owner = owner; }
            public void Dispose()
            {
                if (ReferenceEquals(owner, null)) return;
                owner.visibilityLocks--;
                owner = null;
            }
        }

        private void ValidateSelection(string[] selected)
        {
            if (selected == null) throw new ArgumentException("An outfit selection is required.");
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selected)
            {
                GarmentBinding found = Array.Find(garments, item => item.Id == id);
                if (found == null || !slots.Add(found.Slot))
                    throw new ArgumentException("An outfit selects at most one authored item per slot.");
            }
        }

        private void ValidateConfiguration(string identity, Renderer[] body, GarmentBinding[] items)
        {
            if (string.IsNullOrWhiteSpace(identity) || body == null || body.Length == 0 || items == null || items.Length == 0)
                throw new InvalidOperationException("A player wardrobe requires bare anatomy and an authored outfit.");
            var anatomy = new HashSet<Renderer>();
            foreach (Renderer renderer in body)
                if (renderer == null || !renderer.transform.IsChildOf(transform) || !anatomy.Add(renderer))
                    throw new InvalidOperationException("Bare-body renderer bindings must be unique parts of this rig.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var clothing = new HashSet<Renderer>();
            foreach (GarmentBinding item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Slot) || item.Renderers == null || item.Renderers.Count == 0 ||
                    item.CoveredBodyRenderers == null)
                    throw new InvalidOperationException("Every garment requires a unique identity, slot and renderer bindings.");
                foreach (Renderer renderer in item.Renderers)
                    if (renderer == null || !renderer.transform.IsChildOf(transform) || anatomy.Contains(renderer) || !clothing.Add(renderer))
                        throw new InvalidOperationException("Garment renderers must be distinct parts of this rig, separate from bare anatomy.");
                var coverage = new HashSet<Renderer>();
                foreach (Renderer renderer in item.CoveredBodyRenderers)
                    if (renderer == null || !anatomy.Contains(renderer) || !coverage.Add(renderer))
                        throw new InvalidOperationException("Garment coverage must reference unique authored bare-body renderers.");
            }
        }

        private void OnEnable()
        {
            if (IsConfigured && appearanceLease == null) ApplyVisibility();
        }

        private void OnDestroy() => appearanceLease?.Dispose();

        public sealed class AppearanceLease : IDisposable
        {
            private PlayerWardrobe owner;
            private readonly Dictionary<Renderer, bool> visibility = new Dictionary<Renderer, bool>();
            internal AppearanceLease(PlayerWardrobe wardrobe)
            {
                owner = wardrobe;
                foreach (GarmentBinding item in wardrobe.garments)
                {
                    foreach (Renderer renderer in item.Renderers) visibility[renderer] = renderer.enabled;
                    // Bare hands/head have independent camera visibility owners.
                    // Only anatomy which clothing can cover belongs to this lease.
                    foreach (Renderer renderer in item.CoveredBodyRenderers) visibility[renderer] = renderer.enabled;
                }
            }

            public void Dispose()
            {
                if (ReferenceEquals(owner, null)) return;
                foreach (KeyValuePair<Renderer, bool> state in visibility)
                    if (state.Key != null) state.Key.enabled = state.Value;
                owner.appearanceLease = null;
                owner = null;
                visibility.Clear();
            }
        }
    }
}
