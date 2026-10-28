using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Read-only proof of the actual imported metre slots and winding.</summary>
    public static class MountainCablewayCraftedAssetSetup
    {
        public const string ModelPath = "Assets/Resources/MountainRoad/CablewayCraftedParts3D.fbx";
        public const string ManifestPath = "Assets/Resources/MountainRoad/CablewayCraftedParts3D.json";

        [MenuItem("Bar Promenade/Mountain Road/Validate Crafted Cableway")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != MountainCablewayCraftedGeometry.DesignId ||
                manifest.generator_version != "1.0.0" || manifest.build_signature == null ||
                manifest.build_signature.Length != 64 || manifest.mesh_count != MountainCablewayCraftedGeometry.ExpectedMeshCount ||
                manifest.parts == null || manifest.parts.Length != manifest.mesh_count ||
                manifest.triangle_count > 12000 || manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0)
                throw new InvalidOperationException("Malformed crafted cableway manifest.");
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (template == null) throw new InvalidOperationException("Missing crafted cableway FBX.");
            if (template.GetComponentsInChildren<Collider>(true).Length != 0 ||
                template.GetComponentsInChildren<Light>(true).Length != 0 ||
                template.GetComponentsInChildren<Camera>(true).Length != 0 ||
                template.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("Crafted cableway geometry must remain passive.");
            var filters = template.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != manifest.mesh_count)
                throw new InvalidOperationException("Crafted cableway mesh-role count drift.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int triangles = 0, vertices = 0;
            foreach (Part part in manifest.parts)
            {
                MeshFilter filter = Array.Find(filters, item => item.name == part.name);
                if (filter == null || !seen.Add(part.name) || part.name != "GEO_Cableway_" + part.role)
                    throw new InvalidOperationException("Missing or duplicate cableway role " + part.name);
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.subMeshCount != 1 ||
                    mesh.GetIndexCount(0) / 3 != part.triangle_count)
                    throw new InvalidOperationException("Crafted cableway topology drift " + part.name);
                Vector3[] positions = mesh.vertices;
                Vector2[] uv = mesh.uv;
                if (uv.Length != positions.Length || mesh.normals.Length != positions.Length)
                    throw new InvalidOperationException("Missing authored UVs or normals " + part.name);
                var bounds = new Bounds();
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = matrix.MultiplyPoint3x4(positions[i]);
                    if (!Finite(positions[i].x) || !Finite(positions[i].y) || !Finite(positions[i].z) ||
                        !Finite(uv[i].x) || !Finite(uv[i].y))
                        throw new InvalidOperationException("Non-finite cableway geometry " + part.name);
                    if (i == 0) bounds = new Bounds(positions[i], Vector3.zero);
                    else bounds.Encapsulate(positions[i]);
                }
                if (part.bounds_min == null || part.bounds_max == null || part.bounds_min.Length != 3 || part.bounds_max.Length != 3 ||
                    Vector3.Distance(bounds.min, Vec(part.bounds_min)) > .002f || Vector3.Distance(bounds.max, Vec(part.bounds_max)) > .002f)
                    throw new InvalidOperationException("Imported cableway metre/axis envelope changed " + part.name + ": " + bounds);
                double volume = 0;
                int[] indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                    volume += Vector3.Dot(positions[indices[i]], Vector3.Cross(positions[indices[i + 1]], positions[indices[i + 2]])) / 6d;
                // Transform parity is applied by the runtime mesh bake too.
                volume *= matrix.determinant < 0f ? -1d : 1d;
                if (volume <= 0 || Math.Abs(volume - part.signed_volume) > Math.Max(.002d, part.signed_volume * .015d))
                    throw new InvalidOperationException("Imported cableway face winding/volume changed " + part.name);
                triangles += part.triangle_count;
                vertices += mesh.vertexCount;
            }
            if (triangles != manifest.triangle_count || vertices > MountainCablewayCraftedGeometry.MaximumTemplateVertices)
                throw new InvalidOperationException("Crafted cableway finite geometry budget drift.");
        }

        public static void RunBatch()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateOrThrow();
            Debug.Log("CRAFTED CABLEWAY IMPORT OK");
        }

        /// <summary>One typed preparation entry for the shared road capture.</summary>
        public static void RunAllBatch()
        {
            RunBatch();
            MountainRoadStructureDetailAssetSetup.RunBatch();
        }

        private static Vector3 Vec(float[] values) => new Vector3(values[0], values[1], values[2]);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
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

    public sealed class MountainCablewayCraftedModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if ((assetPath != MountainCablewayCraftedAssetSetup.ModelPath &&
                assetPath != MountainRoadStructureDetailAssetSetup.ModelPath) || !(assetImporter is ModelImporter importer)) return;
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
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true;
            importer.weldVertices = true;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
