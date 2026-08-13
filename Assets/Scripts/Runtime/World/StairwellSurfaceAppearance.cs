using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum StairwellSurfaceKind
    {
        WallPaint,
        Concrete,
        StairConcrete,
        CorrodedMetal,
        DoorPaint,
        Damage,
        DirtyWood,
        Debris
    }

    internal enum StairwellSurfaceProjection
    {
        BoxXY,
        BoxXZ,
        BoxZY,
        CylinderSide,
        CylinderCapXZ
    }

    internal readonly struct StairwellSurfaceRecipe
    {
        public StairwellSurfaceRecipe(
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
    /// Owns the packaged stairwell surface recipes and applies them without
    /// cloning the shared runtime primitive material. Builders declare the
    /// visible primitive plane/axis mapping; native UV scale and stable offset
    /// then live in each renderer's MPB.
    /// </summary>
    internal static class StairwellSurfaceAppearance
    {
        public const string WallPaintTextureResourcePath =
            "Stairwell/Textures/StairwellWallPaintAlbedoV2";
        public const string ConcreteTextureResourcePath =
            "Stairwell/Textures/StairwellConcreteAlbedo";
        public const string StairConcreteTextureResourcePath =
            "Stairwell/Textures/StairwellStairConcreteAlbedo";
        public const string CorrodedMetalTextureResourcePath =
            "Stairwell/Textures/StairwellCorrodedMetalAlbedoV2";
        public const string DoorPaintTextureResourcePath =
            "Stairwell/Textures/StairwellDoorPaintAlbedoV2";
        public const string DamageTextureResourcePath =
            "Stairwell/Textures/StairwellDamageAlbedo";
        public const string DirtyWoodTextureResourcePath =
            "Stairwell/Textures/StairwellDirtyWoodAlbedo";
        public const string DebrisTextureResourcePath =
            "Stairwell/Textures/StairwellDebrisAlbedo";

        private const int SurfaceCount =
            (int)StairwellSurfaceKind.Debris + 1;
        private const float MinimumUvScale = 0.35f;

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

        public static StairwellSurfaceRecipe GetRecipe(
            StairwellSurfaceKind kind)
        {
            switch (kind)
            {
                case StairwellSurfaceKind.WallPaint:
                    return new StairwellSurfaceRecipe(
                        WallPaintTextureResourcePath,
                        2.4f,
                        0.07f,
                        0f,
                        2.52f);
                case StairwellSurfaceKind.Concrete:
                    return new StairwellSurfaceRecipe(
                        ConcreteTextureResourcePath,
                        2.8f,
                        0.06f,
                        0f,
                        2.51f);
                case StairwellSurfaceKind.StairConcrete:
                    return new StairwellSurfaceRecipe(
                        StairConcreteTextureResourcePath,
                        1.6f,
                        0.10f,
                        0f,
                        2.69f);
                case StairwellSurfaceKind.CorrodedMetal:
                    return new StairwellSurfaceRecipe(
                        CorrodedMetalTextureResourcePath,
                        1.2f,
                        0.16f,
                        0.28f,
                        3.98f);
                case StairwellSurfaceKind.DoorPaint:
                    return new StairwellSurfaceRecipe(
                        DoorPaintTextureResourcePath,
                        1.5f,
                        0.12f,
                        0.12f,
                        3.13f);
                case StairwellSurfaceKind.Damage:
                    return new StairwellSurfaceRecipe(
                        DamageTextureResourcePath,
                        1.25f,
                        0.05f,
                        0f,
                        2.17f);
                case StairwellSurfaceKind.DirtyWood:
                    return new StairwellSurfaceRecipe(
                        DirtyWoodTextureResourcePath,
                        1.1f,
                        0.07f,
                        0f,
                        3.54f);
                case StairwellSurfaceKind.Debris:
                    return new StairwellSurfaceRecipe(
                        DebrisTextureResourcePath,
                        0.8f,
                        0.08f,
                        0f,
                        3.50f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }

        public static Texture2D GetTexture(StairwellSurfaceKind kind)
        {
            int index = ValidateKind(kind);
            if (cachedTextures[index] == null)
            {
                StairwellSurfaceRecipe recipe = GetRecipe(kind);
                cachedTextures[index] = Resources.Load<Texture2D>(
                    recipe.ResourcePath);
            }

            if (cachedTextures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing stairwell {kind} surface texture " +
                    $"'{GetRecipe(kind).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static void Apply(
            Renderer renderer,
            StairwellSurfaceKind kind,
            StairwellSurfaceProjection projection,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            StairwellSurfaceRecipe recipe = GetRecipe(kind);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            Color displayTint = CreateDisplayTint(sourceTint, recipe);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetVector(
                BaseMapTransformId,
                CreateBaseMapTransform(
                    renderer,
                    kind,
                    projection,
                    recipe));
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(
            Color sourceTint,
            StairwellSurfaceKind kind)
        {
            return CreateDisplayTint(sourceTint, GetRecipe(kind));
        }

        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            StairwellSurfaceKind kind,
            StairwellSurfaceProjection projection)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            return CreateBaseMapTransform(
                renderer,
                kind,
                projection,
                GetRecipe(kind));
        }

        private static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            StairwellSurfaceKind kind,
            StairwellSurfaceProjection projection,
            StairwellSurfaceRecipe recipe)
        {
            return SurfaceAppearanceCore.CreateBaseMapTransform(
                renderer,
                recipe.MetersPerTile,
                MinimumUvScale,
                ResolveProjection(projection),
                (int)kind);
        }

        private static Color CreateDisplayTint(
            Color sourceTint,
            StairwellSurfaceRecipe recipe)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                recipe.AlbedoCompensation);
        }

        private static SurfaceProjection ResolveProjection(
            StairwellSurfaceProjection projection)
        {
            switch (projection)
            {
                case StairwellSurfaceProjection.BoxXY:
                    return SurfaceProjection.BoxXY;
                case StairwellSurfaceProjection.BoxXZ:
                    return SurfaceProjection.BoxXZ;
                case StairwellSurfaceProjection.BoxZY:
                    return SurfaceProjection.BoxZY;
                case StairwellSurfaceProjection.CylinderSide:
                    return SurfaceProjection.CylinderSide;
                case StairwellSurfaceProjection.CylinderCapXZ:
                    return SurfaceProjection.CylinderCapXZ;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(projection),
                        projection,
                        null);
            }
        }

        private static int ValidateKind(StairwellSurfaceKind kind)
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
