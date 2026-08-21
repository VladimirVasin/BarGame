using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityFringeYardSurfaceKind
    {
        ServiceTrack,
        Concrete,
        Masonry,
        ForefieldGround
    }

    /// <summary>
    /// Owns the four packaged surface recipes specific to the west/south
    /// service fringe. The constants mirror the measured contract emitted by
    /// <c>tools/build-city-fringe-textures.py</c>: a compensated authored tint
    /// multiplies each mean-controlled albedo without changing the brightness
    /// of the former flat-colour surface.
    ///
    /// Separate primitives receive metre-scale tiling and a deterministic UV
    /// offset through a property block. Combined batches already carry baked
    /// metre-scale UVs and therefore receive only the common texture, tint and
    /// surface-response properties. Both paths keep the runtime primitive
    /// material shared; no per-yard material instances are created.
    /// </summary>
    internal static class CityFringeYardSurfaceAppearance
    {
        public const string ServiceTrackTextureResourcePath =
            "Textures/CityFringeServiceTrackAlbedo";
        public const string ConcreteTextureResourcePath =
            "Textures/CityFringeConcreteAlbedo";
        public const string MasonryTextureResourcePath =
            "Textures/CityFringeMasonryAlbedo";
        public const string ForefieldGroundTextureResourcePath =
            "Textures/CityFringeForefieldAlbedo";

        private const int SurfaceCount =
            (int)CityFringeYardSurfaceKind.ForefieldGround + 1;
        private const float MinimumUvScale = 0.35f;
        private const int HashSaltBase = 8000;

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
            CityFringeYardSurfaceKind kind)
        {
            switch (kind)
            {
                case CityFringeYardSurfaceKind.ServiceTrack:
                    return new HomeSurfaceRecipe(
                        ServiceTrackTextureResourcePath,
                        4.0f,
                        0.035f,
                        0f,
                        1.436f);
                case CityFringeYardSurfaceKind.Concrete:
                    return new HomeSurfaceRecipe(
                        ConcreteTextureResourcePath,
                        3.0f,
                        0.055f,
                        0f,
                        1.3965f);
                case CityFringeYardSurfaceKind.Masonry:
                    return new HomeSurfaceRecipe(
                        MasonryTextureResourcePath,
                        2.4f,
                        0.035f,
                        0f,
                        1.3895f);
                case CityFringeYardSurfaceKind.ForefieldGround:
                    return new HomeSurfaceRecipe(
                        ForefieldGroundTextureResourcePath,
                        8.0f,
                        0.025f,
                        0f,
                        1.4415f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(
            CityFringeYardSurfaceKind kind)
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
                    $"Missing fringe-yard {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures a primitive on the plane selected by its proportions.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            CityFringeYardSurfaceKind kind,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            Apply(
                renderer,
                kind,
                SurfaceAppearanceCore.ResolveBoxProjection(renderer),
                sourceTint);
        }

        /// <summary>
        /// Explicit projection for a primitive whose visible face cannot be
        /// inferred from its proportions.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            CityFringeYardSurfaceKind kind,
            SurfaceProjection projection,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            HomeSurfaceRecipe recipe = GetRecipe(kind);
            ApplySharedProperties(renderer, kind, sourceTint, recipe);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetVector(
                BaseMapTransformId,
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer,
                    recipe.MetersPerTile,
                    MinimumUvScale,
                    projection,
                    HashSaltBase + (int)kind));
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Textures a combined batch whose UVs were baked at the recipe's
        /// metre pitch. No transform is added because the mesh already owns
        /// its scale and placement.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CityFringeYardSurfaceKind kind,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySharedProperties(
                renderer,
                kind,
                sourceTint,
                GetRecipe(kind));
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            CityFringeYardSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        private static void ApplySharedProperties(
            Renderer renderer,
            CityFringeYardSurfaceKind kind,
            Color sourceTint,
            HomeSurfaceRecipe recipe)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static int ValidateKind(CityFringeYardSurfaceKind kind)
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
