using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Seven authored appearances on the same rig. Passive copies never advance session state.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCorruptionAppearance : MonoBehaviour
    {
        public const string ShapeName = "HeroCorruption";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly Texture2D[,] textures = new Texture2D[7, 4];
        private static readonly bool[,] resolved = new bool[7, 4];
        private static readonly string[] textureNames = { "Face", "BareSkin", "Dialogue", "Clothing" };
        private readonly List<Surface> surfaces = new List<Surface>();
        private MaterialPropertyBlock properties;
        private Player3DAssetRegistry registry;
        private Texture2D originalBare;
        private Texture2D originalClothing;
        private PlayerHair hair;

        private sealed class Surface
        {
            public Renderer Renderer;
            public SkinnedMeshRenderer Skin;
            public int Shape;
            public int HairShort, HairLong;
            public Color Original;
            public bool BareAtlas, FlatSkin, Clothing, ClothingAtlas;
        }

        public int AppliedLevel { get; private set; } = 1;
        public float Severity => HeroCorruptionRules.Severity(AppliedLevel);
        public int DeformedSurfaceCount { get; private set; }
        internal Texture2D FaceTexture => AppliedLevel > 1 ? ResolveTexture(0) : null;
        internal Texture2D SpeechTexture => AppliedLevel > 1 ? ResolveTexture(2) : null;
        internal Texture2D BareSkinTexture => AppliedLevel > 1 ? ResolveTexture(1) ?? originalBare : originalBare;

        /// <summary>One registry-bound pass at construction; no hierarchy searches in presentation updates.</summary>
        public static PlayerCorruptionAppearance Ensure(Player3DAssetRegistry source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            PlayerCorruptionAppearance owner = source.GetComponent<PlayerCorruptionAppearance>();
            if (owner == null) owner = source.gameObject.AddComponent<PlayerCorruptionAppearance>();
            if (owner.registry != source) owner.Initialize(source);
            return owner;
        }

        private void Initialize(Player3DAssetRegistry source)
        {
            registry = source;
            properties ??= new MaterialPropertyBlock();
            originalBare = Player3DBathingAppearance.BareSkinAtlas;
            hair = source.GetComponent<PlayerHair>();
            surfaces.Clear();
            DeformedSurfaceCount = 0;
            foreach (Player3DMeshBinding binding in source.MeshBindings)
            {
                if (binding?.Renderer == null) continue;
                Renderer renderer = binding.Renderer;
                var skin = renderer as SkinnedMeshRenderer;
                int shape = skin != null ? CharacterJointDeformation.FindShape(skin.sharedMesh, ShapeName) : -1;
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                bool bare = originalBare != null && properties.GetTexture(BaseMapId) == originalBare;
                bool clothingAtlas = binding.PaletteMaterialName == "MAT_JacketAtlas" ||
                    binding.PaletteMaterialName == "MAT_JeansAtlas";
                if (clothingAtlas && originalClothing == null)
                    originalClothing = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture as Texture2D : null;
                surfaces.Add(new Surface
                {
                    Renderer = renderer, Skin = skin, Shape = shape,
                    HairShort = skin != null ? CharacterJointDeformation.FindShape(skin.sharedMesh, "HeroHairShort") : -1,
                    HairLong = skin != null ? CharacterJointDeformation.FindShape(skin.sharedMesh, "HeroHairLong") : -1,
                    Original = bare ? Color.white : binding.BaseColor, BareAtlas = bare,
                    FlatSkin = !bare && binding.PaletteMaterialName != null &&
                        binding.PaletteMaterialName.StartsWith("MAT_Skin", StringComparison.Ordinal),
                    Clothing = binding.Role == "clothing", ClothingAtlas = clothingAtlas
                });
                if (shape >= 0) DeformedSurfaceCount++;
            }
            ApplyLevel(AppliedLevel);
        }

        /// <summary>Explicit preview may reverse; normal progression is owned by the session.</summary>
        public void ApplyLevel(int level)
        {
            if (level < HeroCorruptionRules.FirstLevel || level > HeroCorruptionRules.LastLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            AppliedLevel = level;
            float weight = Severity * 100f;
            foreach (Surface surface in surfaces)
            {
                if (surface.Skin == null) continue;
                if (surface.Shape >= 0) surface.Skin.SetBlendShapeWeight(surface.Shape, weight);
                if (surface.HairShort >= 0) surface.Skin.SetBlendShapeWeight(surface.HairShort,
                    HeroCorruptionRules.ShortHairWeight(level) * 100f);
                if (surface.HairLong >= 0) surface.Skin.SetBlendShapeWeight(surface.HairLong,
                    HeroCorruptionRules.LongHairWeight(level) * 100f);
            }
            hair?.ApplyCorruptionLevel(level);
            RefreshPalette();
        }

        /// <summary>Reapplies after wardrobe/bathing palette restoration, preserving expressions and UV selection.</summary>
        internal void RefreshPalette()
        {
            if (registry == null) return;
            float amount = Severity;
            Texture2D bare = BareSkinTexture;
            Texture2D clothing = AppliedLevel == 1 ? originalClothing : ResolveTexture(3) ?? originalClothing;
            foreach (Surface surface in surfaces)
            {
                if (surface.Renderer == null || !(surface.BareAtlas || surface.FlatSkin || surface.Clothing)) continue;
                properties.Clear();
                surface.Renderer.GetPropertyBlock(properties);
                Color multiplier = surface.FlatSkin
                    ? Color.Lerp(Color.white, new Color(.80f, .91f, .82f), amount).linear
                    : surface.Clothing && !surface.ClothingAtlas
                        ? Color.Lerp(Color.white, new Color(.78f, .77f, .67f), amount) : Color.white;
                Color tint = surface.Original * multiplier;
                tint.a = surface.Original.a;
                properties.SetColor(BaseColorId, tint);
                properties.SetColor(ColorId, tint);
                if (surface.BareAtlas && bare != null)
                {
                    properties.SetTexture(BaseMapId, bare);
                    properties.SetTexture(MainTexId, bare);
                }
                if (surface.ClothingAtlas && clothing != null)
                {
                    properties.SetTexture(BaseMapId, clothing);
                    properties.SetTexture(MainTexId, clothing);
                }
                surface.Renderer.SetPropertyBlock(properties);
            }
        }

        private Texture2D ResolveTexture(int kind)
        {
            int stage = AppliedLevel - 1;
            if (!resolved[stage, kind])
            {
                resolved[stage, kind] = true;
                textures[stage, kind] = Resources.Load<Texture2D>(
                    "Player/Corruption/" + textureNames[kind] + AppliedLevel);
            }
            return textures[stage, kind];
        }

        public void CopyAppearanceTo(PlayerCorruptionAppearance target)
        {
            if (target != null && target.AppliedLevel != AppliedLevel) target.ApplyLevel(AppliedLevel);
        }
    }
}
