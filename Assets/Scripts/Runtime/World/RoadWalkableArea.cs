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

            return new RoadWalkableArea(layout.CreateRoadRects());
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
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            for (int index = 0; index < rectangles.Count; index++)
            {
                if (Contains(rectangles[index], position.x, position.z, radius))
                {
                    return true;
                }
            }

            return false;
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
    }
}
