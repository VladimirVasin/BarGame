using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>No prefab/provider generation: the passive FBX and its measured
    /// manifest are the runtime resources, with shared existing surface sheets.</summary>
    public static class VillageLifePropAssetSetup
    {
        public const string ModelPath = "Assets/Resources/VillageLife/VillageLifeProps3D.fbx";
        public const string ManifestPath = "Assets/Resources/VillageLife/VillageLifeProps3D.json";

        [MenuItem("Bar Promenade/Village/Import Life Props")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        [MenuItem("Bar Promenade/Village/Validate Life Props")]
        public static void ValidateOrThrow()
        {
            var manifest = JsonUtility.FromJson<VillageLifePropManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != VillageLifePropLibrary.DesignId ||
                manifest.generator_version != VillageLifePropLibrary.GeneratorVersion ||
                manifest.scale_mode != "fixed_metres" || manifest.uv_mode != "projected_metres" ||
                manifest.prop_count != VillageLifePropLibrary.ExpectedPropCount || manifest.props == null ||
                manifest.props.Length != VillageLifePropLibrary.ExpectedPropCount ||
                manifest.stage1_signature != VillageLifePropLibrary.Stage1Signature ||
                manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0 ||
                string.IsNullOrEmpty(manifest.build_signature) || manifest.build_signature.Length != 64)
                throw new InvalidOperationException("Village Life prop manifest contract drifted.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion || !importer.preserveHierarchy || !importer.isReadable ||
                importer.importAnimation || importer.importLights || importer.importCameras || importer.addCollider ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Village Life import must preserve passive metre geometry.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null || model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0 ||
                model.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Village Life FBX must contain passive geometry and anchors only.");
            var meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
                transforms[transform.name] = transform;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate/missing imported life mesh.");
            if (meshes.Count != manifest.mesh_count)
                throw new InvalidOperationException("Village Life FBX mesh count differs from manifest.");
            int triangles = 0;
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageLifePropDefinition prop in manifest.props)
            {
                if (!Enum.TryParse(prop.kind, out VillageLifePropKind kind) || !kinds.Add(prop.kind))
                    throw new InvalidOperationException("Unknown or duplicate Village Life prop kind.");
                var propBounds = new Bounds();
                bool first = true;
                foreach (VillageLifePropPart part in prop.parts)
                {
                    if (!meshes.TryGetValue(part.mesh, out MeshFilter filter))
                        throw new InvalidOperationException("Missing imported life prop part: " + part.mesh);
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, point);
                        high = Vector3.Max(high, point);
                    }
                    AssertNear(low, part.bounds_min, part.mesh + " minimum");
                    AssertNear(high, part.bounds_max, part.mesh + " maximum");
                    var measured = new Bounds();
                    measured.SetMinMax(low, high);
                    if (first) { propBounds = measured; first = false; }
                    else propBounds.Encapsulate(measured);
                    int count = 0;
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                        count += (int)filter.sharedMesh.GetIndexCount(i) / 3;
                    if (count != part.triangles)
                        throw new InvalidOperationException("Village Life prop triangle count drifted: " + part.mesh);
                    triangles += count;
                }
                AssertNear(propBounds.min, prop.bounds_min, kind + " aggregate minimum");
                AssertNear(propBounds.max, prop.bounds_max, kind + " aggregate maximum");
                foreach (VillageLifePropAnchor anchor in prop.anchors)
                {
                    string name = "ANCHOR_" + prop.kind + "_" + anchor.name;
                    if (!transforms.TryGetValue(name, out Transform transform))
                        throw new InvalidOperationException("Missing authored Village Life anchor: " + name);
                    AssertNear(transform.position, anchor.position, name);
                }
            }
            if (triangles != manifest.triangle_count)
                throw new InvalidOperationException("Village Life total triangle count drifted.");
            Debug.Log($"VILLAGE LIFE PROPS UNITY VALIDATION OK: {kinds.Count} props; metre bounds and anchors match.");
        }

        private static void AssertNear(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, VillageLifePropLibrary.Vector(expected)) > .005f)
                throw new InvalidOperationException("Village Life imported metre " + label + " drifted: " + actual);
        }
    }

    public sealed class VillageLifePropModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != VillageLifePropAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
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
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
