using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Liquid-only triangle receivers. Coarse hero collision never fills a hollow bowl.</summary>
    public sealed class HomeUrineSurfaceMap
    {
        public sealed class Surface
        {
            public string Id;
            public Transform Transform;
            public Renderer Renderer;
            public Collider Fallback;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
            public bool Absorbs;
            private Bounds worldBounds;
            private Matrix4x4 worldToLocal;
            private Matrix4x4 normalToWorld;
            private bool active;

            public void Refresh()
            {
                active = Transform != null && Transform.gameObject.activeInHierarchy;
                if (!active) return;
                worldToLocal = Transform.worldToLocalMatrix;
                normalToWorld = worldToLocal.transpose;
                worldBounds = Renderer != null ? Renderer.bounds : Fallback != null ? Fallback.bounds : default;
                worldBounds.Expand(0.0002f);
            }

            public bool Cast(Vector3 start, Vector3 end, out Hit hit)
            {
                hit = default;
                if (!active) return false;
                Vector3 delta = end - start;
                float length = delta.magnitude;
                if (length < 0.000001f) return false;
                if (!worldBounds.IntersectRay(new Ray(start, delta / length), out float worldNear) || worldNear > length) return false;
                if (Fallback != null)
                {
                    if (!Fallback.enabled || !Fallback.Raycast(new Ray(start, delta / length), out RaycastHit contact, length)) return false;
                    hit = new Hit(this, contact.point, contact.normal, contact.distance / length);
                    return true;
                }
                Vector3 localStart = worldToLocal.MultiplyPoint3x4(start);
                Vector3 localDelta = worldToLocal.MultiplyVector(delta);
                if (!Bounds.IntersectRay(new Ray(localStart, localDelta.normalized), out float near) || near > localDelta.magnitude) return false;
                float nearest = 1.00001f;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < Triangles.Length; i += 3)
                {
                    Vector3 a = Vertices[Triangles[i]];
                    Vector3 edge1 = Vertices[Triangles[i + 1]] - a;
                    Vector3 edge2 = Vertices[Triangles[i + 2]] - a;
                    Vector3 p = Vector3.Cross(localDelta, edge2);
                    float determinant = Vector3.Dot(edge1, p);
                    if (Mathf.Abs(determinant) < 0.00000000001f) continue;
                    float inverse = 1f / determinant;
                    Vector3 fromA = localStart - a;
                    float u = Vector3.Dot(fromA, p) * inverse;
                    if (u < -0.00001f || u > 1.00001f) continue;
                    Vector3 q = Vector3.Cross(fromA, edge1);
                    float v = Vector3.Dot(localDelta, q) * inverse;
                    if (v < -0.00001f || u + v > 1.00001f) continue;
                    float t = Vector3.Dot(edge2, q) * inverse;
                    if (t < 0f || t > 1f || t >= nearest) continue;
                    nearest = t;
                    normal = normalToWorld.MultiplyVector(Vector3.Cross(edge1, edge2)).normalized;
                }
                if (nearest > 1f) return false;
                if (Vector3.Dot(normal, delta) > 0f) normal = -normal;
                hit = new Hit(this, start + delta * nearest, normal, nearest);
                return true;
            }
        }

        public readonly struct Hit
        {
            public readonly Surface Surface;
            public readonly Vector3 Point;
            public readonly Vector3 Normal;
            public readonly float Fraction;
            public Hit(Surface surface, Vector3 point, Vector3 normal, float fraction)
            { Surface = surface; Point = point; Normal = normal; Fraction = fraction; }
        }

        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly Dictionary<string, Surface> byId = new Dictionary<string, Surface>(StringComparer.Ordinal);
        public int Count => surfaces.Count;
        public bool TryGet(string id, out Surface surface) => byId.TryGetValue(id, out surface);

        public HomeUrineSurfaceMap(Transform root, Transform excluded)
        {
            var readable = new HashSet<Transform>();
            var geometry = new Dictionary<Mesh, (Vector3[] vertices, int[] triangles)>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (excluded != null && filter.transform.IsChildOf(excluded)) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                Mesh mesh = filter.sharedMesh;
                if (renderer == null || mesh == null || !mesh.isReadable || IsEffect(filter.transform, root)) continue;
                if (!geometry.TryGetValue(mesh, out var data))
                { data = (mesh.vertices, mesh.triangles); geometry.Add(mesh, data); }
                var surface = new Surface
                {
                    Id = StablePath(filter.transform, root), Transform = filter.transform, Renderer = renderer,
                    Vertices = data.vertices, Triangles = data.triangles, Bounds = mesh.bounds,
                    Absorbs = IsBowlWater(filter.transform, root)
                };
                Add(surface);
                readable.Add(filter.transform);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider is CharacterController ||
                    (excluded != null && collider.transform.IsChildOf(excluded)) ||
                    IsEffect(collider.transform, root)) continue;
                // A gameplay proxy owning visible descendants is never a fluid surface.
                bool hasMesh = false;
                foreach (Transform rendererTransform in readable)
                    if (rendererTransform == collider.transform || rendererTransform.IsChildOf(collider.transform))
                    { hasMesh = true; break; }
                if (hasMesh) continue;
                Add(new Surface { Id = StablePath(collider.transform, root) + "#collision", Transform = collider.transform, Fallback = collider });
            }
            Refresh();
        }

        public void Refresh() { foreach (Surface surface in surfaces) surface.Refresh(); }

        public bool Cast(Vector3 start, Vector3 end, out Hit hit)
        {
            hit = default;
            float nearest = 1.00001f;
            foreach (Surface surface in surfaces)
            {
                if (surface.Cast(start, end, out Hit candidate) && candidate.Fraction < nearest)
                { nearest = candidate.Fraction; hit = candidate; }
            }
            return nearest <= 1f;
        }

        private void Add(Surface surface)
        {
            if (byId.ContainsKey(surface.Id)) return;
            byId.Add(surface.Id, surface);
            surfaces.Add(surface);
        }

        private static bool IsBowlWater(Transform child, Transform root)
        {
            for (Transform cursor = child; cursor != null && cursor != root; cursor = cursor.parent)
                if (cursor.name.IndexOf("Toilet Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cursor.name.IndexOf("Toilet Bowl Water", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsEffect(Transform child, Transform root)
        {
            for (Transform cursor = child; cursor != null && cursor != root; cursor = cursor.parent)
            {
                string name = cursor.name;
                if (name == "Home Exterior View" ||
                    name.IndexOf("Halo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Urine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("First Person", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string StablePath(Transform child, Transform root)
        {
            var parts = new List<string>();
            for (Transform cursor = child; cursor != null && cursor != root; cursor = cursor.parent)
            {
                int ordinal = 0;
                if (cursor.parent != null)
                    for (int i = 0; i < cursor.GetSiblingIndex(); i++)
                        if (cursor.parent.GetChild(i).name == cursor.name) ordinal++;
                parts.Add(cursor.name + "[" + ordinal + "]");
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
