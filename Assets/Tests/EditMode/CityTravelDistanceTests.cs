using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTravelDistanceTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void BetweenBars_IsSymmetricAndDeterministic()
        {
            CityLayout layout = CreateLayout();
            BuildingLot[] bars = GetBars(layout);

            for (int firstIndex = 0;
                 firstIndex < bars.Length;
                 firstIndex++)
            {
                for (int secondIndex = firstIndex + 1;
                     secondIndex < bars.Length;
                     secondIndex++)
                {
                    float forward = CityTravelDistance.BetweenBars(
                        layout,
                        bars[firstIndex],
                        bars[secondIndex]);
                    float repeated = CityTravelDistance.BetweenBars(
                        layout,
                        bars[firstIndex],
                        bars[secondIndex]);
                    float reverse = CityTravelDistance.BetweenBars(
                        layout,
                        bars[secondIndex],
                        bars[firstIndex]);

                    Assert.That(repeated, Is.EqualTo(forward));
                    Assert.That(
                        reverse,
                        Is.EqualTo(forward).Within(Tolerance));
                }
            }
        }

        [Test]
        public void BetweenBars_WithSameBar_ReturnsZero()
        {
            CityLayout layout = CreateLayout();
            BuildingLot bar = GetBars(layout)[0];

            float distance = CityTravelDistance.BetweenBars(
                layout,
                bar,
                bar);

            Assert.That(distance, Is.Zero.Within(Tolerance));
        }

        [Test]
        public void BetweenBars_MatchesSingleStopRouteLength()
        {
            CityLayout layout = CreateLayout();
            BuildingLot[] bars = GetBars(layout);
            BuildingLot first = bars[0];
            BuildingLot second = bars[1];

            float distance = CityTravelDistance.BetweenBars(
                layout,
                first,
                second);
            CityRoutePath route = CityRoutePathfinder.Build(
                layout,
                first.ReturnPosition,
                new[] { second });

            Assert.That(route.IsEmpty, Is.False);
            Assert.That(
                distance,
                Is.EqualTo(route.TotalLength).Within(Tolerance));
        }

        [Test]
        public void BetweenAnchors_UsesPartialAndWorldWeightedEdges()
        {
            var nodes = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(2, 0)
            };
            var firstEdge = new RoadEdge(nodes[0], nodes[1]);
            var secondEdge = new RoadEdge(nodes[1], nodes[2]);
            RoadEdge[] edges = { firstEdge, secondEdge };
            Vector3 GetPosition(Vector2Int node)
            {
                if (node == nodes[0])
                {
                    return Vector3.zero;
                }

                return node == nodes[1]
                    ? new Vector3(3f, 0f, 0f)
                    : new Vector3(13f, 0f, 0f);
            }

            float forward = InvokeBetweenAnchors(
                nodes,
                edges,
                GetPosition,
                firstEdge,
                new Vector3(1f, 0f, 0f),
                secondEdge,
                new Vector3(7f, 0f, 0f));
            float reverse = InvokeBetweenAnchors(
                nodes,
                edges,
                GetPosition,
                secondEdge,
                new Vector3(7f, 0f, 0f),
                firstEdge,
                new Vector3(1f, 0f, 0f));

            Assert.That(forward, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(reverse, Is.EqualTo(forward).Within(Tolerance));
        }

        [Test]
        public void BetweenAnchors_OnSameEdge_UsesDirectSegment()
        {
            var nodes = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            };
            var edge = new RoadEdge(nodes[0], nodes[1]);
            RoadEdge[] edges = { edge };
            Vector3 GetPosition(Vector2Int node)
            {
                return new Vector3(node.x * 10f, 0f, 0f);
            }

            float distance = InvokeBetweenAnchors(
                nodes,
                edges,
                GetPosition,
                edge,
                new Vector3(2f, 0f, 0f),
                edge,
                new Vector3(8f, 0f, 0f));
            float sameAnchor = InvokeBetweenAnchors(
                nodes,
                edges,
                GetPosition,
                edge,
                new Vector3(4f, 0f, 0f),
                edge,
                new Vector3(4f, 0f, 0f));

            Assert.That(distance, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(sameAnchor, Is.Zero.Within(Tolerance));
        }

        [Test]
        public void BetweenBars_WithInvalidArguments_ThrowsPredictably()
        {
            CityLayout layout = CreateLayout();
            BuildingLot[] bars = GetBars(layout);
            BuildingLot nonBar =
                layout.BuildingLots.First(lot => !lot.IsBar);
            CityLayout foreignLayout = CreateLayout(layout.Seed + 1);
            BuildingLot foreignBar = GetBars(foreignLayout)[0];

            Assert.Throws<ArgumentNullException>(
                () => CityTravelDistance.BetweenBars(
                    null,
                    bars[0],
                    bars[1]));
            Assert.Throws<ArgumentNullException>(
                () => CityTravelDistance.BetweenBars(
                    layout,
                    null,
                    bars[1]));
            Assert.Throws<ArgumentNullException>(
                () => CityTravelDistance.BetweenBars(
                    layout,
                    bars[0],
                    null));
            Assert.Throws<ArgumentException>(
                () => CityTravelDistance.BetweenBars(
                    layout,
                    nonBar,
                    bars[0]));
            Assert.Throws<ArgumentException>(
                () => CityTravelDistance.BetweenBars(
                    layout,
                    bars[0],
                    foreignBar));
        }

        private static CityLayout CreateLayout(int seed = 48125)
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            settings.MinimumBarRouteDistance = 0f;
            return CityLayoutGenerator.Generate(settings, seed);
        }

        private static BuildingLot[] GetBars(CityLayout layout)
        {
            return layout.BuildingLots
                .Where(lot => lot.IsBar)
                .ToArray();
        }

        private static float InvokeBetweenAnchors(
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> edges,
            Func<Vector2Int, Vector3> getNodeWorldPosition,
            RoadEdge firstEdge,
            Vector3 firstAnchor,
            RoadEdge secondEdge,
            Vector3 secondAnchor)
        {
            MethodInfo method = typeof(CityTravelDistance).GetMethod(
                "BetweenAnchors",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object result = method.Invoke(
                null,
                new object[]
                {
                    nodes,
                    edges,
                    getNodeWorldPosition,
                    firstEdge,
                    firstAnchor,
                    secondEdge,
                    secondAnchor
                });
            return (float)result;
        }
    }
}
