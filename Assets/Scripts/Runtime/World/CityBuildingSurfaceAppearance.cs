using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityBuildingSurfaceKind
    {
        FacadePrimary,
        FacadeSecondary,
        Plinth,
        Roof,
        Metal,
        WindowFrame
    }

    internal enum CityBuildingSurfaceUvLayout
    {
        BuildingSideAtlas,
        FullFace,
        WorldMetreProjected
    }

    internal readonly struct CityBuildingSurfaceRecipe
    {
        public CityBuildingSurfaceRecipe(
            string resourcePath,
            CityBuildingSurfaceUvLayout uvLayout,
            float metersPerTile,
            float smoothness,
            float metallic,
            float albedoCompensation)
        {
            ResourcePath = resourcePath;
            UvLayout = uvLayout;
            MetersPerTile = metersPerTile;
            Smoothness = smoothness;
            Metallic = metallic;
            AlbedoCompensation = albedoCompensation;
        }

        public string ResourcePath { get; }
        public CityBuildingSurfaceUvLayout UvLayout { get; }
        public float MetersPerTile { get; }
        public float Smoothness { get; }
        public float Metallic { get; }
        public float AlbedoCompensation { get; }
    }

    /// <summary>
    /// Resolves the opaque semantic roles of the four authored ordinary
    /// buildings to their district surface sheets. Imported meshes already
    /// carry full-building side-atlas, full-face or metre-projected UVs, so
    /// runtime only binds the texture and recipe through one shared material.
    /// </summary>
    internal static class CityBuildingSurfaceAppearance
    {
        public const string TextureResourceRoot =
            "Textures/CityBuildingSurfaces";
        public const float AlbedoCompensation =
            CityFacadeAppearance.AlbedoCompensation;

        private const int DistrictCount = 4;
        private const int SurfaceCount =
            (int)CityBuildingSurfaceKind.WindowFrame + 1;

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

        private static readonly Vector4 IdentityBaseMapTransform =
            new Vector4(1f, 1f, 0f, 0f);

        private static Texture2D[,] cachedTextures =
            new Texture2D[DistrictCount, SurfaceCount];

        /// <summary>
        /// Accepts the v2 semantic surface names directly. The two legacy
        /// aliases keep the current six-role wrappers safe until their next
        /// deterministic Blender import replaces Shell and Trim.
        /// </summary>
        public static bool TryResolveSurface(
            CityDistrictKind district,
            string roleOrSurface,
            out CityBuildingSurfaceKind surface)
        {
            if (!TryGetDistrictIndex(district, out _))
            {
                surface = default;
                return false;
            }

            string semanticName = ExtractSemanticName(roleOrSurface);
            switch (semanticName)
            {
                case "Shell":
                case "FacadePrimary":
                    surface = CityBuildingSurfaceKind.FacadePrimary;
                    return true;
                case "Trim":
                case "FacadeSecondary":
                    surface = CityBuildingSurfaceKind.FacadeSecondary;
                    return true;
                case "Plinth":
                    surface = CityBuildingSurfaceKind.Plinth;
                    return true;
                case "Roof":
                    surface = CityBuildingSurfaceKind.Roof;
                    return true;
                case "Metal":
                    surface = CityBuildingSurfaceKind.Metal;
                    return true;
                case "WindowFrame":
                    surface = CityBuildingSurfaceKind.WindowFrame;
                    return true;
                default:
                    surface = default;
                    return false;
            }
        }

        public static CityBuildingSurfaceRecipe GetRecipe(
            CityDistrictKind district,
            CityBuildingSurfaceKind surface)
        {
            ValidateDistrict(district);
            ValidateSurface(surface);

            string resourcePath = TextureResourceRoot + "/" +
                district + "/" + surface;
            switch (surface)
            {
                case CityBuildingSurfaceKind.FacadePrimary:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.BuildingSideAtlas,
                        0f,
                        ResolveFacadePrimarySmoothness(district),
                        district == CityDistrictKind.Industrial
                            ? 0.20f
                            : 0f,
                        AlbedoCompensation);
                case CityBuildingSurfaceKind.FacadeSecondary:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.BuildingSideAtlas,
                        0f,
                        district == CityDistrictKind.OldTown
                            ? 0.07f
                            : district == CityDistrictKind.Industrial
                                ? 0.10f
                                : 0.09f,
                        district == CityDistrictKind.Industrial
                            ? 0.02f
                            : 0f,
                        AlbedoCompensation);
                case CityBuildingSurfaceKind.Plinth:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.FullFace,
                        0f,
                        district == CityDistrictKind.OldTown
                            ? 0.05f
                            : 0.06f,
                        0f,
                        AlbedoCompensation);
                case CityBuildingSurfaceKind.Roof:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.WorldMetreProjected,
                        4f,
                        district == CityDistrictKind.Industrial
                            ? 0.08f
                            : CityFacadeAppearance.RoofSmoothness,
                        district == CityDistrictKind.Industrial
                            ? 0.20f
                            : 0f,
                        AlbedoCompensation);
                case CityBuildingSurfaceKind.Metal:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.WorldMetreProjected,
                        district == CityDistrictKind.OldTown ||
                        district == CityDistrictKind.Nightlife
                            ? 1.6f
                            : 1.8f,
                        ResolveMetalSmoothness(district),
                        ResolveMetallic(district),
                        AlbedoCompensation);
                case CityBuildingSurfaceKind.WindowFrame:
                    return new CityBuildingSurfaceRecipe(
                        resourcePath,
                        CityBuildingSurfaceUvLayout.WorldMetreProjected,
                        0.8f,
                        ResolveWindowFrameSmoothness(district),
                        ResolveWindowFrameMetallic(district),
                        AlbedoCompensation);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(surface),
                        surface,
                        null);
            }
        }

        private static float ResolveFacadePrimarySmoothness(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return 0.06f;
                case CityDistrictKind.Residential:
                    return 0.09f;
                case CityDistrictKind.Industrial:
                    return 0.16f;
                case CityDistrictKind.Nightlife:
                    return 0.10f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        null);
            }
        }

        private static float ResolveMetalSmoothness(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return 0.24f;
                case CityDistrictKind.Residential:
                    return 0.26f;
                case CityDistrictKind.Industrial:
                    return 0.20f;
                case CityDistrictKind.Nightlife:
                    return 0.22f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        null);
            }
        }

        private static float ResolveMetallic(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                case CityDistrictKind.Nightlife:
                    return 0.58f;
                case CityDistrictKind.Residential:
                    return 0.62f;
                case CityDistrictKind.Industrial:
                    return 0.68f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        null);
            }
        }

        private static float ResolveWindowFrameSmoothness(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                case CityDistrictKind.Nightlife:
                    return 0.16f;
                case CityDistrictKind.Residential:
                    return 0.17f;
                case CityDistrictKind.Industrial:
                    return 0.14f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        null);
            }
        }

        private static float ResolveWindowFrameMetallic(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return 0.18f;
                case CityDistrictKind.Residential:
                    return 0.14f;
                case CityDistrictKind.Industrial:
                    return 0.28f;
                case CityDistrictKind.Nightlife:
                    return 0.20f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        null);
            }
        }

        public static Texture2D GetTexture(
            CityDistrictKind district,
            CityBuildingSurfaceKind surface)
        {
            int districtIndex = ValidateDistrict(district);
            int surfaceIndex = ValidateSurface(surface);
            if (cachedTextures[districtIndex, surfaceIndex] == null)
            {
                CityBuildingSurfaceRecipe recipe = GetRecipe(
                    district,
                    surface);
                cachedTextures[districtIndex, surfaceIndex] =
                    Resources.Load<Texture2D>(recipe.ResourcePath);
            }

            Texture2D texture =
                cachedTextures[districtIndex, surfaceIndex];
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Missing {district} building {surface} texture " +
                    $"'{GetRecipe(district, surface).ResourcePath}'.");
            }

            return texture;
        }

        public static void Apply(
            Renderer renderer,
            CityDistrictKind district,
            string roleOrSurface,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            if (!TryResolveSurface(
                    district,
                    roleOrSurface,
                    out CityBuildingSurfaceKind surface))
            {
                throw new ArgumentException(
                    $"Unsupported {district} building surface role " +
                    $"'{roleOrSurface}'.",
                    nameof(roleOrSurface));
            }

            Apply(renderer, district, surface, sourceTint);
        }

        public static void Apply(
            Renderer renderer,
            CityDistrictKind district,
            CityBuildingSurfaceKind surface,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            CityBuildingSurfaceRecipe recipe = GetRecipe(
                district,
                surface);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(
                BaseMapId,
                GetTexture(district, surface));
            properties.SetVector(
                BaseMapTransformId,
                IdentityBaseMapTransform);
            Color displayTint = CreateDisplayTint(
                sourceTint,
                district,
                surface);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            CityDistrictKind district,
            CityBuildingSurfaceKind surface)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(district, surface).AlbedoCompensation);
        }

        private static string ExtractSemanticName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            int separator = trimmed.LastIndexOf(
                "__",
                StringComparison.Ordinal);
            return separator >= 0
                ? trimmed.Substring(separator + 2)
                : trimmed;
        }

        private static bool TryGetDistrictIndex(
            CityDistrictKind district,
            out int index)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    index = 0;
                    return true;
                case CityDistrictKind.Residential:
                    index = 1;
                    return true;
                case CityDistrictKind.Industrial:
                    index = 2;
                    return true;
                case CityDistrictKind.Nightlife:
                    index = 3;
                    return true;
                default:
                    index = -1;
                    return false;
            }
        }

        private static int ValidateDistrict(CityDistrictKind district)
        {
            if (!TryGetDistrictIndex(district, out int index))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(district),
                    district,
                    "Only ordinary urban districts own building sheets.");
            }

            return index;
        }

        private static int ValidateSurface(
            CityBuildingSurfaceKind surface)
        {
            int index = (int)surface;
            if (index < 0 || index >= SurfaceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(surface),
                    surface,
                    null);
            }

            return index;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            cachedTextures =
                new Texture2D[DistrictCount, SurfaceCount];
        }
    }
}
