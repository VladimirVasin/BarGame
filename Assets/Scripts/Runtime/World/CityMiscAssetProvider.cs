using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Blender-authored City misc assembly families. This catalog is separate
    /// from CityDecorationKind because later waves also cover park and night
    /// infrastructure that is not owned by CityDecorationPlan.
    /// </summary>
    public enum CityMiscKind
    {
        IndustrialStacksAndTanks = 0,
        IndustrialCargo = 1,
        NightlifeVendingAndQueue = 2,
        RoadsideRoadworkAndBicycle = 3,
        ParkTree = 4,
        ParkBench = 5,
        RoadsidePhoneBooth = 6,
        RoadsideDumpsterAndUtility = 7,
        StreetLampShell = 8,
        OldTownChimneysAndDormers = 9,
        OldTownScaffolding = 10,
        OldTownStreetMarket = 11,
        OldTownClockTower = 12,
        ResidentialBalconies = 13,
        ResidentialLaundryAndAntenna = 14,
        ResidentialDiscardedFurniture = 15,
        ResidentialRooftopGreenhouse = 16,
        IndustrialPipeRack = 17,
        IndustrialGantry = 18,
        NightlifeBillboard = 19,
        NightlifeFireEscape = 20,
        NightlifeCinema = 21,
        ParkFountainAndStatue = 22,
        ParkBandstand = 23,
        ParkChessTables = 24,
        ParkPlayground = 25,
        Route01ShelterShell = 26,
        Route01PoleShell = 27,
        TrafficSignalShell = 28,
        YardDeadTree = 29,
        YardBench = 30,
        YardCarpetFrame = 31,
        YardSandpit = 32,
        YardChildToy = 33,
        YardDeadLamp = 34,
        YardBin = 35,
        YardBottle = 36,
        YardSpotlightWallMount = 37,
        YardSpotlightHeadShell = 38,
        CemeteryGraveSlab = 39,
        CemeteryGraveMonument = 40,
        CemeteryOvergrownMound = 41,
        CemeteryGraveEnclosure = 42,
        CemeteryGraveOffering = 43,
        CemeteryTree = 44,
        CemeteryBush = 45,
        CemeteryBench = 46,
        SeacoastBoat = 47,
        SeacoastOar = 48,
        SeacoastSlipwayBarrier = 49,
        SeacoastBarge = 50,
        SeacoastDriftwood = 51,
        FringeUtilityPole = 52,
        FringeRepairStock = 53,
        FringePipeStock = 54,
        FringeUtilityShedShell = 55,
        FringeFloodGaugeShell = 56,
        PoiOldTownWaterworksShell = 57,
        PoiResidentialDryingYardShell = 58,
        PoiIndustrialWeighbridgeShell = 59,
        PoiNightlifeLastRouteIslandShell = 60,
        BarBuildingShell = 61,
        SupermarketBuildingShell = 62,
        PlayerHomeBuildingShell = 63,
        RoadsideDrainAndCover = 64,
        RoadsideCappedStandpipe = 65,
        LotGroundDownpipeOutfall = 66,
        ChurchCourtyardSurface = 67,
        ChurchCourtyardShrub = 68,
        ChurchCourtyardFlowerBed = 69,
        CemeteryFencePost = 70,
        CemeteryFenceRail = 71,
        NightlifeArchBridgeShell = 72,
        NightlifeBurnBarrel = 73,
        NightlifeShelterBedding = 74,
        NightlifeShelterClutter = 75,
        NightlifeShelterFire = 76,
        NightlifeShelterStandingPerson = 77,
        NightlifeShelterSeatedPerson = 78,
        NightlifeShelterSleepingPerson = 79
    }

    /// <summary>
    /// Semantic material role of one passive imported mesh. Unity continues
    /// to own the shared material and tint selected for each role.
    /// </summary>
    public enum CityMiscMeshRole
    {
        Industrial = 0,
        Street = 1,
        Masonry = 2,
        Neon = 3,
        Bark = 4,
        Foliage = 5,
        Timber = 6,
        Residential = 7,
        BacklitSign = 8,
        Fixture = 9
    }

    public enum CityMiscSurfaceKind
    {
        Default = 0,
        Stone = 1,
        Timber = 2,
        PaintedMetal = 3,
        Gravel = 4,
        Lawn = 5
    }

    /// <summary>
    /// Serializable binding used by the provider asset. One flat table keeps
    /// future misc waves additive instead of requiring one serialized field
    /// for every authored part.
    /// </summary>
    [Serializable]
    public sealed class CityMiscMeshEntry
    {
        [SerializeField] private CityMiscKind kind;
        [SerializeField] private int variant;
        [SerializeField] private string component = string.Empty;
        [SerializeField] private CityMiscMeshRole role;
        [SerializeField] private CityMiscSurfaceKind surface;
        [SerializeField] private Mesh mesh;

        public CityMiscKind Kind => kind;
        public int Variant => variant;
        public string Component => component;
        public CityMiscMeshRole Role => role;
        public CityMiscSurfaceKind Surface => surface;
        public Mesh Mesh => mesh;
    }

    public readonly struct CityMiscMeshPart
    {
        internal CityMiscMeshPart(
            CityMiscKind kind,
            int variant,
            string component,
            CityMiscMeshRole role,
            CityMiscSurfaceKind surface,
            Mesh mesh)
        {
            Kind = kind;
            Variant = variant;
            Component = component;
            Role = role;
            Surface = surface;
            Mesh = mesh;
        }

        public CityMiscKind Kind { get; }
        public int Variant { get; }
        public string Component { get; }
        public CityMiscMeshRole Role { get; }
        public CityMiscSurfaceKind Surface { get; }
        public Mesh Mesh { get; }
    }

    /// <summary>
    /// Resources bridge from the deterministic Blender City misc library to
    /// runtime composition. The imported meshes are fixed-metre, passive
    /// geometry; plans still own transforms, semantics and collision.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CityMiscAssetProvider",
        menuName = "Bar Promenade/City Misc Asset Provider")]
    public sealed class CityMiscAssetProvider : ScriptableObject
    {
        public const string ResourcePath = "City/CityMiscAssetProvider";
        public const string GeneratorVersion = "4.6.0";
        public const string DesignId = "city_misc_citywide_v4";
        public const int ExpectedAssemblyCount = 115;
        public const int ExpectedMeshCount = 238;
        public const int SupportedKindCount = 80;

        private const float GroundTolerance = 0.003f;

        private static readonly CityMiscKind[] SupportedKindCatalog =
        {
            CityMiscKind.IndustrialStacksAndTanks,
            CityMiscKind.IndustrialCargo,
            CityMiscKind.NightlifeVendingAndQueue,
            CityMiscKind.RoadsideRoadworkAndBicycle,
            CityMiscKind.ParkTree,
            CityMiscKind.ParkBench,
            CityMiscKind.RoadsidePhoneBooth,
            CityMiscKind.RoadsideDumpsterAndUtility,
            CityMiscKind.StreetLampShell,
            CityMiscKind.OldTownChimneysAndDormers,
            CityMiscKind.OldTownScaffolding,
            CityMiscKind.OldTownStreetMarket,
            CityMiscKind.OldTownClockTower,
            CityMiscKind.ResidentialBalconies,
            CityMiscKind.ResidentialLaundryAndAntenna,
            CityMiscKind.ResidentialDiscardedFurniture,
            CityMiscKind.ResidentialRooftopGreenhouse,
            CityMiscKind.IndustrialPipeRack,
            CityMiscKind.IndustrialGantry,
            CityMiscKind.NightlifeBillboard,
            CityMiscKind.NightlifeFireEscape,
            CityMiscKind.NightlifeCinema,
            CityMiscKind.ParkFountainAndStatue,
            CityMiscKind.ParkBandstand,
            CityMiscKind.ParkChessTables,
            CityMiscKind.ParkPlayground,
            CityMiscKind.Route01ShelterShell,
            CityMiscKind.Route01PoleShell,
            CityMiscKind.TrafficSignalShell,
            CityMiscKind.YardDeadTree,
            CityMiscKind.YardBench,
            CityMiscKind.YardCarpetFrame,
            CityMiscKind.YardSandpit,
            CityMiscKind.YardChildToy,
            CityMiscKind.YardDeadLamp,
            CityMiscKind.YardBin,
            CityMiscKind.YardBottle,
            CityMiscKind.YardSpotlightWallMount,
            CityMiscKind.YardSpotlightHeadShell,
            CityMiscKind.CemeteryGraveSlab,
            CityMiscKind.CemeteryGraveMonument,
            CityMiscKind.CemeteryOvergrownMound,
            CityMiscKind.CemeteryGraveEnclosure,
            CityMiscKind.CemeteryGraveOffering,
            CityMiscKind.CemeteryTree,
            CityMiscKind.CemeteryBush,
            CityMiscKind.CemeteryBench,
            CityMiscKind.SeacoastBoat,
            CityMiscKind.SeacoastOar,
            CityMiscKind.SeacoastSlipwayBarrier,
            CityMiscKind.SeacoastBarge,
            CityMiscKind.SeacoastDriftwood,
            CityMiscKind.FringeUtilityPole,
            CityMiscKind.FringeRepairStock,
            CityMiscKind.FringePipeStock,
            CityMiscKind.FringeUtilityShedShell,
            CityMiscKind.FringeFloodGaugeShell,
            CityMiscKind.PoiOldTownWaterworksShell,
            CityMiscKind.PoiResidentialDryingYardShell,
            CityMiscKind.PoiIndustrialWeighbridgeShell,
            CityMiscKind.PoiNightlifeLastRouteIslandShell,
            CityMiscKind.BarBuildingShell,
            CityMiscKind.SupermarketBuildingShell,
            CityMiscKind.PlayerHomeBuildingShell,
            CityMiscKind.RoadsideDrainAndCover,
            CityMiscKind.RoadsideCappedStandpipe,
            CityMiscKind.LotGroundDownpipeOutfall,
            CityMiscKind.ChurchCourtyardSurface,
            CityMiscKind.ChurchCourtyardShrub,
            CityMiscKind.ChurchCourtyardFlowerBed,
            CityMiscKind.CemeteryFencePost,
            CityMiscKind.CemeteryFenceRail,
            CityMiscKind.NightlifeArchBridgeShell,
            CityMiscKind.NightlifeBurnBarrel,
            CityMiscKind.NightlifeShelterBedding,
            CityMiscKind.NightlifeShelterClutter,
            CityMiscKind.NightlifeShelterFire,
            CityMiscKind.NightlifeShelterStandingPerson,
            CityMiscKind.NightlifeShelterSeatedPerson,
            CityMiscKind.NightlifeShelterSleepingPerson
        };

        private static readonly ExpectedPartSpec[] IndustrialStreetParts =
        {
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[]
            IndustrialStreetMasonryParts =
        {
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Street", CityMiscMeshRole.Street),
            P("Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[] StreetIndustrialNeonParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Neon", CityMiscMeshRole.Neon)
        };

        private static readonly ExpectedPartSpec[]
            StreetMasonryIndustrialParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Masonry", CityMiscMeshRole.Masonry),
            P("Industrial", CityMiscMeshRole.Industrial)
        };

        private static readonly ExpectedPartSpec[] BarkFoliageParts =
        {
            P("Bark", CityMiscMeshRole.Bark),
            P("Foliage", CityMiscMeshRole.Foliage)
        };

        private static readonly ExpectedPartSpec[] TimberPart =
        {
            P("Timber", CityMiscMeshRole.Timber)
        };

        private static readonly ExpectedPartSpec[]
            StreetResidentialBacklitParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential),
            P("BacklitSign", CityMiscMeshRole.BacklitSign)
        };

        private static readonly ExpectedPartSpec[] FixturePart =
        {
            P("Fixture", CityMiscMeshRole.Fixture)
        };

        private static readonly ExpectedPartSpec[] ChimneyParts =
        {
            P("Chimneys_Masonry", CityMiscMeshRole.Masonry),
            P("Dormer_Masonry", CityMiscMeshRole.Masonry),
            P("Window_Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[] StreetMasonryResidentialParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Masonry", CityMiscMeshRole.Masonry),
            P("Residential", CityMiscMeshRole.Residential)
        };

        private static readonly ExpectedPartSpec[] MasonryStreetResidentialParts =
        {
            P("Masonry", CityMiscMeshRole.Masonry),
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential)
        };

        private static readonly ExpectedPartSpec[] ResidentialStreetParts =
        {
            P("Residential", CityMiscMeshRole.Residential),
            P("Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[] StreetResidentialMasonryParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential),
            P("Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[] ResidentialStreetMasonryParts =
        {
            P("Residential", CityMiscMeshRole.Residential),
            P("Street", CityMiscMeshRole.Street),
            P("Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[] GreenhouseParts =
        {
            P("Base_Masonry", CityMiscMeshRole.Masonry),
            P("Frame_Residential", CityMiscMeshRole.Residential),
            P("Roof_Residential", CityMiscMeshRole.Residential),
            P("Hardware_Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[] StreetIndustrialParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Industrial", CityMiscMeshRole.Industrial)
        };

        private static readonly ExpectedPartSpec[] StreetNeonParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Neon", CityMiscMeshRole.Neon)
        };

        private static readonly ExpectedPartSpec[] IndustrialStreetOnlyParts =
        {
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[] IndustrialMasonryParts =
        {
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[] StreetNeonMasonryParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Neon", CityMiscMeshRole.Neon),
            P("Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[] FountainParts =
        {
            P("Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("Street_Stone", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.Stone)
        };

        private static readonly ExpectedPartSpec[] BandstandParts =
        {
            P("Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("Masonry_Timber", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Timber),
            P("Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[] ChessParts =
        {
            P("TableSlab_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("BoardLight_Masonry_Timber", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Timber),
            P("BoardDarkAndRim_Street_Timber", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.Timber),
            P("TableFooting_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("TablePedestal_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("BenchSeat_Street_Timber", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.Timber),
            P("BenchPad_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("BenchLeg_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone)
        };

        private static readonly ExpectedPartSpec[] PlaygroundParts =
        {
            P("Residential_PaintedMetal", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.PaintedMetal),
            P("Masonry_Timber", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Timber),
            P("Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[] FixtureTimberParts =
        {
            P("Fixture", CityMiscMeshRole.Fixture),
            P("Timber", CityMiscMeshRole.Timber)
        };
        private static readonly ExpectedPartSpec[] FixtureStreetResidentialParts =
        {
            P("Fixture", CityMiscMeshRole.Fixture),
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential)
        };
        private static readonly ExpectedPartSpec[] BarkPart =
        {
            P("Bark", CityMiscMeshRole.Bark)
        };
        private static readonly ExpectedPartSpec[] TimberFixtureParts =
        {
            P("Timber", CityMiscMeshRole.Timber),
            P("Fixture", CityMiscMeshRole.Fixture)
        };
        private static readonly ExpectedPartSpec[] ResidentialPart =
        {
            P("Residential", CityMiscMeshRole.Residential)
        };
        private static readonly ExpectedPartSpec[] MasonryPart =
        {
            P("Masonry", CityMiscMeshRole.Masonry)
        };
        private static readonly ExpectedPartSpec[] StreetResidentialParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential)
        };
        private static readonly ExpectedPartSpec[] MasonryStreetParts =
        {
            P("Masonry", CityMiscMeshRole.Masonry),
            P("Street", CityMiscMeshRole.Street)
        };
        private static readonly ExpectedPartSpec[] StreetMasonryParts =
        {
            P("Street", CityMiscMeshRole.Street),
            P("Masonry", CityMiscMeshRole.Masonry)
        };
        private static readonly ExpectedPartSpec[] FoliagePart =
        {
            P("Foliage", CityMiscMeshRole.Foliage)
        };
        private static readonly ExpectedPartSpec[] ResidentialStreetTimberParts =
        {
            P("Residential", CityMiscMeshRole.Residential),
            P("Street", CityMiscMeshRole.Street),
            P("Timber", CityMiscMeshRole.Timber)
        };
        private static readonly ExpectedPartSpec[] StreetPart =
        {
            P("Street", CityMiscMeshRole.Street)
        };
        private static readonly ExpectedPartSpec[] IndustrialFixtureParts =
        {
            P("Industrial", CityMiscMeshRole.Industrial),
            P("Fixture", CityMiscMeshRole.Fixture)
        };
        private static readonly ExpectedPartSpec[] DryingYardParts =
        {
            P("Residential_PaintedMetal", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.PaintedMetal),
            P("Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber)
        };
        private static readonly ExpectedPartSpec[] IndustrialStreetResidentialTimberParts =
        {
            P("Masonry", CityMiscMeshRole.Masonry),
            P("Street", CityMiscMeshRole.Street),
            P("Residential", CityMiscMeshRole.Residential),
            P("Timber", CityMiscMeshRole.Timber)
        };
        private static readonly ExpectedPartSpec[] SpecialBuildingParts =
        {
            P("Shell_Masonry", CityMiscMeshRole.Masonry),
            P("Roof_Street", CityMiscMeshRole.Street),
            P("Trim_Industrial", CityMiscMeshRole.Industrial)
        };

        private static readonly ExpectedPartSpec[] CourtyardStoneSurfacePart =
        {
            P("Surface_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone)
        };

        private static readonly ExpectedPartSpec[] CourtyardGravelSurfacePart =
        {
            P("Surface_Street_Gravel", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.Gravel)
        };

        private static readonly ExpectedPartSpec[] CourtyardLawnSurfacePart =
        {
            P("Surface_Foliage_Lawn", CityMiscMeshRole.Foliage,
                CityMiscSurfaceKind.Lawn)
        };

        private static readonly ExpectedPartSpec[] CourtyardFlowerBedParts =
        {
            P("Edging_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("Foliage", CityMiscMeshRole.Foliage),
            P("Flowers_Residential", CityMiscMeshRole.Residential)
        };

        private static readonly ExpectedPartSpec[]
            NightlifeArchBridgeShellParts =
        {
            P("Shell_Masonry", CityMiscMeshRole.Masonry),
            P("StepsAndRetaining_Masonry", CityMiscMeshRole.Masonry),
            P("PlatformSupport_Masonry", CityMiscMeshRole.Masonry),
            P("PlatformSlab_Street", CityMiscMeshRole.Street),
            P("Cladding_Industrial", CityMiscMeshRole.Industrial),
            P("Roof_Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[] NightlifeBurnBarrelParts =
        {
            P("Barrel_Industrial", CityMiscMeshRole.Industrial),
            P("Fuel_Timber", CityMiscMeshRole.Timber)
        };

        private static readonly ExpectedPartSpec[]
            NightlifeShelterMattressParts =
        {
            P("Mattress_Residential", CityMiscMeshRole.Residential),
            P("Blanket_Street", CityMiscMeshRole.Street),
            P("Cardboard_Timber", CityMiscMeshRole.Timber)
        };

        private static readonly ExpectedPartSpec[]
            NightlifeShelterRollParts =
        {
            P("Roll_Residential", CityMiscMeshRole.Residential),
            P("Tie_Street", CityMiscMeshRole.Street),
            P("Cardboard_Timber", CityMiscMeshRole.Timber)
        };

        private static readonly ExpectedPartSpec[] NightlifeShelterClutterParts =
        {
            P("CrateAndCardboard_Timber", CityMiscMeshRole.Timber),
            P("Bags_Street", CityMiscMeshRole.Street),
            P("Bottles_Residential", CityMiscMeshRole.Residential),
            P("Can_Industrial", CityMiscMeshRole.Industrial)
        };

        private static readonly ExpectedPartSpec[] NightlifeShelterFireParts =
        {
            P("FlameCore_Neon", CityMiscMeshRole.Neon),
            P("FlameOuter_Neon", CityMiscMeshRole.Neon),
            P("FlameLeftTongue_Neon", CityMiscMeshRole.Neon),
            P("FlameRightTongue_Neon", CityMiscMeshRole.Neon),
            P("EmberBed_Neon", CityMiscMeshRole.Neon),
            P("GroundSpill_BacklitSign", CityMiscMeshRole.BacklitSign)
        };

        private static readonly ExpectedPartSpec[] NightlifeShelterPersonParts =
        {
            P("Outerwear_Street", CityMiscMeshRole.Street),
            P("Layer_Residential", CityMiscMeshRole.Residential),
            P("Skin_Masonry", CityMiscMeshRole.Masonry)
        };

        private static readonly ExpectedPartSpec[]
            NightlifeShelterSleepingPersonParts =
        {
            P("Outerwear_Street", CityMiscMeshRole.Street),
            P("BreathingUpper_Residential", CityMiscMeshRole.Residential),
            P("Skin_Masonry", CityMiscMeshRole.Masonry)
        };

        [SerializeField] private CityMiscMeshEntry[] entries =
            Array.Empty<CityMiscMeshEntry>();
        [SerializeField] private string buildSignature = string.Empty;

        public string BuildSignature => buildSignature;

        public bool HasCompleteMeshes
        {
            get
            {
                try
                {
                    ValidateEntriesOrThrow(validateGeometry: false);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public static CityMiscAssetProvider Load()
        {
            return Resources.Load<CityMiscAssetProvider>(ResourcePath);
        }

        public static CityMiscAssetProvider LoadOrThrow()
        {
            CityMiscAssetProvider provider = Load();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Missing City misc provider at Resources/" +
                    $"{ResourcePath}.");
            }

            provider.ValidateOrThrow();
            return provider;
        }

        public void ValidateOrThrow()
        {
            ValidateEntriesOrThrow(validateGeometry: true);
            if (!IsSha256(buildSignature))
            {
                throw new InvalidOperationException(
                    "The City misc provider has no valid SHA-256 build " +
                    "signature.");
            }
        }

        public static bool Supports(CityMiscKind kind)
        {
            int value = (int)kind;
            return value >= 0 && value < SupportedKindCount;
        }

        public static CityMiscKind GetSupportedKind(int index)
        {
            RequireIndex(index, SupportedKindCatalog.Length, nameof(index));
            return SupportedKindCatalog[index];
        }

        public static int GetVariantCount(CityMiscKind kind)
        {
            switch (kind)
            {
                case CityMiscKind.IndustrialCargo:
                case CityMiscKind.RoadsideRoadworkAndBicycle:
                case CityMiscKind.ParkBench:
                case CityMiscKind.OldTownChimneysAndDormers:
                case CityMiscKind.ResidentialBalconies:
                case CityMiscKind.ResidentialDiscardedFurniture:
                case CityMiscKind.IndustrialGantry:
                case CityMiscKind.NightlifeCinema:
                case CityMiscKind.CemeteryTree:
                case CityMiscKind.FringePipeStock:
                case CityMiscKind.FringeFloodGaugeShell:
                case CityMiscKind.NightlifeShelterBedding:
                    return 2;
                case CityMiscKind.ParkTree:
                case CityMiscKind.SeacoastBoat:
                    return 4;
                case CityMiscKind.CemeteryGraveSlab:
                    return 6;
                case CityMiscKind.CemeteryGraveMonument:
                    return 5;
                case CityMiscKind.SeacoastDriftwood:
                case CityMiscKind.FringeRepairStock:
                    return 3;
                case CityMiscKind.ChurchCourtyardSurface:
                    return 3;
                case CityMiscKind.ChurchCourtyardShrub:
                case CityMiscKind.ChurchCourtyardFlowerBed:
                    return 2;
                default:
                    if (Supports(kind))
                    {
                        return 1;
                    }

                    throw UnsupportedKind(kind);
            }
        }

        public static float GetExpectedAssemblyMinimumY(
            CityMiscKind kind,
            int variant)
        {
            RequireIndex(
                variant,
                GetVariantCount(kind),
                nameof(variant));
            switch (kind)
            {
                case CityMiscKind.ResidentialBalconies:
                    return 1.67f;
                case CityMiscKind.YardSpotlightWallMount:
                    return -0.21f;
                case CityMiscKind.YardSpotlightHeadShell:
                    return -0.16f;
                case CityMiscKind.CemeteryGraveMonument:
                    switch (variant)
                    {
                        case 0:
                            return 0.15f;
                        case 1:
                            return 0.14f;
                        case 2:
                            return 0.12f;
                        case 3:
                            return 0.14f;
                        default:
                            return 0.16f;
                    }
                case CityMiscKind.CemeteryOvergrownMound:
                    return 0.08f;
                case CityMiscKind.CemeteryGraveOffering:
                    return 0.160089f;
                case CityMiscKind.SeacoastOar:
                    return -0.430033f;
                case CityMiscKind.SeacoastSlipwayBarrier:
                    return -0.08f;
                case CityMiscKind.SeacoastBarge:
                    return -1.599986f;
                case CityMiscKind.PoiIndustrialWeighbridgeShell:
                    return 0.05f;
                default:
                    return 0f;
            }
        }

        public static int GetPartCount(CityMiscKind kind)
        {
            return GetExpectedParts(kind, 0).Length;
        }

        public static CityMiscMeshRole GetExpectedRole(
            CityMiscKind kind,
            int partIndex)
        {
            return GetExpectedRole(kind, 0, partIndex);
        }

        public static CityMiscMeshRole GetExpectedRole(
            CityMiscKind kind, int variant, int partIndex)
        {
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            RequireIndex(partIndex, parts.Length, nameof(partIndex));
            return parts[partIndex].Role;
        }

        public static string GetExpectedComponent(
            CityMiscKind kind,
            int partIndex)
        {
            return GetExpectedComponent(kind, 0, partIndex);
        }

        public static string GetExpectedComponent(
            CityMiscKind kind, int variant, int partIndex)
        {
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            RequireIndex(partIndex, parts.Length, nameof(partIndex));
            return parts[partIndex].Component;
        }

        public static CityMiscSurfaceKind GetExpectedSurface(
            CityMiscKind kind,
            int partIndex)
        {
            return GetExpectedSurface(kind, 0, partIndex);
        }

        public static CityMiscSurfaceKind GetExpectedSurface(
            CityMiscKind kind, int variant, int partIndex)
        {
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            RequireIndex(partIndex, parts.Length, nameof(partIndex));
            return parts[partIndex].Surface;
        }

        public static string GetExpectedMeshName(
            CityMiscKind kind,
            int variant,
            int partIndex)
        {
            return GetExpectedMeshName(
                kind,
                variant,
                GetExpectedComponent(kind, variant, partIndex));
        }

        public static string GetExpectedMeshName(
            CityMiscKind kind,
            int variant,
            CityMiscMeshRole role)
        {
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            int found = -1;
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Role != role)
                {
                    continue;
                }

                if (found >= 0)
                {
                    throw new InvalidOperationException(
                        $"Role {role} is ambiguous for {kind}. Select the " +
                        "part by index or component.");
                }

                found = index;
            }

            if (found < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(role),
                    role,
                    $"Role {role} is not authored for {kind}.");
            }

            return GetExpectedMeshName(
                kind,
                variant,
                parts[found].Component);
        }

        public static string GetExpectedMeshName(
            CityMiscKind kind,
            int variant,
            string component)
        {
            RequireIndex(
                variant,
                GetVariantCount(kind),
                nameof(variant));
            bool found = false;
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            for (int index = 0; index < parts.Length; index++)
            {
                if (string.Equals(
                        parts[index].Component,
                        component,
                        StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(component),
                    component,
                    $"Component '{component}' is not authored for {kind}.");
            }

            return $"GEO_CMM_{kind}_Variant{variant + 1:00}_{component}";
        }

        public int SelectVariant(CityMiscKind kind, string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A stable City misc ID is required to select a variant.",
                    nameof(stableId));
            }

            return SelectVariant(stableId, GetVariantCount(kind));
        }

        public CityMiscMeshPart GetPartOrThrow(
            CityMiscKind kind,
            string stableId,
            int partIndex)
        {
            return GetPartOrThrow(
                kind,
                SelectVariant(kind, stableId),
                partIndex);
        }

        public CityMiscMeshPart GetPartOrThrow(
            CityMiscKind kind,
            int variant,
            int partIndex)
        {
            ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
            RequireIndex(partIndex, parts.Length, nameof(partIndex));
            return GetPartOrThrow(
                kind,
                variant,
                parts[partIndex].Component);
        }

        public CityMiscMeshPart GetPartOrThrow(
            CityMiscKind kind,
            int variant,
            string component)
        {
            RequireIndex(
                variant,
                GetVariantCount(kind),
                nameof(variant));
            ExpectedPartSpec expected =
                GetExpectedPart(kind, variant, component);

            CityMiscMeshEntry entry = FindEntry(
                kind,
                variant,
                expected.Component);
            if (entry?.Mesh == null)
            {
                throw new InvalidOperationException(
                    $"The City misc provider has no mesh for {kind}, " +
                    $"variant {variant}, component {expected.Component}.");
            }

            return new CityMiscMeshPart(
                kind,
                variant,
                expected.Component,
                expected.Role,
                expected.Surface,
                entry.Mesh);
        }

        private void ValidateEntriesOrThrow(bool validateGeometry)
        {
            if (entries == null || entries.Length != ExpectedMeshCount)
            {
                throw new InvalidOperationException(
                    $"The City misc provider requires exactly " +
                    $"{ExpectedMeshCount} mesh entries.");
            }

            int visited = 0;
            for (int kindIndex = 0;
                 kindIndex < SupportedKindCatalog.Length;
                 kindIndex++)
            {
                CityMiscKind kind = SupportedKindCatalog[kindIndex];
                for (int variant = 0;
                     variant < GetVariantCount(kind);
                     variant++)
                {
                    Bounds assemblyBounds = default;
                    bool hasAssemblyBounds = false;
                    ExpectedPartSpec[] expectedParts =
                        GetExpectedParts(kind, variant);
                    int partCount = expectedParts.Length;
                    for (int partIndex = 0;
                         partIndex < partCount;
                         partIndex++)
                    {
                        ExpectedPartSpec expected =
                            expectedParts[partIndex];
                        CityMiscMeshEntry entry =
                            FindUniqueEntryOrThrow(
                                kind,
                                variant,
                                expected);
                        Mesh mesh = entry.Mesh;
                        if (mesh == null)
                        {
                            throw new InvalidOperationException(
                                $"City misc entry {kind}/{variant}/" +
                                $"{expected.Component} " +
                                "has no mesh.");
                        }

                        if (validateGeometry)
                        {
                            ValidateMesh(
                                mesh,
                                GetExpectedMeshName(
                                    kind,
                                    variant,
                                    expected.Component));
                        }

                        if (!hasAssemblyBounds)
                        {
                            assemblyBounds = mesh.bounds;
                            hasAssemblyBounds = true;
                        }
                        else
                        {
                            assemblyBounds.Encapsulate(mesh.bounds.min);
                            assemblyBounds.Encapsulate(mesh.bounds.max);
                        }

                        visited++;
                    }

                    if (validateGeometry)
                    {
                        ValidateAssemblyBounds(
                            kind,
                            variant,
                            assemblyBounds);
                    }
                }
            }

            if (visited != entries.Length)
            {
                throw new InvalidOperationException(
                    "The City misc provider contains unsupported or " +
                    "duplicate entries.");
            }
        }

        private CityMiscMeshEntry FindUniqueEntryOrThrow(
            CityMiscKind kind,
            int variant,
            ExpectedPartSpec expected)
        {
            CityMiscMeshEntry result = null;
            for (int index = 0; index < entries.Length; index++)
            {
                CityMiscMeshEntry candidate = entries[index];
                if (candidate == null ||
                    candidate.Kind != kind ||
                    candidate.Variant != variant ||
                    !string.Equals(
                        candidate.Component,
                        expected.Component,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"City misc provider duplicates " +
                        $"{kind}/{variant}/{expected.Component}.");
                }

                result = candidate;
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    $"City misc provider is missing " +
                    $"{kind}/{variant}/{expected.Component}.");
            }

            if (result.Role != expected.Role ||
                result.Surface != expected.Surface)
            {
                throw new InvalidOperationException(
                    $"City misc provider entry {kind}/{variant}/" +
                    $"{expected.Component} has stale role or surface.");
            }

            return result;
        }

        private CityMiscMeshEntry FindEntry(
            CityMiscKind kind,
            int variant,
            string component)
        {
            if (entries == null)
            {
                return null;
            }

            for (int index = 0; index < entries.Length; index++)
            {
                CityMiscMeshEntry entry = entries[index];
                if (entry != null &&
                    entry.Kind == kind &&
                    entry.Variant == variant &&
                    string.Equals(
                        entry.Component,
                        component,
                        StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private static ExpectedPartSpec GetExpectedPart(
            CityMiscKind kind,
            int variant,
            string component)
        {
            if (!string.IsNullOrWhiteSpace(component))
            {
                ExpectedPartSpec[] parts = GetExpectedParts(kind, variant);
                for (int index = 0; index < parts.Length; index++)
                {
                    if (string.Equals(
                            parts[index].Component,
                            component,
                            StringComparison.Ordinal))
                    {
                        return parts[index];
                    }
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(component),
                component,
                $"Component '{component}' is not authored for {kind}.");
        }

        private static ExpectedPartSpec[] GetExpectedParts(
            CityMiscKind kind,
            int variant)
        {
            RequireIndex(variant, GetVariantCount(kind), nameof(variant));
            switch (kind)
            {
                case CityMiscKind.IndustrialStacksAndTanks:
                case CityMiscKind.RoadsideDumpsterAndUtility:
                    return IndustrialStreetParts;
                case CityMiscKind.IndustrialCargo:
                    return IndustrialStreetMasonryParts;
                case CityMiscKind.NightlifeVendingAndQueue:
                    return StreetIndustrialNeonParts;
                case CityMiscKind.RoadsideRoadworkAndBicycle:
                    return StreetMasonryIndustrialParts;
                case CityMiscKind.ParkTree:
                    return BarkFoliageParts;
                case CityMiscKind.ParkBench:
                    return TimberPart;
                case CityMiscKind.RoadsidePhoneBooth:
                    return StreetResidentialBacklitParts;
                case CityMiscKind.StreetLampShell:
                    return FixturePart;
                case CityMiscKind.OldTownChimneysAndDormers:
                    return ChimneyParts;
                case CityMiscKind.OldTownScaffolding:
                    return IndustrialMasonryParts;
                case CityMiscKind.OldTownStreetMarket:
                    return StreetMasonryResidentialParts;
                case CityMiscKind.OldTownClockTower:
                    return MasonryStreetResidentialParts;
                case CityMiscKind.ResidentialBalconies:
                    return ResidentialStreetParts;
                case CityMiscKind.ResidentialLaundryAndAntenna:
                    return StreetResidentialMasonryParts;
                case CityMiscKind.ResidentialDiscardedFurniture:
                    return ResidentialStreetMasonryParts;
                case CityMiscKind.ResidentialRooftopGreenhouse:
                    return GreenhouseParts;
                case CityMiscKind.IndustrialPipeRack:
                    return StreetIndustrialParts;
                case CityMiscKind.IndustrialGantry:
                    return IndustrialStreetOnlyParts;
                case CityMiscKind.NightlifeBillboard:
                    return StreetNeonParts;
                case CityMiscKind.NightlifeFireEscape:
                    return IndustrialStreetOnlyParts;
                case CityMiscKind.NightlifeCinema:
                    return StreetNeonMasonryParts;
                case CityMiscKind.ParkFountainAndStatue:
                    return FountainParts;
                case CityMiscKind.ParkBandstand:
                    return BandstandParts;
                case CityMiscKind.ParkChessTables:
                    return ChessParts;
                case CityMiscKind.ParkPlayground:
                    return PlaygroundParts;
                case CityMiscKind.Route01ShelterShell:
                    return FixtureTimberParts;
                case CityMiscKind.Route01PoleShell:
                    return FixtureStreetResidentialParts;
                case CityMiscKind.TrafficSignalShell:
                case CityMiscKind.YardCarpetFrame:
                case CityMiscKind.YardDeadLamp:
                case CityMiscKind.YardBin:
                case CityMiscKind.YardSpotlightWallMount:
                case CityMiscKind.YardSpotlightHeadShell:
                case CityMiscKind.CemeteryGraveEnclosure:
                case CityMiscKind.SeacoastSlipwayBarrier:
                case CityMiscKind.FringeUtilityPole:
                    return FixturePart;
                case CityMiscKind.YardDeadTree:
                    return BarkPart;
                case CityMiscKind.YardBench:
                case CityMiscKind.CemeteryBench:
                    return TimberFixtureParts;
                case CityMiscKind.YardSandpit:
                case CityMiscKind.YardBottle:
                    return TimberPart;
                case CityMiscKind.YardChildToy:
                case CityMiscKind.CemeteryGraveOffering:
                    return ResidentialPart;
                case CityMiscKind.CemeteryGraveSlab:
                case CityMiscKind.CemeteryGraveMonument:
                    return MasonryPart;
                case CityMiscKind.CemeteryOvergrownMound:
                    return StreetResidentialParts;
                case CityMiscKind.CemeteryTree:
                    return BarkFoliageParts;
                case CityMiscKind.CemeteryBush:
                    return FoliagePart;
                case CityMiscKind.SeacoastBoat:
                    return ResidentialStreetTimberParts;
                case CityMiscKind.SeacoastOar:
                case CityMiscKind.SeacoastDriftwood:
                    return StreetPart;
                case CityMiscKind.SeacoastBarge:
                case CityMiscKind.PoiIndustrialWeighbridgeShell:
                    return IndustrialStreetOnlyParts;
                case CityMiscKind.FringeRepairStock:
                    return variant < 2 ? MasonryPart : FixturePart;
                case CityMiscKind.FringePipeStock:
                    return variant == 0 ? MasonryPart : FixturePart;
                case CityMiscKind.FringeUtilityShedShell:
                case CityMiscKind.FringeFloodGaugeShell:
                    return IndustrialFixtureParts;
                case CityMiscKind.PoiOldTownWaterworksShell:
                    return MasonryStreetParts;
                case CityMiscKind.PoiResidentialDryingYardShell:
                    return DryingYardParts;
                case CityMiscKind.PoiNightlifeLastRouteIslandShell:
                    return IndustrialStreetResidentialTimberParts;
                case CityMiscKind.BarBuildingShell:
                case CityMiscKind.SupermarketBuildingShell:
                case CityMiscKind.PlayerHomeBuildingShell:
                    return SpecialBuildingParts;
                case CityMiscKind.RoadsideDrainAndCover:
                case CityMiscKind.RoadsideCappedStandpipe:
                case CityMiscKind.LotGroundDownpipeOutfall:
                    return StreetMasonryParts;
                case CityMiscKind.ChurchCourtyardSurface:
                    switch (variant)
                    {
                        case 0:
                            return CourtyardStoneSurfacePart;
                        case 1:
                            return CourtyardGravelSurfacePart;
                        default:
                            return CourtyardLawnSurfacePart;
                    }
                case CityMiscKind.ChurchCourtyardShrub:
                    return FoliagePart;
                case CityMiscKind.ChurchCourtyardFlowerBed:
                    return CourtyardFlowerBedParts;
                case CityMiscKind.CemeteryFencePost:
                case CityMiscKind.CemeteryFenceRail:
                    return FixturePart;
                case CityMiscKind.NightlifeArchBridgeShell:
                    return NightlifeArchBridgeShellParts;
                case CityMiscKind.NightlifeBurnBarrel:
                    return NightlifeBurnBarrelParts;
                case CityMiscKind.NightlifeShelterBedding:
                    return variant == 0
                        ? NightlifeShelterMattressParts
                        : NightlifeShelterRollParts;
                case CityMiscKind.NightlifeShelterClutter:
                    return NightlifeShelterClutterParts;
                case CityMiscKind.NightlifeShelterFire:
                    return NightlifeShelterFireParts;
                case CityMiscKind.NightlifeShelterStandingPerson:
                case CityMiscKind.NightlifeShelterSeatedPerson:
                    return NightlifeShelterPersonParts;
                case CityMiscKind.NightlifeShelterSleepingPerson:
                    return NightlifeShelterSleepingPersonParts;
                default:
                    throw UnsupportedKind(kind);
            }
        }

        private static void ValidateAssemblyBounds(
            CityMiscKind kind,
            int variant,
            Bounds bounds)
        {
            if (!IsFinite(bounds.min) ||
                !IsFinite(bounds.max) ||
                bounds.size.x <= 0f ||
                bounds.size.y <= 0f ||
                bounds.size.z <= 0f)
            {
                throw new InvalidOperationException(
                    $"City misc assembly {kind}/{variant} has invalid " +
                    "bounds.");
            }

            float expectedMinimumY =
                GetExpectedAssemblyMinimumY(kind, variant);
            if (Mathf.Abs(bounds.min.y - expectedMinimumY) >
                GroundTolerance)
            {
                throw new InvalidOperationException(
                    $"City misc assembly {kind}/{variant} has local min Y " +
                    $"{bounds.min.y:F6}; expected {expectedMinimumY:F6}.");
            }
        }

        private static ExpectedPartSpec P(
            string component,
            CityMiscMeshRole role,
            CityMiscSurfaceKind surface = CityMiscSurfaceKind.Default)
        {
            return new ExpectedPartSpec(component, role, surface);
        }

        private static void ValidateMesh(Mesh mesh, string expectedName)
        {
            if (!mesh.isReadable ||
                mesh.vertexCount <= 0 ||
                !string.Equals(
                    mesh.name,
                    expectedName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid City misc mesh '{expectedName}'. It must be " +
                    "readable, non-empty and keep its authored name.");
            }

            Bounds bounds = mesh.bounds;
            if (!IsFinite(bounds.min) || !IsFinite(bounds.max))
            {
                throw new InvalidOperationException(
                    $"City misc mesh '{expectedName}' has non-finite " +
                    "bounds.");
            }
        }

        private static int SelectVariant(string stableId, int variantCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int index = 0; index < stableId.Length; index++)
                {
                    hash ^= stableId[index];
                    hash *= 16777619u;
                }

                return (int)(hash % (uint)variantCount);
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (!Uri.IsHexDigit(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void RequireIndex(int index, int count, string name)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    index,
                    $"Expected 0..{count - 1}.");
            }
        }

        private static ArgumentOutOfRangeException UnsupportedKind(
            CityMiscKind kind)
        {
            return new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "This City misc kind is not part of the imported wave.");
        }

        private readonly struct ExpectedPartSpec
        {
            public ExpectedPartSpec(
                string component,
                CityMiscMeshRole role,
                CityMiscSurfaceKind surface)
            {
                Component = component;
                Role = role;
                Surface = surface;
            }

            public string Component { get; }
            public CityMiscMeshRole Role { get; }
            public CityMiscSurfaceKind Surface { get; }
        }
    }
}
