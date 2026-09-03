using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum BarSurfaceKind
    {
        WornPlank,
        Wallpaper,
        DarkWood,
        WornLeather,
        CeilingPlaster,
        PolishedWood,
        AgedBrass,
        MirrorGlass,
        PatternedGlass,
        PubCarpet,
        WornFabric,
        PaintedMetal,
        Paper,
        BottleGlass,
        Ceramic
    }

    /// <summary>
    /// Owns the packaged British-pub surface recipes and applies them
    /// without cloning the shared runtime primitive material. The
    /// recipe constants are the measured contract emitted by
    /// `tools/build-bar-textures.py` into
    /// `ArtSource/Bar/bar-textures.json`; compensation follows the
    /// city-facade linear rule. District identity still owns the tint;
    /// the authored part binding owns which physical sheet it wears.
    /// </summary>
    internal static class BarSurfaceAppearance
    {
        public const string WornPlankTextureResourcePath =
            "Bar/Textures/BarWornPlankAlbedo";
        public const string WallpaperTextureResourcePath =
            "Bar/Textures/BarWallpaperAlbedo";
        public const string DarkWoodTextureResourcePath =
            "Bar/Textures/BarDarkWoodAlbedo";
        public const string WornLeatherTextureResourcePath =
            "Bar/Textures/BarWornLeatherAlbedo";
        public const string CeilingPlasterTextureResourcePath =
            "Bar/Textures/BarCeilingPlasterAlbedo";
        public const string PolishedWoodTextureResourcePath =
            "Bar/Textures/BarPolishedWoodAlbedo";
        public const string AgedBrassTextureResourcePath =
            "Bar/Textures/BarAgedBrassAlbedo";
        public const string MirrorGlassTextureResourcePath =
            "Bar/Textures/BarMirrorGlassAlbedo";
        public const string PatternedGlassTextureResourcePath =
            "Bar/Textures/BarPatternedGlassAlbedo";
        public const string PubCarpetTextureResourcePath =
            "Bar/Textures/BarPubCarpetAlbedo";
        public const string WornFabricTextureResourcePath =
            "Bar/Textures/BarWornFabricAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "Bar/Textures/BarPaintedMetalAlbedo";
        public const string PaperTextureResourcePath =
            "Bar/Textures/BarPaperAlbedo";
        public const string BottleGlassTextureResourcePath =
            "Bar/Textures/BarBottleGlassAlbedo";
        public const string CeramicTextureResourcePath =
            "Bar/Textures/BarCeramicAlbedo";

        private const int SurfaceCount =
            (int)BarSurfaceKind.Ceramic + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the home
        // (1000) and supermarket (3000) kinds.
        private const int HashSaltBase = 4000;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceAlbedoCompensationId =
            Shader.PropertyToID("_SurfaceAlbedoCompensation");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly Vector4 IdentityBaseMapTransform =
            new Vector4(1f, 1f, 0f, 0f);

        private static Texture2D[] cachedTextures =
            new Texture2D[SurfaceCount];

        public static HomeSurfaceRecipe GetRecipe(BarSurfaceKind kind)
        {
            switch (kind)
            {
                case BarSurfaceKind.WornPlank:
                    return new HomeSurfaceRecipe(
                        WornPlankTextureResourcePath,
                        1.5f,
                        0.08f,
                        0f,
                        1.4575f);
                case BarSurfaceKind.Wallpaper:
                    return new HomeSurfaceRecipe(
                        WallpaperTextureResourcePath,
                        1.8f,
                        0.04f,
                        0f,
                        1.433f);
                case BarSurfaceKind.DarkWood:
                    return new HomeSurfaceRecipe(
                        DarkWoodTextureResourcePath,
                        1.1f,
                        0.12f,
                        0f,
                        1.4495f);
                case BarSurfaceKind.WornLeather:
                    return new HomeSurfaceRecipe(
                        WornLeatherTextureResourcePath,
                        0.9f,
                        0.06f,
                        0f,
                        1.396f);
                case BarSurfaceKind.CeilingPlaster:
                    return new HomeSurfaceRecipe(
                        CeilingPlasterTextureResourcePath,
                        2.4f,
                        0.025f,
                        0f,
                        1.408f);
                case BarSurfaceKind.PolishedWood:
                    return new HomeSurfaceRecipe(
                        PolishedWoodTextureResourcePath,
                        0.75f,
                        0.34f,
                        0f,
                        1.436f);
                case BarSurfaceKind.AgedBrass:
                    return new HomeSurfaceRecipe(
                        AgedBrassTextureResourcePath,
                        0.42f,
                        0.42f,
                        0.72f,
                        1.1625f);
                case BarSurfaceKind.MirrorGlass:
                    return new HomeSurfaceRecipe(
                        MirrorGlassTextureResourcePath,
                        1.35f,
                        0.78f,
                        0.12f,
                        1.263f);
                case BarSurfaceKind.PatternedGlass:
                    return new HomeSurfaceRecipe(
                        PatternedGlassTextureResourcePath,
                        0.72f,
                        0.64f,
                        0.04f,
                        1.316f);
                case BarSurfaceKind.PubCarpet:
                    return new HomeSurfaceRecipe(
                        PubCarpetTextureResourcePath,
                        1.15f,
                        0.015f,
                        0f,
                        1.3605f);
                case BarSurfaceKind.WornFabric:
                    return new HomeSurfaceRecipe(
                        WornFabricTextureResourcePath,
                        0.68f,
                        0.02f,
                        0f,
                        1.455f);
                case BarSurfaceKind.PaintedMetal:
                    return new HomeSurfaceRecipe(
                        PaintedMetalTextureResourcePath,
                        0.82f,
                        0.20f,
                        0.30f,
                        1.3715f);
                case BarSurfaceKind.Paper:
                    return new HomeSurfaceRecipe(
                        PaperTextureResourcePath,
                        0.55f,
                        0.025f,
                        0f,
                        1.1865f);
                case BarSurfaceKind.BottleGlass:
                    return new HomeSurfaceRecipe(
                        BottleGlassTextureResourcePath,
                        0.36f,
                        0.68f,
                        0.02f,
                        1.111f);
                case BarSurfaceKind.Ceramic:
                    return new HomeSurfaceRecipe(
                        CeramicTextureResourcePath,
                        0.42f,
                        0.48f,
                        0.04f,
                        1.202f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(BarSurfaceKind kind)
        {
            int index = ValidateKind(kind);
            if (cachedTextures[index] == null)
            {
                cachedTextures[index] = Resources.Load<Texture2D>(
                    GetRecipe(kind).ResourcePath);
            }

            if (cachedTextures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing bar {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static bool TryResolveSheet(
            string sheet,
            out BarSurfaceKind kind)
        {
            switch (sheet)
            {
                case "WornPlank":
                    kind = BarSurfaceKind.WornPlank;
                    return true;
                case "Wallpaper":
                    kind = BarSurfaceKind.Wallpaper;
                    return true;
                case "DarkWood":
                    kind = BarSurfaceKind.DarkWood;
                    return true;
                case "WornLeather":
                    kind = BarSurfaceKind.WornLeather;
                    return true;
                case "CeilingPlaster":
                    kind = BarSurfaceKind.CeilingPlaster;
                    return true;
                case "PolishedWood":
                    kind = BarSurfaceKind.PolishedWood;
                    return true;
                case "AgedBrass":
                    kind = BarSurfaceKind.AgedBrass;
                    return true;
                case "MirrorGlass":
                    kind = BarSurfaceKind.MirrorGlass;
                    return true;
                case "PatternedGlass":
                    kind = BarSurfaceKind.PatternedGlass;
                    return true;
                case "PubCarpet":
                    kind = BarSurfaceKind.PubCarpet;
                    return true;
                case "WornFabric":
                    kind = BarSurfaceKind.WornFabric;
                    return true;
                case "PaintedMetal":
                    kind = BarSurfaceKind.PaintedMetal;
                    return true;
                case "Paper":
                    kind = BarSurfaceKind.Paper;
                    return true;
                case "BottleGlass":
                    kind = BarSurfaceKind.BottleGlass;
                    return true;
                case "Ceramic":
                    kind = BarSurfaceKind.Ceramic;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static void Apply(
            Renderer renderer,
            BarSurfaceKind kind,
            SurfaceProjection projection,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            Apply(
                renderer,
                kind,
                sourceTint,
                CreateBaseMapTransform(renderer, kind, projection));
        }

        /// <summary>
        /// Applies a sheet to an imported bar mesh whose metric UVs were
        /// authored in Blender. Re-projecting from renderer bounds would
        /// stretch cut walls, profiled joinery and the service props.
        /// </summary>
        public static void ApplyAuthored(
            Renderer renderer,
            BarSurfaceKind kind,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            Apply(renderer, kind, sourceTint, IdentityBaseMapTransform);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            BarSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            BarSurfaceKind kind,
            SurfaceProjection projection)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            return SurfaceAppearanceCore.CreateBaseMapTransform(
                renderer,
                GetRecipe(kind).MetersPerTile,
                MinimumUvScale,
                projection,
                HashSaltBase + (int)kind);
        }

        private static void Apply(
            Renderer renderer,
            BarSurfaceKind kind,
            Color sourceTint,
            Vector4 baseMapTransform)
        {
            HomeSurfaceRecipe recipe = GetRecipe(kind);
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(
                SurfaceAlbedoCompensationId,
                recipe.AlbedoCompensation);
            properties.SetVector(BaseMapTransformId, baseMapTransform);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static int ValidateKind(BarSurfaceKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= SurfaceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    null);
            }

            return index;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            cachedTextures = new Texture2D[SurfaceCount];
        }
    }
}
