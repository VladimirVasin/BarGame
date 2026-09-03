using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum MountainRoadCafeSurfaceKind
    {
        ExteriorDetail,
        InteriorDetail,
        CounterDetail,
        MetalDetail,
        PropsDetail,
        GlassDetail,
        WarmEmission,
        Coffee
    }

    /// <summary>
    /// Applies the cafe's authored UV sheets through shared materials and
    /// renderer property blocks. The six detail sheets remain inside the
    /// existing Mountain Road palette families; no renderer gets a material
    /// instance and no repeated object is forced onto the same texture patch.
    /// </summary>
    internal static class MountainRoadCafeSurfaceAppearance
    {
        public const string ExteriorTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafeExteriorDetail";
        public const string InteriorTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafeInteriorDetail";
        public const string CounterTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafeCounterDetail";
        public const string MetalTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafeMetalDetail";
        public const string PropsTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafePropsDetail";
        public const string GlassTextureResourcePath =
            "MountainRoad/Cafe/Textures/MountainRoadCafeGlassDetail";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly Color ExteriorTint =
            new Color(0.72f, 0.88f, 0.82f, 1f);
        private static readonly Color InteriorTint =
            new Color(0.98f, 0.91f, 0.70f, 1f);
        private static readonly Color CounterTint =
            new Color(0.92f, 0.72f, 0.58f, 1f);
        private static readonly Color MetalTint =
            new Color(0.86f, 0.90f, 0.82f, 1f);
        private static readonly Color PropsTint =
            new Color(0.95f, 0.93f, 0.82f, 1f);
        private static readonly Color MenuPaperTint =
            new Color(0.82f, 0.77f, 0.64f, 1f);
        private static readonly Color FridgeEnamelTint =
            new Color(0.68f, 0.69f, 0.54f, 1f);
        private static readonly Color FridgeInteriorTint =
            new Color(0.78f, 0.81f, 0.70f, 1f);
        private static readonly Color FridgeShelfTint =
            new Color(0.62f, 0.72f, 0.70f, 1f);
        private static readonly Color StoveEnamelTint =
            new Color(0.88f, 0.91f, 0.86f, 1f);
        private static readonly Color PanMetalTint =
            new Color(0.56f, 0.59f, 0.56f, 1f);
        private static readonly Color GlassTint =
            new Color(0.44f, 0.68f, 0.61f, 0.42f);
        private static readonly Color WarmEmission =
            new Color(2.75f, 1.75f, 0.52f, 1f);
        private static readonly Color ColdTaskEmission =
            new Color(0.72f, 2.20f, 1.85f, 1f);
        private static readonly Color CoffeeTint =
            new Color(0.105f, 0.045f, 0.018f, 1f);

        private static Texture2D exterior;
        private static Texture2D interior;
        private static Texture2D counter;
        private static Texture2D metal;
        private static Texture2D props;
        private static Texture2D glass;

        public static bool TryResolveSheet(
            string sheet,
            out MountainRoadCafeSurfaceKind kind)
        {
            switch (sheet)
            {
                case "CafeExteriorDetail":
                    kind = MountainRoadCafeSurfaceKind.ExteriorDetail;
                    return true;
                case "CafeInteriorDetail":
                    kind = MountainRoadCafeSurfaceKind.InteriorDetail;
                    return true;
                case "CafeCounterDetail":
                    kind = MountainRoadCafeSurfaceKind.CounterDetail;
                    return true;
                case "CafeMetalDetail":
                    kind = MountainRoadCafeSurfaceKind.MetalDetail;
                    return true;
                case "CafePropsDetail":
                    kind = MountainRoadCafeSurfaceKind.PropsDetail;
                    return true;
                case "CafeGlassDetail":
                    kind = MountainRoadCafeSurfaceKind.GlassDetail;
                    return true;
                case "CafeWarmEmission":
                    kind = MountainRoadCafeSurfaceKind.WarmEmission;
                    return true;
                case "CafeCoffee":
                    kind = MountainRoadCafeSurfaceKind.Coffee;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static void Apply(MountainRoadCafePartBinding binding)
        {
            if (binding == null || binding.Renderer == null)
            {
                return;
            }

            if (string.Equals(
                    binding.Role,
                    "stove_task_lens",
                    StringComparison.Ordinal))
            {
                ApplyEmission(binding.Renderer, ColdTaskEmission);
                return;
            }

            if (TryApplyRoleSurface(binding))
            {
                return;
            }

            if (!TryResolveSheet(binding.Sheet, out MountainRoadCafeSurfaceKind kind))
            {
                throw new InvalidOperationException(
                    $"Cafe part '{binding.SourceName}' names unknown sheet " +
                    $"'{binding.Sheet}'.");
            }

            Apply(binding.Renderer, kind);
        }

        private static bool TryApplyRoleSurface(
            MountainRoadCafePartBinding binding)
        {
            switch (binding.Role)
            {
                case "menu_pages":
                    // The shared props sheet contains the green appliance
                    // stripe used by the counter dressing. Mapping that whole
                    // sheet across an open book puts the stripe through its
                    // lettering, so pages use plain warm paper instead.
                    ApplyLit(
                        binding.Renderer,
                        Texture2D.whiteTexture,
                        MenuPaperTint,
                        0.08f,
                        0f);
                    return true;
                case "refrigerator_body":
                case "fridge_door":
                    ApplyLit(
                        binding.Renderer,
                        GetProps(),
                        FridgeEnamelTint,
                        0.45f,
                        0.05f);
                    return true;
                case "refrigerator_cavity":
                    ApplyLit(
                        binding.Renderer,
                        GetProps(),
                        FridgeInteriorTint,
                        0.38f,
                        0.02f);
                    return true;
                case "refrigerator_shelf":
                    ApplyLit(
                        binding.Renderer,
                        GetProps(),
                        FridgeShelfTint,
                        0.34f,
                        0.12f);
                    return true;
                case "stove":
                    ApplyLit(
                        binding.Renderer,
                        GetMetal(),
                        StoveEnamelTint,
                        0.36f,
                        0.16f);
                    return true;
                case "frying_pan":
                    ApplyLit(
                        binding.Renderer,
                        GetMetal(),
                        PanMetalTint,
                        0.30f,
                        0.35f);
                    return true;
                default:
                    return false;
            }
        }

        public static void Apply(
            Renderer renderer,
            MountainRoadCafeSurfaceKind kind)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            switch (kind)
            {
                case MountainRoadCafeSurfaceKind.ExteriorDetail:
                    ApplyLit(renderer, GetExterior(), ExteriorTint, 0.08f, 0f);
                    return;
                case MountainRoadCafeSurfaceKind.InteriorDetail:
                    ApplyLit(renderer, GetInterior(), InteriorTint, 0.06f, 0f);
                    return;
                case MountainRoadCafeSurfaceKind.CounterDetail:
                    ApplyLit(renderer, GetCounter(), CounterTint, 0.18f, 0f);
                    return;
                case MountainRoadCafeSurfaceKind.MetalDetail:
                    ApplyLit(renderer, GetMetal(), MetalTint, 0.30f, 0.28f);
                    return;
                case MountainRoadCafeSurfaceKind.PropsDetail:
                    ApplyLit(renderer, GetProps(), PropsTint, 0.20f, 0f);
                    return;
                case MountainRoadCafeSurfaceKind.GlassDetail:
                    ApplyGlass(renderer);
                    return;
                case MountainRoadCafeSurfaceKind.WarmEmission:
                    ApplyEmission(renderer);
                    return;
                case MountainRoadCafeSurfaceKind.Coffee:
                    ApplyLit(
                        renderer,
                        Texture2D.whiteTexture,
                        CoffeeTint,
                        0.42f,
                        0f);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void ApplyLit(
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

        private static void ApplyGlass(Renderer renderer)
        {
            renderer.sharedMaterial = HomeBalconyResources.GlassMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Texture2D texture = GetGlass();
            properties.SetTexture(BaseMapId, texture);
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor(BaseColorId, GlassTint);
            properties.SetColor(ColorId, GlassTint);
            properties.SetFloat(SmoothnessId, 0.16f);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);
        }

        private static void ApplyEmission(Renderer renderer)
        {
            ApplyEmission(renderer, WarmEmission);
        }

        private static void ApplyEmission(
            Renderer renderer,
            Color emission)
        {
            renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, Texture2D.whiteTexture);
            properties.SetColor(BaseColorId, emission);
            properties.SetColor(ColorId, emission);
            renderer.SetPropertyBlock(properties);
            CityNightGlowRegistry.Register(renderer, emission);
        }

        private static Texture2D GetExterior()
        {
            return exterior ??= Load(ExteriorTextureResourcePath, "exterior detail");
        }

        private static Texture2D GetInterior()
        {
            return interior ??= Load(InteriorTextureResourcePath, "interior detail");
        }

        private static Texture2D GetCounter()
        {
            return counter ??= Load(CounterTextureResourcePath, "counter detail");
        }

        private static Texture2D GetMetal()
        {
            return metal ??= Load(MetalTextureResourcePath, "metal detail");
        }

        private static Texture2D GetProps()
        {
            return props ??= Load(PropsTextureResourcePath, "props detail");
        }

        private static Texture2D GetGlass()
        {
            return glass ??= Load(GlassTextureResourcePath, "glass detail");
        }

        private static Texture2D Load(string resourcePath, string label)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Missing Mountain Road cafe {label} texture " +
                    $"'{resourcePath}'.");
            }

            return texture;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            exterior = null;
            interior = null;
            counter = null;
            metal = null;
            props = null;
            glass = null;
        }
    }
}
