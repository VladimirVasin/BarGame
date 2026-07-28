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
                Assert.That(actual.IsBar, Is.EqualTo(expected.IsBar));
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
                        left.IsBar == right.IsBar)
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

        private static bool ContainsInclusive(Rect rectangle, Vector3 point)
        {
            return point.x >= rectangle.xMin &&
                   point.x <= rectangle.xMax &&
                   point.z >= rectangle.yMin &&
                   point.z <= rectangle.yMax;
        }
    }
}
