using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityLakeSurfaceKind
    {
        Plank,
        Bank,
        Hull
    }

    /// <summary>
    /// Owns the packaged lake surface recipes and applies them without
    /// cloning the shared runtime primitive material. The recipe
    /// constants are the measured contract emitted by
    /// `tools/build-city-lake-textures.py` into
    /// `ArtSource/City/lake-textures.json`: compensation follows the
    /// city-facade linear rule, so a builder tint multiplied against the
    /// sheet keeps the brightness the flat colour had.
    ///
    /// Like the cemetery's, lake geometry is combined meshes carrying
    /// world-planar XZ UVs baked by `RuntimePrimitiveFactory` at each
    /// sheet's metre pitch, so the property block sets no UV transform:
    /// world position already decorrelates neighbouring boards and
    /// hulls. The bank ring and the bed cap are not primitives at all,
    /// but their UVs are laid to the same rule for the same reason.
    ///
    /// Three sheets, not one per style. The iron, the reeds, the sign
    /// paint and the litter stay flat colour: their members are too thin
    /// for a sheet to read through the PS1 composite, which is the same
    /// judgement the cemetery made about its railings and crowns.
    /// </summary>
    internal static class CityLakeSurfaceAppearance
    {
        public const string PlankTextureResourcePath =
            "Textures/CityLakePlankAlbedo";
        public const string BankTextureResourcePath =
            "Textures/CityLakeBankAlbedo";
        public const string HullTextureResourcePath =
            "Textures/CityLakeHullAlbedo";

        private const int SurfaceCount =
            (int)CityLakeSurfaceKind.Hull + 1;

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

        public static HomeSurfaceRecipe GetRecipe(CityLakeSurfaceKind kind)
        {
            switch (kind)
            {
                case CityLakeSurfaceKind.Plank:
                    return new HomeSurfaceRecipe(
                        PlankTextureResourcePath,
                        1.2f,
                        0.06f,
                        0f,
                        1.4355f);
                case CityLakeSurfaceKind.Bank:
                    return new HomeSurfaceRecipe(
                        BankTextureResourcePath,
                        2.4f,
                        0.03f,
                        0f,
                        1.414f);
                case CityLakeSurfaceKind.Hull:
                    return new HomeSurfaceRecipe(
                        HullTextureResourcePath,
                        1.6f,
                        0.12f,
                        0f,
                        1.44f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(CityLakeSurfaceKind kind)
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
                    $"Missing lake {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures one combined lake mesh whose UVs were baked as
        /// world-planar tiles at this kind's metre pitch. The tint is
        /// the batch's authored flat colour; the compensated product
        /// keeps its original brightness.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            CityLakeSurfaceKind kind,
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

        private static int ValidateKind(CityLakeSurfaceKind kind)
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
