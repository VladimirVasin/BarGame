using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityArchShelterSurfaceApplyResult
    {
        internal CityArchShelterSurfaceApplyResult(
            int visitedRendererCount,
            int appliedRendererCount,
            int alreadyAppliedRendererCount,
            int ignoredRendererCount,
            int missingComponentCount,
            int duplicateComponentCount)
        {
            VisitedRendererCount = visitedRendererCount;
            AppliedRendererCount = appliedRendererCount;
            AlreadyAppliedRendererCount = alreadyAppliedRendererCount;
            IgnoredRendererCount = ignoredRendererCount;
            MissingComponentCount = missingComponentCount;
            DuplicateComponentCount = duplicateComponentCount;
        }

        public int VisitedRendererCount { get; }
        public int AppliedRendererCount { get; }
        public int AlreadyAppliedRendererCount { get; }
        public int IgnoredRendererCount { get; }
        public int MissingComponentCount { get; }
        public int DuplicateComponentCount { get; }
        public int TexturedRendererCount =>
            AppliedRendererCount + AlreadyAppliedRendererCount;
        public bool IsComplete =>
            MissingComponentCount == 0 && DuplicateComponentCount == 0;
    }

    /// <summary>
    /// Gives the passive arch-shelter structure and survival props the
    /// city's established close-range surface detail. Recipes are keyed by
    /// the imported component contract so the fire, its spill and the three
    /// resident rigs remain untouched. Every surface keeps the shared runtime
    /// primitive material and carries its albedo, metre tiling and response
    /// through a material property block.
    /// </summary>
    public static class CityArchShelterSurfaceAppearance
    {
        public const string ShellComponentName = "Shell_Masonry";
        public const string StepsComponentName =
            "StepsAndRetaining_Masonry";
        public const string PlatformSupportComponentName =
            "PlatformSupport_Masonry";
        public const string PlatformSlabComponentName =
            "PlatformSlab_Street";
        public const string CladdingComponentName =
            "Cladding_Industrial";
        public const string RoofComponentName = "Roof_Street";
        public const string BarrelComponentName = "Barrel_Industrial";
        public const string FuelComponentName = "Fuel_Timber";
        public const string MattressComponentName =
            "Mattress_Residential";
        public const string BlanketComponentName = "Blanket_Street";
        public const string CardboardComponentName = "Cardboard_Timber";
        public const string CrateComponentName =
            "CrateAndCardboard_Timber";
        public const string BagsComponentName = "Bags_Street";
        public const string BottlesComponentName =
            "Bottles_Residential";
        public const string CanComponentName = "Can_Industrial";

        public const int ExpectedComponentCount = 15;

        private const float MinimumUvScale = 0.35f;
        private const int HashSaltBase = 15000;

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

        private static readonly string[] ComponentNameArray =
        {
            ShellComponentName,
            StepsComponentName,
            PlatformSupportComponentName,
            PlatformSlabComponentName,
            CladdingComponentName,
            RoofComponentName,
            BarrelComponentName,
            FuelComponentName,
            MattressComponentName,
            BlanketComponentName,
            CardboardComponentName,
            CrateComponentName,
            BagsComponentName,
            BottlesComponentName,
            CanComponentName
        };

        private static readonly IReadOnlyList<string> ReadOnlyComponentNames =
            Array.AsReadOnly(ComponentNameArray);

        public static IReadOnlyList<string> SupportedComponentNames =>
            ReadOnlyComponentNames;

        /// <summary>
        /// Applies the appearance once to every supported descendant renderer.
        /// Repeating the call is allocation-light and does not compensate an
        /// already textured tint a second time.
        /// </summary>
        public static CityArchShelterSurfaceApplyResult Apply(
            Transform shelterRoot)
        {
            if (shelterRoot == null)
            {
                throw new ArgumentNullException(nameof(shelterRoot));
            }

            Renderer[] renderers =
                shelterRoot.GetComponentsInChildren<Renderer>(true);
            var foundComponents = new bool[ExpectedComponentCount];
            int appliedCount = 0;
            int alreadyAppliedCount = 0;
            int ignoredCount = 0;
            int duplicateCount = 0;
            var properties = new MaterialPropertyBlock();

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!TryResolveRecipe(renderer.name, out SurfaceRecipe recipe))
                {
                    ignoredCount++;
                    continue;
                }

                if (foundComponents[recipe.ComponentIndex])
                {
                    duplicateCount++;
                }
                else
                {
                    foundComponents[recipe.ComponentIndex] = true;
                }

                Texture2D texture = GetTexture(recipe.Source);
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                if (properties.HasProperty(BaseMapId) &&
                    properties.GetTexture(BaseMapId) == texture)
                {
                    alreadyAppliedCount++;
                    continue;
                }

                Color sourceTint = ResolveSourceTint(renderer, properties);
                ApplySurface(
                    renderer,
                    properties,
                    texture,
                    GetSurfaceRecipe(recipe.Source),
                    recipe.Projection,
                    recipe.Source,
                    sourceTint);
                appliedCount++;
            }

            int missingCount = 0;
            for (int index = 0; index < foundComponents.Length; index++)
            {
                if (!foundComponents[index])
                {
                    missingCount++;
                }
            }

            return new CityArchShelterSurfaceApplyResult(
                renderers.Length,
                appliedCount,
                alreadyAppliedCount,
                ignoredCount,
                missingCount,
                duplicateCount);
        }

        public static bool TryGetTextureResourcePath(
            string componentName,
            out string resourcePath)
        {
            if (!TryResolveRecipe(componentName, out SurfaceRecipe recipe))
            {
                resourcePath = null;
                return false;
            }

            resourcePath = GetSurfaceRecipe(recipe.Source).ResourcePath;
            return true;
        }

        private static void ApplySurface(
            Renderer renderer,
            MaterialPropertyBlock properties,
            Texture2D texture,
            HomeSurfaceRecipe surface,
            SurfaceProjection projection,
            SurfaceSource source,
            Color sourceTint)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            properties.SetTexture(BaseMapId, texture);
            Color displayTint = SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                surface.AlbedoCompensation);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetVector(
                BaseMapTransformId,
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer,
                    surface.MetersPerTile,
                    MinimumUvScale,
                    projection,
                    HashSaltBase + (int)source));
            properties.SetFloat(SmoothnessId, surface.Smoothness);
            properties.SetFloat(MetallicId, surface.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static Color ResolveSourceTint(
            Renderer renderer,
            MaterialPropertyBlock properties)
        {
            if (properties.HasProperty(BaseColorId))
            {
                return properties.GetColor(BaseColorId);
            }

            if (properties.HasProperty(ColorId))
            {
                return properties.GetColor(ColorId);
            }

            Material material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId))
            {
                return material.GetColor(BaseColorId);
            }

            if (material != null && material.HasProperty(ColorId))
            {
                return material.GetColor(ColorId);
            }

            return Color.white;
        }

        private static HomeSurfaceRecipe GetSurfaceRecipe(
            SurfaceSource source)
        {
            switch (source)
            {
                case SurfaceSource.Masonry:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Masonry);
                case SurfaceSource.Concrete:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Concrete);
                case SurfaceSource.Paving:
                    return CityPointOfInterestSurfaceAppearance.GetRecipe(
                        CityPointOfInterestSurfaceKind.Paving);
                case SurfaceSource.PaintedMetal:
                    return CityPointOfInterestSurfaceAppearance.GetRecipe(
                        CityPointOfInterestSurfaceKind.PaintedMetal);
                case SurfaceSource.RustedIron:
                    return MountainRoadSurfaceAppearance.GetRecipe(
                        MountainRoadSurfaceKind.RustedIron);
                case SurfaceSource.Deadwood:
                    return MountainRoadSurfaceAppearance.GetRecipe(
                        MountainRoadSurfaceKind.BarkAndDeadwood);
                case SurfaceSource.Timber:
                    return CityPointOfInterestSurfaceAppearance.GetRecipe(
                        CityPointOfInterestSurfaceKind.Timber);
                case SurfaceSource.Cloth:
                    return CityPointOfInterestSurfaceAppearance.GetRecipe(
                        CityPointOfInterestSurfaceKind.Cloth);
                case SurfaceSource.Paper:
                    return CityPointOfInterestSurfaceAppearance.GetRecipe(
                        CityPointOfInterestSurfaceKind.Paper);
                case SurfaceSource.Enamel:
                    return HomeSurfaceAppearance.GetRecipe(
                        HomeSurfaceKind.Enamel);
                case SurfaceSource.Roof:
                    return BarExteriorSurfaceAppearance.GetRecipe(
                        BarExteriorSurfaceKind.CityRoof);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(source),
                        source,
                        null);
            }
        }

        private static Texture2D GetTexture(SurfaceSource source)
        {
            switch (source)
            {
                case SurfaceSource.Masonry:
                    return CityFringeYardSurfaceAppearance.GetTexture(
                        CityFringeYardSurfaceKind.Masonry);
                case SurfaceSource.Concrete:
                    return CityFringeYardSurfaceAppearance.GetTexture(
                        CityFringeYardSurfaceKind.Concrete);
                case SurfaceSource.Paving:
                    return CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Paving);
                case SurfaceSource.PaintedMetal:
                    return CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.PaintedMetal);
                case SurfaceSource.RustedIron:
                    return MountainRoadSurfaceAppearance.GetTexture(
                        MountainRoadSurfaceKind.RustedIron);
                case SurfaceSource.Deadwood:
                    return MountainRoadSurfaceAppearance.GetTexture(
                        MountainRoadSurfaceKind.BarkAndDeadwood);
                case SurfaceSource.Timber:
                    return CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Timber);
                case SurfaceSource.Cloth:
                    return CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Cloth);
                case SurfaceSource.Paper:
                    return CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Paper);
                case SurfaceSource.Enamel:
                    return HomeSurfaceAppearance.GetTexture(
                        HomeSurfaceKind.Enamel);
                case SurfaceSource.Roof:
                    return BarExteriorSurfaceAppearance.GetTexture(
                        BarExteriorSurfaceKind.CityRoof);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(source),
                        source,
                        null);
            }
        }

        private static bool TryResolveRecipe(
            string componentName,
            out SurfaceRecipe recipe)
        {
            switch (componentName)
            {
                case ShellComponentName:
                    recipe = R(0, SurfaceSource.Masonry,
                        SurfaceProjection.BoxXY);
                    return true;
                case StepsComponentName:
                    recipe = R(1, SurfaceSource.Concrete,
                        SurfaceProjection.BoxXZ);
                    return true;
                case PlatformSupportComponentName:
                    recipe = R(2, SurfaceSource.Concrete,
                        SurfaceProjection.BoxXY);
                    return true;
                case PlatformSlabComponentName:
                    recipe = R(3, SurfaceSource.Paving,
                        SurfaceProjection.BoxXZ);
                    return true;
                case CladdingComponentName:
                    recipe = R(4, SurfaceSource.PaintedMetal,
                        SurfaceProjection.BoxXY);
                    return true;
                case RoofComponentName:
                    recipe = R(5, SurfaceSource.Roof,
                        SurfaceProjection.BoxXZ);
                    return true;
                case BarrelComponentName:
                    recipe = R(6, SurfaceSource.RustedIron,
                        SurfaceProjection.CylinderSide);
                    return true;
                case FuelComponentName:
                    recipe = R(7, SurfaceSource.Deadwood,
                        SurfaceProjection.CylinderSide);
                    return true;
                case MattressComponentName:
                    recipe = R(8, SurfaceSource.Cloth,
                        SurfaceProjection.BoxXZ);
                    return true;
                case BlanketComponentName:
                    recipe = R(9, SurfaceSource.Cloth,
                        SurfaceProjection.BoxXZ);
                    return true;
                case CardboardComponentName:
                    recipe = R(10, SurfaceSource.Paper,
                        SurfaceProjection.BoxXZ);
                    return true;
                case CrateComponentName:
                    recipe = R(11, SurfaceSource.Timber,
                        SurfaceProjection.BoxXY);
                    return true;
                case BagsComponentName:
                    recipe = R(12, SurfaceSource.Cloth,
                        SurfaceProjection.BoxXY);
                    return true;
                case BottlesComponentName:
                    recipe = R(13, SurfaceSource.Enamel,
                        SurfaceProjection.CylinderSide);
                    return true;
                case CanComponentName:
                    recipe = R(14, SurfaceSource.RustedIron,
                        SurfaceProjection.CylinderSide);
                    return true;
                default:
                    recipe = default;
                    return false;
            }
        }

        private static SurfaceRecipe R(
            int componentIndex,
            SurfaceSource source,
            SurfaceProjection projection)
        {
            return new SurfaceRecipe(componentIndex, source, projection);
        }

        private enum SurfaceSource
        {
            Masonry,
            Concrete,
            Paving,
            PaintedMetal,
            RustedIron,
            Deadwood,
            Timber,
            Cloth,
            Paper,
            Enamel,
            Roof
        }

        private readonly struct SurfaceRecipe
        {
            public SurfaceRecipe(
                int componentIndex,
                SurfaceSource source,
                SurfaceProjection projection)
            {
                ComponentIndex = componentIndex;
                Source = source;
                Projection = projection;
            }

            public int ComponentIndex { get; }
            public SurfaceSource Source { get; }
            public SurfaceProjection Projection { get; }
        }
    }
}
