using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports and measures the four passive fixed-metre canopy roles.</summary>
    public static class UpperCablewayCanopyAssetSetup
    {
        public const string ModelPath = "Assets/Resources/Village/UpperCablewayCanopy3D.fbx";
        public const string ManifestPath = "Assets/Resources/Village/UpperCablewayCanopy3D.json";
        private static bool isBuilding;

        [MenuItem("Bar Promenade/Village/Validate Upper Station Canopy")]
        public static void BuildOrThrow()
        {
            if (isBuilding) return;
            isBuilding = true;
            try
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                ValidateOrThrow();
            }
            finally { isBuilding = false; }
        }

        public static void RunBatch() => BuildOrThrow();

        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != UpperCablewayCanopyAssetProvider.DesignId ||
                manifest.generator_version != UpperCablewayCanopyAssetProvider.GeneratorVersion ||
                manifest.build_signature == null || manifest.build_signature.Length != 64 ||
                manifest.mesh_count != UpperCablewayCanopyAssetProvider.MeshCount ||
                manifest.parts == null || manifest.parts.Length != manifest.mesh_count ||
                manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0)
                throw new InvalidOperationException("Malformed upper canopy manifest.");
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (template == null) throw new InvalidOperationException("Missing upper canopy FBX.");
            GameObject model = UnityEngine.Object.Instantiate(template);
            try
            {
                // Preserve imported scale and rotation, exactly as runtime does.
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length != manifest.mesh_count ||
                    model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    model.GetComponentsInChildren<Light>(true).Length != 0 ||
                    model.GetComponentsInChildren<Camera>(true).Length != 0 ||
                    model.GetComponentsInChildren<Animator>(true).Length != 0)
                    throw new InvalidOperationException("Upper canopy is not the four-role passive model.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                int triangles = 0;
                foreach (Part part in manifest.parts)
                {
                    MeshFilter filter = Array.Find(filters, item => item.name == part.name);
                    if (filter == null || !seen.Add(part.name) || part.name != "GEO_UpperCanopy_" + part.role)
                        throw new InvalidOperationException("Missing/duplicate upper canopy role " + part.name);
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || mesh.subMeshCount != 1 ||
                        mesh.GetIndexCount(0) / 3 != part.triangle_count)
                        throw new InvalidOperationException("Upper canopy triangle drift: " + part.name);
                    triangles += part.triangle_count;
                    var bounds = new Bounds();
                    bool first = true;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                    ValidateBounds(bounds, part);
                }
                if (triangles != manifest.triangle_count)
                    throw new InvalidOperationException("Upper canopy total triangle drift.");
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        private static void ValidateBounds(Bounds bounds, Part part)
        {
            if (part.bounds_min == null || part.bounds_max == null ||
                part.bounds_min.Length != 3 || part.bounds_max.Length != 3)
                throw new InvalidOperationException("Missing measured canopy bounds.");
            var min = new Vector3(part.bounds_min[0], part.bounds_min[1], part.bounds_min[2]);
            var max = new Vector3(part.bounds_max[0], part.bounds_max[1], part.bounds_max[2]);
            if (Vector3.Distance(bounds.min, min) > .003f || Vector3.Distance(bounds.max, max) > .003f)
                throw new InvalidOperationException($"Upper canopy {part.name} bounds {bounds.min}/{bounds.max} " +
                    $"differ from {min}/{max}; preserve the imported axes and FBX unit scale.");
        }

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
        }
    }

    public sealed class UpperCablewayCanopyModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != UpperCablewayCanopyAssetSetup.ModelPath ||
                !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
