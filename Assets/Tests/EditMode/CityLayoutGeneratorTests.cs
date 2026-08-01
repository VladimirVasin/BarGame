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
                Assert.That(actual.BarId, Is.EqualTo(expected.BarId));
                Assert.That(
                    actual.BarActivity,
                    Is.EqualTo(expected.BarActivity));
                Assert.That(
                    actual.FrontageDirection,
                    Is.EqualTo(expected.FrontageDirection));
                Assert.That(actual.DoorPosition, Is.EqualTo(expected.DoorPosition));
                Assert.That(actual.ReturnPosition, Is.EqualTo(expected.ReturnPosition));
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
            Assert.That(layout.BuildingLots, Has.Count.EqualTo(144));
            Assert.That(
                Vector3.Distance(
                    layout.GetNodeWorldPosition(Vector2Int.zero),
                    layout.GetNodeWorldPosition(
                        new Vector2Int(settings.BlocksX, 0))),
                Is.EqualTo(288f).Within(0.001f));
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
                        left.IsPlayerHome == right.IsPlayerHome)
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

                Vector3 expectedDirection = new Vector3(
                    bar.FrontageDirection.x,
                    0f,
                    bar.FrontageDirection.y);
                Vector3 doorDirection = bar.DoorPosition - bar.Center;
                Vector3 returnDirection = bar.ReturnPosition - bar.DoorPosition;
                Assert.That(
                    Vector3.Dot(doorDirection, expectedDirection),
                    Is.GreaterThan(0f),
                    bar.BarId);
                Assert.That(
                    Vector3.Dot(returnDirection, expectedDirection),
                    Is.GreaterThan(0f),
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
