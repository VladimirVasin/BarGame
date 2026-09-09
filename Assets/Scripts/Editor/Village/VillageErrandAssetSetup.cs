using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageErrandAssetSetup
    {
        public const string ModelPath = "Assets/Resources/VillageLife/VillageErrandProps.fbx";
        public const string ManifestPath = "Assets/Resources/VillageLife/VillageErrandProps.json";
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null || !importer.useFileScale || !importer.bakeAxisConversion || !importer.isReadable || importer.importAnimation)
                throw new InvalidOperationException("The passive village pail must import in real metres.");
            var manifest = VillageErrandPropLibrary.ReadManifest();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var filters = model.GetComponentsInChildren<MeshFilter>();
            if (filters.Length != 3 || model.GetComponentsInChildren<Collider>().Length != 0)
                throw new InvalidOperationException("The pail is three passive authored meshes.");
            foreach (var row in manifest.parts)
            {
                var mesh = filters.Single(f => f.sharedMesh.name == row.mesh);
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var vertex in mesh.sharedMesh.vertices)
                { Vector3 p = mesh.transform.TransformPoint(vertex); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                if (Vector3.Distance(min, VillageLifePropLibrary.Vector(row.bounds_min)) > .005f ||
                    Vector3.Distance(max, VillageLifePropLibrary.Vector(row.bounds_max)) > .005f)
                    throw new InvalidOperationException("Imported pail metre bounds differ: " + row.mesh);
            }
            foreach (var row in manifest.anchors)
            {
                Transform anchor = model.GetComponentsInChildren<Transform>().Single(t => t.name == "ANCHOR_" + row.name);
                if (Vector3.Distance(anchor.position, VillageLifePropLibrary.Vector(row.position)) > .003f)
                    throw new InvalidOperationException("Imported pail anchor differs: " + row.name);
            }
            // This is also the existing, authoritative NPC binding importer;
            // it adds the one new bank without touching any source body FBX.
            VillageResidentAssetSetup.BuildOrThrow();
            var clip = AssetDatabase.LoadAllAssetsAtPath(VillageResidentAssetSetup.ErrandAnimationPath)
                .OfType<AnimationClip>().Single(c => c.name == "BucketFill");
            if (clip.humanMotion || clip.isLooping || Mathf.Abs(clip.length - 8f) > .02f || AnimationUtility.GetAnimationEvents(clip).Length != 0)
                throw new InvalidOperationException("BucketFill must be an eight-second Generic bone-only action.");
            foreach (var curve in AnimationUtility.GetCurveBindings(clip))
                if (string.IsNullOrEmpty(curve.path) || curve.type != typeof(Transform))
                    throw new InvalidOperationException("BucketFill cannot move an actor root or non-bone property.");
            var station = AssetDatabase.LoadAllAssetsAtPath(VillageResidentAssetSetup.ErrandAnimationPath)
                .OfType<AnimationClip>().Single(c => c.name == "StationStrap");
            if (station.humanMotion || station.isLooping || Mathf.Abs(station.length - 6f) > .02f || AnimationUtility.GetAnimationEvents(station).Length != 0)
                throw new InvalidOperationException("StationStrap must be a six-second Generic bone-only action.");
            foreach (var curve in AnimationUtility.GetCurveBindings(station))
                if (string.IsNullOrEmpty(curve.path) || curve.type != typeof(Transform))
                    throw new InvalidOperationException("StationStrap cannot move an actor root or non-bone property.");
        }
    }
    public sealed class VillageErrandPropImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != VillageErrandAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off; importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
