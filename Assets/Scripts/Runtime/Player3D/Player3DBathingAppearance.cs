using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Temporarily undresses the one production hero. The modular wardrobe
    /// reveals authored bare anatomy and leases its selection until restoration.
    /// Older rigs retain the material/atlas fallback. Neither route creates
    /// a material or changes a renderer already hidden by another owner.
    /// Only one bathing lease can be active; restoration is idempotent.
    /// </summary>
    public sealed class Player3DBathingAppearance
    {
        public const string ClothingRole = "clothing";
        public const string SkinMaterialName = "MAT_Skin";
        public const string SkinShadowMaterialName = "MAT_SkinShadow";
        public const string ShirtMaterialName = "MAT_Shirt";
        public const string JeansAtlasMaterialName = "MAT_JeansAtlas";

        /// <summary>The generator's bare-skin atlas, under Resources so the shower can load it by name.</summary>
        public const string BareSkinAtlasResourcePath = "Player/PlayerBareSkinAtlas";

        /// <summary>The palette's unused dark skin tone, the boots' colour in the shower without the atlas.</summary>
        public static readonly Color SkinDark =
            new Color(57f / 255f, 45f / 255f, 46f / 255f);

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int LegacyMapTransformId =
            Shader.PropertyToID("_MainTex_ST");
        private static readonly Vector4 WholeTexture = new Vector4(1f, 1f, 0f, 0f);

        private static Player3DBathingAppearance active;
        private static Texture2D bareSkinAtlas;
        private static bool bareSkinAtlasResolved;

        private readonly List<Snapshot> snapshots = new List<Snapshot>(16);
        private PlayerWardrobe.AppearanceLease wardrobeLease;

        private Player3DBathingAppearance()
        {
        }

        public enum BareTone
        {
            None = 0,
            Skin = 1,
            SkinShadow = 2,
            SkinDark = 3
        }

        /// <summary>A lease is out on some rig.</summary>
        public static bool IsActive => active != null;

        /// <summary>The packaged bare-skin atlas, or null on a build without it.</summary>
        public static Texture2D BareSkinAtlas
        {
            get
            {
                if (!bareSkinAtlasResolved || bareSkinAtlas == null)
                {
                    bareSkinAtlasResolved = true;
                    bareSkinAtlas = Resources.Load<Texture2D>(BareSkinAtlasResourcePath);
                }

                return bareSkinAtlas;
            }
        }

        public int HiddenRendererCount { get; private set; }
        public int RepaintedRendererCount { get; private set; }
        public bool IsApplied => wardrobeLease != null || snapshots.Count > 0;

        /// <summary>Whether this lease painted the atlas rather than the flat fallback tones.</summary>
        public bool UsesBareSkinAtlas { get; private set; }

        /// <summary>Which roles come off with the clothes.</summary>
        public static bool IsHidden(string role)
        {
            return !string.IsNullOrEmpty(role) &&
                   string.Equals(role, ClothingRole, StringComparison.Ordinal);
        }

        /// <summary>
        /// Which skin tone a garment-painted body part takes: the shirt
        /// becomes the chest's skin; the jeans become skin on the thighs,
        /// shadowed skin on the pelvis and shins so the buttocks separate
        /// from the back, and dark skin on the boot-shaped feet.
        /// </summary>
        public static BareTone ResolveBareTone(
            string paletteMaterialName,
            string boneName)
        {
            if (string.Equals(paletteMaterialName, ShirtMaterialName, StringComparison.Ordinal))
            {
                return BareTone.Skin;
            }

            if (!string.Equals(paletteMaterialName, JeansAtlasMaterialName, StringComparison.Ordinal))
            {
                return BareTone.None;
            }

            string bone = boneName ?? string.Empty;
            if (bone.StartsWith("foot", StringComparison.OrdinalIgnoreCase))
            {
                return BareTone.SkinDark;
            }

            if (bone.StartsWith("shin", StringComparison.OrdinalIgnoreCase) ||
                bone.StartsWith("pelvis", StringComparison.OrdinalIgnoreCase))
            {
                return BareTone.SkinShadow;
            }

            return BareTone.Skin;
        }

        /// <summary>
        /// Undresses the rig and returns the handle that dresses it again.
        /// Throws while another lease is out: two owners restoring in the
        /// wrong order would leave the hero half dressed.
        /// </summary>
        public static Player3DBathingAppearance Apply(
            Player3DAssetRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (active != null)
            {
                throw new InvalidOperationException(
                    "The hero is already undressed by another owner.");
            }

            PlayerWardrobe wardrobe = registry.GetComponent<PlayerWardrobe>();
            if (wardrobe != null && wardrobe.IsConfigured)
            {
                var modularLease = new Player3DBathingAppearance();
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                    foreach (Renderer renderer in garment.Renderers)
                        if (renderer.enabled) modularLease.HiddenRendererCount++;
                modularLease.wardrobeLease = wardrobe.Undress();
                modularLease.UsesBareSkinAtlas = BareSkinAtlas != null;
                active = modularLease;
                return modularLease;
            }

            IReadOnlyList<Player3DMeshBinding> bindings = registry.MeshBindings;
            if (!TryFindPalette(bindings, SkinMaterialName,
                    out Material skinMaterial, out Color skin))
            {
                throw new InvalidOperationException(
                    "The bathing appearance requires the production hero skin material.");
            }

            Color skinShadow = TryFindPalette(bindings, SkinShadowMaterialName,
                out _, out Color shadow)
                ? shadow
                : skin * 0.75f;

            var lease = new Player3DBathingAppearance();
            Texture2D atlas = BareSkinAtlas;
            lease.UsesBareSkinAtlas = atlas != null;
            var block = new MaterialPropertyBlock();
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                if (IsHidden(binding.Role))
                {
                    if (!target.enabled)
                    {
                        continue;
                    }

                    lease.snapshots.Add(Snapshot.Capture(target));
                    target.enabled = false;
                    lease.HiddenRendererCount++;
                    continue;
                }

                BareTone tone = ResolveBareTone(
                    binding.PaletteMaterialName,
                    binding.BoneName);
                if (tone == BareTone.None)
                {
                    continue;
                }

                lease.snapshots.Add(Snapshot.Capture(target));
                if (!ReferenceEquals(target.sharedMaterial, skinMaterial))
                {
                    target.sharedMaterial = skinMaterial;
                }

                block.Clear();
                target.GetPropertyBlock(block);
                if (atlas != null)
                {
                    // The atlas carries final sRGB skin; white keeps the
                    // block from tinting it a second time.
                    block.SetTexture(BaseMapId, atlas);
                    block.SetVector(BaseMapTransformId, WholeTexture);
                    block.SetTexture(LegacyMapId, atlas);
                    block.SetVector(LegacyMapTransformId, WholeTexture);
                    block.SetColor(BaseColorId, Color.white);
                    block.SetColor(LegacyColorId, Color.white);
                }
                else
                {
                    Color color = tone == BareTone.SkinDark
                        ? SkinDark
                        : tone == BareTone.SkinShadow
                            ? skinShadow
                            : skin;
                    block.SetColor(BaseColorId, color);
                    block.SetColor(LegacyColorId, color);
                }

                target.SetPropertyBlock(block);
                lease.RepaintedRendererCount++;
            }

            active = lease;
            return lease;
        }

        /// <summary>Dresses him again, exactly as he was. Idempotent.</summary>
        public void Restore()
        {
            wardrobeLease?.Dispose();
            wardrobeLease = null;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                Snapshot snapshot = snapshots[index];
                if (snapshot.Renderer != null)
                {
                    snapshot.Renderer.sharedMaterial = snapshot.Material;
                    snapshot.Renderer.SetPropertyBlock(snapshot.Block);
                    snapshot.Renderer.enabled = snapshot.Enabled;
                }
            }

            snapshots.Clear();
            HiddenRendererCount = 0;
            RepaintedRendererCount = 0;
            UsesBareSkinAtlas = false;
            if (ReferenceEquals(active, this))
            {
                active = null;
            }
        }

        private static bool TryFindPalette(
            IReadOnlyList<Player3DMeshBinding> bindings,
            string paletteMaterialName,
            out Material material,
            out Color baseColor)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding != null && binding.Renderer != null &&
                    binding.Renderer.sharedMaterial != null &&
                    string.Equals(
                        binding.PaletteMaterialName,
                        paletteMaterialName,
                        StringComparison.Ordinal))
                {
                    material = binding.Renderer.sharedMaterial;
                    baseColor = binding.BaseColor;
                    return true;
                }
            }

            material = null;
            baseColor = Color.white;
            return false;
        }

        private readonly struct Snapshot
        {
            public readonly Renderer Renderer;
            public readonly bool Enabled;
            public readonly Material Material;
            public readonly MaterialPropertyBlock Block;

            private Snapshot(
                Renderer renderer,
                bool enabled,
                Material material,
                MaterialPropertyBlock block)
            {
                Renderer = renderer;
                Enabled = enabled;
                Material = material;
                Block = block;
            }

            public static Snapshot Capture(Renderer renderer)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                return new Snapshot(
                    renderer,
                    renderer.enabled,
                    renderer.sharedMaterial,
                    block);
            }
        }
    }
}
