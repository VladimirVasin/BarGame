using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class RoadWalkableArea : IWalkableArea
    {
        private const float BoundaryEpsilon = 0.0001f;

        private readonly List<Rect> rectangles = new List<Rect>();
        private readonly ReadOnlyCollection<Rect> readOnlyRectangles;
        private SpatialNode[] spatialNodes = Array.Empty<SpatialNode>();
        private int spatialRoot = -1;
        private bool spatialIndexDirty = true;

        public RoadWalkableArea()
        {
            readOnlyRectangles = rectangles.AsReadOnly();
        }

        public RoadWalkableArea(IEnumerable<Rect> xzRectangles)
            : this()
        {
            if (xzRectangles == null)
            {
                throw new ArgumentNullException(nameof(xzRectangles));
            }

            foreach (Rect rectangle in xzRectangles)
            {
                Add(rectangle);
            }
        }

        public IReadOnlyList<Rect> Rectangles => readOnlyRectangles;

        public static RoadWalkableArea FromLayout(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var area = new RoadWalkableArea(layout.CreateRoadRects());
            if (layout.Park.IsEnabled)
            {
                area.Add(layout.Park.WalkableBounds);
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                area.Add(point.PublicBounds);
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    area.Add(point.Accesses[accessIndex].ApproachBounds);
                }
            }

            return area;
        }

        public void Add(Rect xzRectangle)
        {
            float xMin = Mathf.Min(xzRectangle.xMin, xzRectangle.xMax);
            float xMax = Mathf.Max(xzRectangle.xMin, xzRectangle.xMax);
            float zMin = Mathf.Min(xzRectangle.yMin, xzRectangle.yMax);
            float zMax = Mathf.Max(xzRectangle.yMin, xzRectangle.yMax);
            if (!IsFinite(xMin) || !IsFinite(xMax) ||
                !IsFinite(zMin) || !IsFinite(zMax) ||
                xMax <= xMin || zMax <= zMin)
            {
                throw new ArgumentException(
                    "Walkable rectangles must have finite positive dimensions.",
                    nameof(xzRectangle));
            }

            rectangles.Add(Rect.MinMaxRect(xMin, zMin, xMax, zMax));
            spatialIndexDirty = true;
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            EnsureSpatialIndex();
            return spatialRoot >= 0 &&
                   Contains(
                       spatialRoot,
                       position.x,
                       position.z,
                       radius);
        }

        private bool Contains(
            int nodeIndex,
            float x,
            float z,
            float radius)
        {
            SpatialNode node = spatialNodes[nodeIndex];
            if (!node.Bounds.Contains(x, z, BoundaryEpsilon))
            {
                return false;
            }

            if (node.IsLeaf)
            {
                return Contains(
                    rectangles[node.RectangleIndex],
                    x,
                    z,
                    radius);
            }

            return Contains(node.LeftChild, x, z, radius) ||
                   Contains(node.RightChild, x, z, radius);
        }

        public Vector3 Constrain(
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius = 0f)
        {
            ValidateRadius(radius);
            if (Contains(desiredPosition, radius))
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
            bool canMoveX = Contains(xOnly, radius);
            bool canMoveZ = Contains(zOnly, radius);
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
            if (Contains(stationary, radius))
            {
                return stationary;
            }

            return ClosestPoint(desiredPosition, radius, stationary);
        }

        public Vector3 ClosestPoint(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            return ClosestPoint(position, radius, position);
        }

        private Vector3 ClosestPoint(
            Vector3 position,
            float radius,
            Vector3 fallback)
        {
            if (!IsFinite(position.x) ||
                !IsFinite(position.y) ||
                !IsFinite(position.z))
            {
                return ClosestPointLinear(position, radius, fallback);
            }

            EnsureSpatialIndex();
            if (spatialRoot < 0)
            {
                return fallback;
            }

            bool found = false;
            float bestDistance = float.PositiveInfinity;
            Vector3 best = fallback;
            int bestRectangleIndex = int.MaxValue;
            FindClosestPoint(
                spatialRoot,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
            return found ? best : fallback;
        }

        private void FindClosestPoint(
            int nodeIndex,
            Vector3 position,
            float radius,
            ref bool found,
            ref float bestDistance,
            ref int bestRectangleIndex,
            ref Vector3 best)
        {
            SpatialNode node = spatialNodes[nodeIndex];
            float lowerBound = node.Bounds.SquaredDistance(
                position.x,
                position.z);
            if (found &&
                (lowerBound > bestDistance ||
                 (lowerBound == bestDistance &&
                  node.MinimumRectangleIndex >= bestRectangleIndex)))
            {
                return;
            }

            if (node.IsLeaf)
            {
                TryClosestPoint(
                    node.RectangleIndex,
                    position,
                    radius,
                    ref found,
                    ref bestDistance,
                    ref bestRectangleIndex,
                    ref best);
                return;
            }

            int firstChild = node.LeftChild;
            int secondChild = node.RightChild;
            SpatialNode firstNode = spatialNodes[firstChild];
            SpatialNode secondNode = spatialNodes[secondChild];
            float firstDistance = firstNode.Bounds.SquaredDistance(
                position.x,
                position.z);
            float secondDistance = secondNode.Bounds.SquaredDistance(
                position.x,
                position.z);
            if (secondDistance < firstDistance ||
                (secondDistance == firstDistance &&
                 secondNode.MinimumRectangleIndex <
                 firstNode.MinimumRectangleIndex))
            {
                firstChild = node.RightChild;
                secondChild = node.LeftChild;
            }

            FindClosestPoint(
                firstChild,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
            FindClosestPoint(
                secondChild,
                position,
                radius,
                ref found,
                ref bestDistance,
                ref bestRectangleIndex,
                ref best);
        }

        private void TryClosestPoint(
            int rectangleIndex,
            Vector3 position,
            float radius,
            ref bool found,
            ref float bestDistance,
            ref int bestRectangleIndex,
            ref Vector3 best)
        {
            Rect rectangle = rectangles[rectangleIndex];
            float xMin = rectangle.xMin + radius;
            float xMax = rectangle.xMax - radius;
            float zMin = rectangle.yMin + radius;
            float zMax = rectangle.yMax - radius;
            if (xMin > xMax || zMin > zMax)
            {
                return;
            }

            var candidate = new Vector3(
                Mathf.Clamp(position.x, xMin, xMax),
                position.y,
                Mathf.Clamp(position.z, zMin, zMax));
            float distance = (candidate - position).sqrMagnitude;
            if ((!found && distance >= bestDistance) ||
                (found &&
                 (distance > bestDistance ||
                  (distance == bestDistance &&
                   rectangleIndex >= bestRectangleIndex))))
            {
                return;
            }

            found = true;
            bestDistance = distance;
            bestRectangleIndex = rectangleIndex;
            best = candidate;
        }

        private Vector3 ClosestPointLinear(
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

        private void EnsureSpatialIndex()
        {
            if (!spatialIndexDirty)
            {
                return;
            }

            int rectangleCount = rectangles.Count;
            if (rectangleCount == 0)
            {
                spatialNodes = Array.Empty<SpatialNode>();
                spatialRoot = -1;
                spatialIndexDirty = false;
                return;
            }

            var rectangleIndices = new int[rectangleCount];
            for (int index = 0; index < rectangleCount; index++)
            {
                rectangleIndices[index] = index;
            }

            spatialNodes = new SpatialNode[(rectangleCount * 2) - 1];
            var comparer = new RectangleCenterComparer(rectangles);
            int nextNodeIndex = 0;
            spatialRoot = BuildSpatialNode(
                rectangleIndices,
                0,
                rectangleCount,
                comparer,
                ref nextNodeIndex);
            spatialIndexDirty = false;
        }

        private int BuildSpatialNode(
            int[] rectangleIndices,
            int start,
            int count,
            RectangleCenterComparer comparer,
            ref int nextNodeIndex)
        {
            int nodeIndex = nextNodeIndex++;
            if (count == 1)
            {
                int rectangleIndex = rectangleIndices[start];
                spatialNodes[nodeIndex] = SpatialNode.CreateLeaf(
                    SpatialBounds.FromRect(rectangles[rectangleIndex]),
                    rectangleIndex);
                return nodeIndex;
            }

            GetCenterExtents(
                rectangleIndices,
                start,
                count,
                out float xMin,
                out float xMax,
                out float zMin,
                out float zMax);
            comparer.SortByX = xMax - xMin >= zMax - zMin;
            Array.Sort(rectangleIndices, start, count, comparer);

            int leftCount = count / 2;
            int leftChild = BuildSpatialNode(
                rectangleIndices,
                start,
                leftCount,
                comparer,
                ref nextNodeIndex);
            int rightChild = BuildSpatialNode(
                rectangleIndices,
                start + leftCount,
                count - leftCount,
                comparer,
                ref nextNodeIndex);
            spatialNodes[nodeIndex] = SpatialNode.CreateBranch(
                spatialNodes[leftChild],
                leftChild,
                spatialNodes[rightChild],
                rightChild);
            return nodeIndex;
        }

        private void GetCenterExtents(
            int[] rectangleIndices,
            int start,
            int count,
            out float xMin,
            out float xMax,
            out float zMin,
            out float zMax)
        {
            Rect first = rectangles[rectangleIndices[start]];
            xMin = RectangleCenterComparer.CenterX(first);
            xMax = xMin;
            zMin = RectangleCenterComparer.CenterZ(first);
            zMax = zMin;

            int end = start + count;
            for (int index = start + 1; index < end; index++)
            {
                Rect rectangle = rectangles[rectangleIndices[index]];
                float centerX = RectangleCenterComparer.CenterX(rectangle);
                float centerZ = RectangleCenterComparer.CenterZ(rectangle);
                xMin = Mathf.Min(xMin, centerX);
                xMax = Mathf.Max(xMax, centerX);
                zMin = Mathf.Min(zMin, centerZ);
                zMax = Mathf.Max(zMax, centerZ);
            }
        }

        private static bool Contains(
            Rect rectangle,
            float x,
            float z,
            float radius)
        {
            float xMin = rectangle.xMin + radius;
            float xMax = rectangle.xMax - radius;
            float zMin = rectangle.yMin + radius;
            float zMax = rectangle.yMax - radius;
            return xMin <= xMax &&
                   zMin <= zMax &&
                   x >= xMin - BoundaryEpsilon &&
                   x <= xMax + BoundaryEpsilon &&
                   z >= zMin - BoundaryEpsilon &&
                   z <= zMax + BoundaryEpsilon;
        }

        private static void ValidateRadius(float radius)
        {
            if (!IsFinite(radius) || radius < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct SpatialBounds
        {
            public SpatialBounds(
                float xMin,
                float xMax,
                float zMin,
                float zMax)
            {
                XMin = xMin;
                XMax = xMax;
                ZMin = zMin;
                ZMax = zMax;
            }

            public float XMin { get; }

            public float XMax { get; }

            public float ZMin { get; }

            public float ZMax { get; }

            public static SpatialBounds FromRect(Rect rectangle)
            {
                return new SpatialBounds(
                    rectangle.xMin,
                    rectangle.xMax,
                    rectangle.yMin,
                    rectangle.yMax);
            }

            public static SpatialBounds Encapsulate(
                SpatialBounds first,
                SpatialBounds second)
            {
                return new SpatialBounds(
                    Mathf.Min(first.XMin, second.XMin),
                    Mathf.Max(first.XMax, second.XMax),
                    Mathf.Min(first.ZMin, second.ZMin),
                    Mathf.Max(first.ZMax, second.ZMax));
            }

            public bool Contains(float x, float z, float epsilon)
            {
                return x >= XMin - epsilon &&
                       x <= XMax + epsilon &&
                       z >= ZMin - epsilon &&
                       z <= ZMax + epsilon;
            }

            public float SquaredDistance(float x, float z)
            {
                float deltaX = x < XMin
                    ? XMin - x
                    : x > XMax
                        ? x - XMax
                        : 0f;
                float deltaZ = z < ZMin
                    ? ZMin - z
                    : z > ZMax
                        ? z - ZMax
                        : 0f;
                return (deltaX * deltaX) + (deltaZ * deltaZ);
            }
        }

        private readonly struct SpatialNode
        {
            private SpatialNode(
                SpatialBounds bounds,
                int leftChild,
                int rightChild,
                int rectangleIndex,
                int minimumRectangleIndex)
            {
                Bounds = bounds;
                LeftChild = leftChild;
                RightChild = rightChild;
                RectangleIndex = rectangleIndex;
                MinimumRectangleIndex = minimumRectangleIndex;
            }

            public SpatialBounds Bounds { get; }

            public int LeftChild { get; }

            public int RightChild { get; }

            public int RectangleIndex { get; }

            public int MinimumRectangleIndex { get; }

            public bool IsLeaf => RectangleIndex >= 0;

            public static SpatialNode CreateLeaf(
                SpatialBounds bounds,
                int rectangleIndex)
            {
                return new SpatialNode(
                    bounds,
                    -1,
                    -1,
                    rectangleIndex,
                    rectangleIndex);
            }

            public static SpatialNode CreateBranch(
                SpatialNode left,
                int leftChild,
                SpatialNode right,
                int rightChild)
            {
                return new SpatialNode(
                    SpatialBounds.Encapsulate(left.Bounds, right.Bounds),
                    leftChild,
                    rightChild,
                    -1,
                    Math.Min(
                        left.MinimumRectangleIndex,
                        right.MinimumRectangleIndex));
            }
        }

        private sealed class RectangleCenterComparer : IComparer<int>
        {
            private readonly List<Rect> source;

            public RectangleCenterComparer(List<Rect> source)
            {
                this.source = source;
            }

            public bool SortByX { get; set; }

            public int Compare(int firstIndex, int secondIndex)
            {
                Rect first = source[firstIndex];
                Rect second = source[secondIndex];
                float firstCenter = SortByX
                    ? CenterX(first)
                    : CenterZ(first);
                float secondCenter = SortByX
                    ? CenterX(second)
                    : CenterZ(second);
                int comparison = firstCenter.CompareTo(secondCenter);
                return comparison != 0
                    ? comparison
                    : firstIndex.CompareTo(secondIndex);
            }

            public static float CenterX(Rect rectangle)
            {
                return (rectangle.xMin * 0.5f) +
                       (rectangle.xMax * 0.5f);
            }

            public static float CenterZ(Rect rectangle)
            {
                return (rectangle.yMin * 0.5f) +
                       (rectangle.yMax * 0.5f);
            }
        }
    }
}
