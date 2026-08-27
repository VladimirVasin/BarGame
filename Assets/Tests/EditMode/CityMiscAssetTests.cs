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
            Part(CityMiscKind.LotGroundDownpipeOutfall, 0, "Masonry", CityMiscMeshRole.Masonry)
        };

        [Test]
        public void Catalog_DeclaresExactNinetySevenAssemblyOneNinetyTwoMeshWave()
        {
            Assert.That(
                CityMiscAssetProvider.DesignId,
                Is.EqualTo("city_misc_citywide_v4"));
            Assert.That(
                CityMiscAssetProvider.ExpectedAssemblyCount,
                Is.EqualTo(97));
            Assert.That(
                CityMiscAssetProvider.ExpectedMeshCount,
                Is.EqualTo(192));
            Assert.That(ExpectedParts, Has.Length.EqualTo(192));

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

            Assert.That(assemblies, Is.EqualTo(97));
            Assert.That(
                actualNames,
                Is.EqualTo(ExpectedParts.Select(part => part.MeshName)));
            Assert.That(
                actualNames.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(192));
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
            Assert.That(manifest.mesh_count, Is.EqualTo(192));
            Assert.That(manifest.assembly_count, Is.EqualTo(97));
            Assert.That(manifest.triangle_count, Is.GreaterThan(0));
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
            Assert.That(meshes, Has.Length.EqualTo(192));
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

            Assert.That(actualNames, Has.Count.EqualTo(192));
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
            Assert.That(migrated, Has.Length.EqualTo(81));

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
            public MiscRootContract root_contract;
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
