using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared visual recipe for the generated City and its bounded Home view.
    /// Keeping these values together prevents the balcony proxy from drifting
    /// away from the same seeded location rendered in City.
    /// </summary>
    internal static class CityExteriorAppearance
    {
        public const string GroundTextureResourcePath =
            "Textures/CityGroundSoilAlbedo";
        public const string RoadTextureResourcePath =
            "Textures/CityRoadAsphaltAlbedo";
        public const string SidewalkTextureResourcePath =
            "Textures/CitySidewalkAlbedo";
        public const string RoadMarkingTextureResourcePath =
            "Textures/CityRoadMarkingAlbedo";
        public const float GroundTextureTileSize = 12f;
        public const float RoadTextureTileSize = 12f;
        public const float SidewalkTextureTileSize = 6f;
        public const float RoadMarkingTextureTileSize = 2f;
        public const float GroundSmoothness = 0.04f;
        public const float RoadSmoothness = 0.10f;
        public const float SidewalkSmoothness = 0.08f;
        public const float RoadMarkingSmoothness = 0.12f;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static Texture2D groundTexture;
        private static Texture2D roadTexture;
        private static Texture2D sidewalkTexture;
        private static Texture2D roadMarkingTexture;

        public static readonly Color Asphalt =
            new Color(0.175f, 0.195f, 0.195f);
        public static readonly Color ParkPath =
            new Color(0.39f, 0.34f, 0.24f);
        public static readonly Color BeachSand =
            new Color(0.52f, 0.45f, 0.30f);
        public static readonly Color Water =
            new Color(0.10f, 0.29f, 0.38f);
        public static readonly Color CemeteryGround =
            new Color(0.15f, 0.20f, 0.16f);
        public static readonly Color YardGround =
            new Color(0.30f, 0.26f, 0.19f);
        public static readonly Color ChurchGround =
            new Color(0.30f, 0.29f, 0.25f);
        public static readonly Color WindowOff =
            new Color(0.025f, 0.035f, 0.040f);
        public static readonly Color ColdWindow =
            new Color(0.24f, 0.43f, 0.56f);
        public static readonly Color WarmWindow =
            new Color(0.88f, 0.48f, 0.20f);
        public static readonly Color BarWindow =
            new Color(1.35f, 0.72f, 0.28f);
        public static readonly Color HomeWindow =
            new Color(0.82f, 1.10f, 1.22f);
        public static readonly Color SupermarketWindow =
            new Color(0.50f, 0.82f, 0.66f);

        public static Texture2D GroundTexture
        {
            get
            {
                return LoadSurfaceTexture(
                    ref groundTexture,
                    GroundTextureResourcePath,
                    "ground");
            }
        }

        public static Texture2D RoadTexture
        {
            get
            {
                return LoadSurfaceTexture(
                    ref roadTexture,
                    RoadTextureResourcePath,
                    "road");
            }
        }

        public static Texture2D SidewalkTexture
        {
            get
            {
                return LoadSurfaceTexture(
                    ref sidewalkTexture,
                    SidewalkTextureResourcePath,
                    "sidewalk");
            }
        }

        public static Texture2D RoadMarkingTexture
        {
            get
            {
                return LoadSurfaceTexture(
                    ref roadMarkingTexture,
                    RoadMarkingTextureResourcePath,
                    "road marking");
            }
        }

        public static void ApplyGroundSurface(Renderer renderer)
        {
            ApplyGroundSurface(renderer, Color.white);
        }

        public static void ApplyGroundSurface(
            Renderer renderer,
            Color dryTint)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySurface(
                renderer,
                GroundTexture,
                GroundSmoothness,
                CityWetSurfaceKind.Ground,
                dryTint);
        }

        public static void ApplyRoadSurface(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySurface(
                renderer,
                RoadTexture,
                RoadSmoothness,
                CityWetSurfaceKind.Road,
                Color.white);
        }

        public static void ApplySidewalkSurface(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySurface(
                renderer,
                SidewalkTexture,
                SidewalkSmoothness,
                CityWetSurfaceKind.Sidewalk,
                Color.white);
        }

        public static void ApplyRoadMarkingSurface(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySurface(
                renderer,
                RoadMarkingTexture,
                RoadMarkingSmoothness,
                CityWetSurfaceKind.RoadMarking,
                Color.white);
        }

        public static void ApplyPuddleSurface(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySurface(
                renderer,
                RoadTexture,
                RoadSmoothness,
                CityWetSurfaceKind.Puddle,
                Color.white);
        }

        private static void ApplySurface(
            Renderer renderer,
            Texture2D texture,
            float smoothness,
            CityWetSurfaceKind wetSurfaceKind,
            Color dryTint)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, texture);
            properties.SetColor(BaseColorId, dryTint);
            properties.SetColor(ColorId, dryTint);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);
            CityWetSurfaceRegistry.Register(
                renderer,
                wetSurfaceKind,
                dryTint);
        }

        private static Texture2D LoadSurfaceTexture(
            ref Texture2D cachedTexture,
            string resourcePath,
            string surfaceName)
        {
            if (cachedTexture == null)
            {
                cachedTexture = Resources.Load<Texture2D>(resourcePath);
            }

            if (cachedTexture == null)
            {
                throw new InvalidOperationException(
                    $"Missing {surfaceName} surface texture " +
                    $"'{resourcePath}'.");
            }

            return cachedTexture;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            groundTexture = null;
            roadTexture = null;
            sidewalkTexture = null;
            roadMarkingTexture = null;
        }

        public static Color CreateNightFacadeColor(
            BuildingLot lot)
        {
            if (lot.IsBar)
            {
                return new Color(
                    lot.Color.r * 0.70f,
                    lot.Color.g * 0.65f,
                    lot.Color.b * 0.68f,
                    1f);
            }

            if (lot.IsPlayerHome)
            {
                return new Color(
                    lot.Color.r * 0.72f,
                    lot.Color.g * 0.78f,
                    lot.Color.b * 0.80f,
                    1f);
            }

            if (lot.IsSupermarket)
            {
                return new Color(
                    lot.Color.r * 0.68f,
                    lot.Color.g * 0.74f,
                    lot.Color.b * 0.62f,
                    1f);
            }

            float value =
                (lot.Color.r +
                 lot.Color.g +
                 lot.Color.b) /
                3f;
            Color tintedValue = Color.Lerp(
                new Color(value, value, value, 1f),
                lot.Color,
                0.32f);
            return new Color(
                tintedValue.r * 0.68f,
                tintedValue.g * 0.73f,
                tintedValue.b * 0.70f,
                1f);
        }

        /// <summary>
        /// Which family a facade pane belongs to, plus the stable hash the
        /// window appearance uses to pick its texture variant. Special
        /// buildings keep their authored family; ordinary buildings read the
        /// deterministic district presentation profile so residential rooms,
        /// industrial work lights and Nightlife ground floors no longer share
        /// one city-wide schedule.
        /// </summary>
        public static CityWindowFamily ResolveWindowFamily(
            BuildingLot lot,
            int citySeed,
            int floor,
            int pane,
            int side,
            out uint paneHash)
        {
            paneHash = StableHash(
                citySeed,
                lot.Cell.x,
                lot.Cell.y,
                floor,
                pane,
                side);
            if (lot.IsBar)
            {
                return CityWindowFamily.Bar;
            }

            if (lot.IsPlayerHome)
            {
                return CityWindowFamily.Home;
            }

            if (lot.IsSupermarket)
            {
                return CityWindowFamily.Supermarket;
            }

            if (!TryGetUrbanWindowProfile(
                    lot.District,
                    out CityDistrictWindowProfile windowProfile))
            {
                return ResolveLegacyWindowFamily(paneHash);
            }

            uint variationKey = CityDistrictPresentationPlanner
                .ResolveWindowVariationKey(
                    citySeed,
                    lot.Cell.x,
                    lot.Cell.y,
                    lot.District);
            int groupSize = 2 + (int)(variationKey % 2u);
            int groupPhase = (int)(
                (variationKey >> 8) % (uint)groupSize);
            int groupPane = (pane + groupPhase) / groupSize;
            uint clusterHash = StableHash(
                citySeed ^ unchecked(
                    (int)variationKey),
                lot.Cell.x,
                lot.Cell.y,
                floor,
                groupPane,
                side);
            uint clusterChoiceHash = Mix(clusterHash, 0x434C5354u);
            bool useApartmentCluster =
                (clusterChoiceHash % 10000u) <
                Mathf.RoundToInt(
                    windowProfile.RhythmRegularity * 10000f);
            uint selectionHash = useApartmentCluster
                ? Mix(clusterHash, 0x53454C45u)
                : paneHash;
            float litRatio = ResolveLitWindowRatio(
                windowProfile,
                floor,
                side);
            if ((selectionHash % 10000u) >=
                Mathf.RoundToInt(litRatio * 10000f))
            {
                return CityWindowFamily.Off;
            }

            uint temperatureHash = Mix(selectionHash, 0x57494E44u);
            return (temperatureHash % 10000u) <
                   Mathf.RoundToInt(windowProfile.WarmShare * 10000f)
                ? CityWindowFamily.Warm
                : CityWindowFamily.Cold;
        }

        private static bool TryGetUrbanWindowProfile(
            CityDistrictKind district,
            out CityDistrictWindowProfile profile)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                case CityDistrictKind.Residential:
                case CityDistrictKind.Industrial:
                case CityDistrictKind.Nightlife:
                    profile = CityDistrictPresentationPlanner
                        .GetProfile(district)
                        .Window;
                    return true;
                default:
                    profile = default;
                    return false;
            }
        }

        private static CityWindowFamily ResolveLegacyWindowFamily(uint hash)
        {
            int selection = (int)(hash % 100u);
            if (selection < 65)
            {
                return CityWindowFamily.Off;
            }

            return selection < 90
                ? CityWindowFamily.Cold
                : CityWindowFamily.Warm;
        }

        private static float ResolveLitWindowRatio(
            CityDistrictWindowProfile profile,
            int floor,
            int side)
        {
            float offset;
            switch (profile.Family)
            {
                case CityDistrictWindowFamily.NarrowIrregular:
                    offset = floor == 0 ? -0.04f : 0.01f;
                    break;
                case CityDistrictWindowFamily.OccupiedClusters:
                    offset = floor == 0 ? -0.06f : 0.02f;
                    break;
                case CityDistrictWindowFamily.SparseUtility:
                    offset = floor == 0 ? -0.04f : 0.01f;
                    break;
                case CityDistrictWindowFamily.DarkUpperActiveBase:
                    // The front threshold is active; the service/rear side
                    // stays quiet so Nightlife does not glow as one volume.
                    offset = floor == 0 && side == 0 ? 0.34f : -0.08f;
                    break;
                default:
                    offset = 0f;
                    break;
            }

            return Mathf.Clamp01(profile.LitWindowRatio + offset);
        }

        public static Color Darken(
            Color color,
            float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            int floor,
            int pane,
            int side)
        {
            uint hash =
                unchecked((uint)seed) ^
                0x9E3779B9u;
            hash = Mix(hash, unchecked((uint)x));
            hash = Mix(hash, unchecked((uint)z));
            hash = Mix(hash, unchecked((uint)floor));
            hash = Mix(hash, unchecked((uint)pane));
            return Mix(hash, unchecked((uint)side));
        }

        internal static uint Mix(
            uint first,
            uint second)
        {
            uint hash = first;
            hash ^=
                second +
                0x85EBCA6Bu +
                (hash << 6) +
                (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u
                ? 0xA341316Cu
                : hash;
        }
    }
}
