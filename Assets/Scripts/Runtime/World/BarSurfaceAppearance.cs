using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum BarSurfaceKind
    {
        WornPlank,
        Wallpaper,
        DarkWood,
        WornLeather
    }

    /// <summary>
    /// Owns the packaged worn-bar surface recipes and applies them
    /// without cloning the shared runtime primitive material. The
    /// recipe constants are the measured contract emitted by
    /// `tools/build-bar-textures.py` into
    /// `ArtSource/Bar/bar-textures.json`; compensation follows the
    /// city-facade linear rule. Which bars wear these sheets is the
    /// district identity's decision, not this class's.
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

        private const int SurfaceCount =
            (int)BarSurfaceKind.WornLeather + 1;
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
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

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

            HomeSurfaceRecipe recipe = GetRecipe(kind);
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetVector(
                BaseMapTransformId,
                CreateBaseMapTransform(renderer, kind, projection));
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
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
