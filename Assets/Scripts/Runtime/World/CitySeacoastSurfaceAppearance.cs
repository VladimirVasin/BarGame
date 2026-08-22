using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CitySeacoastSurfaceKind
    {
        Sand,
        Concrete,
        Granite,
        Plank,
        Hull
    }

    /// <summary>
    /// Owns the packaged seacoast surface recipes and applies them
    /// without cloning the shared runtime primitive material. The
    /// recipe constants are the measured contract emitted by
    /// `tools/build-city-seacoast-textures.py` into
    /// `ArtSource/City/seacoast-textures.json`: compensation follows
    /// the city-facade linear rule, so a builder tint multiplied
    /// against the sheet keeps the brightness the flat colour had.
    ///
    /// Like the lake's, seacoast geometry is combined meshes carrying
    /// world-planar XZ UVs baked at each sheet's metre pitch, so the
    /// property block sets no UV transform. The sand sheet also skins
    /// the beach terrain mesh, whose UVs are laid to the same rule.
    ///
    /// Five sheets, not one per style. The granite deliberately reuses
    /// the river's quay stone — the esplanade IS the embankment
    /// vocabulary carried to the sea — and the plank and hull sheets
    /// moved here from the lake with the boat station they dress. The
    /// iron, the rust, the grass, the sign paint and the litter stay
    /// flat colour: their members are too thin for a sheet to read
    /// through the PS1 composite.
    /// </summary>
    internal static class CitySeacoastSurfaceAppearance
    {
        public const string SandTextureResourcePath =
            "Textures/CitySeacoastSandAlbedo";
        public const string ConcreteTextureResourcePath =
            "Textures/CitySeacoastConcreteAlbedo";
        public const string GraniteTextureResourcePath =
            "Textures/CityRiverQuayAlbedo";
        public const string PlankTextureResourcePath =
            "Textures/CitySeacoastPlankAlbedo";
        public const string HullTextureResourcePath =
            "Textures/CitySeacoastHullAlbedo";

        private const int SurfaceCount =
            (int)CitySeacoastSurfaceKind.Hull + 1;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
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
            CitySeacoastSurfaceKind kind)
        {
            switch (kind)
            {
                case CitySeacoastSurfaceKind.Sand:
                    return new HomeSurfaceRecipe(
                        SandTextureResourcePath,
                        2.6f,
                        0.03f,
                        0f,
                        1.3825f);
                case CitySeacoastSurfaceKind.Concrete:
                    return new HomeSurfaceRecipe(
                        ConcreteTextureResourcePath,
                        2.2f,
                        0.05f,
                        0f,
                        1.401f);
                case CitySeacoastSurfaceKind.Granite:
                    // The river quay sheet's own measured recipe,
                    // transcribed from CityRiverSurfaceAppearance:
                    // same stone, same numbers.
                    return new HomeSurfaceRecipe(
                        GraniteTextureResourcePath,
                        2.2f,
                        0.06f,
                        0f,
                        1.404f);
                case CitySeacoastSurfaceKind.Plank:
                    return new HomeSurfaceRecipe(
                        PlankTextureResourcePath,
                        1.2f,
                        0.06f,
                        0f,
                        1.4355f);
                case CitySeacoastSurfaceKind.Hull:
                    return new HomeSurfaceRecipe(
                        HullTextureResourcePath,
                        1.6f,
                        0.12f,
                        0f,
                        1.439f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(CitySeacoastSurfaceKind kind)
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
                    $"Missing seacoast {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures one combined seacoast mesh whose UVs were baked as
        /// world-planar tiles at this kind's metre pitch. The tint is
        /// the batch's authored flat colour; the compensated product
        /// keeps its original brightness.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CitySeacoastSurfaceKind kind,
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
            Color displayTint = SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                recipe.AlbedoCompensation);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static int ValidateKind(CitySeacoastSurfaceKind kind)
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
