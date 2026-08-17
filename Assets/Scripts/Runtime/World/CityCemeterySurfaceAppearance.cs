using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityCemeterySurfaceKind
    {
        Granite,
        Stone,
        Gravel,
        Soil
    }

    /// <summary>
    /// Owns the packaged cemetery surface recipes and applies them
    /// without cloning the shared runtime primitive material. The
    /// recipe constants are the measured contract emitted by
    /// `tools/build-cemetery-textures.py` into
    /// `ArtSource/City/cemetery-textures.json`: compensation follows
    /// the city-facade linear rule, so a builder tint multiplied
    /// against the sheet keeps the brightness the flat colour had.
    ///
    /// Unlike the single-primitive pipelines, cemetery geometry is
    /// combined meshes carrying world-planar XZ UVs baked by
    /// `RuntimePrimitiveFactory` at each sheet's metre pitch, so the
    /// property block sets no UV transform: world position already
    /// decorrelates neighbouring stones.
    /// </summary>
    internal static class CityCemeterySurfaceAppearance
    {
        public const string GraniteTextureResourcePath =
            "Textures/CityCemeteryGraniteAlbedo";
        public const string StoneTextureResourcePath =
            "Textures/CityCemeteryStoneAlbedo";
        public const string GravelTextureResourcePath =
            "Textures/CityCemeteryGravelAlbedo";
        public const string SoilTextureResourcePath =
            "Textures/CityCemeterySoilAlbedo";

        private const int SurfaceCount =
            (int)CityCemeterySurfaceKind.Soil + 1;

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
            CityCemeterySurfaceKind kind)
        {
            switch (kind)
            {
                case CityCemeterySurfaceKind.Granite:
                    return new HomeSurfaceRecipe(
                        GraniteTextureResourcePath,
                        1.4f,
                        0.18f,
                        0f,
                        1.398f);
                case CityCemeterySurfaceKind.Stone:
                    return new HomeSurfaceRecipe(
                        StoneTextureResourcePath,
                        1.8f,
                        0.05f,
                        0f,
                        1.397f);
                case CityCemeterySurfaceKind.Gravel:
                    return new HomeSurfaceRecipe(
                        GravelTextureResourcePath,
                        1.6f,
                        0.04f,
                        0f,
                        1.4055f);
                case CityCemeterySurfaceKind.Soil:
                    return new HomeSurfaceRecipe(
                        SoilTextureResourcePath,
                        3.0f,
                        0.03f,
                        0f,
                        1.4755f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(
            CityCemeterySurfaceKind kind)
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
                    $"Missing cemetery {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures one combined cemetery mesh whose UVs were baked as
        /// world-planar tiles at this kind's metre pitch. The tint is
        /// the batch's authored flat colour; the compensated product
        /// keeps its original brightness.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CityCemeterySurfaceKind kind,
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

        private static int ValidateKind(CityCemeterySurfaceKind kind)
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
