using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum SupermarketSurfaceKind
    {
        Linoleum,
        WallPaint,
        Ceiling,
        ShelfMetal,
        Counter,
        Cardboard
    }

    /// <summary>
    /// Owns the packaged supermarket surface recipes and applies them
    /// without cloning the shared runtime primitive material. The recipe
    /// constants are the measured contract emitted by
    /// `tools/build-supermarket-textures.py` into
    /// `ArtSource/Supermarket/supermarket-textures.json`: compensation
    /// follows the city-facade linear rule, so a builder tint multiplied
    /// against the sheet keeps the brightness the flat colour had.
    /// </summary>
    internal static class SupermarketSurfaceAppearance
    {
        public const string LinoleumTextureResourcePath =
            "Supermarket/Textures/SupermarketLinoleumAlbedo";
        public const string WallPaintTextureResourcePath =
            "Supermarket/Textures/SupermarketWallPaintAlbedo";
        public const string CeilingTextureResourcePath =
            "Supermarket/Textures/SupermarketCeilingAlbedo";
        public const string ShelfMetalTextureResourcePath =
            "Supermarket/Textures/SupermarketShelfMetalAlbedo";
        public const string CounterTextureResourcePath =
            "Supermarket/Textures/SupermarketCounterAlbedo";
        public const string CardboardTextureResourcePath =
            "Supermarket/Textures/SupermarketCardboardAlbedo";

        private const int SurfaceCount =
            (int)SupermarketSurfaceKind.Cardboard + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the home
        // (1000) and stairwell kinds, so two surfaces with the same enum
        // index never share offsets should the scenes ever meet.
        private const int HashSaltBase = 3000;

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

        public static HomeSurfaceRecipe GetRecipe(
            SupermarketSurfaceKind kind)
        {
            switch (kind)
            {
                case SupermarketSurfaceKind.Linoleum:
                    return new HomeSurfaceRecipe(
                        LinoleumTextureResourcePath,
                        2.4f,
                        0.16f,
                        0f,
                        1.421f);
                case SupermarketSurfaceKind.WallPaint:
                    return new HomeSurfaceRecipe(
                        WallPaintTextureResourcePath,
                        2.6f,
                        0.05f,
                        0f,
                        1.36f);
                case SupermarketSurfaceKind.Ceiling:
                    return new HomeSurfaceRecipe(
                        CeilingTextureResourcePath,
                        3.0f,
                        0.04f,
                        0f,
                        1.3555f);
                case SupermarketSurfaceKind.ShelfMetal:
                    return new HomeSurfaceRecipe(
                        ShelfMetalTextureResourcePath,
                        1.3f,
                        0.24f,
                        0.25f,
                        1.39f);
                case SupermarketSurfaceKind.Counter:
                    return new HomeSurfaceRecipe(
                        CounterTextureResourcePath,
                        1.2f,
                        0.20f,
                        0f,
                        1.3775f);
                case SupermarketSurfaceKind.Cardboard:
                    return new HomeSurfaceRecipe(
                        CardboardTextureResourcePath,
                        0.9f,
                        0.03f,
                        0f,
                        1.407f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(SupermarketSurfaceKind kind)
        {
            int index = ValidateKind(kind);
            if (cachedTextures[index] == null)
            {
                HomeSurfaceRecipe recipe = GetRecipe(kind);
                cachedTextures[index] = Resources.Load<Texture2D>(
                    recipe.ResourcePath);
            }

            if (cachedTextures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing supermarket {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static void Apply(
            Renderer renderer,
            SupermarketSurfaceKind kind,
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
            SupermarketSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            SupermarketSurfaceKind kind,
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

        private static int ValidateKind(SupermarketSurfaceKind kind)
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
