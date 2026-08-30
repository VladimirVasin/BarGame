namespace BarPromenade
{
    /// <summary>
    /// Exact passive-mesh contracts for the late City misc catalog waves.
    /// Kept beside the provider so its validation surface remains explicit
    /// without turning the main resource bridge into an oversized file.
    /// </summary>
    public sealed partial class CityMiscAssetProvider
    {
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

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardNardiParts =
        {
            P("TableAndStools_Residential_Timber",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("NardiPieces_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("TeaTray_Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardBicycleParts =
        {
            P("BicycleFrame_Residential_PaintedMetal",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.PaintedMetal),
            P("RepairCrate_Residential_Timber",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("TyresAndTools_Street", CityMiscMeshRole.Street)
        };

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardBasketParts =
        {
            P("Basket_Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("Rope_Street", CityMiscMeshRole.Street),
            P("Pulley_Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardChairParts =
        {
            P("ChairAndBench_Residential_Timber",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("ClampAndTools_Street_PaintedMetal",
                CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal),
            P("ReplacementSlats_Residential_Timber",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber)
        };

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardSweepingParts =
        {
            P("Broom_Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("Dustpan_Street", CityMiscMeshRole.Street),
            P("Bucket_Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[]
            ResidentialCourtyardQuietParts =
        {
            P("LowBench_Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("Planters_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("Basin_Street_PaintedMetal", CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal)
        };

        private static readonly ExpectedPartSpec[] FringeMasonCartParts =
        {
            P("Cart_Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("MasonryLoad_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("WheelAndHardware_Fixture", CityMiscMeshRole.Fixture)
        };

        private static readonly ExpectedPartSpec[] FringeWinchServiceParts =
        {
            P("Winch_Industrial", CityMiscMeshRole.Industrial),
            P("TimberCrib_Residential_Timber",
                CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("CableAndTools_Fixture", CityMiscMeshRole.Fixture)
        };

        private static readonly ExpectedPartSpec[] FringeTunnelServiceParts =
        {
            P("BarrierAndRail_Industrial", CityMiscMeshRole.Industrial),
            P("RepairBlocks_Masonry_Stone", CityMiscMeshRole.Masonry,
                CityMiscSurfaceKind.Stone),
            P("Tools_Fixture", CityMiscMeshRole.Fixture)
        };

        private static readonly ExpectedPartSpec[]
            FringeFloodMaintenanceParts =
        {
            P("PumpAndPipe_Industrial", CityMiscMeshRole.Industrial),
            P("Planks_Residential_Timber", CityMiscMeshRole.Residential,
                CityMiscSurfaceKind.Timber),
            P("DryHoseAndTools_Fixture", CityMiscMeshRole.Fixture)
        };

        private static readonly ExpectedPartSpec[] FringeOpenHoodCarParts =
        {
            P("BodyAndOpenHood_Street_PaintedMetal",
                CityMiscMeshRole.Street,
                CityMiscSurfaceKind.PaintedMetal),
            P("TyresCabinAndEngine_Street", CityMiscMeshRole.Street),
            P("JackRemovedWheelAndTools_Fixture",
                CityMiscMeshRole.Fixture)
        };
    }
}
