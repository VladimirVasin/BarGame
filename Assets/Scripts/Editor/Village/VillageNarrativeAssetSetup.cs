using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageNarrativeAssetSetup
    {
        public const string ModelPath = "Assets/Resources/VillageNarrative/VillageNarrative3D.fbx";
        public const string ManifestPath = "Assets/Resources/VillageNarrative/VillageNarrative3D.json";

        [MenuItem("Bar Promenade/Village/Import Narrative Props")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateOrThrow();
        }

        [MenuItem("Bar Promenade/Village/Validate Narrative Props")]
        public static void ValidateOrThrow()
        {
            var manifest = JsonUtility.FromJson<VillageNarrativeManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != VillageNarrativeLibrary.DesignId ||
                manifest.generator_version != VillageNarrativeLibrary.GeneratorVersion ||
                manifest.scale_mode != "fixed_metres" || manifest.uv_mode != "projected_metres" ||
                manifest.prop_count != 30 || manifest.props == null || manifest.props.Length != 30 ||
                manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0 ||
                string.IsNullOrEmpty(manifest.build_signature) || manifest.build_signature.Length != 64)
                throw new InvalidOperationException("Narrative model manifest contract drifted.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !importer.useFileScale || !Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion || !importer.preserveHierarchy || !importer.isReadable ||
                importer.importAnimation || importer.importLights || importer.importCameras || importer.addCollider ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Narrative props must retain passive real-metre import.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null || model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Camera>(true).Length != 0 || model.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Narrative FBX must contain only meshes and anchors.");
            var meshes = new Dictionary<string, MeshFilter>();
            var transforms = new Dictionary<string, Transform>();
            foreach (var transform in model.GetComponentsInChildren<Transform>(true)) transforms[transform.name] = transform;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Missing or duplicated narrative mesh.");
            if (meshes.Count != manifest.mesh_count) throw new InvalidOperationException("Narrative mesh count drifted.");
            var ids = new HashSet<int>(); int triangleCount = 0;
            foreach (var definition in manifest.props)
            {
                if (!ids.Add(definition.id) || definition.id < 1 || definition.id > 32 ||
                    definition.id == 26 || definition.id == 27 || definition.kind != "N" + definition.id.ToString("00"))
                    throw new InvalidOperationException("Narrative model identity drifted.");
                Vector3 minimum = Vector3.positiveInfinity, maximum = Vector3.negativeInfinity;
                foreach (var part in definition.parts)
                {
                    if (!meshes.TryGetValue(part.mesh, out var filter)) throw new InvalidOperationException("Missing " + part.mesh);
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var point = filter.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                    }
                    Near(low, part.bounds_min, part.mesh + " minimum"); Near(high, part.bounds_max, part.mesh + " maximum");
                    minimum = Vector3.Min(minimum, low); maximum = Vector3.Max(maximum, high);
                    int triangles = 0;
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += (int)filter.sharedMesh.GetIndexCount(i) / 3;
                    if (triangles != part.triangles) throw new InvalidOperationException("Narrative topology differs: " + part.mesh);
                    triangleCount += triangles;
                }
                Near(minimum, definition.bounds_min, definition.kind + " minimum");
                Near(maximum, definition.bounds_max, definition.kind + " maximum");
                foreach (var anchor in definition.anchors)
                {
                    string name = "ANCHOR_" + definition.kind + "_" + anchor.name;
                    if (!transforms.TryGetValue(name, out var transform)) throw new InvalidOperationException("Missing " + name);
                    Near(transform.position, anchor.position, name);
                }
            }
            if (triangleCount != manifest.triangle_count) throw new InvalidOperationException("Narrative triangle total differs.");
            Debug.Log("VILLAGE NARRATIVE UNITY VALIDATION OK: measured metre bounds, topology and anchors.");
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, VillageLifePropLibrary.Vector(expected)) > .005f)
                throw new InvalidOperationException("Narrative imported metre bounds drifted: " + label + " " + actual);
        }
    }

    public sealed class VillageNarrativeModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != VillageNarrativeAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.importBlendShapes = false;
            importer.addCollider = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true; importer.keepQuads = false; importer.generateSecondaryUV = false;
            importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
