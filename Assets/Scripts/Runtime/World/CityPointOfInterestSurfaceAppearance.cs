using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityPointOfInterestSurfaceKind
    {
        Paving,
        PaintedMetal,
        Cloth,
        Timber
    }

    /// <summary>
    /// Owns the packaged district point-of-interest surface recipes and
    /// applies them without cloning the shared runtime primitive
    /// material. The recipe constants are the measured contract emitted
    /// by `tools/build-city-poi-textures.py` into
    /// `ArtSource/City/poi-textures.json`: compensation follows the
    /// city-facade linear rule, so a builder tint multiplied against the
    /// sheet keeps the brightness the flat colour had. The cloth path
    /// keeps the panel's shared two-sided material and its matte zeroed
    /// specular, because a laundry piece is a simulated Cloth renderer
    /// rather than an ordinary primitive.
    /// </summary>
    internal static class CityPointOfInterestSurfaceAppearance
    {
        public const string PavingTextureResourcePath =
            "Textures/CityPoiPavingAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "Textures/CityPoiPaintedMetalAlbedo";
        public const string ClothTextureResourcePath =
            "Textures/CityPoiClothAlbedo";
        public const string TimberTextureResourcePath =
            "Textures/CityPoiTimberAlbedo";

        private const int SurfaceCount =
            (int)CityPointOfInterestSurfaceKind.Timber + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the home
        // (1000), stairwell, supermarket (3000) and bar (4000) kinds,
        // so two surfaces with the same enum index never share offsets
        // should the scenes ever meet.
        private const int HashSaltBase = 5000;

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
            CityPointOfInterestSurfaceKind kind)
        {
            switch (kind)
            {
                case CityPointOfInterestSurfaceKind.Paving:
                    return new HomeSurfaceRecipe(
                        PavingTextureResourcePath,
                        3.0f,
                        0.06f,
                        0f,
                        1.422f);
                case CityPointOfInterestSurfaceKind.PaintedMetal:
                    return new HomeSurfaceRecipe(
                        PaintedMetalTextureResourcePath,
                        1.4f,
                        0.22f,
                        0.25f,
                        1.4465f);
                case CityPointOfInterestSurfaceKind.Cloth:
                    return new HomeSurfaceRecipe(
                        ClothTextureResourcePath,
                        1.6f,
                        0f,
                        0f,
                        1.396f);
                case CityPointOfInterestSurfaceKind.Timber:
                    return new HomeSurfaceRecipe(
                        TimberTextureResourcePath,
                        1.6f,
                        0.08f,
                        0f,
                        1.433f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(
            CityPointOfInterestSurfaceKind kind)
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
                    $"Missing point-of-interest {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static void Apply(
            Renderer renderer,
            CityPointOfInterestSurfaceKind kind,
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

        /// <summary>
        /// Textures one hanging laundry panel. The cloth keeps the
        /// shared cull-off panel material and its matte zeroed specular;
        /// only the sheet, its metre tiling over the panel's authored
        /// size and the compensated tint ride the property block.
        /// </summary>
        public static void ApplyClothPanel(
            Renderer renderer,
            Color sourceTint,
            float widthMeters,
            float heightMeters)
        {
            if (renderer == null)
            {
                return;
            }

            const CityPointOfInterestSurfaceKind kind =
                CityPointOfInterestSurfaceKind.Cloth;
            HomeSurfaceRecipe recipe = GetRecipe(kind);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetVector(
                BaseMapTransformId,
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer.transform,
                    widthMeters,
                    heightMeters,
                    recipe.MetersPerTile,
                    MinimumUvScale,
                    HashSaltBase + (int)kind));
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            CityPointOfInterestSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            CityPointOfInterestSurfaceKind kind,
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

        private static int ValidateKind(
            CityPointOfInterestSurfaceKind kind)
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
