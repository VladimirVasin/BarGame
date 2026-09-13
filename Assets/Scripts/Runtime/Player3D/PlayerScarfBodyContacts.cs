using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded, approximate cloth contacts with the hero alone. Installation
    /// measures bare anatomy and primary garment shells once; animation only
    /// moves the currently worn alternative of each anatomical ellipsoid.
    /// No scene geometry, physics queries or per-frame body baking is involved.
    /// </summary>
    public sealed class PlayerScarfBodyContacts
    {
        private const int MaximumParts = 17;
        private const float SurfacePadding = .003f;
        private const float SplitOverlap = .015f;
        private readonly Proxy[] proxies;
        private readonly PlayerWardrobe wardrobe;

        // These are primary shells in the production registry. Jacket coverage
        // deliberately does not hide the torso: the shirt shows through its open
        // front, but the jacket still owns the outer physical envelope.
        private static readonly Dictionary<Player3DAnatomicalPart, string[]> PrimaryGarments =
            new Dictionary<Player3DAnatomicalPart, string[]>
            {
                { Player3DAnatomicalPart.Torso, new[] { "CLO_JacketBody", "CLO_ShirtBody" } },
                { Player3DAnatomicalPart.LowerTorso, new[] { "CLO_JacketBody", "CLO_ShirtBody" } },
                { Player3DAnatomicalPart.Pelvis, new[] { "CLO_TrousersPelvis" } },
                { Player3DAnatomicalPart.LeftUpperArm, new[] { "CLO_JacketSleeve.L" } },
                { Player3DAnatomicalPart.RightUpperArm, new[] { "CLO_JacketSleeve.R" } },
                { Player3DAnatomicalPart.LeftForearm, new[] { "CLO_JacketForearm.L" } },
                { Player3DAnatomicalPart.RightForearm, new[] { "CLO_JacketForearm.R" } },
                { Player3DAnatomicalPart.LeftThigh, new[] { "CLO_TrousersThigh.L" } },
                { Player3DAnatomicalPart.RightThigh, new[] { "CLO_TrousersThigh.R" } },
                { Player3DAnatomicalPart.LeftShin, new[] { "CLO_TrousersShin.L" } },
                { Player3DAnatomicalPart.RightShin, new[] { "CLO_TrousersShin.R" } },
                { Player3DAnatomicalPart.LeftFoot, new[] { "CLO_Boot.L" } },
                { Player3DAnatomicalPart.RightFoot, new[] { "CLO_Boot.R" } }
            };

        private sealed class Envelope
        {
            public Renderer Renderer;
            public string GarmentId;
            public Matrix4x4 UnitToBone;
            public Matrix4x4 BoneToUnit;
            public Vector3[] SupportNormals;
            public float[] SupportOffsets;
        }

        private sealed class Proxy
        {
            public Player3DAnatomicalPart Part;
            public Transform Bone;
            public Envelope Bare;
            public Envelope[] Clothing;
            public Envelope Selected;
            public Matrix4x4 UnitToWorld;
            public Matrix4x4 WorldToUnit;
            public Bounds WorldBounds;
            public Vector3 CenterEscape;
            public float MinimumWorldRadius;
            public bool Active;
            public Plane[] WorldPlanes;
        }

        public int Count => proxies.Length;
        /// <summary>Number of vertices corrected by the most recent Resolve.</summary>
        public int LastContactCount { get; private set; }

        /// <summary>The measured shell currently supplying one anatomy's contact envelope.</summary>
        public Renderer EnvelopeRenderer(Player3DAnatomicalPart part)
        {
            foreach (Proxy proxy in proxies)
                if (proxy.Part == part) return proxy.Selected?.Renderer;
            return null;
        }

        public PlayerScarfBodyContacts(Player3DAssetRegistry registry, string excludedGarmentSlot = null, bool useMeshSupportPlanes = false)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            wardrobe = registry.GetComponent<PlayerWardrobe>();
            var garmentIds = new Dictionary<Renderer, string>();
            if (wardrobe != null && wardrobe.IsConfigured)
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                {
                    if (garment.Slot == excludedGarmentSlot) continue;
                    foreach (Renderer renderer in garment.Renderers)
                        if (renderer != null) garmentIds[renderer] = garment.Id;
                }
            var meshBindings = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
                if (binding != null && binding.Renderer != null && !string.IsNullOrEmpty(binding.MeshName))
                    meshBindings[binding.MeshName] = binding.Renderer;
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
                Vector3[] MeasureOnce(Renderer renderer)
                {
                    if (!measured.TryGetValue(renderer, out Vector3[] vertices))
                    {
                        vertices = MeasureWorldVertices(renderer, scratch);
                        measured.Add(renderer, vertices);
                    }
                    return vertices;
                }
                foreach (Player3DAnatomicalPartBinding part in parts)
                {
                    Envelope bare = BuildEnvelope(part, parts, part.Renderer, MeasureOnce(part.Renderer), useMeshSupportPlanes);
                    if (bare == null) continue;
                    var clothing = new List<Envelope>(2);
                    if (wardrobe != null && wardrobe.IsConfigured &&
                        PrimaryGarments.TryGetValue(part.Part, out string[] candidates))
                    {
                        foreach (string name in candidates)
                        {
                            if (!meshBindings.TryGetValue(name, out Renderer renderer) ||
                                !garmentIds.TryGetValue(renderer, out string garmentId)) continue;
                            Envelope envelope = BuildEnvelope(part, parts, renderer, MeasureOnce(renderer), useMeshSupportPlanes);
                            if (envelope != null) { envelope.GarmentId = garmentId; clothing.Add(envelope); }
                        }
                    }
                    built.Add(new Proxy { Part = part.Part, Bone = part.Bone, Bare = bare, Clothing = clothing.ToArray() });
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
                proxy.Selected = proxy.Bare;
                if (wardrobe != null)
                    foreach (Envelope envelope in proxy.Clothing)
                    {
                        if (envelope.Renderer == null || !wardrobe.IsEquipped(envelope.GarmentId)) continue;
                        // A camera can hide dressed geometry without removing it.
                        // An owned appearance may temporarily undress the same selection.
                        if (wardrobe.HasAppearanceLease && !envelope.Renderer.enabled) continue;
                        proxy.Selected = envelope;
                        break;
                    }
                proxy.UnitToWorld = proxy.Bone.localToWorldMatrix * proxy.Selected.UnitToBone;
                proxy.WorldToUnit = proxy.Selected.BoneToUnit * proxy.Bone.worldToLocalMatrix;
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
                if (proxy.Selected.SupportNormals != null)
                {
                    int count = proxy.Selected.SupportNormals.Length;
                    if (proxy.WorldPlanes == null || proxy.WorldPlanes.Length != count) proxy.WorldPlanes = new Plane[count];
                    Matrix4x4 normalMatrix = proxy.Bone.worldToLocalMatrix.transpose;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 normal = normalMatrix.MultiplyVector(proxy.Selected.SupportNormals[i]).normalized;
                        Vector3 support = proxy.Bone.TransformPoint(proxy.Selected.SupportNormals[i] * proxy.Selected.SupportOffsets[i]);
                        proxy.WorldPlanes[i] = new Plane(normal, support + normal * SurfacePadding);
                    }
                }
                else proxy.WorldPlanes = null;
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
                        if (proxy.WorldPlanes != null)
                        {
                            float escape = float.PositiveInfinity;
                            Vector3 normal = Vector3.zero;
                            foreach (Plane plane in proxy.WorldPlanes)
                            {
                                float distance = plane.GetDistanceToPoint(point);
                                if (distance >= 0f) { escape = 0f; break; }
                                if (-distance < escape) { escape = -distance; normal = plane.normal; }
                            }
                            if (escape <= 0f) continue;
                            point += normal * (escape + .0001f);
                            moved = corrected = true;
                            continue;
                        }
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
                if (proxy.WorldPlanes != null)
                {
                    bool inside = true;
                    foreach (Plane plane in proxy.WorldPlanes)
                        if (plane.GetDistanceToPoint(worldPoint) >= -Mathf.Max(0f, tolerance)) { inside = false; break; }
                    if (inside) return true;
                    continue;
                }
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

        private static Envelope BuildEnvelope(
            Player3DAnatomicalPartBinding part,
            List<Player3DAnatomicalPartBinding> parts,
            Renderer renderer,
            Vector3[] worldVertices, bool useMeshSupportPlanes)
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
            var envelope = new Envelope
            {
                Renderer = renderer,
                UnitToBone = unitToBone,
                BoneToUnit = unitToBone.inverse
            };
            if (useMeshSupportPlanes) BuildSupportPlanes(envelope, renderer, toBone, worldVertices, localVertices);
            return envelope;
        }

        private static void BuildSupportPlanes(Envelope envelope, Renderer renderer, Matrix4x4 toBone,
            Vector3[] worldVertices, List<Vector3> ownedVertices)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !mesh.isReadable) return;
            int[] triangles = mesh.triangles;
            var normals = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = toBone.MultiplyPoint3x4(worldVertices[triangles[i]]);
                Vector3 b = toBone.MultiplyPoint3x4(worldVertices[triangles[i + 1]]);
                Vector3 c = toBone.MultiplyPoint3x4(worldVertices[triangles[i + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float length = normal.magnitude;
                if (length < 1e-16f) continue;
                normal /= length; // Imported bone coordinates can be much smaller than Vector3.Normalize's epsilon.
                bool duplicate = false;
                foreach (Vector3 previous in normals)
                    if (Vector3.Dot(normal, previous) > .9999f) { duplicate = true; break; }
                if (!duplicate) normals.Add(normal);
            }
            var offsets = new float[normals.Count];
            for (int i = 0; i < normals.Count; i++)
            {
                float maximum = float.NegativeInfinity;
                foreach (Vector3 vertex in ownedVertices) maximum = Mathf.Max(maximum, Vector3.Dot(normals[i], vertex));
                offsets[i] = maximum;
            }
            // Each half-space supports the real vertex cloud, including mildly
            // non-planar authored quads. This keeps every source point enclosed
            // without the empty corner volume of an expanded ellipsoid.
            envelope.SupportNormals = normals.ToArray(); envelope.SupportOffsets = offsets;
        }
    }
}
