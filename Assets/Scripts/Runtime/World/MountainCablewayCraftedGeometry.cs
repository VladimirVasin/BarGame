using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Passive authored profiles in the existing primitive envelopes. The plan,
    /// transforms, colliders, renderer/property blocks and moving pivots remain
    /// owned by MountainCablewayWorldBuilder. One finite mesh set is shared by
    /// every cabin/support in this world and released with that world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainCablewayCraftedGeometry : MonoBehaviour
    {
        public const string ResourcePath = "MountainRoad/CablewayCraftedParts3D";
        public const string DesignId = "mountain_cableway_crafted_parts_v1";
        public const int ExpectedMeshCount = 20;
        public const int MaximumTemplateVertices = 24000;
        public const int MaximumReplacements = 512;
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        public int ReplacementCount { get; private set; }
        public int SharedMeshCount => meshes.Count;
        public int TemplateVertexCount { get; private set; }

        public void Apply(Transform world)
        {
            if (meshes.Count != 0) throw new InvalidOperationException("Cableway crafted geometry is already initialized.");
            GameObject source = Resources.Load<GameObject>(ResourcePath);
            if (source == null) throw new InvalidOperationException("Missing generated cableway geometry at Resources/" + ResourcePath);
            try
            {
                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    string role = filter.name;
                    if (!role.StartsWith("GEO_Cableway_", StringComparison.Ordinal) || meshes.ContainsKey(role))
                        throw new InvalidOperationException("Unexpected or duplicate authored cableway role " + role);
                    Mesh mesh = BakeImportedMesh(filter);
                    meshes.Add(role, mesh);
                    TemplateVertexCount += mesh.vertexCount;
                    if (TemplateVertexCount > MaximumTemplateVertices)
                        throw new InvalidOperationException("Cableway authored geometry exceeds its finite vertex budget.");
                }
                if (meshes.Count != ExpectedMeshCount)
                    throw new InvalidOperationException("Cableway pack must have exactly twenty authored roles.");
                foreach (MeshFilter filter in world.GetComponentsInChildren<MeshFilter>(true))
                {
                    string role = RoleFor(filter.name);
                    if (role == null) continue;
                    if (!meshes.TryGetValue("GEO_Cableway_" + role, out Mesh mesh))
                        throw new InvalidOperationException("Cableway pack is missing " + role);
                    // A cylinder's source envelope is height two; a box's is
                    // height one. Compare the actual meshes, not just metadata.
                    Bounds before = filter.sharedMesh.bounds;
                    if (Vector3.Distance(before.min, mesh.bounds.min) > .002f ||
                        Vector3.Distance(before.max, mesh.bounds.max) > .002f)
                        throw new InvalidOperationException("Authored cableway profile changes the envelope of " + filter.name);
                    filter.sharedMesh = mesh;
                    ReplacementCount++;
                    if (ReplacementCount > MaximumReplacements)
                        throw new InvalidOperationException("Cableway crafted replacement budget exceeded.");
                }
                if (ReplacementCount == 0) throw new InvalidOperationException("Cableway authored profiles bound to no receivers.");
            }
            catch { Release(); throw; }
        }

        public static string RoleFor(string name)
        {
            switch (name)
            {
                case "Corrugated Station Canopy": return "Canopy";
                case "Rusty Service Hut Door": return "Door";
                case "Visible Cableway Motor Reducer": return "Reducer";
                case "Machine Deck": return "Deck";
                case "Cabin Roof": return "Roof";
                case "Cabin Lower Skirt": return "Skirt";
                case "Cabin Bench": return "Bench";
                case "Cabin Corner Post": return "PostY";
                case "Cabin Hanger": return "PostY";
                case "Physical Station Column":
                case "Physical Bullwheel Pedestal":
                case "Physical Machine Deck Prop":
                case "Physical Boarding Post": return "PostY";
                case "Physical Drive Service Hut": return "Hut";
                case "Physical Tension Carriage": return "Carriage";
                case "Cabin Cable Grip": return "Grip";
                case "Cabin Grab Rail":
                case "Cabin Safety Bar": return "RailZ";
                case "A-Frame Left Leg":
                case "A-Frame Right Leg":
                case "Headframe Upper Strut":
                case "Headframe Lower Strut":
                case "Rusty Tower Cross Brace":
                case "Tension Hanger": return "BeamZ";
                case "Visible Support Roller Crossbeam": return "BeamX";
                case "Physical Boarding Rail":
                case "Bullwheel Spoke": return "BeamX";
                case "Platform Edge Nosing": return "Nosing";
                case "Tension Rail": return "ChannelZ";
                case "Tension Weight Plate": return "Weight";
                case "Bullwheel Disc": return "Bullwheel";
                case "Bullwheel Hub": return "Hub";
                case "Visible Lower Guide Roller":
                case "Visible Cable Roller": return "Roller";
                default: return null;
            }
        }

        internal static Mesh BakeImportedMesh(MeshFilter filter)
        {
            Mesh source = filter.sharedMesh;
            if (source == null || !source.isReadable || source.subMeshCount != 1)
                throw new InvalidOperationException("Cableway authored mesh must be readable with one material role.");
            // FBX root owns the unit/axis conversion. Bake that full transform
            // exactly once into each shared slot, never reset an instance scale.
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3[] vertices = source.vertices, normals = source.normals;
            Vector4[] tangents = source.tangents;
            float parity = matrix.determinant < 0f ? -1f : 1f;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                if (normals.Length == vertices.Length) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                if (tangents.Length == vertices.Length)
                {
                    Vector3 tangent = matrix.MultiplyVector(tangents[i]).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w * parity);
                }
            }
            int[] indices = source.triangles;
            if (parity < 0f)
                for (int i = 0; i < indices.Length; i += 3)
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            var result = new Mesh { name = filter.name + " (shared metre slot)", hideFlags = HideFlags.DontSave };
            result.vertices = vertices;
            result.triangles = indices;
            result.uv = source.uv;
            if (normals.Length == vertices.Length) result.normals = normals;
            else result.RecalculateNormals();
            if (tangents.Length == vertices.Length) result.tangents = tangents;
            else result.RecalculateTangents();
            result.RecalculateBounds();
            return result;
        }

        private void OnDestroy() => Release();
        private void Release()
        {
            foreach (Mesh mesh in meshes.Values)
                if (mesh != null)
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
            meshes.Clear();
        }
    }
}
