using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRoutePathfinderTests
    {
        private const float PositionTolerance = 0.001f;

        [Test]
        public void Build_WithNoStops_ReturnsEmptyPath()
        {
            CityLayout layout = CreateLayout();

            CityRoutePath path = CityRoutePathfinder.Build(
                layout,
                layout.SpawnWorldPosition,
                Array.Empty<BuildingLot>());

            Assert.That(path, Is.Not.Null);
            Assert.That(path.IsEmpty, Is.True);
            Assert.That(path.Points, Is.Empty);
            Assert.That(path.TotalLength, Is.Zero);
        }

        [Test]
        public void Build_WithSameInputs_ProducesIdenticalRoute()
        {
            CityLayout layout = CreateLayout();
            BuildingLot[] stops = GetBars(layout).ToArray();
            Vector3 start = layout.SpawnWorldPosition +
                            new Vector3(1.2f, 0.4f, -0.8f);

            CityRoutePath first = CityRoutePathfinder.Build(
                layout,
                start,
                stops);
            CityRoutePath second = CityRoutePathfinder.Build(
                layout,
                start,
                stops);

            CollectionAssert.AreEqual(first.Points, second.Points);
            Assert.That(second.TotalLength, Is.EqualTo(first.TotalLength));
        }

        [Test]
        public void Build_VisitsStopsInOrderAndEndsAtLastReturnPosition()
        {
            CityLayout layout = CreateLayout();
            List<BuildingLot> bars = GetBars(layout);
            BuildingLot firstStop = bars[0];
            BuildingLot secondStop = bars[1];

            CityRoutePath forward = CityRoutePathfinder.Build(
                layout,
                layout.SpawnWorldPosition,
                new[] { firstStop, secondStop });
            CityRoutePath reverse = CityRoutePathfinder.Build(
                layout,
                layout.SpawnWorldPosition,
                new[] { secondStop, firstStop });

            Assert.That(forward.IsEmpty, Is.False);
            Assert.That(
                Vector3.Distance(
                    forward.Points[forward.Points.Count - 1],
                    Flatten(secondStop.ReturnPosition)),
                Is.LessThan(PositionTolerance));
            Assert.That(
                Vector3.Distance(
                    reverse.Points[reverse.Points.Count - 1],
                    Flatten(firstStop.ReturnPosition)),
                Is.LessThan(PositionTolerance));
            Assert.That(
                PolylineContains(forward.Points, firstStop.ReturnPosition),
                Is.True,
                "The first ordered stop must lie on the combined route.");
            Assert.That(
                PolylineContains(forward.Points, secondStop.ReturnPosition),
                Is.True,
                "The second ordered stop must lie on the combined route.");
        }

        [Test]
        public void Build_ProducesOnlyConnectedRoadSegments()
        {
            CityLayout layout = CreateLayout();
            Vector3 startPosition =
                layout.SpawnWorldPosition + new Vector3(1.1f, 0f, 1.4f);
            CityRoutePath path = CityRoutePathfinder.Build(
                layout,
                startPosition,
                GetBars(layout));
            RoadWalkableArea walkableArea =
                RoadWalkableArea.FromLayout(layout);

            Assert.That(path.IsEmpty, Is.False);
            Assert.That(
                Vector3.Distance(
                    path.Points[0],
                    Flatten(startPosition)),
                Is.LessThan(PositionTolerance));
            for (int index = 1; index < path.Points.Count; index++)
            {
                Assert.That(
                    IsOnRoadCenterLine(layout, path.Points[index]),
                    Is.True,
                    $"Route point {index} is not on a road center line.");
            }

            for (int index = 1; index < path.Points.Count; index++)
            {
                Vector3 start = path.Points[index - 1];
                Vector3 end = path.Points[index];
                Assert.That(
                    Mathf.Abs(start.x - end.x) <= PositionTolerance ||
                    Mathf.Abs(start.z - end.z) <= PositionTolerance,
                    Is.True,
                    $"Route segment {index - 1} cuts diagonally through a block.");

                for (int sample = 0; sample <= 16; sample++)
                {
                    Vector3 point = Vector3.Lerp(
                        start,
                        end,
                        sample / 16f);
                    Assert.That(
                        walkableArea.Contains(point),
                        Is.True,
                        $"Route segment {index - 1} leaves the road graph.");
                }
            }
        }

        [Test]
        public void Build_IncludesPlayerConnectorInPointsAndLength()
        {
            CityLayout layout = CreateLayout();
            BuildingLot stop = GetBars(layout)[0];
            Assert.That(
                layout.TryGetFrontageEdge(stop, out RoadEdge frontage),
                Is.True);
            Vector3 edgeStart = layout.GetNodeWorldPosition(frontage.A);
            Vector3 edgeEnd = layout.GetNodeWorldPosition(frontage.B);
            Vector3 projectedStart = Vector3.Lerp(
                edgeStart,
                edgeEnd,
                0.37f);
            Vector3 edgeDirection = (edgeEnd - edgeStart).normalized;
            Vector3 perpendicular = new Vector3(
                -edgeDirection.z,
                0f,
                edgeDirection.x);
            Vector3 playerStart =
                projectedStart + (perpendicular * 1.1f) + (Vector3.up * 0.4f);

            CityRoutePath fromPlayer = CityRoutePathfinder.Build(
                layout,
                playerStart,
                new[] { stop });
            CityRoutePath fromRoad = CityRoutePathfinder.Build(
                layout,
                projectedStart,
                new[] { stop });

            Assert.That(
                Vector3.Distance(
                    fromPlayer.Points[0],
                    Flatten(playerStart)),
                Is.LessThan(PositionTolerance));
            Assert.That(
                fromPlayer.TotalLength,
                Is.EqualTo(
                        Vector3.Distance(
                            Flatten(playerStart),
                            projectedStart) +
                        fromRoad.TotalLength)
                    .Within(PositionTolerance));
        }

        [Test]
        public void Build_OnOneFrontageEdge_UsesDirectWeightedDistance()
        {
            CityLayout layout = CreateLayout();
            BuildingLot stop = GetBars(layout)[0];
            Assert.That(
                layout.TryGetFrontageEdge(stop, out RoadEdge frontage),
                Is.True);
            Vector3 edgeStart = layout.GetNodeWorldPosition(frontage.A);
            Vector3 edgeEnd = layout.GetNodeWorldPosition(frontage.B);
            Vector3 start = Vector3.Lerp(edgeStart, edgeEnd, 0.2f);

            CityRoutePath path = CityRoutePathfinder.Build(
                layout,
                start,
                new[] { stop });
            float expectedLength = Vector3.Distance(
                start,
                Flatten(stop.ReturnPosition));

            Assert.That(path.IsEmpty, Is.False);
            Assert.That(
                path.TotalLength,
                Is.EqualTo(expectedLength).Within(PositionTolerance));
            Assert.That(
                CalculateLength(path.Points),
                Is.EqualTo(path.TotalLength).Within(PositionTolerance));
            AssertNoDuplicateOrRedundantPoints(path.Points);
        }

        [Test]
        public void Build_WithInvalidArguments_ThrowsPredictably()
        {
            CityLayout layout = CreateLayout();
            BuildingLot nonBar =
                layout.BuildingLots.First(lot => !lot.IsBar);
            BuildingLot foreignBar = GetBars(
                CityLayoutGenerator.Generate(
                    CityGenerationSettings.Default,
                    layout.Seed + 1))[0];

            Assert.Throws<ArgumentNullException>(
                () => CityRoutePathfinder.Build(
                    null,
                    Vector3.zero,
                    Array.Empty<BuildingLot>()));
            Assert.Throws<ArgumentNullException>(
                () => CityRoutePathfinder.Build(
                    layout,
                    Vector3.zero,
                    null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CityRoutePathfinder.Build(
                    layout,
                    new Vector3(float.NaN, 0f, 0f),
                    Array.Empty<BuildingLot>()));
            Assert.Throws<ArgumentException>(
                () => CityRoutePathfinder.Build(
                    layout,
                    Vector3.zero,
                    new BuildingLot[] { null }));
            Assert.Throws<ArgumentException>(
                () => CityRoutePathfinder.Build(
                    layout,
                    Vector3.zero,
                    new[] { nonBar }));
            Assert.Throws<ArgumentException>(
                () => CityRoutePathfinder.Build(
                    layout,
                    Vector3.zero,
                    new[] { foreignBar }));
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static List<BuildingLot> GetBars(CityLayout layout)
        {
            return layout.BuildingLots
                .Where(lot => lot.IsBar)
                .ToList();
        }

        private static bool IsOnRoadCenterLine(
            CityLayout layout,
            Vector3 point)
        {
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                if (DistanceToSegment(point, start, end) <=
                    PositionTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PolylineContains(
            IReadOnlyList<Vector3> points,
            Vector3 target)
        {
            target = Flatten(target);
            if (points.Count == 1)
            {
                return Vector3.Distance(points[0], target) <=
                       PositionTolerance;
            }

            for (int index = 1; index < points.Count; index++)
            {
                if (DistanceToSegment(
                        target,
                        points[index - 1],
                        points[index]) <= PositionTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistanceToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            point = Flatten(point);
            start = Flatten(start);
            end = Flatten(end);
            Vector3 delta = end - start;
            float denominator = delta.sqrMagnitude;
            float amount = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(
                    Vector3.Dot(point - start, delta) / denominator);
            return Vector3.Distance(
                point,
                start + (delta * amount));
        }

        private static float CalculateLength(
            IReadOnlyList<Vector3> points)
        {
            float result = 0f;
            for (int index = 1; index < points.Count; index++)
            {
                result += Vector3.Distance(
                    points[index - 1],
                    points[index]);
            }

            return result;
        }

        private static void AssertNoDuplicateOrRedundantPoints(
            IReadOnlyList<Vector3> points)
        {
            for (int index = 1; index < points.Count; index++)
            {
                Assert.That(
                    Vector3.Distance(points[index - 1], points[index]),
                    Is.GreaterThan(PositionTolerance));
            }

            for (int index = 2; index < points.Count; index++)
            {
                Vector3 first =
                    points[index - 1] - points[index - 2];
                Vector3 second =
                    points[index] - points[index - 1];
                float cross = Mathf.Abs(
                    (first.x * second.z) -
                    (first.z * second.x));
                bool redundant =
                    cross <= PositionTolerance &&
                    Vector3.Dot(first, second) > 0f;
                Assert.That(
                    redundant,
                    Is.False,
                    $"Point {index - 1} is a redundant collinear waypoint.");
            }
        }

        private static Vector3 Flatten(Vector3 point)
        {
            point.y = 0f;
            return point;
        }
    }
}
