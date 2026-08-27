using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Binds the window-slot IDs authored into the combined Blender glass
    /// mesh to the existing deterministic district light schedule. UV2 keeps
    /// every pane addressable while the FBX remains non-readable at runtime.
    /// </summary>
    internal static class CityBuildingWindowSlotAppearance
    {
        public const string ShaderResourcePath =
            "Shaders/CityBuildingWindowSlots";
        public const int MaximumSlotCount =
            CityBuildingAssetRegistry.MaximumWindowSlotId + 1;
        public const float Uv2SlotDivisor =
            CityBuildingAssetRegistry.WindowSlotUv2Divisor;

        private static readonly int WindowStatesId =
            Shader.PropertyToID("_CityBuildingWindowStates");
        private static readonly int OffColorId =
            Shader.PropertyToID("_OffColor");
        private static readonly int DayColorId =
            Shader.PropertyToID("_DayColor");
        private static readonly int ColdColorId =
            Shader.PropertyToID("_ColdColor");
        private static readonly int WarmColorId =
            Shader.PropertyToID("_WarmColor");

        private static Material material;

        public static void Apply(
            Renderer renderer,
            CityBuildingAssetRegistry registry,
            BuildingLot lot,
            int citySeed)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            var states = new float[MaximumSlotCount];
            for (int index = 0; index < registry.WindowSlots.Count; index++)
            {
                CityBuildingWindowSlot slot = registry.WindowSlots[index];
                int shaderIndex = slot.Uv2SlotId;
                if (shaderIndex <= 0 || shaderIndex >= MaximumSlotCount)
                {
                    throw new InvalidOperationException(
                        $"Window slot {shaderIndex} in '{registry.StableId}' " +
                        "escapes the runtime shader table.");
                }

                int side = ResolveSideIndex(slot.Side);
                CityWindowFamily family =
                    CityExteriorAppearance.ResolveWindowFamily(
                        lot,
                        citySeed,
                        slot.Floor,
                        slot.Bay,
                        side,
                        out uint paneHash);
                int variant = (int)((paneHash >> 8) %
                    CityWindowAppearance.VariantCount);
                states[shaderIndex] =
                    ((int)family * CityWindowAppearance.VariantCount) +
                    variant;
            }

            renderer.sharedMaterial = Material;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloatArray(WindowStatesId, states);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material Material
        {
            get
            {
                if (material == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        ShaderResourcePath);
                    if (shader == null || !shader.isSupported)
                    {
                        throw new InvalidOperationException(
                            "Missing or unsupported City building window " +
                            $"shader '{ShaderResourcePath}'.");
                    }

                    material = new Material(shader)
                    {
                        name = "City Building Window Slots",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    material.SetColor(
                        OffColorId,
                        CityExteriorAppearance.WindowOff);
                    material.SetColor(
                        DayColorId,
                        CityWindowAppearance.DayGlass);
                    material.SetColor(
                        ColdColorId,
                        CityExteriorAppearance.ColdWindow);
                    material.SetColor(
                        WarmColorId,
                        CityExteriorAppearance.WarmWindow);
                    CityWindowAppearance.SetNightFactor(
                        CityWindowAppearance.NightFactor);
                }

                return material;
            }
        }

        private static int ResolveSideIndex(string side)
        {
            switch (side)
            {
                case "Front":
                    return 0;
                case "Rear":
                    return 1;
                case "Left":
                    return 2;
                case "Right":
                    return 3;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(side),
                        side,
                        "Unknown City building facade side.");
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }

            material = null;
        }
    }
}
