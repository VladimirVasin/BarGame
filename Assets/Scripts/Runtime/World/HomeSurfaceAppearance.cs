using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum HomeSurfaceKind
    {
        Wallpaper,
        CeilingPlaster,
        PlankFloor,
        DarkWood,
        WornLaminate,
        Upholstery,
        BedLinen,
        BathroomTile,
        Enamel,
        PaintedMetal,
        Concrete,
        Rug
    }

    internal readonly struct HomeSurfaceRecipe
    {
        public HomeSurfaceRecipe(
            string resourcePath,
            float metersPerTile,
            float smoothness,
            float metallic,
            float albedoCompensation)
        {
            ResourcePath = resourcePath;
            MetersPerTile = metersPerTile;
            Smoothness = smoothness;
            Metallic = metallic;
            AlbedoCompensation = albedoCompensation;
        }

        public string ResourcePath { get; }
        public float MetersPerTile { get; }
        public float Smoothness { get; }
        public float Metallic { get; }
        public float AlbedoCompensation { get; }
    }

    /// <summary>
    /// Owns the packaged home-interior surface recipes and applies them
    /// without cloning the shared runtime primitive material. The recipe
    /// constants are the measured contract emitted by
    /// `tools/build-home-textures.py` into `ArtSource/Home/home-textures.json`:
    /// compensation follows the city-facade linear rule, so a builder tint
    /// multiplied against the sheet keeps the brightness the flat colour had.
    /// </summary>
    internal static class HomeSurfaceAppearance
    {
        public const string WallpaperTextureResourcePath =
            "Home/Textures/HomeWallpaperAlbedo";
        public const string CeilingPlasterTextureResourcePath =
            "Home/Textures/HomeCeilingPlasterAlbedo";
        public const string PlankFloorTextureResourcePath =
            "Home/Textures/HomePlankFloorAlbedo";
        public const string DarkWoodTextureResourcePath =
            "Home/Textures/HomeDarkWoodAlbedo";
        public const string WornLaminateTextureResourcePath =
            "Home/Textures/HomeWornLaminateAlbedo";
        public const string UpholsteryTextureResourcePath =
            "Home/Textures/HomeUpholsteryAlbedo";
        public const string BedLinenTextureResourcePath =
            "Home/Textures/HomeBedLinenAlbedo";
        public const string BathroomTileTextureResourcePath =
            "Home/Textures/HomeBathroomTileAlbedo";
        public const string EnamelTextureResourcePath =
            "Home/Textures/HomeEnamelAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "Home/Textures/HomePaintedMetalAlbedo";
        public const string ConcreteTextureResourcePath =
            "Home/Textures/HomeConcreteAlbedo";
        public const string RugTextureResourcePath =
            "Home/Textures/HomeRugAlbedo";

        private const int SurfaceCount = (int)HomeSurfaceKind.Rug + 1;
        private const float MinimumUvScale = 0.35f;

        // Salts the deterministic UV offset hash away from the stairwell
        // kinds, so a home surface and a stairwell surface with the same
        // enum index never share offsets should the scenes ever meet.
        private const int HashSaltBase = 1000;

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

        public static HomeSurfaceRecipe GetRecipe(HomeSurfaceKind kind)
        {
            switch (kind)
            {
                case HomeSurfaceKind.Wallpaper:
                    return new HomeSurfaceRecipe(
                        WallpaperTextureResourcePath,
                        1.9f,
                        0.05f,
                        0f,
                        1.414f);
                case HomeSurfaceKind.CeilingPlaster:
                    return new HomeSurfaceRecipe(
                        CeilingPlasterTextureResourcePath,
                        2.8f,
                        0.04f,
                        0f,
                        1.401f);
                case HomeSurfaceKind.PlankFloor:
                    return new HomeSurfaceRecipe(
                        PlankFloorTextureResourcePath,
                        1.6f,
                        0.10f,
                        0f,
                        1.5225f);
                case HomeSurfaceKind.DarkWood:
                    return new HomeSurfaceRecipe(
                        DarkWoodTextureResourcePath,
                        1.1f,
                        0.12f,
                        0f,
                        1.43f);
                case HomeSurfaceKind.WornLaminate:
                    return new HomeSurfaceRecipe(
                        WornLaminateTextureResourcePath,
                        1.2f,
                        0.18f,
                        0f,
                        1.4425f);
                case HomeSurfaceKind.Upholstery:
                    return new HomeSurfaceRecipe(
                        UpholsteryTextureResourcePath,
                        0.85f,
                        0.03f,
                        0f,
                        1.5325f);
                case HomeSurfaceKind.BedLinen:
                    return new HomeSurfaceRecipe(
                        BedLinenTextureResourcePath,
                        0.9f,
                        0.03f,
                        0f,
                        1.3005f);
                case HomeSurfaceKind.BathroomTile:
                    return new HomeSurfaceRecipe(
                        BathroomTileTextureResourcePath,
                        1.2f,
                        0.35f,
                        0f,
                        1.39f);
                case HomeSurfaceKind.Enamel:
                    return new HomeSurfaceRecipe(
                        EnamelTextureResourcePath,
                        1.0f,
                        0.45f,
                        0.05f,
                        1.2545f);
                case HomeSurfaceKind.PaintedMetal:
                    return new HomeSurfaceRecipe(
                        PaintedMetalTextureResourcePath,
                        1.0f,
                        0.22f,
                        0.30f,
                        1.4855f);
                case HomeSurfaceKind.Concrete:
                    return new HomeSurfaceRecipe(
                        ConcreteTextureResourcePath,
                        2.4f,
                        0.06f,
                        0f,
                        1.4985f);
                case HomeSurfaceKind.Rug:
                    return new HomeSurfaceRecipe(
                        RugTextureResourcePath,
                        1.5f,
                        0.02f,
                        0f,
                        1.5445f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(HomeSurfaceKind kind)
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
                    $"Missing home {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static void Apply(
            Renderer renderer,
            HomeSurfaceKind kind,
            SurfaceProjection projection,
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
            Color displayTint = CreateDisplayTint(sourceTint, kind);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetVector(
                BaseMapTransformId,
                CreateBaseMapTransform(renderer, kind, projection));
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            HomeSurfaceKind kind)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe(kind).AlbedoCompensation);
        }

        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            HomeSurfaceKind kind,
            SurfaceProjection projection)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            return SurfaceAppearanceCore.CreateBaseMapTransform(
                renderer,
                GetRecipe(kind).MetersPerTile,
                MinimumUvScale,
                projection,
                HashSaltBase + (int)kind);
        }

        private static int ValidateKind(HomeSurfaceKind kind)
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

    /// <summary>
    /// Convenience wrappers the home builders share: create the primitive
    /// with its authored flat colour, then apply the packaged surface so the
    /// colour survives as the albedo-compensated tint.
    /// </summary>
    internal static class HomeSurfacePrimitives
    {
        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            HomeSurfaceKind surfaceKind,
            SurfaceProjection projection,
            bool collider = true)
        {
            GameObject box = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                localPosition,
                size,
                color,
                collider);
            HomeSurfaceAppearance.Apply(
                box.GetComponent<Renderer>(),
                surfaceKind,
                projection,
                color);
            return box;
        }

        public static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            HomeSurfaceKind surfaceKind,
            SurfaceProjection projection,
            bool collider = true)
        {
            GameObject cylinder = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                localPosition,
                size,
                color,
                collider);
            HomeSurfaceAppearance.Apply(
                cylinder.GetComponent<Renderer>(),
                surfaceKind,
                projection,
                color);
            return cylinder;
        }
    }
}
