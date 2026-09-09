using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageResidentDoorAssetSetup
    {
        public const string ModelPath = "Assets/Resources/VillageLife/VillageResidentDoors3D.fbx";
        public const string ManifestPath = "Assets/Resources/VillageLife/VillageResidentDoors3D.json";

        [MenuItem("Bar Promenade/Village/Import Resident Doorways")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            var manifest = JsonUtility.FromJson<VillageResidentDoorManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != VillageResidentDoorAssets.DesignId ||
                manifest.generator_version != VillageResidentDoorAssets.GeneratorVersion || manifest.scale_mode != "fixed_metres" ||
                manifest.houses == null || manifest.houses.Length != 3 || manifest.door_parts == null ||
                manifest.door_parts.Length != 3 || manifest.mesh_count != 15 || string.IsNullOrEmpty(manifest.build_signature))
                throw new InvalidOperationException("Resident doorway manifest contract drifted.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !Mathf.Approximately(importer.globalScale, 1f) || !importer.bakeAxisConversion ||
                !importer.preserveHierarchy || !importer.isReadable || importer.importAnimation || importer.addCollider ||
                importer.importCameras || importer.importLights || importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Resident doorways must import as passive fixed-metre geometry.");
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null || model.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Resident doorway source unexpectedly owns runtime collision.");
            var meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Missing or duplicate resident doorway mesh.");
            if (meshes.Count != manifest.mesh_count) throw new InvalidOperationException("Resident doorway imported mesh count drifted.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var house in manifest.houses)
            {
                if (!VillageResidentDoorPlan.IsResidentHouse(house.plot_id) || !ids.Add(house.plot_id) || house.parts.Length != 4)
                    throw new InvalidOperationException("Resident doorway house catalogue drifted.");
                int index = int.Parse(house.plot_id.Substring(house.plot_id.Length - 2));
                VillageResidentDoorPlan.TryGetHouseDimensions(index, out Vector2 size, out float height, out float across);
                if (Mathf.Abs(size.x-house.width) > .0001f || Mathf.Abs(size.y-house.depth) > .0001f ||
                    Mathf.Abs(height-house.height) > .0001f || Mathf.Abs(across-house.door_across) > .0001f)
                    throw new InvalidOperationException("Resident house source differs from the fixed plan dimensions.");
                foreach (var part in house.parts) Measure(part);
                if (house.anchors == null || house.anchors.Length != 7)
                    throw new InvalidOperationException("Resident house must have two distinct hidden parking docks.");
            }
            foreach (var part in manifest.door_parts) Measure(part);
            Debug.Log("VILLAGE RESIDENT DOORS UNITY VALIDATION OK: 3 fitted houses, 15 real-metre meshes.");

            void Measure(VillageResidentDoorPart part)
            {
                if (!meshes.TryGetValue(part.mesh, out MeshFilter filter))
                    throw new InvalidOperationException("Missing resident doorway part " + part.mesh);
                Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(vertex);
                    low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
                if (Vector3.Distance(low, VillageLifePropLibrary.Vector(part.bounds_min)) > .005f ||
                    Vector3.Distance(high, VillageLifePropLibrary.Vector(part.bounds_max)) > .005f)
                    throw new InvalidOperationException("Resident doorway imported metres drifted: " + part.mesh);
                int triangles = 0;
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += (int)filter.sharedMesh.GetIndexCount(i) / 3;
                if (triangles != part.triangles) throw new InvalidOperationException("Resident doorway triangle count drifted: " + part.mesh);
            }
        }
    }

    public sealed class VillageResidentDoorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != VillageResidentDoorAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.bakeAxisConversion = true;
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
