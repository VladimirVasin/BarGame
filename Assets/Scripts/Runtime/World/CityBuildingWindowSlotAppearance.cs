using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Binds the window-slot IDs authored into the combined Blender glass
    /// mesh to a guaranteed warm pane, one optional timed pane and dark variants. UV2
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
            ulong guaranteed = ResolveLitWindowMask(registry, lot, citySeed);
            registry.GuaranteedWindowMask = guaranteed;
            registry.OptionalWindowMask = ResolveOptionalWindowMask(registry, lot, citySeed, guaranteed);
            registry.WindowScheduleSeed = CityExteriorAppearance.StableHash(citySeed, lot.Cell.x, lot.Cell.y,
                Mathf.RoundToInt(lot.Center.x * 100f), Mathf.RoundToInt(lot.Center.z * 100f), lot.BuildingVariant) ^ 0x9e3779b9u;
            ulong litWindowMask = ResolveTimedMask(registry, AbsoluteMinutes);
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
            registry.WindowGlassRenderer = renderer;
            registry.WindowStateBuffer = states;
            renderer.sharedMaterial = Material;
            var properties = new MaterialPropertyBlock();
            registry.WindowProperties = properties;
            renderer.GetPropertyBlock(properties);
            properties.SetFloatArray(WindowStatesId, states);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        internal static double AbsoluteMinutes =>
            GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes;

        internal static ulong ResolveTimedMask(CityBuildingAssetRegistry registry, double absoluteMinutes) =>
            registry.GuaranteedWindowMask | (registry.OptionalWindowMask != 0 && OptionalPaneIsOn(absoluteMinutes, registry.WindowScheduleSeed)
                ? registry.OptionalWindowMask : 0UL);

        // Each four-hour block owns one independently seeded occupancy interval.
        // The surrounding off gaps and the on interval vary without replaying
        // history after a time jump, checkpoint restore or exterior reconstruction.
        internal static bool OptionalPaneIsOn(double absoluteMinutes, uint seed)
        {
            GetOptionalInterval(absoluteMinutes, seed, out double start, out double end);
            return absoluteMinutes >= start && absoluteMinutes < end;
        }

        internal static void GetOptionalInterval(double absoluteMinutes, uint seed, out double start, out double end)
        {
            if (double.IsNaN(absoluteMinutes) || double.IsInfinity(absoluteMinutes) || absoluteMinutes < 0d)
            { start = end = double.PositiveInfinity; return; }
            double block = Math.Floor(absoluteMinutes / 240d);
            uint hash = CityCoveredPassagePlan.Mix(seed ^ unchecked((uint)(long)block) * 0x85ebca6bu);
            start = block * 240d + (hash & 0xffffu) / 65535d * 120d;
            uint duration = CityCoveredPassagePlan.Mix(hash ^ 0xc2b2ae35u);
            end = start + 20d + (duration & 0xffffu) / 65535d * 30d;
        }

        internal static void RefreshTimedMask(CityBuildingAssetRegistry registry, double absoluteMinutes)
        {
            if (registry.GuaranteedWindowMask == 0 || registry.WindowGlassRenderer == null ||
                registry.WindowStateBuffer == null || registry.WindowProperties == null) return;
            ulong mask = ResolveTimedMask(registry, absoluteMinutes);
            if (mask == registry.LitWindowMask) return;
            foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
            {
                int index = slot.Uv2SlotId;
                int variant = (int)registry.WindowStateBuffer[index] % CityWindowAppearance.VariantCount;
                registry.WindowStateBuffer[index] = variant +
                    ((mask & (1UL << index)) != 0 ? (int)CityWindowFamily.Warm * CityWindowAppearance.VariantCount : 0);
            }
            registry.WindowGlassRenderer.GetPropertyBlock(registry.WindowProperties);
            registry.WindowProperties.SetFloatArray(WindowStatesId, registry.WindowStateBuffer);
            registry.WindowGlassRenderer.SetPropertyBlock(registry.WindowProperties);
            registry.LitWindowMask = mask;
        }

        private static ulong ResolveOptionalWindowMask(CityBuildingAssetRegistry registry, BuildingLot lot,
            int citySeed, ulong guaranteed)
        {
            if (!lot.IsOrdinaryBuilding || lot.District != CityDistrictKind.Residential) return 0;
            uint best = uint.MaxValue;
            int selected = -1;
            foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
            {
                if (slot.OpeningKind != CityBuildingOpeningKind.Window || (guaranteed & (1UL << slot.Uv2SlotId)) != 0) continue;
                uint hash = CityExteriorAppearance.StableHash(citySeed ^ 0x25be13, lot.Cell.x, lot.Cell.y,
                    slot.Floor, slot.Bay, ResolveSideIndex(slot.Side));
                if (selected >= 0 && hash >= best) continue;
                best = hash; selected = slot.Uv2SlotId;
            }
            return selected >= 0 ? 1UL << selected : 0;
        }

        // The minimum belongs to the street-facing facade. A random rear or
        // gable pane can exist in the mask while the whole public front stays
        // dark. Floor and bay still vary stably within the authored front panes.
        internal static ulong ResolveLitWindowMask(CityBuildingAssetRegistry registry,
            BuildingLot lot, int citySeed)
        {
            if (!lot.IsOrdinaryBuilding) return 0;
            uint best = uint.MaxValue;
            int selected = -1;
            foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
            {
                if (slot.OpeningKind != CityBuildingOpeningKind.Window ||
                    !string.Equals(slot.Side, "Front", StringComparison.Ordinal)) continue;
                uint hash = CityExteriorAppearance.StableHash(citySeed, lot.Cell.x, lot.Cell.y,
                    slot.Floor, slot.Bay, ResolveSideIndex(slot.Side));
                if (selected >= 0 && hash >= best) continue;
                best = hash;
                selected = slot.Uv2SlotId;
            }
            if (selected < 0)
                throw new InvalidOperationException($"Ordinary building '{registry.StableId}' has no street-facing window.");
            return 1UL << selected;
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
