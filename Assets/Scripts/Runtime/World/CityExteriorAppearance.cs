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
            CityNightAtmosphere.StreetLampColor;
        public static readonly Color WarmWindow =
            CityNightAtmosphere.StreetLampColor;
        public static readonly Color BarWindow =
            CityNightAtmosphere.StreetLampColor;
        public static readonly Color HomeWindow =
            CityNightAtmosphere.StreetLampColor;
        public static readonly Color SupermarketWindow =
            CityNightAtmosphere.StreetLampColor;

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

        /// <summary>
        /// What a district's soil has taken on from the district. This
        /// is the first consumer of <see cref="CityDistrictArtProfile"/>'s
        /// wear channel, which was authored long ago and read by nothing:
        /// the family says what the dirt here is made of and the amount
        /// says how much of it there is.
        ///
        /// The cast stays small on purpose. It has to be enough that the
        /// four districts separate in grayscale — the art bible tests
        /// for that — and small enough that the ground never becomes a
        /// coloured floor. Nothing here is a seam risk: the buildable
        /// surfaces are cut on `26 m` cell edges, which run down the
        /// middle of every street, so one district's soil meets the
        /// next under four metres of asphalt.
        /// </summary>
        public static Color ResolveDistrictGroundTint(
            CityDistrictWearProfile wear)
        {
            Color cast;
            switch (wear.Family)
            {
                case CityDistrictWearFamily.SootWaterAndPatch:
                    // Old Town: brick dust and soot washed down.
                    cast = new Color(1.00f, 0.955f, 0.905f);
                    break;
                case CityDistrictWearFamily.RepairAndPersonalUse:
                    // Residential: swept, greyer, colder.
                    cast = new Color(0.955f, 0.975f, 1.00f);
                    break;
                case CityDistrictWearFamily.RustAndProcess:
                    // Industrial: the darkest soil in the city.
                    cast = new Color(0.965f, 0.905f, 0.845f);
                    break;
                default:
                    // Nightlife: violet-cool, and never quite dry.
                    cast = new Color(0.955f, 0.940f, 1.00f);
                    break;
            }

            float amount = Mathf.Clamp01(wear.Amount);
            return Color.Lerp(Color.white, cast, amount);
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
        /// window appearance uses to pick its texture variant. Every row gets
        /// an exact district-sized share of lit panes, phase-shifted per floor
        /// and facade so light reaches the whole building without turning it
        /// into a full glowing grid. Every selected pane uses the same warm
        /// street-lamp colour and remains on at every hour.
        /// </summary>
        public static CityWindowFamily ResolveWindowFamily(
            BuildingLot lot,
            int citySeed,
            int floor,
            int pane,
            int paneCount,
            int side,
            out uint paneHash)
        {
            if (paneCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(paneCount),
                    paneCount,
                    "A facade row must contain at least one pane.");
            }

            if (pane < 0 || pane >= paneCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pane),
                    pane,
                    "The pane index must belong to its facade row.");
            }

            paneHash = StableHash(
                citySeed,
                lot.Cell.x,
                lot.Cell.y,
                floor,
                pane,
                side);
            float litRatio = TryGetUrbanWindowProfile(
                lot.District,
                out CityDistrictWindowProfile windowProfile)
                ? windowProfile.LitWindowRatio
                : 0.28f;
            if (!IsEvenlySelectedLitPane(
                    lot,
                    citySeed,
                    floor,
                    pane,
                    paneCount,
                    side,
                    litRatio))
            {
                return CityWindowFamily.Off;
            }

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

            return CityWindowFamily.Warm;
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

        private static bool IsEvenlySelectedLitPane(
            BuildingLot lot,
            int citySeed,
            int floor,
            int pane,
            int paneCount,
            int side,
            float litRatio)
        {
            int maximumLit = paneCount > 1 ? paneCount - 1 : 1;
            int litCount = Mathf.Clamp(
                Mathf.RoundToInt(paneCount * litRatio),
                1,
                maximumLit);
            uint rowHash = StableHash(
                citySeed,
                lot.Cell.x,
                lot.Cell.y,
                floor,
                0,
                side);
            uint variationKey = CityDistrictPresentationPlanner
                .ResolveWindowVariationKey(
                    citySeed,
                    lot.Cell.x,
                    lot.Cell.y,
                    lot.District);
            int phase = (int)(Mix(rowHash, variationKey) %
                (uint)paneCount);
            int shiftedPane = (pane + phase) % paneCount;
            int previousBand =
                (shiftedPane * litCount) / paneCount;
            int nextBand =
                ((shiftedPane + 1) * litCount) / paneCount;
            return nextBand > previousBand;
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
