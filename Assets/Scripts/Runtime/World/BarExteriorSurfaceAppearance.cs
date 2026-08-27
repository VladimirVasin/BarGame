using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum BarExteriorSurfaceKind
    {
        Brick,
        Plaster,
        DarkWood,
        CityRoof
    }

    /// <summary>
    /// Applies the four passive surface families used by the authored pub
    /// exterior. The two exterior sheets are measured by
    /// <c>tools/build-bar-textures.py</c>; dark wood and roof reuse their
    /// established packaged sources.
    /// </summary>
    internal static class BarExteriorSurfaceAppearance
    {
        public const string BrickTextureResourcePath =
            "Bar/Textures/BarExteriorBrickAlbedo";
        public const string PlasterTextureResourcePath =
            "Bar/Textures/BarExteriorPlasterAlbedo";

        private const int SurfaceCount =
            (int)BarExteriorSurfaceKind.CityRoof + 1;

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

        // The Blender generator bakes metre-scaled UVs from the same recipe
        // pitches. Re-deriving scale from a profiled mesh's bounds would
        // stretch openings and roof slopes, so the runtime transform is exact.
        private static readonly Vector4 IdentityBaseMapTransform =
            new Vector4(1f, 1f, 0f, 0f);

        private static Texture2D[] cachedTextures =
            new Texture2D[SurfaceCount];

        public static bool TryResolveSheet(
            string sheet,
            out BarExteriorSurfaceKind kind)
        {
            switch (sheet)
            {
                case "ExteriorBrick":
                    kind = BarExteriorSurfaceKind.Brick;
                    return true;
                case "ExteriorPlaster":
                    kind = BarExteriorSurfaceKind.Plaster;
                    return true;
                case "DarkWood":
                    kind = BarExteriorSurfaceKind.DarkWood;
                    return true;
                case "CityRoof":
                    kind = BarExteriorSurfaceKind.CityRoof;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static HomeSurfaceRecipe GetRecipe(
            BarExteriorSurfaceKind kind)
        {
            switch (kind)
            {
                case BarExteriorSurfaceKind.Brick:
                    return new HomeSurfaceRecipe(
                        BrickTextureResourcePath,
                        1.2f,
                        0.04f,
                        0f,
                        1.498f);
                case BarExteriorSurfaceKind.Plaster:
                    return new HomeSurfaceRecipe(
                        PlasterTextureResourcePath,
                        2.6f,
                        0.035f,
                        0f,
                        1.4065f);
                case BarExteriorSurfaceKind.DarkWood:
                    return BarSurfaceAppearance.GetRecipe(
                        BarSurfaceKind.DarkWood);
                case BarExteriorSurfaceKind.CityRoof:
                    return new HomeSurfaceRecipe(
                        CityFacadeAppearance.RoofTextureResourcePath,
                        CityFacadeAppearance.RoofTextureTileSize,
                        CityFacadeAppearance.RoofSmoothness,
                        0f,
                        CityFacadeAppearance.AlbedoCompensation);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(BarExteriorSurfaceKind kind)
        {
            switch (kind)
            {
                case BarExteriorSurfaceKind.DarkWood:
                    return BarSurfaceAppearance.GetTexture(
                        BarSurfaceKind.DarkWood);
                case BarExteriorSurfaceKind.CityRoof:
                    return CityFacadeAppearance.RoofTexture;
            }

            int index = ValidateKind(kind);
            if (cachedTextures[index] == null)
            {
                cachedTextures[index] = Resources.Load<Texture2D>(
                    GetRecipe(kind).ResourcePath);
            }

            if (cachedTextures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing bar exterior {kind} albedo " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static Color CreateDisplayTint(
            Color sourceTint,
            BarExteriorSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        public static void Apply(
            Renderer renderer,
            BarExteriorSurfaceKind kind,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            HomeSurfaceRecipe recipe = GetRecipe(kind);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            properties.SetVector(
                BaseMapTransformId,
                IdentityBaseMapTransform);
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static int ValidateKind(BarExteriorSurfaceKind kind)
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
