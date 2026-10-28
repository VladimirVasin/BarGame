using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Measures imported portal clearance, unit profiles and face winding.</summary>
    public static class MountainRoadStructureDetailAssetSetup
    {
        public const string ModelPath = "Assets/Resources/MountainRoad/MountainRoadStructureDetails3D.fbx";
        public const string ManifestPath = "Assets/Resources/MountainRoad/MountainRoadStructureDetails3D.json";
        [MenuItem("Bar Promenade/Mountain Road/Validate Authored Structure Details")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != MountainRoadStructureDetails.DesignId || manifest.generator_version != "1.0.0" ||
                manifest.build_signature == null || manifest.build_signature.Length != 64 ||
                manifest.mesh_count != MountainRoadStructureDetails.ExpectedMeshCount || manifest.parts == null ||
                manifest.parts.Length != manifest.mesh_count || manifest.triangle_count > 6000 ||
                manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0)
                throw new InvalidOperationException("Malformed authored road-structure manifest.");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("Missing authored road-structure FBX.");
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != manifest.mesh_count || source.GetComponentsInChildren<Collider>(true).Length != 0 ||
                source.GetComponentsInChildren<Light>(true).Length != 0 || source.GetComponentsInChildren<Camera>(true).Length != 0 ||
                source.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("Road-structure asset is not the expected passive geometry pack.");
            int triangles = 0, vertices = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in manifest.parts)
            {
                MeshFilter filter = Array.Find(filters, item => item.name == part.name);
                if (filter == null || !seen.Add(part.name) || part.name != "GEO_RoadStructure_" + part.role)
                    throw new InvalidOperationException("Missing/duplicate road-structure role " + part.name);
                Mesh mesh = MountainCablewayCraftedGeometry.BakeImportedMesh(filter);
                try
                {
                    if (part.bounds_min == null || part.bounds_max == null || part.bounds_min.Length != 3 || part.bounds_max.Length != 3 ||
                        Vector3.Distance(mesh.bounds.min, Vec(part.bounds_min)) > .002f ||
                        Vector3.Distance(mesh.bounds.max, Vec(part.bounds_max)) > .002f || mesh.triangles.Length / 3 != part.triangle_count)
                        throw new InvalidOperationException("Actual imported road-structure metres changed " + part.name);
                    Vector3[] points = mesh.vertices;
                    if (part.role == "Portal")
                        foreach (Vector3 point in points)
                            if (Mathf.Abs(point.x) < 3.999f && point.y < 5.499f || point.z < -.001f || point.z > .121f)
                                throw new InvalidOperationException("Portal detail closes the original aperture.");
                    double volume = 0;
                    int[] indices = mesh.triangles;
                    for (int i = 0; i < indices.Length; i += 3)
                        volume += Vector3.Dot(points[indices[i]], Vector3.Cross(points[indices[i + 1]], points[indices[i + 2]])) / 6d;
                    if (volume <= 0 || Math.Abs(volume - part.signed_volume) > Math.Max(.002d, part.signed_volume * .015d))
                        throw new InvalidOperationException("Road-structure face winding changed " + part.name);
                    triangles += part.triangle_count;
                    vertices += mesh.vertexCount;
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            if (triangles != manifest.triangle_count || vertices > MountainRoadStructureDetails.MaximumTemplateVertices)
                throw new InvalidOperationException("Road-structure import exceeds its finite budget.");
        }
        public static void RunBatch()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateOrThrow();
            Debug.Log("MOUNTAIN ROAD STRUCTURE IMPORT OK");
        }
        private static Vector3 Vec(float[] values) => new Vector3(values[0], values[1], values[2]);
        [Serializable] private sealed class Manifest
        {
            public string design_id, generator_version, build_signature;
            public int mesh_count, triangle_count, animation_count;
            public bool colliders, lights, cameras;
            public Part[] parts;
        }
        [Serializable] private sealed class Part
        {
            public string name, role;
            public int triangle_count;
            public float[] bounds_min, bounds_max;
            public double signed_volume;
        }
    }
}
