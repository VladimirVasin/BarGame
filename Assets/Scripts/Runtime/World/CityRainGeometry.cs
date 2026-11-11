using System;
using UnityEngine;

namespace BarPromenade
{
    // The same hierarchy prunes world renderers and the triangles of a shared mesh.
    internal sealed class CityRainGeometry<T>
    {
        internal struct Item
        {
            internal Bounds Bounds;
            internal T Value;

            internal Item(Bounds bounds, T value)
            {
                Bounds = bounds;
                Value = value;
            }
        }

        internal interface IVisitor
        {
            void Visit(T value, ref float distance);
        }

        private struct Node
        {
            internal Bounds Bounds;
            internal int Start, Count, Left, Right;
        }

        private readonly Item[] items;
        private readonly Node[] nodes;
        private int nodeCount;

        internal CityRainGeometry(Item[] items)
        {
            this.items = items;
            // A split can leave one item on one side. Splitting close to the
            // median keeps the hierarchy shallow even on long, thin road meshes.
            nodes = new Node[items.Length == 0 ? 0 : 2 * (items.Length / 4 + 1)];
            if (items.Length != 0) Build(0, items.Length);
        }

        private int Build(int start, int count)
        {
            int index = nodeCount++;
            Bounds bounds = items[start].Bounds;
            Bounds centres = new Bounds(bounds.center, Vector3.zero);
            int end = start + count;
            for (int i = start + 1; i < end; i++)
            {
                bounds.Encapsulate(items[i].Bounds);
                centres.Encapsulate(items[i].Bounds.center);
            }

            if (count <= 8)
            {
                nodes[index] = new Node { Bounds = bounds, Start = start, Count = count };
                return index;
            }

            Vector3 span = centres.size;
            int axis = span.x >= span.z && span.x >= span.y ? 0 : span.z >= span.y ? 2 : 1;
            float pivot = centres.center[axis];
            int cut = start;
            for (int i = start; i < end; i++)
            {
                if (items[i].Bounds.center[axis] >= pivot) continue;
                (items[cut], items[i]) = (items[i], items[cut]);
                cut++;
            }

            int leftCount = cut - start;
            int minimum = Mathf.Max(4, count / 4);
            if (leftCount < minimum || count - leftCount < minimum) leftCount = count / 2;
            int left = Build(start, leftCount);
            int right = Build(start + leftCount, count - leftCount);
            nodes[index] = new Node { Bounds = bounds, Left = left, Right = right };
            return index;
        }

        internal void Trace<TVisitor>(Ray ray, ref float distance, ref TVisitor visitor)
            where TVisitor : struct, IVisitor
        {
            if (nodeCount != 0 && EntryDistance(nodes[0].Bounds, ray) < distance)
                TraceNode(0, ray, ref distance, ref visitor);
        }

        private void TraceNode<TVisitor>(int index, Ray ray, ref float distance, ref TVisitor visitor)
            where TVisitor : struct, IVisitor
        {
            ref Node node = ref nodes[index];
            if (node.Count != 0)
            {
                int end = node.Start + node.Count;
                for (int i = node.Start; i < end; i++)
                    if (EntryDistance(items[i].Bounds, ray) < distance)
                        visitor.Visit(items[i].Value, ref distance);
                return;
            }

            int first = node.Left;
            int second = node.Right;
            float near = EntryDistance(nodes[first].Bounds, ray);
            float far = EntryDistance(nodes[second].Bounds, ray);
            if (near > far)
            {
                (first, second) = (second, first);
                (near, far) = (far, near);
            }
            if (near < distance) TraceNode(first, ray, ref distance, ref visitor);
            if (far < distance) TraceNode(second, ray, ref distance, ref visitor);
        }

        internal static float EntryDistance(Bounds bounds, Ray ray)
        {
            if (bounds.Contains(ray.origin)) return 0f;
            return bounds.IntersectRay(ray, out float distance) ? distance : float.PositiveInfinity;
        }
    }

    internal sealed class CityRainMesh
    {
        private struct Face
        {
            internal int A, B, C, Slot;
        }

        private struct Probe : CityRainGeometry<Face>.IVisitor
        {
            internal Vector3[] Vertices;
            internal CityRainSurfaceKind[] Kinds;
            internal Ray Ray;
            internal Vector3 Normal;
            internal int Slot;

            public void Visit(Face face, ref float distance)
            {
                if (Kinds[Mathf.Min(face.Slot, Kinds.Length - 1)] == CityRainSurfaceKind.Ignore) return;
                Vector3 a = Vertices[face.A];
                if (!TryTriangle(Ray, a, Vertices[face.B], Vertices[face.C], distance,
                    out float hit, out Vector3 normal)) return;
                distance = hit;
                Slot = face.Slot;
                Normal = normal;
            }
        }

        private readonly Vector3[] vertices;
        private readonly CityRainGeometry<Face> geometry;
        internal int FaceCount { get; }

        internal CityRainMesh(Mesh mesh, int faceCount)
        {
            int[] flattened = null;
            bool flatten = !mesh.isReadable;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
                flatten |= mesh.GetTopology(slot) == MeshTopology.Quads;
            if (flatten) PlayerScarfCollisionWorld.ReadMesh(mesh, out vertices, out flattened);
            else vertices = mesh.vertices;
            var faces = new CityRainGeometry<Face>.Item[faceCount];
            int next = 0;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                int count = FacesInSlot(mesh, slot);
                if (count == 0) continue;
                int[] indices = flattened ?? mesh.GetTriangles(slot);
                int start = flattened == null ? 0 : next * 3;
                for (int i = start; i < start + count * 3; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    var bounds = new Bounds(vertices[a], Vector3.zero);
                    bounds.Encapsulate(vertices[b]);
                    bounds.Encapsulate(vertices[c]);
                    bounds.Expand(.0001f);
                    faces[next++] = new CityRainGeometry<Face>.Item(bounds,
                        new Face { A = a, B = b, C = c, Slot = slot });
                }
            }
            FaceCount = faceCount;
            geometry = new CityRainGeometry<Face>(faces);
        }

        internal static int FacesInSlot(Mesh mesh, int slot) => mesh.GetTopology(slot) switch
        {
            MeshTopology.Triangles => (int)mesh.GetIndexCount(slot) / 3,
            MeshTopology.Quads => (int)mesh.GetIndexCount(slot) / 2,
            _ => 0
        };

        internal bool Trace(Ray ray, CityRainSurfaceKind[] kinds, ref float distance, out int slot, out Vector3 normal)
        {
            var probe = new Probe { Vertices = vertices, Kinds = kinds, Ray = ray, Slot = -1 };
            geometry.Trace(ray, ref distance, ref probe);
            slot = probe.Slot;
            normal = probe.Normal;
            return slot >= 0;
        }

        internal static bool TryTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c,
            float limit, out float distance, out Vector3 normal)
        {
            distance = 0f;
            normal = default;
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 cross = Vector3.Cross(ray.direction, ac);
            float determinant = Vector3.Dot(ab, cross);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1f / determinant;
            Vector3 fromA = ray.origin - a;
            float u = Vector3.Dot(fromA, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            cross = Vector3.Cross(fromA, ab);
            float v = Vector3.Dot(ray.direction, cross) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(ac, cross) * inverse;
            if (distance < 0f || distance >= limit) return false;
            normal = Vector3.Cross(ab, ac);
            if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
            return true;
        }
    }
}
