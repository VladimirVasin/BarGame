using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum MountainRoadSurfaceKind
    {
        Asphalt,
        ForestFloor,
        WindSnow,
        LayeredStone,
        ConiferNeedles,
        BarkAndDeadwood,
        Concrete,
        RustedIron,
        PaintedMetal,
        PaleEnamel,
        Masonry,
        Linoleum,
        Timber,
        WallPaint,
        InteriorPaint
    }

    /// <summary>
    /// Owns the fifteen packaged surface recipes of the mountain road. The
    /// constants mirror the measured contract emitted by
    /// <c>tools/build-mountain-road-textures.py</c> into
    /// <c>ArtSource/MountainRoad/mountain-road-textures.json</c>: a
    /// compensated authored tint multiplies each mean-controlled albedo
    /// without changing the brightness of the former flat-colour surface.
    ///
    /// Six of the kinds print their own sheet — the climb's asphalt, forest
    /// floor, wind snow, layered stone, conifer needles and bark. The other
    /// nine borrow a sheet that already ships, because the bridge, the
    /// cableway and the cafe are made of concrete, iron, painted metal,
    /// masonry, linoleum, timber and wall paint the City already prints.
    /// A borrowed sheet keeps its own bytes and its own generator; only its
    /// compensation is re-solved here, since compensation fits the tints
    /// that multiply a sheet rather than the sheet itself. Two kinds may
    /// therefore name one resource path and still differ — PaintedMetal and
    /// PaleEnamel read the same park sheet at opposite ends of its tint
    /// range, and WallPaint and InteriorPaint do the same.
    ///
    /// Hand-built mountain meshes bake metre-scale UVs at the recipe pitch
    /// and take <see cref="ApplyCombined"/>; the scene's many single
    /// primitives carry per-face 0..1 UVs and take <see cref="Apply"/>,
    /// which adds the metre tiling and a deterministic offset through the
    /// property block. Both paths keep the runtime primitive material
    /// shared; no per-object material instances are created.
    /// </summary>
    internal static class MountainRoadSurfaceAppearance
    {
        public const string AsphaltTextureResourcePath =
            "Textures/MountainRoadAsphaltAlbedo";
        public const string ForestFloorTextureResourcePath =
            "Textures/MountainRoadForestFloorAlbedo";
        public const string WindSnowTextureResourcePath =
            "Textures/MountainRoadSnowAlbedo";
        public const string LayeredStoneTextureResourcePath =
            "Textures/MountainRoadStoneAlbedo";
        public const string ConiferNeedlesTextureResourcePath =
            "Textures/MountainRoadNeedleAlbedo";
        public const string BarkAndDeadwoodTextureResourcePath =
            "Textures/MountainRoadBarkAlbedo";
        public const string ConcreteTextureResourcePath =
            "Textures/CityFringeConcreteAlbedo";
        public const string RustedIronTextureResourcePath =
            "Textures/CityRiverIronAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "Textures/CityParkPaintedMetalAlbedo";
        public const string PaleEnamelTextureResourcePath =
            "Textures/CityParkPaintedMetalAlbedo";
        public const string MasonryTextureResourcePath =
            "Textures/CityFringeMasonryAlbedo";
        public const string LinoleumTextureResourcePath =
            "Supermarket/Textures/SupermarketLinoleumAlbedo";
        public const string TimberTextureResourcePath =
            "Textures/CityParkTimberAlbedo";
        public const string WallPaintTextureResourcePath =
            "Supermarket/Textures/SupermarketWallPaintAlbedo";
        public const string InteriorPaintTextureResourcePath =
            "Supermarket/Textures/SupermarketWallPaintAlbedo";

        private const int SurfaceCount =
            (int)MountainRoadSurfaceKind.InteriorPaint + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the home (1000),
        // stairwell, supermarket (3000), bar (4000), point-of-interest
        // (5000), river (6000), city mountain rock (7000) and fringe-yard
        // (8000) kinds, so two surfaces with the same enum index never
        // share offsets should the areas ever meet.
        private const int HashSaltBase = 9000;

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
            MountainRoadSurfaceKind kind)
        {
            switch (kind)
            {
                case MountainRoadSurfaceKind.Asphalt:
                    return new HomeSurfaceRecipe(
                        AsphaltTextureResourcePath,
                        3.5f,
                        0.045f,
                        0f,
                        1.488f);
                case MountainRoadSurfaceKind.ForestFloor:
                    return new HomeSurfaceRecipe(
                        ForestFloorTextureResourcePath,
                        5.0f,
                        0.030f,
                        0f,
                        1.445f);
                case MountainRoadSurfaceKind.WindSnow:
                    return new HomeSurfaceRecipe(
                        WindSnowTextureResourcePath,
                        5.0f,
                        0.050f,
                        0f,
                        1.368f);
                case MountainRoadSurfaceKind.LayeredStone:
                    return new HomeSurfaceRecipe(
                        LayeredStoneTextureResourcePath,
                        6.0f,
                        0.025f,
                        0f,
                        1.415f);
                case MountainRoadSurfaceKind.ConiferNeedles:
                    return new HomeSurfaceRecipe(
                        ConiferNeedlesTextureResourcePath,
                        2.5f,
                        0.020f,
                        0f,
                        1.456f);
                case MountainRoadSurfaceKind.BarkAndDeadwood:
                    return new HomeSurfaceRecipe(
                        BarkAndDeadwoodTextureResourcePath,
                        2.5f,
                        0.040f,
                        0f,
                        1.434f);
                case MountainRoadSurfaceKind.Concrete:
                    return new HomeSurfaceRecipe(
                        ConcreteTextureResourcePath,
                        3.0f,
                        0.055f,
                        0f,
                        1.4065f);
                case MountainRoadSurfaceKind.RustedIron:
                    return new HomeSurfaceRecipe(
                        RustedIronTextureResourcePath,
                        1.2f,
                        0.180f,
                        0.200f,
                        1.417f);
                case MountainRoadSurfaceKind.PaintedMetal:
                    return new HomeSurfaceRecipe(
                        PaintedMetalTextureResourcePath,
                        1.2f,
                        0.160f,
                        0.150f,
                        1.3375f);
                case MountainRoadSurfaceKind.PaleEnamel:
                    return new HomeSurfaceRecipe(
                        PaleEnamelTextureResourcePath,
                        1.2f,
                        0.160f,
                        0.150f,
                        1.315f);
                case MountainRoadSurfaceKind.Masonry:
                    return new HomeSurfaceRecipe(
                        MasonryTextureResourcePath,
                        2.4f,
                        0.035f,
                        0f,
                        1.4465f);
                case MountainRoadSurfaceKind.Linoleum:
                    return new HomeSurfaceRecipe(
                        LinoleumTextureResourcePath,
                        2.4f,
                        0.160f,
                        0f,
                        1.4185f);
                case MountainRoadSurfaceKind.Timber:
                    return new HomeSurfaceRecipe(
                        TimberTextureResourcePath,
                        1.4f,
                        0.080f,
                        0f,
                        1.328f);
                case MountainRoadSurfaceKind.WallPaint:
                    return new HomeSurfaceRecipe(
                        WallPaintTextureResourcePath,
                        2.6f,
                        0.050f,
                        0f,
                        1.4635f);
                case MountainRoadSurfaceKind.InteriorPaint:
                    return new HomeSurfaceRecipe(
                        InteriorPaintTextureResourcePath,
                        2.6f,
                        0.050f,
                        0f,
                        1.348f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(MountainRoadSurfaceKind kind)
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
                    $"Missing mountain-road {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        /// <summary>
        /// Textures a primitive on the plane selected by its proportions.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            MountainRoadSurfaceKind kind,
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
        /// inferred from its proportions — the cafe's thin dressing panels,
        /// its mullions and the cableway's square-section members all look
        /// like the wrong slab to the automatic choice.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            MountainRoadSurfaceKind kind,
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
        /// Textures a hand-built or combined mesh whose UVs were baked at
        /// the recipe's metre pitch. No transform is added because the mesh
        /// already owns its scale and placement.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            MountainRoadSurfaceKind kind,
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
            MountainRoadSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        private static void ApplySharedProperties(
            Renderer renderer,
            MountainRoadSurfaceKind kind,
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

        private static int ValidateKind(MountainRoadSurfaceKind kind)
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
