using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityRiverSurfaceKind
    {
        Paving,
        Quay,
        Iron
    }

    /// <summary>
    /// Owns the packaged embankment surface recipes and applies them
    /// without cloning the shared runtime primitive material. The recipe
    /// constants are the measured contract emitted by
    /// `tools/build-city-river-textures.py` into
    /// `ArtSource/City/river-textures.json`: compensation follows the
    /// city-facade linear rule, so a builder tint multiplied against the
    /// sheet keeps the brightness the flat colour had.
    ///
    /// The embankment is built both ways, so both paths exist here. Its
    /// slabs, walls, rails and posts are separate primitives carrying
    /// plain 0..1 cube UVs, so they take the metre-scale `_BaseMap_ST`
    /// tiling with a per-transform offset; its stair flights and lamp
    /// posts are combined batches carrying world UVs baked at the
    /// recipe's pitch, so those set no UV transform.
    ///
    /// Unlike the other primitive pipelines the projection is resolved
    /// from the mesh rather than passed in. A promenade slab, a quay
    /// wall, a rail along the bank and a rail across a landing are the
    /// same three kinds in four orientations, and naming a plane at each
    /// of some thirty call sites is how a rail ends up sampling one
    /// stretched patch of the sheet along its length.
    /// </summary>
    internal static class CityRiverSurfaceAppearance
    {
        public const string PavingTextureResourcePath =
            "Textures/CityRiverPavingAlbedo";
        public const string QuayTextureResourcePath =
            "Textures/CityRiverQuayAlbedo";
        public const string IronTextureResourcePath =
            "Textures/CityRiverIronAlbedo";

        private const int SurfaceCount =
            (int)CityRiverSurfaceKind.Iron + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the home
        // (1000), supermarket (3000), bar (4000) and point-of-interest
        // (5000) kinds, so two surfaces with the same enum index never
        // share offsets should the scenes ever meet.
        private const int HashSaltBase = 6000;

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
            CityRiverSurfaceKind kind)
        {
            switch (kind)
            {
                case CityRiverSurfaceKind.Paving:
                    return new HomeSurfaceRecipe(
                        PavingTextureResourcePath,
                        3.2f,
                        0.10f,
                        0f,
                        1.3875f);
                case CityRiverSurfaceKind.Quay:
                    return new HomeSurfaceRecipe(
                        QuayTextureResourcePath,
                        2.2f,
                        0.06f,
                        0f,
                        1.404f);
                case CityRiverSurfaceKind.Iron:
                    return new HomeSurfaceRecipe(
                        IronTextureResourcePath,
                        1.2f,
                        0.18f,
                        0.20f,
                        1.5145f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(CityRiverSurfaceKind kind)
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
                    $"Missing river {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures one embankment primitive, projecting the sheet onto
        /// the plane its own proportions choose.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            CityRiverSurfaceKind kind,
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
        /// The explicit-plane variant, for the primitives whose visible
        /// surface is not a box face: the cast mooring bollards.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            CityRiverSurfaceKind kind,
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
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer,
                    recipe.MetersPerTile,
                    MinimumUvScale,
                    projection,
                    HashSaltBase + (int)kind));
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Textures one combined embankment batch whose UVs were baked
        /// at this kind's metre pitch, so it sets no UV transform: world
        /// position already decorrelates neighbouring steps and posts.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CityRiverSurfaceKind kind,
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
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            CityRiverSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        private static int ValidateKind(CityRiverSurfaceKind kind)
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
