using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded, approximate cloth contacts with the hero alone. Installation
    /// measures the registered anatomy once; animation only moves ellipsoids.
    /// No scene geometry, physics queries or per-frame body baking is involved.
    /// </summary>
    public sealed class PlayerScarfBodyContacts
    {
        private const int MaximumParts = 17;
        private const float SurfacePadding = .003f;
        private const float SplitOverlap = .015f;
        private readonly Proxy[] proxies;

        private sealed class Proxy
        {
            public Transform Bone;
            public Matrix4x4 UnitToBone;
            public Matrix4x4 BoneToUnit;
            public Matrix4x4 UnitToWorld;
            public Matrix4x4 WorldToUnit;
            public Bounds WorldBounds;
            public Vector3 CenterEscape;
            public float MinimumWorldRadius;
            public bool Active;
        }

        public int Count => proxies.Length;
        /// <summary>Number of vertices corrected by the most recent Resolve.</summary>
        public int LastContactCount { get; private set; }

        public PlayerScarfBodyContacts(Player3DAssetRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var parts = new List<Player3DAnatomicalPartBinding>(MaximumParts);
            var seenParts = new HashSet<Player3DAnatomicalPart>();
            foreach (Player3DAnatomicalPartBinding part in registry.AnatomicalParts)
            {
                if (part == null || part.Renderer == null || part.Bone == null ||
                    parts.Count >= MaximumParts || !seenParts.Add(part.Part)) continue;
                parts.Add(part);
            }

            // Production V2 has one continuous torso renderer and a separate
            // spine anchor, rather than a serialized LowerTorso renderer.
            if (!seenParts.Contains(Player3DAnatomicalPart.LowerTorso) &&
                parts.Count < MaximumParts && registry.Anchors.Spine != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Part != Player3DAnatomicalPart.Torso ||
                        parts[i].Bone == registry.Anchors.Spine) continue;
                    parts.Add(new Player3DAnatomicalPartBinding(
                        Player3DAnatomicalPart.LowerTorso, parts[i].Renderer,
                        registry.Anchors.Spine));
                    break;
                }
            }

            var measured = new Dictionary<Renderer, Vector3[]>();
            var built = new List<Proxy>(parts.Count);
            var scratch = new Mesh { name = "Scarf Body Measurement", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (Player3DAnatomicalPartBinding part in parts)
                {
                    if (!measured.TryGetValue(part.Renderer, out Vector3[] worldVertices))
                    {
                        worldVertices = MeasureWorldVertices(part.Renderer, scratch);
                        measured.Add(part.Renderer, worldVertices);
                    }
                    Proxy proxy = BuildProxy(part, parts, worldVertices);
                    if (proxy != null) built.Add(proxy);
                }
            }
            finally
            {
                PlayerScarfResources.DestroyOwned(scratch);
            }
            proxies = built.ToArray();
            UpdatePose();
        }

        public void UpdatePose()
        {
            foreach (Proxy proxy in proxies)
            {
                proxy.Active = proxy.Bone != null;
                if (!proxy.Active) continue;
                proxy.UnitToWorld = proxy.Bone.localToWorldMatrix * proxy.UnitToBone;
                proxy.WorldToUnit = proxy.BoneToUnit * proxy.Bone.worldToLocalMatrix;
                Matrix4x4 matrix = proxy.UnitToWorld;
                // The row lengths are the exact AABB extents of a transformed
                // unit sphere, including rotated/non-uniform imported scales.
                var extent = new Vector3(
                    Mathf.Sqrt(matrix.m00 * matrix.m00 + matrix.m01 * matrix.m01 + matrix.m02 * matrix.m02),
                    Mathf.Sqrt(matrix.m10 * matrix.m10 + matrix.m11 * matrix.m11 + matrix.m12 * matrix.m12),
                    Mathf.Sqrt(matrix.m20 * matrix.m20 + matrix.m21 * matrix.m21 + matrix.m22 * matrix.m22));
                proxy.WorldBounds = new Bounds(matrix.MultiplyPoint3x4(Vector3.zero), extent * 2f);
                float x = matrix.MultiplyVector(Vector3.right).magnitude;
                float y = matrix.MultiplyVector(Vector3.up).magnitude;
                float z = matrix.MultiplyVector(Vector3.forward).magnitude;
                proxy.MinimumWorldRadius = Mathf.Max(.00001f, Mathf.Min(x, Mathf.Min(y, z)));
                proxy.CenterEscape = x <= y && x <= z ? Vector3.right : y <= z ? Vector3.up : Vector3.forward;
            }
        }

        public void Resolve(Vector3[] worldVertices, float[] freedom = null)
        {
            if (worldVertices == null) throw new ArgumentNullException(nameof(worldVertices));
            if (freedom != null && freedom.Length != worldVertices.Length)
                throw new ArgumentException("Scarf freedom must match the vertex count.", nameof(freedom));
            LastContactCount = 0;
            for (int i = 0; i < worldVertices.Length; i++)
            {
                if (freedom != null && freedom[i] <= 0f) continue;
                Vector3 point = worldVertices[i];
                bool corrected = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool moved = false;
                    foreach (Proxy proxy in proxies)
                    {
                        if (!proxy.Active || !proxy.WorldBounds.Contains(point)) continue;
                        Vector3 unit = proxy.WorldToUnit.MultiplyPoint3x4(point);
                        float square = unit.sqrMagnitude;
                        if (square >= 1f) continue;
                        unit = square > .00000001f ? unit / Mathf.Sqrt(square) : proxy.CenterEscape;
                        // A tiny outward bias prevents boundary round-off from
                        // reporting the same stationary contact next frame.
                        point = proxy.UnitToWorld.MultiplyPoint3x4(unit * 1.0001f);
                        moved = corrected = true;
                    }
                    if (!moved) break;
                }
                if (corrected)
                {
                    worldVertices[i] = point;
                    LastContactCount++;
                }
            }
        }

        public bool Contains(Vector3 worldPoint, float tolerance = .001f)
        {
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active || !proxy.WorldBounds.Contains(worldPoint)) continue;
                float limit = Mathf.Max(0f, 1f - Mathf.Max(0f, tolerance) / proxy.MinimumWorldRadius);
                if (proxy.WorldToUnit.MultiplyPoint3x4(worldPoint).sqrMagnitude < limit * limit) return true;
            }
            return false;
        }

        private static Vector3[] MeasureWorldVertices(Renderer renderer, Mesh scratch)
        {
            Vector3[] vertices;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                // Match the production skin convention: useScale plus the full
                // renderer matrix applies FBX unit conversion exactly once.
                skin.BakeMesh(scratch, true);
                vertices = scratch.vertices;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return Array.Empty<Vector3>();
                vertices = filter.sharedMesh.vertices;
            }
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            return vertices;
        }

        private static Proxy BuildProxy(
            Player3DAnatomicalPartBinding part,
            List<Player3DAnatomicalPartBinding> parts,
            Vector3[] worldVertices)
        {
            Matrix4x4 toBone = part.Bone.worldToLocalMatrix;
            var localVertices = new List<Vector3>(worldVertices.Length);
            foreach (Vector3 point in worldVertices)
            {
                // A shared torso mesh is divided between chest and spine; a
                // small overlap keeps their join covered while bending.
                bool owned = true;
                foreach (Player3DAnatomicalPartBinding other in parts)
                {
                    if (other == part || other.Renderer != part.Renderer || other.Bone == part.Bone) continue;
                    Vector3 axis = other.Bone.position - part.Bone.position;
                    float length = axis.magnitude;
                    if (length > .00001f && Vector3.Dot(point - part.Bone.position, axis / length) > length * .5f + SplitOverlap)
                    {
                        owned = false;
                        break;
                    }
                }
                if (owned) localVertices.Add(toBone.MultiplyPoint3x4(point));
            }
            if (localVertices.Count == 0) return null;
            var bounds = new Bounds(localVertices[0], Vector3.zero);
            foreach (Vector3 point in localVertices) bounds.Encapsulate(point);
            Matrix4x4 toWorld = part.Bone.localToWorldMatrix;
            Vector3 padding = new Vector3(
                SurfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.right).magnitude),
                SurfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.up).magnitude),
                SurfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.forward).magnitude));
            Vector3 radii = bounds.extents + padding;
            float expansion = 1f;
            foreach (Vector3 point in localVertices)
            {
                Vector3 offset = point - bounds.center;
                var unit = new Vector3(offset.x / radii.x, offset.y / radii.y, offset.z / radii.z);
                expansion = Mathf.Max(expansion, unit.magnitude);
            }
            radii *= expansion;
            Matrix4x4 unitToBone = Matrix4x4.TRS(bounds.center, Quaternion.identity, radii);
            return new Proxy
            {
                Bone = part.Bone,
                UnitToBone = unitToBone,
                BoneToUnit = unitToBone.inverse
            };
        }
    }
}
