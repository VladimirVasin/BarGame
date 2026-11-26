using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityWalkingPath
    {
        internal readonly IReadOnlyList<Vector3> Points;
        internal readonly float[] Distances;
        internal float Length => Distances[Distances.Length - 1];

        internal CityWalkingPath(List<Vector3> points)
        {
            Points = points.AsReadOnly();
            Distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                Distances[i] = Distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }

        internal Vector3 Sample(float distance, out Vector3 direction)
        {
            int index = Array.BinarySearch(Distances, Mathf.Clamp(distance, 0f, Length));
            int next = Mathf.Clamp(index >= 0 ? index + 1 : ~index, 1, Points.Count - 1);
            while (next < Points.Count - 1 && Distances[next] - Distances[next - 1] < .0001f) next++;
            direction = Vector3.ProjectOnPlane(Points[next] - Points[next - 1], Vector3.up).normalized;
            return Vector3.Lerp(Points[next - 1], Points[next], Mathf.InverseLerp(Distances[next - 1], Distances[next], distance));
        }
    }
}
