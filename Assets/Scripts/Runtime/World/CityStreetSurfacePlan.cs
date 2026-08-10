using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityCrosswalkDescriptor :
        IEquatable<CityCrosswalkDescriptor>
    {
        internal CityCrosswalkDescriptor(
            Vector2Int node,
            RoadEdge approachEdge,
            Rect walkableBounds,
            Vector3 center,
            Vector3 alongDirection,
            Vector3 acrossDirection)
        {
            Node = node;
            ApproachEdge = approachEdge;
            WalkableBounds = walkableBounds;
            Center = center;
            AlongDirection = alongDirection;
            AcrossDirection = acrossDirection;
        }

        public Vector2Int Node { get; }
        public RoadEdge ApproachEdge { get; }
        public Rect WalkableBounds { get; }
        public Vector3 Center { get; }
        public Vector3 AlongDirection { get; }
        public Vector3 AcrossDirection { get; }

        public bool Equals(CityCrosswalkDescriptor other)
        {
            return Node == other.Node &&
                   ApproachEdge.Equals(other.ApproachEdge) &&
                   WalkableBounds.Equals(other.WalkableBounds) &&
                   Center.Equals(other.Center) &&
                   AlongDirection.Equals(other.AlongDirection) &&
                   AcrossDirection.Equals(other.AcrossDirection);
        }

        public override bool Equals(object obj)
        {
            return obj is CityCrosswalkDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Node.GetHashCode();
                hash = (hash * 397) ^ ApproachEdge.GetHashCode();
                hash = (hash * 397) ^ WalkableBounds.GetHashCode();
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ AlongDirection.GetHashCode();
                return (hash * 397) ^ AcrossDirection.GetHashCode();
            }
        }
    }

    public sealed class CityStreetSurfacePlan
    {
        internal CityStreetSurfacePlan(
            float carriagewayWidth,
            IList<Bounds> streetSurfaces,
            IList<Bounds> parkPaths,
            IList<Bounds> sidewalks,
            IList<Bounds> centerMarkings,
            IList<Bounds> crosswalkMarkings,
            IList<Rect> sidewalkWalkableRectangles,
            IList<Rect> crosswalkWalkableRectangles,
            IList<Vector2Int> crosswalkNodes,
            IList<CityCrosswalkDescriptor> crosswalks)
        {
            if (float.IsNaN(carriagewayWidth) ||
                float.IsInfinity(carriagewayWidth) ||
                carriagewayWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(carriagewayWidth));
            }

            CarriagewayWidth = carriagewayWidth;
            StreetSurfaces = Copy(streetSurfaces);
            ParkPaths = Copy(parkPaths);
            Sidewalks = Copy(sidewalks);
            CenterMarkings = Copy(centerMarkings);
            CrosswalkMarkings = Copy(crosswalkMarkings);
            SidewalkWalkableRectangles = Copy(
                sidewalkWalkableRectangles);
            CrosswalkWalkableRectangles = Copy(
                crosswalkWalkableRectangles);
            CrosswalkNodes = Copy(crosswalkNodes);
            Crosswalks = Copy(crosswalks);
        }

        public float CarriagewayWidth { get; }
        public IReadOnlyList<Bounds> StreetSurfaces { get; }
        public IReadOnlyList<Bounds> ParkPaths { get; }
        public IReadOnlyList<Bounds> Sidewalks { get; }
        public IReadOnlyList<Bounds> CenterMarkings { get; }
        public IReadOnlyList<Bounds> CrosswalkMarkings { get; }
        public IReadOnlyList<Rect> SidewalkWalkableRectangles { get; }
        public IReadOnlyList<Rect> CrosswalkWalkableRectangles { get; }
        public IReadOnlyList<Vector2Int> CrosswalkNodes { get; }
        public IReadOnlyList<CityCrosswalkDescriptor> Crosswalks { get; }

        private static IReadOnlyList<T> Copy<T>(IList<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
