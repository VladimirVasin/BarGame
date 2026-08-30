using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum SupermarketExteriorSurfaceKind
    {
        WallAtlas,
        FasciaAtlas,
        Brick,
        Roof,
        Metal,
        Glass,
        InteriorDark,
        InteriorLight,
        SignHousing,
        SignGlow,
        Mat
    }

    /// <summary>
    /// Resolves the semantic material sheets declared by the Blender model.
    /// Atlas UVs stay unique to each wall/fascia element, while brick, metal
    /// and roof meshes carry their own metre-aware authored UVs. No texture is
    /// stretched over the whole building and no per-instance materials are
    /// created.
    /// </summary>
    internal static class SupermarketExteriorSurfaceAppearance
    {
        public const string WallAtlasTextureResourcePath =
            "Supermarket/ExteriorTextures/SupermarketExteriorWallAtlas";
        public const string FasciaAtlasTextureResourcePath =
            "Supermarket/ExteriorTextures/SupermarketExteriorFasciaAtlas";
        public const string BrickTextureResourcePath =
            "Supermarket/ExteriorTextures/SupermarketExteriorBrickAlbedo";
        public const string MetalTextureResourcePath =
            "Supermarket/ExteriorTextures/SupermarketExteriorMetalAlbedo";

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

        private static readonly Color WallTint =
            new Color(0.96f, 0.93f, 0.82f, 1f);
        private static readonly Color BrickTint =
            new Color(0.55f, 0.42f, 0.36f, 1f);
        private static readonly Color RoofTint =
            new Color(0.50f, 0.50f, 0.47f, 1f);
        private static readonly Color MetalTint =
            new Color(0.62f, 0.64f, 0.58f, 1f);
        private static readonly Color InteriorDarkTint =
            new Color(0.08f, 0.10f, 0.085f, 1f);
        private static readonly Color SignHousingTint =
            new Color(0.19f, 0.18f, 0.15f, 1f);
        private static readonly Color MatTint =
            new Color(0.11f, 0.13f, 0.105f, 1f);
        private static readonly Color InteriorLightColor =
            new Color(0.78f, 0.92f, 0.72f, 1f);
        private static readonly Color SignGlowColor =
            new Color(1.04f, 0.88f, 0.54f, 1f);

        private static Texture2D wallAtlas;
        private static Texture2D fasciaAtlas;
        private static Texture2D brick;
        private static Texture2D metal;
        private static Texture2D roof;

        public static bool TryResolveSheet(
            string sheet,
            out SupermarketExteriorSurfaceKind kind)
        {
            switch (sheet)
            {
                case "ExteriorWallAtlas":
                    kind = SupermarketExteriorSurfaceKind.WallAtlas;
                    return true;
                case "ExteriorFasciaAtlas":
                    kind = SupermarketExteriorSurfaceKind.FasciaAtlas;
                    return true;
                case "ExteriorBrick":
                    kind = SupermarketExteriorSurfaceKind.Brick;
                    return true;
                case "ExteriorRoof":
                    kind = SupermarketExteriorSurfaceKind.Roof;
                    return true;
                case "ExteriorMetal":
                    kind = SupermarketExteriorSurfaceKind.Metal;
                    return true;
                case "ExteriorGlass":
                    kind = SupermarketExteriorSurfaceKind.Glass;
                    return true;
                case "ExteriorInteriorDark":
                    kind = SupermarketExteriorSurfaceKind.InteriorDark;
                    return true;
                case "ExteriorInteriorLight":
                    kind = SupermarketExteriorSurfaceKind.InteriorLight;
                    return true;
                case "ExteriorSignHousing":
                    kind = SupermarketExteriorSurfaceKind.SignHousing;
                    return true;
                case "ExteriorSignGlow":
                    kind = SupermarketExteriorSurfaceKind.SignGlow;
                    return true;
                case "ExteriorMat":
                    kind = SupermarketExteriorSurfaceKind.Mat;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static void Apply(
            Renderer renderer,
            SupermarketExteriorSurfaceKind kind)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            switch (kind)
            {
                case SupermarketExteriorSurfaceKind.WallAtlas:
                    ApplyTextured(renderer, GetWallAtlas(), WallTint, 0.08f, 0f);
                    return;
                case SupermarketExteriorSurfaceKind.FasciaAtlas:
                    ApplyTextured(renderer, GetFasciaAtlas(), Color.white, 0.14f, 0f);
                    return;
                case SupermarketExteriorSurfaceKind.Brick:
                    ApplyTextured(renderer, GetBrick(), BrickTint, 0.07f, 0f);
                    return;
                case SupermarketExteriorSurfaceKind.Roof:
                    ApplyTextured(renderer, GetRoof(), RoofTint, 0.04f, 0f);
                    return;
                case SupermarketExteriorSurfaceKind.Metal:
                    ApplyTextured(renderer, GetMetal(), MetalTint, 0.22f, 0.30f);
                    return;
                case SupermarketExteriorSurfaceKind.Glass:
                    renderer.sharedMaterial =
                        CityWindowAppearance.ResolveLitMaterial(
                            CityWindowFamily.Supermarket);
                    CityWindowAppearance.ApplyAuthoredGlassPane(renderer);
                    return;
                case SupermarketExteriorSurfaceKind.InteriorDark:
                    ApplyFlat(renderer, InteriorDarkTint, 0.03f, 0f);
                    return;
                case SupermarketExteriorSurfaceKind.InteriorLight:
                    ApplyEmission(renderer, InteriorLightColor);
                    return;
                case SupermarketExteriorSurfaceKind.SignHousing:
                    ApplyFlat(renderer, SignHousingTint, 0.12f, 0.12f);
                    return;
                case SupermarketExteriorSurfaceKind.SignGlow:
                    ApplyEmission(renderer, SignGlowColor);
                    return;
                case SupermarketExteriorSurfaceKind.Mat:
                    ApplyTextured(renderer, GetMetal(), MatTint, 0.02f, 0f);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, null);
            }
        }

        private static void ApplyTextured(
            Renderer renderer,
            Texture2D texture,
            Color tint,
            float smoothness,
            float metallic)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, texture);
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static void ApplyFlat(
            Renderer renderer,
            Color tint,
            float smoothness,
            float metallic)
        {
            ApplyTextured(
                renderer,
                Texture2D.whiteTexture,
                tint,
                smoothness,
                metallic);
        }

        private static void ApplyEmission(Renderer renderer, Color color)
        {
            renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            renderer.SetPropertyBlock(properties);
            CityNightGlowRegistry.Register(renderer, color);
        }

        private static Texture2D GetWallAtlas()
        {
            return wallAtlas ??= Load(
                WallAtlasTextureResourcePath,
                "wall atlas");
        }

        private static Texture2D GetFasciaAtlas()
        {
            return fasciaAtlas ??= Load(
                FasciaAtlasTextureResourcePath,
                "fascia atlas");
        }

        private static Texture2D GetBrick()
        {
            return brick ??= Load(BrickTextureResourcePath, "brick");
        }

        private static Texture2D GetMetal()
        {
            return metal ??= Load(MetalTextureResourcePath, "metal");
        }

        private static Texture2D GetRoof()
        {
            return roof ??= Load(
                CityFacadeAppearance.RoofTextureResourcePath,
                "roof");
        }

        private static Texture2D Load(string resourcePath, string label)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Missing supermarket exterior {label} texture " +
                    $"'{resourcePath}'.");
            }

            return texture;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            wallAtlas = null;
            fasciaAtlas = null;
            brick = null;
            metal = null;
            roof = null;
        }
    }
}
