using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class RoadWalkableAreaTests
    {
        private const float BoundaryEpsilon = 0.0001f;

        [Test]
        public void Contains_UsesUnionAndAccountsForPlayerRadius()
        {
            var area = new RoadWalkableArea(
                new[]
                {
                    new Rect(0f, 0f, 10f, 4f),
                    new Rect(4f, 0f, 2f, 10f)
                });

            Assert.That(area.Contains(new Vector3(8f, 0f, 2f), 0.5f), Is.True);
            Assert.That(area.Contains(new Vector3(5f, 0f, 8f), 0.5f), Is.True);
            Assert.That(area.Contains(new Vector3(0.25f, 0f, 2f), 0.5f), Is.False);
            Assert.That(area.Contains(new Vector3(9.5f, 0f, 3.5f), 0.5f), Is.True);
        }

        [Test]
        public void Constrain_ReturnsDesiredPositionWhenItIsWalkable()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 10f, 4f) });
            var desired = new Vector3(6f, 1.2f, 2f);

            Vector3 constrained = area.Constrain(
                new Vector3(2f, 0f, 2f),
                desired,
                0.5f);

            Assert.That(constrained, Is.EqualTo(desired));
        }

        [Test]
        public void Constrain_FallsBackToSingleValidAxis()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 10f, 4f) });
            var current = new Vector3(2f, 0f, 2f);

            Vector3 constrained = area.Constrain(
                current,
                new Vector3(6f, 0f, 7f),
                0.5f);

            Assert.That(constrained, Is.EqualTo(new Vector3(6f, 0f, 2f)));
        }

        [Test]
        public void Constrain_StaysPutWhenNeitherAxisIsValid()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 4f, 4f) });
            var current = new Vector3(2f, 0f, 2f);

            Vector3 constrained = area.Constrain(
                current,
                new Vector3(8f, 0f, 8f),
                0.5f);

            Assert.That(constrained, Is.EqualTo(current));
        }

        [Test]
        public void Add_AfterFirstQueryUpdatesEveryOperation()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 4f, 4f) });

            Assert.That(
                area.Contains(new Vector3(22f, 0f, 2f)),
                Is.False);

            area.Add(new Rect(20f, 0f, 4f, 4f));

            Assert.That(
                area.Contains(new Vector3(22f, 0f, 2f), 0.5f),
                Is.True);
            Assert.That(
                area.ClosestPoint(new Vector3(26f, 3f, 2f), 0.5f),
                Is.EqualTo(new Vector3(23.5f, 3f, 2f)));
            Assert.That(
                area.Constrain(
                    new Vector3(22f, 0f, 2f),
                    new Vector3(23f, 0f, 7f),
                    0.5f),
                Is.EqualTo(new Vector3(23f, 0f, 2f)));
        }

        [Test]
        public void ClosestPoint_UsesFirstRectangleWhenDistancesAreEqual()
        {
            var area = new RoadWalkableArea(
                new[]
                {
                    new Rect(-6f, -1f, 2f, 2f),
                    new Rect(4f, -1f, 2f, 2f)
                });

            Vector3 closest = area.ClosestPoint(
                new Vector3(0f, 2f, 0f));

            Assert.That(closest, Is.EqualTo(new Vector3(-4f, 2f, 0f)));
        }

        [Test]
        public void FromLayout_AddsParkLawnButNotPerimeterGap()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                731942);
            RoadWalkableArea area = RoadWalkableArea.FromLayout(layout);
            Rect park = layout.Park.WalkableBounds;
            var lawn = new Vector3(
                park.xMin + (layout.NodeSpacing.x * 0.5f),
                0f,
                park.yMin + (layout.NodeSpacing.y * 0.5f));
            CityParkGateDescriptor southGate = layout.Park.Gates[0];
            for (int index = 1; index < layout.Park.Gates.Count; index++)
            {
                if (layout.Park.Gates[index].Center.z <
                    southGate.Center.z)
                {
                    southGate = layout.Park.Gates[index];
                }
            }

            var perimeterGap = new Vector3(
                lawn.x,
                0f,
                (southGate.Center.z + park.yMin) * 0.5f);

            Assert.That(layout.Park.IsEnabled, Is.True);
            Assert.That(area.Contains(layout.Park.Center, 0.5f), Is.True);
            Assert.That(area.Contains(lawn, 0.5f), Is.True);
            Assert.That(area.Contains(perimeterGap), Is.False);
        }

        [Test]
        public void FromLayout_AddsEveryDistrictPointAndStreetApproach()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                731942);
            RoadWalkableArea area = RoadWalkableArea.FromLayout(layout);

            Assert.That(
                layout.DistrictPointsOfInterest,
                Has.Count.EqualTo(4));
            foreach (CityDistrictPointOfInterestDescriptor point
                     in layout.DistrictPointsOfInterest)
            {
                Assert.That(
                    area.Contains(point.Center, 0.32f),
                    Is.True,
                    point.Id);
                foreach (
                    CityDistrictPointOfInterestAccessDescriptor access
                    in point.Accesses)
                {
                    for (int sample = -5; sample <= 5; sample++)
                    {
                        Vector3 position = access.Center +
                            (access.OutwardNormal * (sample * 0.25f));
                        Assert.That(
                            area.Contains(position, 0.32f),
                            Is.True,
                            $"{access.Id} sample {sample}");
                    }
                }
            }
        }

        [Test]
        public void SpatialIndex_MatchesLinearReference_ForSeededRectanglesAndBoundaries()
        {
            const int seed = 0x51A7C17;
            var random = new System.Random(seed);
            var rectangles = new List<Rect>();
            var area = new RoadWalkableArea();

            AddSeededRectangles(area, rectangles, random, 96);
            AssertEquivalentQueries(area, rectangles, random, 320);
            AssertEquivalentBoundaryQueries(area, rectangles);

            AddSeededRectangles(area, rectangles, random, 96);
            AssertEquivalentQueries(area, rectangles, random, 640);
            AssertEquivalentBoundaryQueries(area, rectangles);
        }

        private static void AddSeededRectangles(
            RoadWalkableArea area,
            ICollection<Rect> normalizedRectangles,
            System.Random random,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                float x = NextFloat(random, -320f, 320f);
                float z = NextFloat(random, -320f, 320f);
                float width = index % 11 == 0
                    ? NextFloat(random, 0.05f, 0.75f)
                    : NextFloat(random, 0.5f, 36f);
                float height = index % 13 == 0
                    ? NextFloat(random, 0.05f, 0.75f)
                    : NextFloat(random, 0.5f, 36f);
                bool reverseX = index % 5 == 0;
                bool reverseZ = index % 7 == 0;
                var input = new Rect(
                    reverseX ? x + width : x,
                    reverseZ ? z + height : z,
                    reverseX ? -width : width,
                    reverseZ ? -height : height);

                area.Add(input);
                normalizedRectangles.Add(Normalize(input));
            }
        }

        private static void AssertEquivalentQueries(
            RoadWalkableArea area,
            IReadOnlyList<Rect> rectangles,
            System.Random random,
            int queryCount)
        {
            float[] radii =
            {
                0f,
                BoundaryEpsilon,
                0.1f,
                0.5f,
                2f,
                8f,
                20f
            };

            for (int index = 0; index < queryCount; index++)
            {
                float radius = radii[random.Next(radii.Length)];
                var current = new Vector3(
                    NextFloat(random, -380f, 380f),
                    NextFloat(random, -4f, 4f),
                    NextFloat(random, -380f, 380f));
                var desired = new Vector3(
                    NextFloat(random, -380f, 380f),
                    NextFloat(random, -4f, 4f),
                    NextFloat(random, -380f, 380f));

                Assert.That(
                    area.Contains(desired, radius),
                    Is.EqualTo(
                        ReferenceContains(rectangles, desired, radius)),
                    $"Contains mismatch at query {index}, radius {radius}.");
                Assert.That(
                    area.ClosestPoint(desired, radius),
                    Is.EqualTo(
                        ReferenceClosestPoint(
                            rectangles,
                            desired,
                            radius,
                            desired)),
                    $"ClosestPoint mismatch at query {index}, radius {radius}.");
                Assert.That(
                    area.Constrain(current, desired, radius),
                    Is.EqualTo(
                        ReferenceConstrain(
                            rectangles,
                            current,
                            desired,
                            radius)),
                    $"Constrain mismatch at query {index}, radius {radius}.");
            }
        }

        private static void AssertEquivalentBoundaryQueries(
            RoadWalkableArea area,
            IReadOnlyList<Rect> rectangles)
        {
            int count = Mathf.Min(rectangles.Count, 32);
            for (int index = 0; index < count; index++)
            {
                Rect rectangle = rectangles[index];
                float radius = Mathf.Min(
                    rectangle.width,
                    rectangle.height) * 0.25f;
                float z = rectangle.center.y;
                float effectiveMin = rectangle.xMin + radius;
                float[] xCoordinates =
                {
                    effectiveMin,
                    effectiveMin - (BoundaryEpsilon * 0.99f),
                    effectiveMin - (BoundaryEpsilon * 1.01f),
                    rectangle.xMax - radius
                };

                for (int boundaryIndex = 0;
                     boundaryIndex < xCoordinates.Length;
                     boundaryIndex++)
                {
                    var position = new Vector3(
                        xCoordinates[boundaryIndex],
                        index * 0.125f,
                        z);
                    Assert.That(
                        area.Contains(position, radius),
                        Is.EqualTo(
                            ReferenceContains(
                                rectangles,
                                position,
                                radius)),
                        $"Boundary mismatch for rectangle {index}, " +
                        $"case {boundaryIndex}.");
                    Assert.That(
                        area.ClosestPoint(position, radius),
                        Is.EqualTo(
                            ReferenceClosestPoint(
                                rectangles,
                                position,
                                radius,
                                position)),
                        $"Boundary closest-point mismatch for rectangle " +
                        $"{index}, case {boundaryIndex}.");
                }
            }
        }

        private static Vector3 ReferenceConstrain(
            IReadOnlyList<Rect> rectangles,
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius)
        {
            if (ReferenceContains(rectangles, desiredPosition, radius))
            {
                return desiredPosition;
            }

            var xOnly = new Vector3(
                desiredPosition.x,
                desiredPosition.y,
                currentPosition.z);
            var zOnly = new Vector3(
                currentPosition.x,
                desiredPosition.y,
                desiredPosition.z);
            bool canMoveX = ReferenceContains(rectangles, xOnly, radius);
            bool canMoveZ = ReferenceContains(rectangles, zOnly, radius);
            if (canMoveX && canMoveZ)
            {
                return (xOnly - desiredPosition).sqrMagnitude <=
                       (zOnly - desiredPosition).sqrMagnitude
                    ? xOnly
                    : zOnly;
            }

            if (canMoveX)
            {
                return xOnly;
            }

            if (canMoveZ)
            {
                return zOnly;
            }

            var stationary = new Vector3(
                currentPosition.x,
                desiredPosition.y,
                currentPosition.z);
            if (ReferenceContains(rectangles, stationary, radius))
            {
                return stationary;
            }

            return ReferenceClosestPoint(
                rectangles,
                desiredPosition,
                radius,
                stationary);
        }

        private static bool ReferenceContains(
            IReadOnlyList<Rect> rectangles,
            Vector3 position,
            float radius)
        {
            for (int index = 0; index < rectangles.Count; index++)
            {
                Rect rectangle = rectangles[index];
                float xMin = rectangle.xMin + radius;
                float xMax = rectangle.xMax - radius;
                float zMin = rectangle.yMin + radius;
                float zMax = rectangle.yMax - radius;
                if (xMin <= xMax &&
                    zMin <= zMax &&
                    position.x >= xMin - BoundaryEpsilon &&
                    position.x <= xMax + BoundaryEpsilon &&
                    position.z >= zMin - BoundaryEpsilon &&
                    position.z <= zMax + BoundaryEpsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 ReferenceClosestPoint(
            IReadOnlyList<Rect> rectangles,
            Vector3 position,
            float radius,
            Vector3 fallback)
        {
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            Vector3 best = fallback;
            for (int index = 0; index < rectangles.Count; index++)
            {
                Rect rectangle = rectangles[index];
                float xMin = rectangle.xMin + radius;
                float xMax = rectangle.xMax - radius;
                float zMin = rectangle.yMin + radius;
                float zMax = rectangle.yMax - radius;
                if (xMin > xMax || zMin > zMax)
                {
                    continue;
                }

                var candidate = new Vector3(
                    Mathf.Clamp(position.x, xMin, xMax),
                    position.y,
                    Mathf.Clamp(position.z, zMin, zMax));
                float distance = (candidate - position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                best = candidate;
            }

            return found ? best : fallback;
        }

        private static Rect Normalize(Rect rectangle)
        {
            return Rect.MinMaxRect(
                Mathf.Min(rectangle.xMin, rectangle.xMax),
                Mathf.Min(rectangle.yMin, rectangle.yMax),
                Mathf.Max(rectangle.xMin, rectangle.xMax),
                Mathf.Max(rectangle.yMin, rectangle.yMax));
        }

        private static float NextFloat(
            System.Random random,
            float minimum,
            float maximum)
        {
            return minimum +
                   ((float)random.NextDouble() * (maximum - minimum));
        }
    }
}
