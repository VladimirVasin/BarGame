using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the first City misc Blender catalog, its strict passive import and
    /// the flat Resources provider binding. Runtime placement is covered by
    /// the focused world-builder tests that consume this contract.
    /// </summary>
    public sealed class CityMiscAssetTests
    {
        private const string ModelPath =
            "Assets/City/Models/CityMisc3D.fbx";
        private const string ManifestPath =
            "Assets/City/Models/CityMisc3D.json";

        private static readonly ExpectedPart[] ExpectedParts =
        {
            Part(
                CityMiscKind.IndustrialStacksAndTanks,
                0,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.IndustrialStacksAndTanks,
                0,
                CityMiscMeshRole.Street),

            Part(
                CityMiscKind.IndustrialCargo,
                0,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.IndustrialCargo,
                0,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.IndustrialCargo,
                0,
                CityMiscMeshRole.Masonry),
            Part(
                CityMiscKind.IndustrialCargo,
                1,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.IndustrialCargo,
                1,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.IndustrialCargo,
                1,
                CityMiscMeshRole.Masonry),

            Part(
                CityMiscKind.NightlifeVendingAndQueue,
                0,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.NightlifeVendingAndQueue,
                0,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.NightlifeVendingAndQueue,
                0,
                CityMiscMeshRole.Neon),

            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                0,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                0,
                CityMiscMeshRole.Masonry),
            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                0,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                1,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                1,
                CityMiscMeshRole.Masonry),
            Part(
                CityMiscKind.RoadsideRoadworkAndBicycle,
                1,
                CityMiscMeshRole.Industrial),

            Part(CityMiscKind.ParkTree, 0, CityMiscMeshRole.Bark),
            Part(CityMiscKind.ParkTree, 0, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ParkTree, 1, CityMiscMeshRole.Bark),
            Part(CityMiscKind.ParkTree, 1, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ParkTree, 2, CityMiscMeshRole.Bark),
            Part(CityMiscKind.ParkTree, 2, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ParkTree, 3, CityMiscMeshRole.Bark),
            Part(CityMiscKind.ParkTree, 3, CityMiscMeshRole.Foliage),

            Part(CityMiscKind.ParkBench, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.ParkBench, 1, CityMiscMeshRole.Timber),

            Part(
                CityMiscKind.RoadsidePhoneBooth,
                0,
                CityMiscMeshRole.Street),
            Part(
                CityMiscKind.RoadsidePhoneBooth,
                0,
                CityMiscMeshRole.Residential),
            Part(
                CityMiscKind.RoadsidePhoneBooth,
                0,
                CityMiscMeshRole.BacklitSign),

            Part(
                CityMiscKind.RoadsideDumpsterAndUtility,
                0,
                CityMiscMeshRole.Industrial),
            Part(
                CityMiscKind.RoadsideDumpsterAndUtility,
                0,
                CityMiscMeshRole.Street),

            Part(
                CityMiscKind.StreetLampShell,
                0,
                CityMiscMeshRole.Fixture),

            Part(CityMiscKind.OldTownChimneysAndDormers, 0, "Chimneys_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownChimneysAndDormers, 0, "Dormer_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownChimneysAndDormers, 0, "Window_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.OldTownChimneysAndDormers, 1, "Chimneys_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownChimneysAndDormers, 1, "Dormer_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownChimneysAndDormers, 1, "Window_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.OldTownScaffolding, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.OldTownScaffolding, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownStreetMarket, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.OldTownStreetMarket, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownStreetMarket, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.OldTownClockTower, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.OldTownClockTower, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.OldTownClockTower, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialBalconies, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialBalconies, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialBalconies, 1, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialBalconies, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialLaundryAndAntenna, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialLaundryAndAntenna, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialLaundryAndAntenna, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 1, CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialDiscardedFurniture, 1, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ResidentialRooftopGreenhouse, 0, "Base_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ResidentialRooftopGreenhouse, 0, "Frame_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialRooftopGreenhouse, 0, "Roof_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.ResidentialRooftopGreenhouse, 0, "Hardware_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.IndustrialPipeRack, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.IndustrialPipeRack, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.IndustrialGantry, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.IndustrialGantry, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.IndustrialGantry, 1, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.IndustrialGantry, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeBillboard, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeBillboard, 0, CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeFireEscape, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.NightlifeFireEscape, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeCinema, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeCinema, 0, CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeCinema, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeCinema, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeCinema, 1, CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeCinema, 1, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ParkFountainAndStatue, 0, "Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkFountainAndStatue, 0, "Street_Stone", CityMiscMeshRole.Street, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkBandstand, 0, "Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkBandstand, 0, "Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkBandstand, 0, "Masonry_Timber", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkBandstand, 0, "Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ParkChessTables, 0, "TableSlab_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkChessTables, 0, "BoardLight_Masonry_Timber", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkChessTables, 0, "BoardDarkAndRim_Street_Timber", CityMiscMeshRole.Street, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkChessTables, 0, "TableFooting_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkChessTables, 0, "TablePedestal_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkChessTables, 0, "BenchSeat_Street_Timber", CityMiscMeshRole.Street, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkChessTables, 0, "BenchPad_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkChessTables, 0, "BenchLeg_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ParkPlayground, 0, "Residential_PaintedMetal", CityMiscMeshRole.Residential, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ParkPlayground, 0, "Masonry_Timber", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ParkPlayground, 0, "Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.Route01ShelterShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.Route01ShelterShell, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.Route01PoleShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.Route01PoleShell, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.Route01PoleShell, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.TrafficSignalShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardDeadTree, 0, CityMiscMeshRole.Bark),
            Part(CityMiscKind.YardBench, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.YardBench, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardCarpetFrame, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardSandpit, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.YardChildToy, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.YardDeadLamp, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardBin, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardBottle, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.YardSpotlightWallMount, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.YardSpotlightHeadShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.CemeteryGraveSlab, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveSlab, 1, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveSlab, 2, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveSlab, 3, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveSlab, 4, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveSlab, 5, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveMonument, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveMonument, 1, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveMonument, 2, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveMonument, 3, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryGraveMonument, 4, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.CemeteryOvergrownMound, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.CemeteryOvergrownMound, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.CemeteryGraveEnclosure, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.CemeteryGraveOffering, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.CemeteryTree, 0, CityMiscMeshRole.Bark),
            Part(CityMiscKind.CemeteryTree, 0, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.CemeteryTree, 1, CityMiscMeshRole.Bark),
            Part(CityMiscKind.CemeteryTree, 1, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.CemeteryBush, 0, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.CemeteryBench, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.CemeteryBench, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.SeacoastBoat, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.SeacoastBoat, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastBoat, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.SeacoastBoat, 1, CityMiscMeshRole.Residential),
            Part(CityMiscKind.SeacoastBoat, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastBoat, 1, CityMiscMeshRole.Timber),
            Part(CityMiscKind.SeacoastBoat, 2, CityMiscMeshRole.Residential),
            Part(CityMiscKind.SeacoastBoat, 2, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastBoat, 2, CityMiscMeshRole.Timber),
            Part(CityMiscKind.SeacoastBoat, 3, CityMiscMeshRole.Residential),
            Part(CityMiscKind.SeacoastBoat, 3, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastBoat, 3, CityMiscMeshRole.Timber),
            Part(CityMiscKind.SeacoastOar, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastSlipwayBarrier, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.SeacoastBarge, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.SeacoastBarge, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastDriftwood, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastDriftwood, 1, CityMiscMeshRole.Street),
            Part(CityMiscKind.SeacoastDriftwood, 2, CityMiscMeshRole.Street),
            Part(CityMiscKind.FringeUtilityPole, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeRepairStock, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.FringeRepairStock, 1, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.FringeRepairStock, 2, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringePipeStock, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.FringePipeStock, 1, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeUtilityShedShell, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeUtilityShedShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeFloodGaugeShell, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeFloodGaugeShell, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeFloodGaugeShell, 1, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeFloodGaugeShell, 1, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.PoiOldTownWaterworksShell, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.PoiOldTownWaterworksShell, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.PoiResidentialDryingYardShell, 0, "Residential_PaintedMetal", CityMiscMeshRole.Residential, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.PoiResidentialDryingYardShell, 0, "Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.PoiIndustrialWeighbridgeShell, 0, CityMiscMeshRole.Industrial),
            Part(CityMiscKind.PoiIndustrialWeighbridgeShell, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.PoiNightlifeLastRouteIslandShell, 0, CityMiscMeshRole.Masonry),
            Part(CityMiscKind.PoiNightlifeLastRouteIslandShell, 0, CityMiscMeshRole.Street),
            Part(CityMiscKind.PoiNightlifeLastRouteIslandShell, 0, CityMiscMeshRole.Residential),
            Part(CityMiscKind.PoiNightlifeLastRouteIslandShell, 0, CityMiscMeshRole.Timber),
            Part(CityMiscKind.BarBuildingShell, 0, "Shell_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.BarBuildingShell, 0, "Roof_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.BarBuildingShell, 0, "Trim_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.SupermarketBuildingShell, 0, "Shell_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.SupermarketBuildingShell, 0, "Roof_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.SupermarketBuildingShell, 0, "Trim_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.PlayerHomeBuildingShell, 0, "Shell_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.PlayerHomeBuildingShell, 0, "Roof_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.PlayerHomeBuildingShell, 0, "Trim_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.RoadsideDrainAndCover, 0, "Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.RoadsideDrainAndCover, 0, "Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.RoadsideCappedStandpipe, 0, "Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.RoadsideCappedStandpipe, 0, "Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.LotGroundDownpipeOutfall, 0, "Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.LotGroundDownpipeOutfall, 0, "Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ChurchCourtyardSurface, 0, "Surface_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ChurchCourtyardSurface, 1, "Surface_Street_Gravel", CityMiscMeshRole.Street, CityMiscSurfaceKind.Gravel),
            Part(CityMiscKind.ChurchCourtyardSurface, 2, "Surface_Foliage_Lawn", CityMiscMeshRole.Foliage, CityMiscSurfaceKind.Lawn),
            Part(CityMiscKind.ChurchCourtyardShrub, 0, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ChurchCourtyardShrub, 1, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 0, "Edging_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 0, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 0, "Flowers_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 1, "Edging_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 1, CityMiscMeshRole.Foliage),
            Part(CityMiscKind.ChurchCourtyardFlowerBed, 1, "Flowers_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.CemeteryFencePost, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.CemeteryFenceRail, 0, CityMiscMeshRole.Fixture),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "Shell_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "StepsAndRetaining_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "PlatformSupport_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "PlatformSlab_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "Cladding_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.NightlifeArchBridgeShell, 0, "Roof_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeBurnBarrel, 0, "Barrel_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.NightlifeBurnBarrel, 0, "Fuel_Timber", CityMiscMeshRole.Timber),
            Part(CityMiscKind.NightlifeShelterBedding, 0, "Mattress_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterBedding, 0, "Blanket_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterBedding, 0, "Cardboard_Timber", CityMiscMeshRole.Timber),
            Part(CityMiscKind.NightlifeShelterBedding, 1, "Roll_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterBedding, 1, "Tie_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterBedding, 1, "Cardboard_Timber", CityMiscMeshRole.Timber),
            Part(CityMiscKind.NightlifeShelterClutter, 0, "CrateAndCardboard_Timber", CityMiscMeshRole.Timber),
            Part(CityMiscKind.NightlifeShelterClutter, 0, "Bags_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterClutter, 0, "Bottles_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterClutter, 0, "Can_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.NightlifeShelterFire, 0, "FlameCore_Neon", CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeShelterFire, 0, "FlameOuter_Neon", CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeShelterFire, 0, "FlameLeftTongue_Neon", CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeShelterFire, 0, "FlameRightTongue_Neon", CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeShelterFire, 0, "EmberBed_Neon", CityMiscMeshRole.Neon),
            Part(CityMiscKind.NightlifeShelterFire, 0, "GroundSpill_BacklitSign", CityMiscMeshRole.BacklitSign),
            Part(CityMiscKind.NightlifeShelterStandingPerson, 0, "Outerwear_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterStandingPerson, 0, "Layer_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterStandingPerson, 0, "Skin_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeShelterSeatedPerson, 0, "Outerwear_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterSeatedPerson, 0, "Layer_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterSeatedPerson, 0, "Skin_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.NightlifeShelterSleepingPerson, 0, "Outerwear_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.NightlifeShelterSleepingPerson, 0, "BreathingUpper_Residential", CityMiscMeshRole.Residential),
            Part(CityMiscKind.NightlifeShelterSleepingPerson, 0, "Skin_Masonry", CityMiscMeshRole.Masonry),
            Part(CityMiscKind.ResidentialCourtyardPocket, 0, "TableAndStools_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 0, "NardiPieces_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ResidentialCourtyardPocket, 0, "TeaTray_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ResidentialCourtyardPocket, 1, "BicycleFrame_Residential_PaintedMetal", CityMiscMeshRole.Residential, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ResidentialCourtyardPocket, 1, "RepairCrate_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 1, "TyresAndTools_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialCourtyardPocket, 2, "Basket_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 2, "Rope_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialCourtyardPocket, 2, "Pulley_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ResidentialCourtyardPocket, 3, "ChairAndBench_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 3, "ClampAndTools_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ResidentialCourtyardPocket, 3, "ReplacementSlats_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 4, "Broom_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 4, "Dustpan_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.ResidentialCourtyardPocket, 4, "Bucket_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.ResidentialCourtyardPocket, 5, "LowBench_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.ResidentialCourtyardPocket, 5, "Planters_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.ResidentialCourtyardPocket, 5, "Basin_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.FringeMasonCart, 0, "Cart_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.FringeMasonCart, 0, "MasonryLoad_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.FringeMasonCart, 0, "WheelAndHardware_Fixture", CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeWinchServiceSet, 0, "Winch_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeWinchServiceSet, 0, "TimberCrib_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.FringeWinchServiceSet, 0, "CableAndTools_Fixture", CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeTunnelServiceSet, 0, "BarrierAndRail_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeTunnelServiceSet, 0, "RepairBlocks_Masonry_Stone", CityMiscMeshRole.Masonry, CityMiscSurfaceKind.Stone),
            Part(CityMiscKind.FringeTunnelServiceSet, 0, "Tools_Fixture", CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeFloodMaintenanceSet, 0, "PumpAndPipe_Industrial", CityMiscMeshRole.Industrial),
            Part(CityMiscKind.FringeFloodMaintenanceSet, 0, "Planks_Residential_Timber", CityMiscMeshRole.Residential, CityMiscSurfaceKind.Timber),
            Part(CityMiscKind.FringeFloodMaintenanceSet, 0, "DryHoseAndTools_Fixture", CityMiscMeshRole.Fixture),
            Part(CityMiscKind.FringeOpenHoodCar, 0, "BodyAndOpenHood_Street_PaintedMetal", CityMiscMeshRole.Street, CityMiscSurfaceKind.PaintedMetal),
            Part(CityMiscKind.FringeOpenHoodCar, 0, "TyresCabinAndEngine_Street", CityMiscMeshRole.Street),
            Part(CityMiscKind.FringeOpenHoodCar, 0, "JackRemovedWheelAndTools_Fixture", CityMiscMeshRole.Fixture)
        };

        [Test]
        public void Catalog_DeclaresExactCurrentAssemblyAndMeshWave()
        {
            Assert.That(
                CityMiscAssetProvider.DesignId,
                Is.EqualTo("city_misc_citywide_v4"));
            Assert.That(
                CityMiscAssetProvider.GeneratorVersion,
                Is.EqualTo("4.8.0"));
            Assert.That(
                CityMiscAssetProvider.SupportedKindCount,
                Is.EqualTo(86));
            Assert.That(
                CityMiscAssetProvider.ExpectedAssemblyCount,
                Is.EqualTo(126));
            Assert.That(
                CityMiscAssetProvider.ExpectedMeshCount,
                Is.EqualTo(271));
            Assert.That(ExpectedParts, Has.Length.EqualTo(271));

            var actualNames = new List<string>();
            int assemblies = 0;
            for (int kindIndex = 0;
                 kindIndex < CityMiscAssetProvider.SupportedKindCount;
                 kindIndex++)
            {
                CityMiscKind kind =
                    CityMiscAssetProvider.GetSupportedKind(kindIndex);
                Assert.That(CityMiscAssetProvider.Supports(kind), Is.True);
                int variants =
                    CityMiscAssetProvider.GetVariantCount(kind);
                assemblies += variants;
                for (int variant = 0; variant < variants; variant++)
                {
                    for (int partIndex = 0;
                         partIndex <
                         CityMiscAssetProvider.GetPartCount(kind);
                         partIndex++)
                    {
                        actualNames.Add(
                            CityMiscAssetProvider.GetExpectedMeshName(
                                kind,
                                variant,
                                partIndex));
                    }
                }
            }

            Assert.That(assemblies, Is.EqualTo(126));
            Assert.That(
                actualNames,
                Is.EqualTo(ExpectedParts.Select(part => part.MeshName)));
            Assert.That(
                actualNames.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(271));
            Assert.That(
                actualNames.Take(33),
                Is.EqualTo(ExpectedParts.Take(33).Select(part => part.MeshName)),
                "The frozen v1 catalog prefix changed.");
            Assert.That(
                actualNames.Take(97),
                Is.EqualTo(ExpectedParts.Take(97).Select(part => part.MeshName)),
                "The frozen v2 catalog prefix changed.");
            Assert.That(
                actualNames.All(name =>
                    name.StartsWith(
                        "GEO_CMM_",
                        StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void ImportedKit_ValidatesAsPassiveFixedMeterGeometry()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                "Run tools/build-city-misc-3d-model.py first.");
            ContractManifest manifest =
                JsonUtility.FromJson<ContractManifest>(source.text);

            Assert.That(
                manifest.design_id,
                Is.EqualTo(CityMiscAssetProvider.DesignId));
            Assert.That(
                manifest.generator_version,
                Is.EqualTo(CityMiscAssetProvider.GeneratorVersion));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.mesh_count, Is.EqualTo(271));
            Assert.That(manifest.assembly_count, Is.EqualTo(126));
            Assert.That(manifest.triangle_count, Is.EqualTo(48926));
            Assert.That(
                manifest.build_signature,
                Is.EqualTo(
                    "45026a9b34c7d7390f5c70fdced3090cd27527a7d2c4f2bd09a4832461b256e1"));
            Assert.That(
                manifest.wave1_compatibility_signature,
                Is.EqualTo(
                    "dd2e814d906fd2c7a7855c6d75ee54fe912ebb90f7cd02633c95c558d752f9f6"));
            Assert.That(
                manifest.v2_compatibility_signature,
                Is.EqualTo(
                    "8ec3ffe04ffbcfba94cbf708d9c8263afbe853aeea4ffdeabfe638857a043193"));
            Assert.That(manifest.root_contract, Is.Not.Null);
            Assert.That(
                manifest.root_contract.origin,
                Is.EqualTo("per_assembly_root_derivation"));
            Assert.That(
                manifest.root_contract.scale_mode,
                Is.EqualTo("fixed_meters"));
            Assert.That(
                manifest.root_contract.source_forward_axis,
                Is.EqualTo("+Y"));
            Assert.That(
                manifest.root_contract.unity_forward_axis,
                Is.EqualTo("+Z"));
            Assert.That(
                manifest.root_contract
                    .legacy_recipe_x_to_unity_local_x,
                Is.EqualTo(-1f));
            Assert.That(manifest.static_humanoid_contract, Is.Not.Null);
            Assert.That(
                manifest.static_humanoid_contract.standard,
                Is.EqualTo("static_humanoid_anatomy_v2"));
            Assert.That(
                manifest.static_humanoid_contract.standard_version,
                Is.EqualTo(2f));
            Assert.That(manifest.static_humanoid_contract.rigged, Is.False);
            Assert.That(
                manifest.static_humanoid_contract.resident_kinds,
                Is.EqualTo(new[]
                {
                    "NightlifeShelterStandingPerson",
                    "NightlifeShelterSeatedPerson",
                    "NightlifeShelterSleepingPerson"
                }));
            Assert.That(
                manifest.static_humanoid_contract
                    .standing_equivalent_height_m,
                Is.EqualTo(1.75f));
            Assert.That(
                manifest.static_humanoid_contract.head_width_m,
                Is.EqualTo(0.22f));
            Assert.That(
                manifest.static_humanoid_contract.head_height_m,
                Is.EqualTo(0.24f));
            Assert.That(
                manifest.static_humanoid_contract.heads_tall,
                Is.EqualTo(7.291667f).Within(0.00001f));
            Assert.That(
                manifest.static_humanoid_contract.shoulder_joint_span_m,
                Is.EqualTo(0.52f));
            Assert.That(
                manifest.static_humanoid_contract
                    .shoulder_joint_span_head_widths,
                Is.InRange(2.3f, 2.5f));
            Assert.That(
                manifest.static_humanoid_contract.polygon_growth_allowed,
                Is.False);
            Assert.That(
                manifest.static_humanoid_contract.legacy_triangle_caps
                    .NightlifeShelterStandingPerson,
                Is.EqualTo(582));
            Assert.That(
                manifest.static_humanoid_contract.legacy_triangle_caps
                    .NightlifeShelterSeatedPerson,
                Is.EqualTo(638));
            Assert.That(
                manifest.static_humanoid_contract.legacy_triangle_caps
                    .NightlifeShelterSleepingPerson,
                Is.EqualTo(414));

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));

            UnityEngine.Object[] imported =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            Assert.That(imported.OfType<Material>(), Is.Empty);
            Assert.That(imported.OfType<AnimationClip>(), Is.Empty);
            Mesh[] meshes = imported.OfType<Mesh>().ToArray();
            Assert.That(meshes, Has.Length.EqualTo(271));
            Assert.That(meshes.All(mesh => mesh.isReadable), Is.True);
            Assert.That(meshes.All(mesh => mesh.vertexCount > 0), Is.True);

            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null);
            Assert.That(
                model.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                model.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                model.GetComponentsInChildren<Camera>(true),
                Is.Empty);
            Assert.That(
                model.GetComponentsInChildren<Animator>(true),
                Is.Empty);
            Assert.That(
                model.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
        }

        [Test]
        public void Provider_CarriesEveryExactMeshAndCurrentSignature()
        {
            TextAsset manifestSource =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ManifestPath);
            Assert.That(manifestSource, Is.Not.Null);
            SignatureManifest manifest =
                JsonUtility.FromJson<SignatureManifest>(manifestSource.text);

            CityMiscAssetProvider provider =
                CityMiscAssetProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.HasCompleteMeshes, Is.True);
            Assert.That(
                provider.BuildSignature,
                Is.EqualTo(manifest.build_signature));
            Assert.DoesNotThrow(provider.ValidateOrThrow);

            var actualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExpectedPart expected in ExpectedParts)
            {
                int partIndex = PartIndex(
                    expected.Kind, expected.Variant, expected.Component);
                CityMiscMeshPart part = provider.GetPartOrThrow(
                    expected.Kind,
                    expected.Variant,
                    partIndex);
                Assert.That(part.Kind, Is.EqualTo(expected.Kind));
                Assert.That(part.Variant, Is.EqualTo(expected.Variant));
                Assert.That(part.Component, Is.EqualTo(expected.Component));
                Assert.That(part.Role, Is.EqualTo(expected.Role));
                Assert.That(part.Surface, Is.EqualTo(expected.Surface));
                Assert.That(part.Mesh, Is.Not.Null);
                Assert.That(part.Mesh.name, Is.EqualTo(expected.MeshName));
                string stableId = StableIdForVariant(
                    provider, expected.Kind, expected.Variant);
                Assert.That(
                    provider.GetPartOrThrow(
                        expected.Kind, stableId, partIndex).Mesh,
                    Is.SameAs(part.Mesh));
                actualNames.Add(part.Mesh.name);
            }

            Assert.That(actualNames, Has.Count.EqualTo(271));
            UnityEngine.Object[] imported =
                AssetDatabase.LoadAllAssetsAtPath(
                    ModelPath);
            Assert.That(
                imported.OfType<Mesh>().Select(mesh => mesh.name),
                Is.EquivalentTo(actualNames));
        }

        [Test]
        public void StableIds_SelectRepeatableBoundedVariants()
        {
            CityMiscAssetProvider provider =
                ScriptableObject.CreateInstance<CityMiscAssetProvider>();
            try
            {
                for (int kindIndex = 0;
                     kindIndex < CityMiscAssetProvider.SupportedKindCount;
                     kindIndex++)
                {
                    CityMiscKind kind =
                        CityMiscAssetProvider.GetSupportedKind(kindIndex);
                    int count =
                        CityMiscAssetProvider.GetVariantCount(kind);
                    int first = provider.SelectVariant(
                        kind,
                        "city-misc-stable-id-17");
                    int second = provider.SelectVariant(
                        kind,
                        "city-misc-stable-id-17");
                    Assert.That(second, Is.EqualTo(first));
                    Assert.That(first, Is.InRange(0, count - 1));
                }

                Assert.Throws<ArgumentException>(() =>
                    provider.SelectVariant(CityMiscKind.ParkTree, ""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
            }
        }

        [Test]
        public void DefaultCity_MigratesWaveOneWithoutMovingRuntimeContracts()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan complete = CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                night);
            CityDecorationDescriptor[] migrated = complete.Descriptors
                .Where(descriptor =>
                    IsWaveOneDecoration(descriptor.Kind))
                .ToArray();
            // A census, not a contract: it exists so that a migration is
            // never allowed to quietly drop a wave-one prop. It moves when
            // the city's composition moves, and it is expected to be
            // re-counted then - fd691b8 cut the city from four bars to one,
            // which handed three lots back to ordinary frontage dressing.
            Assert.That(migrated, Has.Length.EqualTo(83));

            var parent = new GameObject("City Misc Runtime Test");
            try
            {
                GameObject details = CityDecorationWorldBuilder.Build(
                    parent.transform,
                    layout,
                    new CityDecorationPlan(complete.Seed, migrated));
                Renderer[] detailRenderers = details
                    .GetComponentsInChildren<Renderer>(true);
                Assert.That(detailRenderers, Is.Not.Empty);
                Assert.That(
                    detailRenderers.All(renderer =>
                        renderer.name.StartsWith(
                            "Imported ",
                            StringComparison.Ordinal)),
                    Is.True,
                    "A migrated decoration emitted a legacy box renderer.");
                Assert.That(
                    details.GetComponentsInChildren<Collider>(true),
                    Is.Not.Empty,
                    "Plan-owned collision proxies must remain present.");

                GameObject park = CityWorldBuilder.BuildPark(
                    parent.transform,
                    layout,
                    null);
                string[] parkRendererNames = park
                    .GetComponentsInChildren<Renderer>(true)
                    .Select(renderer => renderer.name)
                    .ToArray();
                Assert.That(
                    parkRendererNames,
                    Does.Contain("Imported Park Tree Trunks"));
                Assert.That(
                    parkRendererNames,
                    Does.Contain("Imported Park Tree Canopies"));
                Assert.That(
                    parkRendererNames,
                    Does.Contain("Imported Park Benches"));
                Assert.That(
                    parkRendererNames,
                    Does.Not.Contain("Park Tree Trunks"));
                Assert.That(
                    parkRendererNames,
                    Does.Not.Contain("Park Tree Canopies"));
                Assert.That(
                    parkRendererNames,
                    Does.Not.Contain("Park Benches"));

                CityNightWorldResult nightWorld = CityNightWorldBuilder.Build(
                    parent.transform,
                    night,
                    Array.Empty<BarEntrance>());
                Assert.That(
                    nightWorld.LampAnchors,
                    Has.Count.EqualTo(night.StreetLamps.Count));
                Assert.That(
                    nightWorld.StreetLampBulbRenderers,
                    Is.Not.Empty,
                    "Bulb renderers remain Unity-owned for night dimming.");
                Renderer[] nightRenderers = nightWorld.Root
                    .GetComponentsInChildren<Renderer>(true);
                Assert.That(
                    nightRenderers.Count(renderer =>
                        renderer.name == "Imported Street Lamp Fixtures"),
                    Is.GreaterThan(0));
                Assert.That(
                    nightRenderers.Count(renderer =>
                        renderer.name == "Street Lamp Fixtures"),
                    Is.Zero);
                Assert.That(
                    nightRenderers.Count(renderer =>
                        renderer.name ==
                        "Imported Traffic Signal Shell Fixture"),
                    Is.EqualTo(night.TrafficSignals.Count));
                Assert.That(
                    nightRenderers.Count(renderer =>
                        renderer.name == "Amber Lens"),
                    Is.EqualTo(night.TrafficSignals.Count),
                    "Signal lenses and their controller anchors stay " +
                    "Unity-owned.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void DefaultCity_MigratesCitywideStaticShellsAndKeepsRuntimeParts()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            var parent = new GameObject("Citywide Misc Runtime Test");
            try
            {
                GameObject stops = CityBusStopWorldBuilder.Build(
                    parent.transform,
                    CityBusPlanner.Create(layout));
                string[] stopRenderers = stops
                    .GetComponentsInChildren<Renderer>(true)
                    .Select(renderer => renderer.name)
                    .ToArray();
                Assert.That(
                    stopRenderers.Any(name => name.StartsWith(
                        "Imported Route 01 Shelter Shell",
                        StringComparison.Ordinal)),
                    Is.True);
                Assert.That(
                    stopRenderers.Any(name => name.StartsWith(
                        "Imported Route 01 Pole Shell",
                        StringComparison.Ordinal)),
                    Is.True);
                Assert.That(
                    stops.GetComponentsInChildren<Collider>(true),
                    Is.Not.Empty,
                    "Route 01 keeps its exact Unity collision proxies.");

                CityOpenAreaDecorationPlan openAreaPlan =
                    CityOpenAreaDecorationPlanner.Create(layout);
                GameObject openArea = CityOpenAreaWorldBuilder.Build(
                    parent.transform,
                    openAreaPlan);
                Assert.That(
                    openArea.GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer.name.StartsWith(
                            "Imported Open Area Chunk",
                            StringComparison.Ordinal)),
                    Is.GreaterThan(0));
                Assert.That(
                    openArea.transform.Find("Open Area Collision Proxies")
                        .GetComponents<BoxCollider>(),
                    Has.Length.EqualTo(openAreaPlan.Descriptors.Count(
                        descriptor => descriptor.BlocksMovement)));
                Assert.That(
                    openArea.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(1),
                    "The yard spotlight remains a realtime Unity light.");

                GameObject cemetery = CityCemeteryWorldBuilder.Build(
                    parent.transform,
                    CityCemeteryPlanner.Create(layout));
                Assert.That(
                    cemetery.GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer.name.StartsWith(
                            "Imported Cemetery Chunk",
                            StringComparison.Ordinal)),
                    Is.GreaterThan(0));
                Assert.That(
                    cemetery.GetComponentsInChildren<Light>(true),
                    Is.Not.Empty,
                    "Cemetery practical lights remain Unity-owned.");
                Assert.That(
                    cemetery.GetComponentsInChildren<Collider>(true),
                    Is.Not.Empty,
                    "Imported cemetery shells retain plan-owned proxies.");

                GameObject seacoast = CitySeacoastWorldBuilder.Build(
                    parent.transform,
                    CitySeacoastPlanner.Create(layout));
                Assert.That(
                    seacoast.GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer.name.StartsWith(
                            "Imported Seacoast Chunk",
                            StringComparison.Ordinal)),
                    Is.GreaterThan(0));
                Assert.That(
                    seacoast.GetComponentsInChildren<Collider>(true),
                    Is.Not.Empty,
                    "Boats and the barrier retain Unity collision.");

                CityMountainBoundaryPlan mountains =
                    CityMountainBoundaryPlanner.Create(layout);
                CityFringeYardWorldResult fringe =
                    CityFringeYardWorldBuilder.Build(
                        parent.transform,
                        CityFringeYardPlanner.Create(layout, mountains));
                Assert.That(fringe, Is.Not.Null);
                Assert.That(
                    fringe.Root.GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer.name.StartsWith(
                            "Imported Fringe Chunk",
                            StringComparison.Ordinal)),
                    Is.GreaterThan(0));
                Assert.That(
                    fringe.PracticalAnchors,
                    Is.Not.Empty,
                    "Fringe cables/practicals remain plan-owned.");

                GameObject pointsOfInterest =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);
                Renderer[] poiRenderers = pointsOfInterest
                    .GetComponentsInChildren<Renderer>(true);
                Assert.That(
                    poiRenderers.Count(renderer =>
                        renderer.name.StartsWith(
                            "Imported ",
                            StringComparison.Ordinal)),
                    Is.EqualTo(10),
                    "All four static POI shells use the ten authored " +
                    "role meshes.");
                Assert.That(
                    poiRenderers.Any(renderer =>
                        renderer.name == "Dark Water"),
                    Is.True);
                Assert.That(
                    poiRenderers.Any(renderer =>
                        renderer.name == "Scale Needle"),
                    Is.True);
                Assert.That(
                    poiRenderers.Any(renderer =>
                        renderer.name == "Cold Service Lamp"),
                    Is.True);
                Assert.That(
                    pointsOfInterest.GetComponentsInChildren<Cloth>(true),
                    Is.Not.Empty,
                    "Laundry, carpets and canopy rags stay dynamic.");
                Assert.That(
                    pointsOfInterest.GetComponentsInChildren<Light>(true),
                    Is.Not.Empty,
                    "POI floodlights remain Unity-owned.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static bool IsWaveOneDecoration(CityDecorationKind kind)
        {
            switch (kind)
            {
                case CityDecorationKind.IndustrialStacksAndTanks:
                case CityDecorationKind.IndustrialCargo:
                case CityDecorationKind.NightlifeVendingAndQueue:
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                case CityDecorationKind.RoadsidePhoneBooth:
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                    return true;
                default:
                    return false;
            }
        }

        private static ExpectedPart Part(
            CityMiscKind kind,
            int variant,
            CityMiscMeshRole role)
        {
            return Part(kind, variant, role.ToString(), role);
        }

        private static ExpectedPart Part(
            CityMiscKind kind,
            int variant,
            string component,
            CityMiscMeshRole role,
            CityMiscSurfaceKind surface = CityMiscSurfaceKind.Default)
        {
            return new ExpectedPart(
                $"GEO_CMM_{kind}_Variant{variant + 1:00}_{component}",
                kind, variant, component, role, surface);
        }

        private static int PartIndex(
            CityMiscKind kind,
            int variant,
            string component)
        {
            for (int index = 0;
                 index < CityMiscAssetProvider.GetPartCount(kind);
                 index++)
            {
                if (CityMiscAssetProvider.GetExpectedComponent(
                        kind, variant, index) == component)
                {
                    return index;
                }
            }

            Assert.Fail($"No part index for {kind}/{component}.");
            return -1;
        }

        private static string StableIdForVariant(
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            int variant)
        {
            for (int index = 0; index < 1000; index++)
            {
                string stableId = $"v3-contract-{kind}-{index}";
                if (provider.SelectVariant(kind, stableId) == variant)
                {
                    return stableId;
                }
            }

            Assert.Fail($"Could not select {kind} variant {variant}.");
            return string.Empty;
        }

        private sealed class ExpectedPart
        {
            public ExpectedPart(
                string meshName,
                CityMiscKind kind,
                int variant,
                string component,
                CityMiscMeshRole role,
                CityMiscSurfaceKind surface)
            {
                MeshName = meshName;
                Kind = kind;
                Variant = variant;
                Component = component;
                Role = role;
                Surface = surface;
            }

            public string MeshName { get; }
            public CityMiscKind Kind { get; }
            public int Variant { get; }
            public string Component { get; }
            public CityMiscMeshRole Role { get; }
            public CityMiscSurfaceKind Surface { get; }
        }

        [Serializable]
        private sealed class SignatureManifest
        {
            public string build_signature;
        }

        [Serializable]
        private sealed class ContractManifest
        {
            public string design_id;
            public string generator_version;
            public string build_signature;
            public MiscRootContract root_contract;
            public StaticHumanoidContract static_humanoid_contract;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int mesh_count;
            public int assembly_count;
            public int triangle_count;
            public string wave1_compatibility_signature;
            public string v2_compatibility_signature;
        }

        [Serializable]
        private sealed class StaticHumanoidContract
        {
            public string standard;
            public float standard_version;
            public bool rigged;
            public string[] resident_kinds;
            public float standing_equivalent_height_m;
            public float head_width_m;
            public float head_height_m;
            public float heads_tall;
            public float shoulder_joint_span_m;
            public float shoulder_joint_span_head_widths;
            public bool polygon_growth_allowed;
            public ShelterLegacyTriangleCaps legacy_triangle_caps;
        }

        [Serializable]
        private sealed class ShelterLegacyTriangleCaps
        {
            public int NightlifeShelterStandingPerson;
            public int NightlifeShelterSeatedPerson;
            public int NightlifeShelterSleepingPerson;
        }

        [Serializable]
        private sealed class MiscRootContract
        {
            public string origin;
            public string scale_mode;
            public string source_forward_axis;
            public string unity_forward_axis;
            public float legacy_recipe_x_to_unity_local_x;
        }
    }
}
