using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three fixed corpse outfits; material, body topology and global living catalog are retained.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatZombieAppearance : MonoBehaviour
    {
        private static readonly string[][] Outfits = {
            new[] { "shirt.everyday", "outerwear.everyday", "trousers.everyday", "boots.everyday" },
            new[] { "shirt.work", "outerwear.work", "trousers.work", "boots.work" },
            new[] { "shirt.warm", "outerwear.warm", "trousers.warm", "boots.everyday" }
        };
        private static readonly Color[] Skins = { new Color(.63f,.65f,.58f), new Color(.60f,.63f,.59f), new Color(.65f,.64f,.57f) };
        private static readonly Color[] Coats = { new Color(.29f,.34f,.29f), new Color(.26f,.31f,.35f), new Color(.36f,.31f,.26f) };
        private static readonly Color[] Shirts = { new Color(.50f,.48f,.40f), new Color(.47f,.49f,.45f), new Color(.40f,.43f,.38f) };
        private static readonly Color[] Trousers = { new Color(.30f,.29f,.25f), new Color(.26f,.29f,.31f), new Color(.31f,.32f,.28f) };
        private Renderer[] permanent;
        private Color[] colors;
        private MaterialPropertyBlock properties;
        private int variant = -1;
        public int Variant => variant;

        public void Configure(int selectedVariant)
        {
            if ((uint)selectedVariant >= 3) throw new ArgumentOutOfRangeException(nameof(selectedVariant));
            if (variant == selectedVariant && permanent != null) { Restore(); return; }
            var wardrobe = GetComponent<NpcWardrobe>();
            var face = GetComponent<DefaultNpcAppearance>();
            if (wardrobe == null || !wardrobe.IsModular || face == null)
                throw new InvalidOperationException("A combat corpse requires the retained modular ordinary worker.");
            variant = selectedVariant;
            Texture2D atlas = CombatZombieAssetProvider.LoadFace(variant);
            var body = new List<Renderer>();
            var hair = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (wardrobe.Owns(renderer)) continue;
                body.Add(renderer);
                if (renderer.name.StartsWith("HAIR_", StringComparison.Ordinal)) hair.Add(renderer);
            }
            permanent = body.ToArray(); colors = new Color[permanent.Length];
            for (int i = 0; i < permanent.Length; i++)
                colors[i] = permanent[i].name == "GEO_FaceSurface" ? Color.white :
                    permanent[i].name.StartsWith("HAIR_", StringComparison.Ordinal) ? new Color(.24f,.25f,.21f) : Skins[variant];
            var items = new NpcWardrobe.ItemBinding[wardrobe.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                NpcWardrobe.ItemBinding item = wardrobe.Items[i];
                var parts = new NpcWardrobe.GarmentBinding[item.Parts.Count];
                for (int j = 0; j < parts.Length; j++)
                {
                    Color tint = item.Slot switch {
                        "shirt" => Shirts[variant], "outerwear" => Coats[variant],
                        "trousers" => Trousers[variant], "boots" => new Color(.22f,.23f,.20f),
                        _ => item.Parts[j].Color
                    };
                    // Preserve the authored laces, soles, buttons and seam contrast.
                    string name = item.Parts[j].Renderer.name;
                    if (name.Contains("Button", StringComparison.Ordinal) || name.Contains("Sole", StringComparison.Ordinal)) tint *= .65f;
                    tint.a = 1f;
                    parts[j] = new NpcWardrobe.GarmentBinding(item.Slot, item.Parts[j].Renderer, tint);
                }
                items[i] = new NpcWardrobe.ItemBinding(item.Id, item.Slot, parts, Copy(item.CoveredRenderers));
            }
            Material shared = wardrobe.Garments[0].Renderer.sharedMaterial;
            wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWorker, atlas, shared, permanent, items,
                new[] { new NpcWardrobe.OutfitPreset("corpse", Outfits[variant]) }, "corpse");
            face.Configure(new[] { new DefaultNpcAppearance.FaceBinding("corpse", atlas) }, "corpse",
                new[] { new DefaultNpcAppearance.HairColorBinding("gray", new Color(.24f,.25f,.21f)) }, hair.ToArray());
            GetComponent<NpcHandPose>()?.ResetGrip();
            Restore();
        }

        private static Renderer[] Copy(IReadOnlyList<Renderer> values)
        {
            var result = new Renderer[values.Count];
            for (int i = 0; i < result.Length; i++) result[i] = values[i];
            return result;
        }

        public void Restore()
        {
            if (permanent == null || variant < 0) return;
            properties ??= new MaterialPropertyBlock();
            Texture2D atlas = CombatZombieAssetProvider.LoadFace(variant);
            for (int i = 0; i < permanent.Length; i++)
            {
                Renderer renderer = permanent[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", colors[i]); properties.SetColor("_Color", colors[i]);
                properties.SetTexture("_BaseMap", atlas); properties.SetTexture("_MainTex", atlas);
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
        }

        private void OnEnable() => Restore();
    }
}
