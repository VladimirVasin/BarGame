using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageWorkroomAssetSetup
    {
        public const string ModelPath = "Assets/Resources/VillageLife/VillageWorkroom3D.fbx";
        public const string ManifestPath = "Assets/Resources/VillageLife/VillageWorkroom3D.json";
        [MenuItem("Bar Promenade/Village/Import Workroom 08")]
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }
        public static void ValidateOrThrow()
        {
            var data = JsonUtility.FromJson<VillageWorkroomManifest>(File.ReadAllText(ManifestPath));
            if (data == null || data.design_id != VillageWorkroomAssets.DesignId || data.generator_version != VillageWorkroomAssets.GeneratorVersion ||
                data.scale_mode != "fixed_metres" || data.house_id != VillageWorkroomPlan.HouseId ||
                data.parts == null || data.parts.Length != data.mesh_count || string.IsNullOrEmpty(data.build_signature))
                throw new InvalidOperationException("Workroom manifest contract drifted.");
            if (Vector3.Distance(V(data.room_min), VillageWorkroomPlan.LocalRoomBounds.min) > .0001f ||
                Vector3.Distance(V(data.room_max), VillageWorkroomPlan.LocalRoomBounds.max) > .0001f)
                throw new InvalidOperationException("Workroom dimensions differ from the pure plan.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !Mathf.Approximately(importer.globalScale, 1f) || !importer.useFileScale || !importer.bakeAxisConversion ||
                !importer.preserveHierarchy || !importer.isReadable || importer.importAnimation || importer.addCollider ||
                importer.importCameras || importer.importLights || importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Workroom must import as passive metre geometry.");
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null || model.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Workroom source must contain no gameplay collision.");
            var meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate workroom source mesh.");
            if (meshes.Count != data.mesh_count) throw new InvalidOperationException("Imported workroom mesh count drifted.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            int triangles = 0;
            foreach (VillageWorkroomPart part in data.parts)
            {
                if (!names.Add(part.name) || !meshes.TryGetValue(part.mesh, out MeshFilter mesh) ||
                    (!string.IsNullOrEmpty(part.parent) && !names.Contains(part.parent)))
                    throw new InvalidOperationException("Workroom part hierarchy is incomplete: " + part.name);
                Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 point = mesh.transform.TransformPoint(vertex); low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
                if (Vector3.Distance(low, V(part.bounds_min)) > .005f || Vector3.Distance(high, V(part.bounds_max)) > .005f)
                    throw new InvalidOperationException("Imported workroom metres drifted: " + part.name);
                int count = 0;
                for (int i = 0; i < mesh.sharedMesh.subMeshCount; i++) count += (int)mesh.sharedMesh.GetIndexCount(i) / 3;
                if (count != part.triangles) throw new InvalidOperationException("Workroom triangulation drifted: " + part.name);
                triangles += count;
            }
            if (triangles != data.triangle_count) throw new InvalidOperationException("Workroom triangle total drifted.");
            foreach (string key in new[] { "Hammer", "Mitten", "Cloth", "ClothFlap", "Box", "BoxLid", "RepairRail", "Bench", "ChairFrame" })
                if (!names.Contains(key)) throw new InvalidOperationException("Missing finite workroom object " + key);
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageLifePropAnchor anchor in data.anchors)
                if (!anchors.Add(anchor.name) || !VillageWorkroomPlan.LocalAnchors.TryGetValue(anchor.name, out Vector3 planned) ||
                    Vector3.Distance(planned, V(anchor.position)) > .0001f)
                    throw new InvalidOperationException("Workroom anchor differs from plan: " + anchor.name);
            if (anchors.Count != VillageWorkroomPlan.LocalAnchors.Count) throw new InvalidOperationException("Missing workroom plan anchors.");
            Debug.Log("VILLAGE WORKROOM UNITY VALIDATION OK: metre room, two real windows, finite objects and hidden night routes.");
        }
        private static Vector3 V(float[] v) => VillageLifePropLibrary.Vector(v);
    }
    public sealed class VillageWorkroomModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != VillageWorkroomAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true; importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false; importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
            importer.importBlendShapes = false; importer.addCollider = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true; importer.keepQuads = false; importer.generateSecondaryUV = false;
            importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
