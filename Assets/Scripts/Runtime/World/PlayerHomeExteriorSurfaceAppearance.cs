using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum PlayerHomeExteriorSurfaceKind
    {
        StuccoPrimary,
        StuccoRepair,
        BrickPlinth,
        RoofSlate,
        PaintedWood,
        PaintedMetal,
        WindowFrame,
        WindowGlass,
        Concrete
    }

    /// <summary>
    /// Binds the semantic sheets shared by the complete City model and the
    /// bounded facade seen from the apartment balcony. Authored meshes retain
    /// their UVs; the Home-only facade reconstruction uses the same sheets at
    /// a physical metre pitch through <see cref="ApplyProjected"/>.
    /// </summary>
    internal static class PlayerHomeExteriorSurfaceAppearance
    {
        public const string StuccoPrimaryTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorStuccoPrimaryAlbedo";
        public const string StuccoRepairTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorStuccoRepairAlbedo";
        public const string BrickPlinthTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorBrickPlinthAlbedo";
        public const string RoofSlateTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorRoofSlateAlbedo";
        public const string PaintedWoodTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorPaintedWoodAlbedo";
        public const string PaintedMetalTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorPaintedMetalAlbedo";
        public const string WindowFrameTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorWindowFrameAlbedo";
        public const string WindowGlassTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorWindowGlassAlbedo";
        public const string ConcreteTextureResourcePath =
            "PlayerHome/ExteriorTextures/" +
            "PlayerHomeExteriorConcreteAlbedo";

        private const int SurfaceCount =
            (int)PlayerHomeExteriorSurfaceKind.Concrete + 1;
        private const float MinimumProjectedUvScale = 0.35f;
        private const int ProjectedHashSalt = 2400;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionMapId =
            Shader.PropertyToID("_EmissionMap");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static Texture2D[] textures =
            new Texture2D[SurfaceCount];

        public static bool TryResolveSheet(
            string sheet,
            out PlayerHomeExteriorSurfaceKind kind)
        {
            switch (sheet)
            {
                case "StuccoPrimary":
                    kind = PlayerHomeExteriorSurfaceKind.StuccoPrimary;
                    return true;
                case "StuccoRepair":
                    kind = PlayerHomeExteriorSurfaceKind.StuccoRepair;
                    return true;
                case "BrickPlinth":
                    kind = PlayerHomeExteriorSurfaceKind.BrickPlinth;
                    return true;
                case "RoofSlate":
                    kind = PlayerHomeExteriorSurfaceKind.RoofSlate;
                    return true;
                case "PaintedWood":
                    kind = PlayerHomeExteriorSurfaceKind.PaintedWood;
                    return true;
                case "PaintedMetal":
                    kind = PlayerHomeExteriorSurfaceKind.PaintedMetal;
                    return true;
                case "WindowFrame":
                    kind = PlayerHomeExteriorSurfaceKind.WindowFrame;
                    return true;
                case "WindowGlass":
                    kind = PlayerHomeExteriorSurfaceKind.WindowGlass;
                    return true;
                case "Concrete":
                    kind = PlayerHomeExteriorSurfaceKind.Concrete;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static string GetTextureResourcePath(
            PlayerHomeExteriorSurfaceKind kind)
        {
            switch (kind)
            {
                case PlayerHomeExteriorSurfaceKind.StuccoPrimary:
                    return StuccoPrimaryTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.StuccoRepair:
                    return StuccoRepairTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.BrickPlinth:
                    return BrickPlinthTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.RoofSlate:
                    return RoofSlateTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.PaintedWood:
                    return PaintedWoodTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.PaintedMetal:
                    return PaintedMetalTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.WindowFrame:
                    return WindowFrameTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.WindowGlass:
                    return WindowGlassTextureResourcePath;
                case PlayerHomeExteriorSurfaceKind.Concrete:
                    return ConcreteTextureResourcePath;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, null);
            }
        }

        public static Texture2D GetTexture(
            PlayerHomeExteriorSurfaceKind kind)
        {
            string resourcePath = GetTextureResourcePath(kind);
            int index = (int)kind;
            if (textures[index] == null)
            {
                textures[index] = Resources.Load<Texture2D>(resourcePath);
            }

            if (textures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing player-home exterior {kind} texture " +
                    $"'{resourcePath}'.");
            }

            return textures[index];
        }

        public static void Apply(
            Renderer renderer,
            PlayerHomeExteriorSurfaceKind kind,
            bool emissive = false)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            if (kind == PlayerHomeExteriorSurfaceKind.WindowGlass)
            {
                ApplyGlass(renderer, emissive);
                return;
            }

            ApplyTextured(
                renderer,
                kind,
                new Vector4(1f, 1f, 0f, 0f));
        }

        public static void ApplyProjected(
            Renderer renderer,
            PlayerHomeExteriorSurfaceKind kind,
            SurfaceProjection projection)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            if (kind == PlayerHomeExteriorSurfaceKind.WindowGlass)
            {
                throw new ArgumentException(
                    "Projected opaque appearance cannot be used for glass.",
                    nameof(kind));
            }

            Vector4 transform = SurfaceAppearanceCore.CreateBaseMapTransform(
                renderer,
                ResolveMetersPerTile(kind),
                MinimumProjectedUvScale,
                projection,
                ProjectedHashSalt + (int)kind);
            ApplyTextured(renderer, kind, transform);
        }

        private static void ApplyTextured(
            Renderer renderer,
            PlayerHomeExteriorSurfaceKind kind,
            Vector4 textureTransform)
        {
            ResolveMaterialValues(
                kind,
                out float smoothness,
                out float metallic);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture(kind));
            properties.SetVector(BaseMapTransformId, textureTransform);
            properties.SetColor(BaseColorId, Color.white);
            properties.SetColor(ColorId, Color.white);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static void ApplyGlass(Renderer renderer, bool emissive)
        {
            if (emissive)
            {
                renderer.sharedMaterial =
                    CityWindowAppearance.ResolveLitMaterial(
                        CityWindowFamily.Home);
                CityWindowAppearance.ApplyAuthoredGlassPane(renderer);
                var litProperties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(litProperties);
                Texture2D glassTexture = GetTexture(
                    PlayerHomeExteriorSurfaceKind.WindowGlass);
                litProperties.SetTexture(BaseMapId, glassTexture);
                litProperties.SetTexture(EmissionMapId, glassTexture);
                litProperties.SetVector(
                    BaseMapTransformId,
                    new Vector4(1f, 1f, 0f, 0f));
                renderer.SetPropertyBlock(litProperties);
                return;
            }

            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(
                BaseMapId,
                GetTexture(PlayerHomeExteriorSurfaceKind.WindowGlass));
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor(
                BaseColorId,
                CityExteriorAppearance.WindowOff);
            properties.SetColor(ColorId, CityExteriorAppearance.WindowOff);
            properties.SetFloat(SmoothnessId, 0.18f);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);
        }

        private static float ResolveMetersPerTile(
            PlayerHomeExteriorSurfaceKind kind)
        {
            switch (kind)
            {
                case PlayerHomeExteriorSurfaceKind.StuccoPrimary:
                    return 2.4f;
                case PlayerHomeExteriorSurfaceKind.StuccoRepair:
                    return 1.8f;
                case PlayerHomeExteriorSurfaceKind.BrickPlinth:
                    return 1.2f;
                case PlayerHomeExteriorSurfaceKind.RoofSlate:
                    return 2.4f;
                case PlayerHomeExteriorSurfaceKind.PaintedWood:
                    return 1.0f;
                case PlayerHomeExteriorSurfaceKind.WindowFrame:
                    return 0.8f;
                case PlayerHomeExteriorSurfaceKind.PaintedMetal:
                    return 1.2f;
                case PlayerHomeExteriorSurfaceKind.Concrete:
                    return 1.5f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, null);
            }
        }

        private static void ResolveMaterialValues(
            PlayerHomeExteriorSurfaceKind kind,
            out float smoothness,
            out float metallic)
        {
            metallic = 0f;
            switch (kind)
            {
                case PlayerHomeExteriorSurfaceKind.StuccoPrimary:
                    smoothness = 0.06f;
                    return;
                case PlayerHomeExteriorSurfaceKind.StuccoRepair:
                    smoothness = 0.05f;
                    return;
                case PlayerHomeExteriorSurfaceKind.Concrete:
                    smoothness = 0.08f;
                    return;
                case PlayerHomeExteriorSurfaceKind.BrickPlinth:
                    smoothness = 0.07f;
                    return;
                case PlayerHomeExteriorSurfaceKind.RoofSlate:
                    smoothness = 0.12f;
                    return;
                case PlayerHomeExteriorSurfaceKind.PaintedWood:
                    smoothness = 0.14f;
                    return;
                case PlayerHomeExteriorSurfaceKind.PaintedMetal:
                    smoothness = 0.22f;
                    metallic = 0.24f;
                    return;
                case PlayerHomeExteriorSurfaceKind.WindowFrame:
                    smoothness = 0.13f;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, null);
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            textures = new Texture2D[SurfaceCount];
        }
    }
}
