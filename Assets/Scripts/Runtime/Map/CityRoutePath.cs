using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityRoutePath
    {
        private readonly ReadOnlyCollection<Vector3> points;

        internal CityRoutePath(IList<Vector3> routePoints)
        {
            if (routePoints == null)
            {
                throw new ArgumentNullException(nameof(routePoints));
            }

            points = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(routePoints));

            float length = 0f;
            for (int index = 1; index < points.Count; index++)
            {
                length += Vector3.Distance(points[index - 1], points[index]);
            }

            TotalLength = length;
        }

        public IReadOnlyList<Vector3> Points => points;
        public float TotalLength { get; }
        public bool IsEmpty => points.Count == 0;
    }
}
