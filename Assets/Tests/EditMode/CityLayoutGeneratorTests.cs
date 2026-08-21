using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class CityLayoutGeneratorTests
    {
        [Test]
        public void Generate_WithSameSeed_ProducesIdenticalLayout()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;

            CityLayout first = CityLayoutGenerator.Generate(settings, 128734);
            CityLayout second = CityLayoutGenerator.Generate(settings, 128734);

            CollectionAssert.AreEqual(first.Nodes, second.Nodes);
            CollectionAssert.AreEqual(first.RoadEdges, second.RoadEdges);
            CollectionAssert.AreEqual(
                first.PathKinds,
                second.PathKinds);
            Assert.That(second.SpawnNode, Is.EqualTo(first.SpawnNode));
            Assert.That(second.SpawnWorldPosition, Is.EqualTo(first.SpawnWorldPosition));
            Assert.That(second.BuildingLots.Count, Is.EqualTo(first.BuildingLots.Count));

            for (int index = 0; index < first.BuildingLots.Count; index++)
            {
                BuildingLot expected = first.BuildingLots[index];
                BuildingLot actual = second.BuildingLots[index];
                Assert.That(actual.Cell, Is.EqualTo(expected.Cell));
                Assert.That(actual.Center, Is.EqualTo(expected.Center));
                Assert.That(actual.Size, Is.EqualTo(expected.Size));
                Assert.That(actual.Height, Is.EqualTo(expected.Height));
                Assert.That(actual.Color, Is.EqualTo(expected.Color));
                Assert.That(
                    actual.District,
                    Is.EqualTo(expected.District));
                Assert.That(
                    actual.LandUse,
                    Is.EqualTo(expected.LandUse));
                Assert.That(actual.IsBar, Is.EqualTo(expected.IsBar));
                Assert.That(
                    actual.IsPlayerHome,
                    Is.EqualTo(expected.IsPlayerHome));
                Assert.That(
                    actual.IsSupermarket,
                    Is.EqualTo(expected.IsSupermarket));
                Assert.That(actual.BarId, Is.EqualTo(expected.BarId));
                Assert.That(
                    actual.BarActivity,
                    Is.EqualTo(expected.BarActivity));
                Assert.That(
                    actual.FrontageDirection,
                    Is.EqualTo(expected.FrontageDirection));
                Assert.That(actual.DoorPosition, Is.EqualTo(expected.DoorPosition));
                Assert.That(actual.ReturnPosition, Is.EqualTo(expected.ReturnPosition));
                Assert.That(
                    actual.SidewalkArrivalPosition,
                    Is.EqualTo(expected.SidewalkArrivalPosition));
            }

            CollectionAssert.AreEqual(
                first.Park.Cells,
                second.Park.Cells);
            CollectionAssert.AreEqual(
                first.Park.Gates,
                second.Park.Gates);
            CollectionAssert.AreEqual(
                first.Park.TreePositions,
                second.Park.TreePositions);
        }

        [Test]
        public void DefaultSettings_CreateNineTimesLargerDistrictCity()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;

            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                GameSessionState.DefaultCitySeed);

            Assert.That(settings.BlocksX, Is.EqualTo(12));
            Assert.That(settings.BlocksZ, Is.EqualTo(12));
            Assert.That(
                settings.RoadWidth,
                Is.EqualTo(CityGenerationSettings.DefaultRoadWidth));
            Assert.That(
                settings.NodeSpacing,
                Is.EqualTo(new Vector2(26f, 26f)));
            Assert.That(layout.BuildingLots, Has.Count.EqualTo(144));
            Assert.That(
                Vector3.Distance(
                    layout.GetNodeWorldPosition(Vector2Int.zero),
                    layout.GetNodeWorldPosition(
                        new Vector2Int(settings.BlocksX, 0))),
                Is.EqualTo(312f).Within(0.001f));
            Assert.That(layout.Districts, Has.Count.EqualTo(5));
            Assert.That(
                layout.Districts.Select(district => district.Kind),
                Is.EquivalentTo(new[]
                {
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential,
                    CityDistrictKind.Industrial,
                    CityDistrictKind.Nightlife,
                    CityDistrictKind.CentralPark
                }));
        }

        [Test]
        public void DefaultSettings_HideOnlyOrdinaryBuildingRoofsInFog()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                GameSessionState.DefaultCitySeed);
            BuildingLot[] ordinary = layout.BuildingLots
                .Where(lot => lot.IsOrdinaryBuilding)
                .ToArray();
            BuildingLot[] bars = layout.BuildingLots
                .Where(lot => lot.IsBar)
                .ToArray();

            Assert.That(
                settings.MinimumOrdinaryBuildingHeight,
                Is.EqualTo(36f));
            Assert.That(
                settings.MaximumOrdinaryBuildingHeight,
                Is.EqualTo(52f));
            Assert.That(
                settings.MinimumBuildingHeight,
                Is.EqualTo(5f));
            Assert.That(
                settings.MaximumBuildingHeight,
                Is.EqualTo(13f));
            Assert.That(ordinary, Is.Not.Empty);
            Assert.That(
                ordinary.All(lot =>
                    lot.Height >=
                        settings.MinimumOrdinaryBuildingHeight &&
                    lot.Height <=
                        settings.MaximumOrdinaryBuildingHeight),
                Is.True);

            float minimumRoofDepth =
                settings.MinimumOrdinaryBuildingHeight -
                CityGenerationSettings
                    .FogHiddenRoofReferenceCameraHeight;
            float fogTerm =
                RuntimeSceneSetup.CityFogDensity * minimumRoofDepth;
            float roofTransmittance =
                Mathf.Exp(-(fogTerm * fogTerm));
            Assert.That(
                roofTransmittance,
                Is.LessThanOrEqualTo(
                    CityGenerationSettings
                        .MaximumFogHiddenRoofTransmittance));

            Assert.That(bars, Has.Length.EqualTo(settings.BarCount));
            Assert.That(
                bars.All(lot =>
                    lot.Height >= settings.MinimumBuildingHeight &&
                    lot.Height <= settings.MaximumBuildingHeight),
                Is.True,
                "Bars must retain their original low-rise range.");
            Assert.That(
                layout.PlayerHome.Height,
                Is.EqualTo(
                        PlayerHomeBalconyGeometry
                            .ResolveBuildingHeight(settings))
                    .Within(0.001f));
            Assert.That(
                layout.Supermarket.Height,
                Is.EqualTo(
                        Mathf.Clamp(
                            6.4f,
                            settings.MinimumBuildingHeight,
                            settings.MaximumBuildingHeight))
                    .Within(0.001f));
        }

        [Test]
        public void DefaultCoastalBlueprint_CreatesReachableBeachLakeAndCemetery()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.DefaultCitySeed);

            Assert.That(
                layout.BlueprintId,
                Is.EqualTo(CityBlueprintCatalog.DefaultBlueprintId));
            Assert.That(layout.Blueprint, Is.SameAs(blueprint));
            Assert.That(
                blueprint.CellBounds.width,
                Is.Not.EqualTo(blueprint.CellBounds.height));
            Assert.That(blueprint.CentralPark, Is.Not.Null);
            Assert.That(blueprint.CentralPark.Cells, Has.Count.EqualTo(16));
            Assert.That(
                layout.Park.Cells,
                Is.EquivalentTo(blueprint.CentralPark.Cells));
            Vector3 westCenterNode = layout.GetGridWorldPosition(
                blueprint.CenterNode);
            Vector3 eastCenterNode = layout.GetGridWorldPosition(
                blueprint.CenterNode + Vector2Int.right);
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        layout.Park.Center.x,
                        layout.Park.Center.z),
                    new Vector2(
                        (westCenterNode.x + eastCenterNode.x) * 0.5f,
                        westCenterNode.z)),
                Is.LessThan(0.001f));

            Assert.That(
                blueprint.CellBounds.xMax,
                Is.GreaterThan(settings.BlocksX));
            Assert.That(
                blueprint.TryGetArea("lake", out CityAreaPlacement lake),
                Is.True);
            Assert.That(
                blueprint.TryGetArea(
                    "cemetery",
                    out CityAreaPlacement cemetery),
                Is.True);
            Assert.That(
                lake.Cells.All(cell => cell.x >= settings.BlocksX),
                Is.True);
            Assert.That(
                cemetery.Cells.All(cell => cell.x >= settings.BlocksX),
                Is.True);
            Assert.That(
                lake.Cells.Count(cell =>
                    lake.GetTopology(cell) == CityCellTopologyKind.Water),
                Is.EqualTo(4));
            Assert.That(
                cemetery.Cells.All(cell =>
                    cemetery.GetTopology(cell) ==
                    CityCellTopologyKind.OpenLand),
                Is.True);
            Assert.That(
                lake.Cells.Concat(cemetery.Cells).All(cell =>
                    !blueprint.CreatesLot(cell)),
                Is.True);
            Assert.That(
                layout.BuildingLots,
                Has.Count.EqualTo(settings.BlocksX * settings.BlocksZ));
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsBar),
                Is.EqualTo(settings.BarCount));
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsPlayerHome),
                Is.EqualTo(1));
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsSupermarket),
                Is.EqualTo(1));

            Assert.That(blueprint.NorthWaterfront, Is.Not.Null);
            int northernCellZ = blueprint.CellBounds.yMax - 1;
            // The water row spans the normalized footprint; the negative
            // west yard column carries no waterfront duty.
            for (int x = 0; x < blueprint.CellBounds.xMax; x++)
            {
                var northernCell = new Vector2Int(x, northernCellZ);
                Assert.That(
                    blueprint.TryGetCell(
                        northernCell,
                        out CityBlueprintCell descriptor),
                    Is.True,
                    northernCell.ToString());
                Assert.That(
                    descriptor.Area.Id,
                    Is.EqualTo(blueprint.NorthWaterfront.Id),
                    northernCell.ToString());
                Assert.That(
                    descriptor.Topology,
                    Is.EqualTo(CityCellTopologyKind.Water),
                    northernCell.ToString());
            }

            // Every declared cell is surfaced. The river is carried
            // by its own definition rather than by an area, so the
            // cells it floods are the one legal surplus.
            var declaredCells = new HashSet<Vector2Int>(
                blueprint.Cells.Select(cell => cell.Cell));
            Assert.That(
                layout.Surfaces
                    .Select(surface => surface.Cell)
                    .Where(declaredCells.Contains),
                Is.EquivalentTo(declaredCells));
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (declaredCells.Contains(surface.Cell))
                {
                    continue;
                }

                Assert.That(
                    surface.Kind,
                    Is.EqualTo(CitySurfaceKind.RiverWater),
                    surface.Cell.ToString());
            }
            Assert.That(
                layout.BuildingLots.All(
                    lot => blueprint.CreatesLot(lot.Cell)),
                Is.True);
            Assert.That(
                layout.BuildingLots.Any(
                    lot =>
                        lot.District ==
                        CityDistrictKind.NorthWaterfront),
                Is.False);

            CityOpenAreaAccessDescriptor access =
                layout.OpenAreaAccesses.Single(candidate =>
                    candidate.Feature ==
                    CityAreaFeatureKind.NorthWaterfront);
            CitySurfaceDescriptor beach = layout.Surfaces.Single(
                surface => surface.Cell == access.Cell);
            CitySurfaceDescriptor water = layout.Surfaces.First(
                surface =>
                    surface.Feature ==
                        CityAreaFeatureKind.NorthWaterfront &&
                    surface.Kind == CitySurfaceKind.Water);
            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);

            Assert.That(layout.HasRoad(access.FrontageEdge), Is.True);
            Assert.That(
                layout.GetPathKind(access.FrontageEdge),
                Is.EqualTo(CityPathKind.Street));
            Assert.That(beach.Kind, Is.EqualTo(CitySurfaceKind.Beach));
            Assert.That(beach.IsWalkable, Is.True);
            Assert.That(walkable.Contains(access.Center, 0.28f), Is.True);
            Assert.That(walkable.Contains(beach.Center, 0.28f), Is.True);
            Assert.That(water.IsWalkable, Is.False);
            Assert.That(layout.IsWater(water.Center), Is.True);
            Assert.That(layout.IsWater(beach.Center), Is.False);
            Assert.That(walkable.Contains(water.Center, 0.28f), Is.False);

            foreach (CityAreaFeatureKind feature in new[]
                     {
                         CityAreaFeatureKind.Lake,
                         CityAreaFeatureKind.Cemetery
                     })
            {
                CityOpenAreaAccessDescriptor openAreaAccess =
                    layout.OpenAreaAccesses.Single(candidate =>
                        candidate.Feature == feature);
                CitySurfaceDescriptor openAreaSurface =
                    layout.Surfaces.Single(surface =>
                        surface.Cell == openAreaAccess.Cell);
                CitySurfaceKind expectedSurfaceKind =
                    feature == CityAreaFeatureKind.Lake
                        ? CitySurfaceKind.LakeShore
                        : CitySurfaceKind.CemeteryGround;

                Assert.That(
                    layout.HasRoad(openAreaAccess.FrontageEdge),
                    Is.True,
                    feature.ToString());
                Assert.That(
                    layout.GetPathKind(openAreaAccess.FrontageEdge),
                    Is.EqualTo(CityPathKind.Street),
                    feature.ToString());
                Assert.That(
                    openAreaSurface.Kind,
                    Is.EqualTo(expectedSurfaceKind));
                Assert.That(openAreaSurface.IsWalkable, Is.True);
                Assert.That(
                    walkable.Contains(openAreaAccess.Center, 0.28f),
                    Is.True,
                    feature.ToString());
                Assert.That(
                    walkable.Contains(openAreaSurface.Center, 0.28f),
                    Is.True,
                    feature.ToString());
            }

            Assert.That(
                layout.Surfaces.Any(surface =>
                    surface.Feature == CityAreaFeatureKind.Lake &&
                    surface.Kind == CitySurfaceKind.Water &&
                    !surface.IsWalkable &&
                    !walkable.Contains(surface.Center, 0.28f)),
                Is.True);
            Assert.That(layout.IsRoadGraphConnected(), Is.True);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void DefaultCoastalBlueprint_CreatesReachableEastYard()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.DefaultCitySeed);

            Assert.That(
                blueprint.TryGetArea(
                    "yard-east",
                    out CityAreaPlacement yard),
                Is.True);
            Assert.That(yard.Cells, Has.Count.EqualTo(24));
            var expectedCells = new List<Vector2Int>();
            for (int x = settings.BlocksX + 1;
                 x < settings.BlocksX + 5;
                 x++)
            {
                for (int z = 2; z < 8; z++)
                {
                    expectedCells.Add(new Vector2Int(x, z));
                }
            }

            Assert.That(yard.Cells, Is.EquivalentTo(expectedCells));
            Assert.That(
                yard.Cells.All(cell =>
                    yard.GetTopology(cell) ==
                    CityCellTopologyKind.OpenLand),
                Is.True);
            Assert.That(
                yard.Cells.All(cell => !blueprint.CreatesLot(cell)),
                Is.True);
            // 200 pre-yard cells + 24 east + 12 south + 12 west.
            Assert.That(blueprint.Cells, Has.Count.EqualTo(248));
            Assert.That(
                yard.Definition.Feature,
                Is.EqualTo(CityAreaFeatureKind.Yard));
            Assert.That(
                yard.Definition.Archetype,
                Is.EqualTo(CityDistrictKind.Yard));
            Assert.That(
                yard.Definition.LocalizationKey,
                Is.EqualTo("map.district.yard"));

            List<CitySurfaceDescriptor> yardSurfaces = layout.Surfaces
                .Where(surface =>
                    string.Equals(
                        surface.AreaId,
                        "yard-east",
                        StringComparison.Ordinal))
                .ToList();
            Assert.That(yardSurfaces, Has.Count.EqualTo(24));
            foreach (CitySurfaceDescriptor surface in yardSurfaces)
            {
                Assert.That(
                    surface.Kind,
                    Is.EqualTo(CitySurfaceKind.OpenGround),
                    surface.Cell.ToString());
                Assert.That(
                    surface.IsWalkable,
                    Is.True,
                    surface.Cell.ToString());
                Assert.That(
                    surface.DatumY,
                    Is.EqualTo(yardSurfaces[0].DatumY).Within(0.001f),
                    surface.Cell.ToString());
            }

            CityOpenAreaAccessDescriptor access =
                layout.OpenAreaAccesses.Single(candidate =>
                    string.Equals(
                        candidate.AreaId,
                        "yard-east",
                        StringComparison.Ordinal));
            Assert.That(access.Id, Is.EqualTo("yard-east-access"));
            Assert.That(
                access.Feature,
                Is.EqualTo(CityAreaFeatureKind.Yard));
            Assert.That(layout.HasRoad(access.FrontageEdge), Is.True);
            Assert.That(
                layout.GetPathKind(access.FrontageEdge),
                Is.EqualTo(CityPathKind.Street));

            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);
            CitySurfaceDescriptor accessSurface = layout.Surfaces.Single(
                surface => surface.Cell == access.Cell);
            Assert.That(
                accessSurface.Kind,
                Is.EqualTo(CitySurfaceKind.OpenGround));
            Assert.That(walkable.Contains(access.Center, 0.28f), Is.True);
            Assert.That(
                walkable.Contains(accessSurface.Center, 0.28f),
                Is.True);

            // The home-yard decorator does not claim typed fringe precincts:
            // the gap immediately left of the home-frontage bar and the
            // separately authored city edge remain distinct systems.
            CityOpenAreaDecorationPlan decorations =
                CityOpenAreaDecorationPlanner.Create(layout);
            Rect pocket = layout.Surfaces
                .Where(surface => string.Equals(
                    surface.AreaId,
                    "yard-east",
                    StringComparison.Ordinal))
                .Select(surface => surface.WorldBounds)
                .Aggregate((left, right) => Rect.MinMaxRect(
                    Mathf.Min(left.xMin, right.xMin),
                    Mathf.Min(left.yMin, right.yMin),
                    Mathf.Max(left.xMax, right.xMax),
                    Mathf.Max(left.yMax, right.yMax)));
            Assert.That(
                decorations.Descriptors.Any(descriptor =>
                    descriptor.Feature == CityAreaFeatureKind.Yard &&
                    pocket.Contains(
                        new Vector2(
                            descriptor.Bounds.center.x,
                            descriptor.Bounds.center.z))),
                Is.False,
                "The eastern pocket carries no home-yard decoration.");

            CityLayout repeat = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.DefaultCitySeed);
            CityOpenAreaAccessDescriptor repeatAccess =
                repeat.OpenAreaAccesses.Single(candidate =>
                    string.Equals(
                        candidate.AreaId,
                        "yard-east",
                        StringComparison.Ordinal));
            Assert.That(repeatAccess.Cell, Is.EqualTo(access.Cell));
            Assert.That(
                repeatAccess.Center,
                Is.EqualTo(access.Center));
            Assert.That(
                repeat.Surfaces
                    .Where(surface =>
                        string.Equals(
                            surface.AreaId,
                            "yard-east",
                            StringComparison.Ordinal))
                    .Select(surface => surface.Center),
                Is.EqualTo(
                    yardSurfaces.Select(surface => surface.Center)));
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void DefaultCoastalBlueprint_CreatesReachablePerimeterYards()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.DefaultCitySeed);
            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);

            string[] perimeterYards =
            {
                "yard-south-west",
                "yard-south-east",
                "yard-west-south",
                "yard-west-north"
            };
            foreach (string yardId in perimeterYards)
            {
                Assert.That(
                    blueprint.TryGetArea(
                        yardId,
                        out CityAreaPlacement yard),
                    Is.True,
                    yardId);
                Assert.That(
                    yard.Cells.All(cell => cell.x < 0 || cell.y < 0),
                    Is.True,
                    yardId);
                Assert.That(
                    yard.Cells.All(cell =>
                        yard.GetTopology(cell) ==
                        CityCellTopologyKind.OpenLand),
                    Is.True,
                    yardId);
                Assert.That(
                    yard.Cells.All(cell => !blueprint.CreatesLot(cell)),
                    Is.True,
                    yardId);

                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses.Single(candidate =>
                        string.Equals(
                            candidate.AreaId,
                            yardId,
                            StringComparison.Ordinal));
                Assert.That(
                    layout.HasRoad(access.FrontageEdge),
                    Is.True,
                    yardId);
                Assert.That(
                    layout.GetPathKind(access.FrontageEdge),
                    Is.EqualTo(CityPathKind.Street),
                    yardId);
                Assert.That(
                    walkable.Contains(access.Center, 0.28f),
                    Is.True,
                    yardId);

                List<CitySurfaceDescriptor> surfaces = layout.Surfaces
                    .Where(surface =>
                        string.Equals(
                            surface.AreaId,
                            yardId,
                            StringComparison.Ordinal))
                    .ToList();
                Assert.That(surfaces, Is.Not.Empty, yardId);
                foreach (CitySurfaceDescriptor surface in surfaces)
                {
                    Assert.That(
                        surface.Kind,
                        Is.EqualTo(CitySurfaceKind.OpenGround),
                        surface.Cell.ToString());
                    Assert.That(
                        surface.IsWalkable,
                        Is.True,
                        surface.Cell.ToString());
                    Assert.That(
                        surface.DatumY,
                        Is.EqualTo(surfaces[0].DatumY).Within(0.001f),
                        surface.Cell.ToString());
                }
            }

            // The lot footprint stays normalized even though the fringe is
            // negative: the coordinate-seeded random streams depend on it.
            Assert.That(
                layout.BuildingLots.All(lot =>
                    lot.Cell.x >= 0 && lot.Cell.y >= 0),
                Is.True);
            Assert.That(
                layout.BuildingLots,
                Has.Count.EqualTo(settings.BlocksX * settings.BlocksZ));
            Assert.That(
                blueprint.ContainsCell(new Vector2Int(-1, -1)),
                Is.False,
                "The south-west corner cell stays void.");
            Assert.That(
                layout.OpenAreaAccesses.Count(candidate =>
                    candidate.Feature == CityAreaFeatureKind.Yard),
                Is.EqualTo(5));
            Assert.That(layout.IsRoadGraphConnected(), Is.True);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void DefaultSeed_KeepsCanonicalHomePlacement()
        {
            // Canary against random-stream drift: the river shift must keep
            // the canonical home beside its partner bar and public places.
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            BuildingLot home = layout.BuildingLots.Single(
                lot => lot.IsPlayerHome);
            Assert.That(home.Cell, Is.EqualTo(new Vector2Int(12, 5)));
            Assert.That(home.Center.x, Is.EqualTo(169f).Within(0.001f));
            Assert.That(home.Center.z, Is.EqualTo(-13f).Within(0.001f));
            Assert.That(
                layout.BuildingLots.Any(lot =>
                    lot.IsBar &&
                    lot.Cell == new Vector2Int(12, 6)),
                Is.True);
            Assert.That(
                layout.DistrictPointsOfInterest,
                Has.Count.EqualTo(4));
        }

        [Test]
        public void SwappingUrbanAreas_PreservesTopologyNodesAndRoads()
        {
            CityBlueprint originalBlueprint =
                CityBlueprintCatalog.Default;
            Assert.That(
                originalBlueprint.TryGetArea(
                    "old-town",
                    out CityAreaPlacement oldTown),
                Is.True);
            Assert.That(
                originalBlueprint.TryGetArea(
                    "residential",
                    out CityAreaPlacement residential),
                Is.True);
            Vector2Int[] oldTownCells = oldTown.Cells.ToArray();
            Vector2Int[] residentialCells =
                residential.Cells.ToArray();
            CityBlueprint swappedBlueprint =
                CityBlueprintBuilder.From(originalBlueprint)
                    .SwapUrbanAreas(oldTown.Id, residential.Id)
                    .Build();
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            const int seed = 271828;

            CityLayout original = CityLayoutGenerator.Generate(
                originalBlueprint,
                settings,
                seed);
            CityLayout swapped = CityLayoutGenerator.Generate(
                swappedBlueprint,
                settings,
                seed);

            Assert.That(
                swappedBlueprint.Cells.Count,
                Is.EqualTo(originalBlueprint.Cells.Count));
            for (int index = 0;
                 index < originalBlueprint.Cells.Count;
                 index++)
            {
                CityBlueprintCell expected =
                    originalBlueprint.Cells[index];
                Assert.That(
                    swappedBlueprint.TryGetCell(
                        expected.Cell,
                        out CityBlueprintCell actual),
                    Is.True,
                    expected.Cell.ToString());
                Assert.That(
                    actual.Topology,
                    Is.EqualTo(expected.Topology),
                    expected.Cell.ToString());
            }

            CollectionAssert.AreEqual(original.Nodes, swapped.Nodes);
            CollectionAssert.AreEqual(
                original.RoadEdges,
                swapped.RoadEdges);
            CollectionAssert.AreEqual(
                original.PathKinds,
                swapped.PathKinds);
            CollectionAssert.AreEqual(
                original.Park.Cells,
                swapped.Park.Cells);
            Assert.That(
                swapped.TryGetArea(
                    oldTownCells[0],
                    out CityAreaDefinition movedOldTownCell),
                Is.True);
            Assert.That(
                movedOldTownCell.Id,
                Is.EqualTo(residential.Id));
            Assert.That(
                swapped.TryGetArea(
                    residentialCells[0],
                    out CityAreaDefinition movedResidentialCell),
                Is.True);
            Assert.That(
                movedResidentialCell.Id,
                Is.EqualTo(oldTown.Id));
            Assert.That(
                swapped.BuildingLots.Single(
                    lot => lot.Cell == oldTownCells[0]).AreaId,
                Is.EqualTo(residential.Id));
            Assert.That(
                swapped.BuildingLots.Single(
                    lot => lot.Cell == residentialCells[0]).AreaId,
                Is.EqualTo(oldTown.Id));
            Assert.DoesNotThrow(swapped.ValidateOrThrow);
        }

        [Test]
        public void IrregularBlueprint_AddsLakeAndCemeteryWithoutFillingHole()
        {
            CityBlueprint source = CityBlueprintCatalog.CreateLegacy(
                CityGenerationSettings.Default);
            Assert.That(
                source.TryGetArea(
                    "industrial",
                    out CityAreaPlacement industrial),
                Is.True);
            Assert.That(
                source.TryGetArea(
                    "nightlife",
                    out CityAreaPlacement nightlife),
                Is.True);

            var waterCell = new Vector2Int(0, 0);
            var shoreCells = new[]
            {
                new Vector2Int(0, 1),
                new Vector2Int(1, 0),
                new Vector2Int(1, 1)
            };
            var lakeCells = new HashSet<Vector2Int>(shoreCells)
            {
                waterCell
            };
            var cemeteryCell = new Vector2Int(11, 0);
            var holeCell = new Vector2Int(11, 1);
            Vector2Int[] remainingIndustrialCells = industrial.Cells
                .Where(cell => !lakeCells.Contains(cell))
                .ToArray();
            Vector2Int[] remainingNightlifeCells = nightlife.Cells
                .Where(cell =>
                    cell != cemeteryCell &&
                    cell != holeCell)
                .ToArray();
            CityAreaDefinition lake =
                CityBlueprintCatalog.CreateLake();
            CityAreaDefinition cemetery =
                CityBlueprintCatalog.CreateCemetery();
            CityBlueprint blueprint = CityBlueprintBuilder.From(source)
                .SetAreaCells(
                    industrial.Definition,
                    remainingIndustrialCells,
                    CityCellTopologyKind.BuildableLand)
                .SetAreaCells(
                    nightlife.Definition,
                    remainingNightlifeCells,
                    CityCellTopologyKind.BuildableLand)
                .AddCells(
                    lake,
                    new[] { waterCell },
                    CityCellTopologyKind.Water)
                .AddCells(
                    lake,
                    shoreCells,
                    CityCellTopologyKind.OpenLand)
                .AddCells(
                    cemetery,
                    new[] { cemeteryCell },
                    CityCellTopologyKind.OpenLand)
                .Build();
            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                CityGenerationSettings.Default,
                314159);

            Assert.That(blueprint.ContainsCell(holeCell), Is.False);
            Assert.That(
                blueprint.Cells.Count,
                Is.LessThan(
                    blueprint.CellBounds.width *
                    blueprint.CellBounds.height));
            Assert.That(
                layout.BuildingLots.Any(lot => lot.Cell == holeCell),
                Is.False);
            Assert.That(
                layout.Surfaces.Any(surface => surface.Cell == holeCell),
                Is.False);
            Assert.That(
                layout.BuildingLots.Any(lot =>
                    lakeCells.Contains(lot.Cell) ||
                    lot.Cell == cemeteryCell),
                Is.False);

            CitySurfaceDescriptor water = layout.Surfaces.Single(
                surface => surface.Cell == waterCell);
            Assert.That(water.Kind, Is.EqualTo(CitySurfaceKind.Water));
            Assert.That(water.IsWalkable, Is.False);
            Assert.That(
                blueprint.ParticipatesInRoadGrid(Vector2Int.right),
                Is.False);
            Assert.That(
                blueprint.ParticipatesInRoadGrid(Vector2Int.up),
                Is.False);
            Assert.That(
                layout.Surfaces
                    .Where(surface =>
                        shoreCells.Contains(surface.Cell))
                    .All(surface =>
                        surface.Kind == CitySurfaceKind.LakeShore &&
                        surface.IsWalkable),
                Is.True);
            CitySurfaceDescriptor cemeterySurface =
                layout.Surfaces.Single(
                    surface => surface.Cell == cemeteryCell);
            Assert.That(
                cemeterySurface.Kind,
                Is.EqualTo(CitySurfaceKind.CemeteryGround));
            Assert.That(cemeterySurface.IsWalkable, Is.True);
            Assert.That(
                layout.OpenAreaAccesses.Any(access =>
                    access.Feature == CityAreaFeatureKind.Lake),
                Is.True);
            Assert.That(
                layout.OpenAreaAccesses.Any(access =>
                    access.Feature == CityAreaFeatureKind.Cemetery),
                Is.True);
            Assert.That(layout.IsRoadGraphConnected(), Is.True);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void DefaultSettings_CreateConnectedWalkableCentralPark()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                73119);

            Assert.That(layout.Park.IsEnabled, Is.True);
            Assert.That(layout.Park.Cells, Has.Count.EqualTo(16));
            Assert.That(layout.Park.Gates, Has.Count.EqualTo(4));
            Assert.That(
                layout.BuildingLots
                    .Where(lot => lot.IsPark)
                    .All(lot =>
                        !lot.HasBuilding &&
                        !lot.IsBar &&
                        lot.District ==
                        CityDistrictKind.CentralPark),
                Is.True);
            Assert.That(
                layout.RoadEdges.Count(edge =>
                    layout.GetPathKind(edge) ==
                    CityPathKind.ParkPath),
                Is.GreaterThanOrEqualTo(8));

            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);
            Assert.That(
                walkable.Contains(layout.Park.Center),
                Is.True);
            for (int index = 0;
                 index < layout.Park.Gates.Count;
                 index++)
            {
                Assert.That(
                    walkable.Contains(layout.Park.Gates[index].Center),
                    Is.True,
                    layout.Park.Gates[index].Id);
            }
        }

        [Test]
        public void DefaultSettings_PlaceFourDistantBarsInUrbanDistricts()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            BuildingLot[] bars = layout.BuildingLots
                .Where(lot => lot.IsBar)
                .ToArray();

            Assert.That(bars, Has.Length.EqualTo(4));
            Assert.That(
                bars.Select(bar => bar.District),
                Is.Unique);
            Assert.That(
                bars.All(bar =>
                    bar.HasBuilding &&
                    !bar.IsPark &&
                    layout.GetPathKind(
                        RoadEdge.ForCellFrontage(
                            bar.Cell,
                            bar.FrontageDirection)) ==
                    CityPathKind.Street),
                Is.True);

            for (int first = 0; first < bars.Length; first++)
            {
                for (int second = first + 1;
                     second < bars.Length;
                     second++)
                {
                    Assert.That(
                        CityTravelDistance.BetweenBars(
                            layout,
                            bars[first],
                            bars[second]),
                        Is.GreaterThanOrEqualTo(
                            layout.MinimumBarRouteDistance - 0.001f));
                }
            }
        }

        [TestCase(GameSessionState.DefaultCitySeed)]
        [TestCase(73119)]
        [TestCase(-99123)]
        public void Generate_WithBars_SpawnsWalkablyNearABar(int seed)
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);
            float nearestBarDistance = layout.BuildingLots
                .Where(lot => lot.IsBar)
                .Min(lot =>
                    CityRoutePathfinder.Build(
                        layout,
                        layout.SpawnWorldPosition,
                        new[] { lot })
                    .TotalLength);

            Assert.That(
                layout.SpawnWorldPosition,
                Is.EqualTo(
                    layout.GetNodeWorldPosition(layout.SpawnNode)));
            Assert.That(
                walkable.Contains(layout.SpawnWorldPosition, 0.32f),
                Is.True);
            Assert.That(
                nearestBarDistance,
                Is.LessThanOrEqualTo(
                    Mathf.Max(
                        layout.NodeSpacing.x,
                        layout.NodeSpacing.y) *
                    0.5f +
                    0.001f));
        }

        [Test]
        public void Generate_WithoutBars_FallsBackToCentralRoadNode()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BarCount = 0;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout = CityLayoutGenerator.Generate(
                settings,
                17029);
            var expectedNode = new Vector2Int(
                settings.BlocksX / 2,
                settings.BlocksZ / 2);

            Assert.That(layout.SpawnNode, Is.EqualTo(expectedNode));
            Assert.That(
                layout.SpawnWorldPosition,
                Is.EqualTo(layout.GetNodeWorldPosition(expectedNode)));
        }

        [Test]
        public void Generate_WithDifferentSeed_ChangesRoadsOrLots()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;

            CityLayout first = CityLayoutGenerator.Generate(settings, 101);
            CityLayout second = CityLayoutGenerator.Generate(settings, 202);

            bool sameRoads = first.RoadEdges.SequenceEqual(second.RoadEdges);
            bool sameLots = first.BuildingLots
                .Zip(
                    second.BuildingLots,
                    (left, right) =>
                        left.Height == right.Height &&
                        left.Color == right.Color &&
                        left.IsBar == right.IsBar &&
                        left.IsPlayerHome == right.IsPlayerHome &&
                        left.IsSupermarket == right.IsSupermarket)
                .All(value => value);
            Assert.That(sameRoads && sameLots, Is.False);
        }

        [Test]
        public void Generate_RoadGraphConnectsEveryNode()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                -99123);

            var visited = new HashSet<Vector2Int>();
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(layout.SpawnNode);
            visited.Add(layout.SpawnNode);

            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();
                foreach (RoadEdge edge in layout.RoadEdges)
                {
                    if (!edge.Contains(current))
                    {
                        continue;
                    }

                    Vector2Int neighbour = edge.Other(current);
                    if (visited.Add(neighbour))
                    {
                        pending.Enqueue(neighbour);
                    }
                }
            }

            Assert.That(visited.Count, Is.EqualTo(layout.Nodes.Count));
            Assert.That(layout.IsRoadGraphConnected(), Is.True);
        }

        [Test]
        public void Generate_CreatesExactlyConfiguredBarCount()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;
            settings.BarCount = 5;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout = CityLayoutGenerator.Generate(settings, 77);

            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsBar),
                Is.EqualTo(settings.BarCount));
            Assert.That(
                layout.BuildingLots.Where(lot => lot.IsBar).Select(lot => lot.BarId),
                Is.Unique);
        }

        [Test]
        public void DefaultSettings_CreateFourActivityBars()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;

            Assert.That(settings.BarCount, Is.EqualTo(4));

            CityLayout layout = CityLayoutGenerator.Generate(settings, 48125);
            Assert.That(
                layout.BuildingLots.Count(lot => lot.IsBar),
                Is.EqualTo(4));
            Assert.That(
                layout.BuildingLots
                    .Where(lot => lot.IsBar)
                    .Select(lot => lot.BarActivity),
                Is.EquivalentTo(new[]
                {
                    BarActivityKind.Cocktail,
                    BarActivityKind.BeerPong,
                    BarActivityKind.SplitTheG,
                    BarActivityKind.TinctureMatch
                }));
        }

        [Test]
        public void Generate_AssignsActivitiesByRowMajorOrder()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;
            settings.BarCount = 5;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout = CityLayoutGenerator.Generate(settings, 91275);
            BuildingLot[] orderedBars = layout.BuildingLots
                .Where(lot => lot.IsBar)
                .OrderBy(lot => lot.Cell.y)
                .ThenBy(lot => lot.Cell.x)
                .ToArray();

            Assert.That(orderedBars, Has.Length.EqualTo(settings.BarCount));
            for (int ordinal = 0; ordinal < orderedBars.Length; ordinal++)
            {
                BarActivityKind expected =
                    BarActivityAssignment.Resolve(ordinal);
                Assert.That(
                    orderedBars[ordinal].BarActivity,
                    Is.EqualTo(expected),
                    $"Unexpected activity for row-major bar ordinal {ordinal}.");
            }

            Assert.That(
                layout.BuildingLots
                    .Where(lot => !lot.IsBar)
                    .All(lot => lot.BarActivity == BarActivityKind.None),
                Is.True);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [TestCase(0, BarActivityKind.Cocktail)]
        [TestCase(1, BarActivityKind.BeerPong)]
        [TestCase(2, BarActivityKind.SplitTheG)]
        [TestCase(3, BarActivityKind.TinctureMatch)]
        [TestCase(4, BarActivityKind.Cocktail)]
        [TestCase(17, BarActivityKind.Cocktail)]
        public void BarActivityAssignment_ResolvesRowMajorOrdinal(
            int ordinal,
            BarActivityKind expected)
        {
            Assert.That(
                BarActivityAssignment.Resolve(ordinal),
                Is.EqualTo(expected));
        }

        [Test]
        public void BarActivityAssignment_RejectsNegativeOrdinal()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BarActivityAssignment.Resolve(-1));
        }

        [Test]
        public void Generate_EveryBarDoorFacesItsReachableFrontageRoad()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                8844);

            foreach (BuildingLot bar in layout.BuildingLots.Where(lot => lot.IsBar))
            {
                Assert.That(bar.HasRoadFrontage, Is.True, bar.BarId);
                Assert.That(
                    layout.TryGetFrontageEdge(bar, out RoadEdge edge),
                    Is.True,
                    bar.BarId);

                Rect road = layout.GetRoadRect(edge);
                Assert.That(
                    ContainsInclusive(road, bar.ReturnPosition),
                    Is.True,
                    bar.BarId);
                Assert.That(
                    ContainsInclusive(
                        road,
                        bar.SidewalkArrivalPosition),
                    Is.True,
                    bar.BarId);

                Vector3 expectedDirection = new Vector3(
                    bar.FrontageDirection.x,
                    0f,
                    bar.FrontageDirection.y);
                Vector3 doorDirection = bar.DoorPosition - bar.Center;
                Vector3 returnDirection = bar.ReturnPosition - bar.DoorPosition;
                Vector3 sidewalkDirection =
                    bar.SidewalkArrivalPosition - bar.DoorPosition;
                Assert.That(
                    Vector3.Dot(doorDirection, expectedDirection),
                    Is.GreaterThan(0f),
                    bar.BarId);
                Assert.That(
                    Vector3.Dot(returnDirection, expectedDirection),
                    Is.GreaterThan(0f),
                    bar.BarId);
                Assert.That(
                    Vector3.Dot(sidewalkDirection, expectedDirection),
                    Is.GreaterThan(0f),
                    bar.BarId);
                Assert.That(
                    Vector3.Dot(
                        bar.ReturnPosition -
                        bar.SidewalkArrivalPosition,
                        expectedDirection),
                    Is.EqualTo(
                            layout.RoadWidth * 0.5f -
                            CityStreetSurfacePlanner.SidewalkWidth * 0.5f)
                        .Within(0.001f),
                    bar.BarId);
                Assert.That(
                    Vector3.Cross(doorDirection, expectedDirection).sqrMagnitude,
                    Is.LessThan(0.0001f),
                    bar.BarId);
            }
        }

        [Test]
        public void DefaultSettings_CreateOneOpenPointInEveryUrbanDistrict()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            Assert.That(
                layout.DistrictPointsOfInterest,
                Has.Count.EqualTo(4));
            Assert.That(
                layout.DistrictPointsOfInterest
                    .Select(point => point.District),
                Is.EquivalentTo(new[]
                {
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential,
                    CityDistrictKind.Industrial,
                    CityDistrictKind.Nightlife
                }));
            Assert.That(
                layout.DistrictPointsOfInterest
                    .Select(point => point.Kind),
                Is.EquivalentTo(new[]
                {
                    CityDistrictPointOfInterestKind
                        .OldTownWaterworksCourt,
                    CityDistrictPointOfInterestKind
                        .ResidentialDryingYard,
                    CityDistrictPointOfInterestKind
                        .IndustrialWeighbridge,
                    CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland
                }));
            Assert.That(
                layout.DistrictPointsOfInterest.Select(point => point.Id),
                Is.Unique);

            foreach (CityDistrictPointOfInterestDescriptor point
                     in layout.DistrictPointsOfInterest)
            {
                BuildingLot lot = layout.BuildingLots.Single(
                    candidate => candidate.Cell == point.Cell);
                Assert.That(lot.IsDistrictPointOfInterest, Is.True);
                Assert.That(
                    lot.LandUse,
                    Is.EqualTo(
                        CityLandUseKind.DistrictPointOfInterest));
                Assert.That(lot.HasBuilding, Is.False);
                Assert.That(lot.IsPark, Is.False);
                Assert.That(lot.IsBar, Is.False);
                Assert.That(lot.IsPlayerHome, Is.False);
                Assert.That(lot.IsSupermarket, Is.False);
                Assert.That(lot.District, Is.EqualTo(point.District));
                Assert.That(point.Center, Is.EqualTo(lot.Center));
                Assert.That(point.PublicBounds.width, Is.EqualTo(18f));
                Assert.That(point.PublicBounds.height, Is.EqualTo(18f));
                Assert.That(point.Accesses, Is.Not.Empty);
                Assert.That(
                    layout.TryGetDistrictPointOfInterest(
                        point.Cell,
                        out CityDistrictPointOfInterestDescriptor indexed),
                    Is.True);
                Assert.That(indexed, Is.SameAs(point));

                foreach (
                    CityDistrictPointOfInterestAccessDescriptor access
                    in point.Accesses)
                {
                    Assert.That(
                        layout.HasRoad(access.FrontageEdge),
                        Is.True,
                        access.Id);
                    Assert.That(
                        layout.GetPathKind(access.FrontageEdge),
                        Is.EqualTo(CityPathKind.Street),
                        access.Id);
                    float expectedWidth =
                        access.StreetSideDirection.x != 0
                            ? point.PublicBounds.height
                            : point.PublicBounds.width;
                    Assert.That(
                        access.Width,
                        Is.EqualTo(expectedWidth).Within(0.001f),
                        access.Id);
                    Assert.That(
                        access.ApproachBounds.width,
                        Is.GreaterThan(0f));
                    Assert.That(
                        access.ApproachBounds.height,
                        Is.GreaterThan(0f));
                }

                Assert.That(
                    layout.TryGetPrimaryLandmarkCell(
                        point.District,
                        out Vector2Int primaryCell),
                    Is.True);
                Assert.That(primaryCell, Is.Not.EqualTo(point.Cell));
                Assert.That(
                    layout.BuildingLots.Single(
                        candidate => candidate.Cell == primaryCell)
                        .HasBuilding,
                    Is.True);
            }

            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void DistrictPoints_WithSameSeed_AreDeterministic()
        {
            CityLayout first = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                -470119);
            CityLayout second = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                -470119);

            Assert.That(
                second.DistrictPointsOfInterest.Count,
                Is.EqualTo(first.DistrictPointsOfInterest.Count));
            for (int index = 0;
                 index < first.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor expected =
                    first.DistrictPointsOfInterest[index];
                CityDistrictPointOfInterestDescriptor actual =
                    second.DistrictPointsOfInterest[index];
                Assert.That(actual.Id, Is.EqualTo(expected.Id));
                Assert.That(actual.District, Is.EqualTo(expected.District));
                Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
                Assert.That(actual.Cell, Is.EqualTo(expected.Cell));
                Assert.That(actual.Center, Is.EqualTo(expected.Center));
                Assert.That(
                    actual.PublicBounds,
                    Is.EqualTo(expected.PublicBounds));
                Assert.That(
                    actual.Accesses.Count,
                    Is.EqualTo(expected.Accesses.Count));
                for (int accessIndex = 0;
                     accessIndex < expected.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor
                        expectedAccess = expected.Accesses[accessIndex];
                    CityDistrictPointOfInterestAccessDescriptor
                        actualAccess = actual.Accesses[accessIndex];
                    Assert.That(
                        actualAccess.Id,
                        Is.EqualTo(expectedAccess.Id));
                    Assert.That(
                        actualAccess.StreetSideDirection,
                        Is.EqualTo(expectedAccess.StreetSideDirection));
                    Assert.That(
                        actualAccess.Center,
                        Is.EqualTo(expectedAccess.Center));
                    Assert.That(
                        actualAccess.OutwardNormal,
                        Is.EqualTo(expectedAccess.OutwardNormal));
                    Assert.That(
                        actualAccess.Width,
                        Is.EqualTo(expectedAccess.Width));
                    Assert.That(
                        actualAccess.ApproachBounds,
                        Is.EqualTo(expectedAccess.ApproachBounds));
                    Assert.That(
                        actualAccess.FrontageEdge,
                        Is.EqualTo(expectedAccess.FrontageEdge));
                }
            }
        }

        [Test]
        public void CompactDistrictWithoutSpareLot_OmitsOpenPoint()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlocksX = 1;
            settings.BlocksZ = 1;
            settings.BarCount = 0;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 17031);

            Assert.That(layout.PrimaryLandmarkCells, Has.Count.EqualTo(1));
            Assert.That(layout.DistrictPointsOfInterest, Is.Empty);
            Assert.That(
                layout.BuildingLots.Single().HasBuilding,
                Is.True);
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        public void UndersizedBlocks_OmitAuthoredOpenPoints()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.BlockWidth =
                CityLayoutGenerator.MinimumDistrictPointLotDimension - 1f;
            settings.BlockDepth =
                CityLayoutGenerator.MinimumDistrictPointLotDimension - 1f;
            settings.BarCount = 0;
            settings.MinimumBarRouteDistance = 0f;

            CityLayout layout =
                CityLayoutGenerator.Generate(settings, 17032);

            Assert.That(layout.DistrictPointsOfInterest, Is.Empty);
            Assert.That(layout.PrimaryLandmarkCells, Has.Count.EqualTo(4));
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        private static bool ContainsInclusive(Rect rectangle, Vector3 point)
        {
            return point.x >= rectangle.xMin &&
                   point.x <= rectangle.xMax &&
                   point.z >= rectangle.yMin &&
                   point.z <= rectangle.yMax;
        }
    }
}
