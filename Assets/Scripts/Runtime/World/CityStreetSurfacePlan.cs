using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
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
            IList<Vector2Int> crosswalkNodes)
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
