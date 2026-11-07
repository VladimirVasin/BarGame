using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Binds the window-slot IDs authored into the combined Blender glass
    /// mesh to a sparse, permanent warm-light selection and dark variants. UV2
    /// keeps every pane addressable while the FBX remains non-readable at
    /// runtime; pane-local UV0 carries one complete atlas window per slot.
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
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int OffColorId =
            Shader.PropertyToID("_OffColor");
        private static readonly int DayColorId =
            Shader.PropertyToID("_DayColor");
        private static readonly int WarmColorId =
            Shader.PropertyToID("_WarmColor");
        private static readonly int EmissionStrengthId =
            Shader.PropertyToID("_EmissionStrength");

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
            ulong litWindowMask = ResolveLitWindowMask(registry, lot, citySeed);
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
                        ResolveRowPaneCount(registry, slot),
                        side,
                        out uint paneHash);
                if (lot.IsOrdinaryBuilding)
                    family = (litWindowMask & (1UL << shaderIndex)) != 0
                        ? CityWindowFamily.Warm : CityWindowFamily.Off;
                int variant = (int)((paneHash >> 8) %
                    CityWindowAppearance.VariantCount);
                states[shaderIndex] =
                    ((int)family * CityWindowAppearance.VariantCount) +
                    variant;
                if (family != CityWindowFamily.Off)
                    litWindowMask |= 1UL << shaderIndex;
            }

            registry.LitWindowMask = litWindowMask;
            renderer.sharedMaterial = Material;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloatArray(WindowStatesId, states);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Select once from the actual authored panes, across every floor and
        // facade. Every luminous ordinary pane then owns its own fixed Light;
        // neither emission nor the receiving street changes with the observer.
        internal static ulong ResolveLitWindowMask(CityBuildingAssetRegistry registry,
            BuildingLot lot, int citySeed)
        {
            if (!lot.IsOrdinaryBuilding) return 0;
            uint best = uint.MaxValue;
            int selected = -1;
            foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
            {
                if (slot.OpeningKind != CityBuildingOpeningKind.Window) continue;
                uint hash = CityExteriorAppearance.StableHash(citySeed, lot.Cell.x, lot.Cell.y,
                    slot.Floor, slot.Bay, ResolveSideIndex(slot.Side));
                if (selected >= 0 && hash >= best) continue;
                best = hash;
                selected = slot.Uv2SlotId;
            }
            return selected >= 0 ? 1UL << selected : 0;
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
                    material.SetTexture(
                        BaseMapId,
                        CityWindowAppearance.Texture);
                    material.SetColor(
                        OffColorId,
                        CityExteriorAppearance.WindowOff);
                    material.SetColor(
                        DayColorId,
                        CityWindowAppearance.DayGlass);
                    material.SetColor(
                        WarmColorId,
                        CityExteriorAppearance.WarmWindow);
                    material.SetFloat(
                        EmissionStrengthId,
                        CityWindowAppearance.EmissionStrength);
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

        private static int ResolveRowPaneCount(
            CityBuildingAssetRegistry registry,
            CityBuildingWindowSlot selected)
        {
            int count = 0;
            for (int index = 0;
                 index < registry.WindowSlots.Count;
                 index++)
            {
                CityBuildingWindowSlot candidate =
                    registry.WindowSlots[index];
                if (candidate.Floor == selected.Floor &&
                    string.Equals(
                        candidate.Side,
                        selected.Side,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
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
