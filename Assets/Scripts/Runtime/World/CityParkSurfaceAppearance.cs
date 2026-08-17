using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityParkSurfaceKind
    {
        Lawn,
        Path,
        Plaza,
        Bark,
        Foliage,
        Timber,
        Stone,
        PaintedMetal
    }

    /// <summary>
    /// Owns the packaged Central Park surface recipes and applies them
    /// without cloning the shared runtime primitive material. The recipe
    /// constants are the measured contract emitted by
    /// `tools/build-city-park-textures.py` into
    /// `ArtSource/City/park-textures.json`: compensation follows the
    /// city-facade linear rule, so a builder tint multiplied against the
    /// sheet keeps the brightness the flat colour had.
    ///
    /// Like the cemetery, park geometry is combined meshes carrying
    /// world UVs baked by `RuntimePrimitiveFactory` and
    /// `CityTerrainSurfaceWorldBuilder` at each sheet's metre pitch, so
    /// the property block sets no UV transform: world position already
    /// decorrelates neighbouring trees, benches and hedge runs. The
    /// ground surfaces project down; the upright park objects use the
    /// box projection, because a twenty-metre hedge lit by a planar
    /// projection would show one stretched line of leaves.
    /// </summary>
    internal static class CityParkSurfaceAppearance
    {
        public const string LawnTextureResourcePath =
            "Textures/CityParkLawnAlbedo";
        public const string PathTextureResourcePath =
            "Textures/CityParkPathAlbedo";
        public const string PlazaTextureResourcePath =
            "Textures/CityParkPlazaAlbedo";
        public const string BarkTextureResourcePath =
            "Textures/CityParkBarkAlbedo";
        public const string FoliageTextureResourcePath =
            "Textures/CityParkFoliageAlbedo";
        public const string TimberTextureResourcePath =
            "Textures/CityParkTimberAlbedo";
        public const string StoneTextureResourcePath =
            "Textures/CityParkStoneAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "Textures/CityParkPaintedMetalAlbedo";

        private const int SurfaceCount =
            (int)CityParkSurfaceKind.PaintedMetal + 1;

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
            CityParkSurfaceKind kind)
        {
            switch (kind)
            {
                case CityParkSurfaceKind.Lawn:
                    return new HomeSurfaceRecipe(
                        LawnTextureResourcePath,
                        3.0f,
                        0.03f,
                        0f,
                        1.394f);
                case CityParkSurfaceKind.Path:
                    return new HomeSurfaceRecipe(
                        PathTextureResourcePath,
                        2.2f,
                        0.05f,
                        0f,
                        1.3965f);
                case CityParkSurfaceKind.Plaza:
                    return new HomeSurfaceRecipe(
                        PlazaTextureResourcePath,
                        2.8f,
                        0.06f,
                        0f,
                        1.391f);
                case CityParkSurfaceKind.Bark:
                    return new HomeSurfaceRecipe(
                        BarkTextureResourcePath,
                        1.2f,
                        0.04f,
                        0f,
                        1.486f);
                case CityParkSurfaceKind.Foliage:
                    return new HomeSurfaceRecipe(
                        FoliageTextureResourcePath,
                        1.6f,
                        0.04f,
                        0f,
                        1.3825f);
                case CityParkSurfaceKind.Timber:
                    return new HomeSurfaceRecipe(
                        TimberTextureResourcePath,
                        1.4f,
                        0.08f,
                        0f,
                        1.3345f);
                case CityParkSurfaceKind.Stone:
                    return new HomeSurfaceRecipe(
                        StoneTextureResourcePath,
                        1.5f,
                        0.05f,
                        0f,
                        1.3445f);
                case CityParkSurfaceKind.PaintedMetal:
                    return new HomeSurfaceRecipe(
                        PaintedMetalTextureResourcePath,
                        1.2f,
                        0.16f,
                        0.15f,
                        1.341f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        /// <summary>
        /// How a kind's mesh must be unwrapped for its recipe pitch to
        /// mean anything: flat park ground projects down, upright park
        /// objects project per face.
        /// </summary>
        public static RuntimeWorldUvMode GetUvMode(
            CityParkSurfaceKind kind)
        {
            switch (kind)
            {
                case CityParkSurfaceKind.Lawn:
                case CityParkSurfaceKind.Path:
                case CityParkSurfaceKind.Plaza:
                    return RuntimeWorldUvMode.XZPlanar;
                case CityParkSurfaceKind.Bark:
                case CityParkSurfaceKind.Foliage:
                case CityParkSurfaceKind.Timber:
                case CityParkSurfaceKind.Stone:
                case CityParkSurfaceKind.PaintedMetal:
                    return RuntimeWorldUvMode.BoxProjected;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(
            CityParkSurfaceKind kind)
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
                    $"Missing park {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures one combined park mesh whose UVs were baked at this
        /// kind's metre pitch. The tint is the batch's authored flat
        /// colour; the compensated product keeps its original
        /// brightness.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CityParkSurfaceKind kind,
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

        private static int ValidateKind(CityParkSurfaceKind kind)
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
