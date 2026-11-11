using System;
using System.Collections.Generic;
using UnityEngine;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;

namespace BarPromenade
{
    /// <summary>Contacts between actual upward faces, never between their XZ footprints.</summary>
    internal static class GroundJointTopology
    {
        private const float HorizontalTolerance = .0002f;
        private const float HeightTolerance = .001f;
        private const float WeldBucket = .002f;

        internal readonly struct Joint
        {
            internal Joint(Vector3 a, Vector3 b, int left, int right)
            { A = a; B = b; Left = left; Right = right; }
            internal readonly Vector3 A, B;
            internal readonly int Left, Right;
        }

        internal static List<Joint> Find(IReadOnlyList<Triangle> triangles, Func<int, bool> eligible)
        {
            var exact = new Dictionary<Vector3, int>();
            var welded = new Dictionary<Vector2Int, int>();
            var points = new List<Vector3>();
            var next = new List<int>();
            var open = new Dictionary<(int, int), (int Face, int First)>();
            var result = new List<Joint>();
            for (int face = 0; face < triangles.Count; face++)
            {
                Triangle triangle = triangles[face];
                if (!eligible(triangle.Surface)) continue;
                Vector3 a = triangle.A.Position, b = triangle.B.Position, c = triangle.C.Position;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-14f || Mathf.Abs(cross.y) < cross.magnitude * .2f) continue;
                int i = Weld(a), j = Weld(b), k = Weld(c);
                if (i == j || j == k || k == i) continue;
                // Some import paths wind opposite to the plan. Use one XZ convention.
                if (cross.y < 0f) (j, k) = (k, j);
                Edge(i, j, face); Edge(j, k, face); Edge(k, i, face);
            }
            return result;

            void Edge(int first, int last, int face)
            {
                var key = first < last ? (first, last) : (last, first);
                if (!open.TryGetValue(key, out var previous)) { open.Add(key, (face, first)); return; }
                if (previous.First == first) return; // overlap, not two sides of a contact
                open.Remove(key);
                if (triangles[previous.Face].Surface != triangles[face].Surface)
                    result.Add(new Joint(points[first], points[last], previous.Face, face));
            }

            int Weld(Vector3 p)
            {
                if (exact.TryGetValue(p, out int id)) return id;
                int x0 = Mathf.FloorToInt((p.x - HorizontalTolerance) / WeldBucket);
                int z0 = Mathf.FloorToInt((p.z - HorizontalTolerance) / WeldBucket);
                int x1 = Mathf.FloorToInt((p.x + HorizontalTolerance) / WeldBucket);
                int z1 = Mathf.FloorToInt((p.z + HorizontalTolerance) / WeldBucket);
                int best = int.MaxValue;
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                        if (welded.TryGetValue(new Vector2Int(x, z), out int head))
                            for (int candidate = head; candidate >= 0; candidate = next[candidate])
                            {
                                Vector3 d = points[candidate] - p;
                                if (d.x * d.x + d.z * d.z <= HorizontalTolerance * HorizontalTolerance &&
                                    Mathf.Abs(d.y) <= HeightTolerance)
                                    best = Mathf.Min(best, candidate);
                            }
                if (best != int.MaxValue) id = best;
                else
                {
                    id = points.Count;
                    var home = new Vector2Int(Mathf.FloorToInt(p.x / WeldBucket), Mathf.FloorToInt(p.z / WeldBucket));
                    next.Add(welded.TryGetValue(home, out int head) ? head : -1);
                    points.Add(p);
                    welded[home] = id;
                }
                exact.Add(p, id);
                return id;
            }
        }
    }
}
