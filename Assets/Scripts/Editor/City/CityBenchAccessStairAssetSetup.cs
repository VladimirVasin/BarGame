using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Fixed imported metres; runtime supplies shared City surfaces and physical contacts.</summary>
    [InitializeOnLoad]
    public static class CityBenchAccessStairAssetSetup
    {
        public const string ModelPath = "Assets/Resources/City/BenchAccess/BenchAccessStair.fbx";
        public const string ManifestPath = "Assets/Resources/City/BenchAccess/BenchAccessStair.json";
        public const string ResourcePath = "City/BenchAccess/BenchAccessStair";

        static CityBenchAccessStairAssetSetup() { EditorApplication.delayCall += ValidateWhenReady; }

        private static void ValidateWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(ManifestPath) || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
            ValidateOrThrow();
        }

        public static void EnsureAssetsReady()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Missing imported broad bench stair.");
            var measured = UnityEngine.Object.Instantiate(model);
            measured.hideFlags = HideFlags.HideAndDontSave;
            try { ValidatePlacedModel(measured); }
            finally { UnityEngine.Object.DestroyImmediate(measured); }
        }

        private static void ValidatePlacedModel(GameObject measured)
        {
            var parts = measured.GetComponentsInChildren<MeshRenderer>(true);
            if (parts.Length != 2 || parts.Any(part => !part.name.EndsWith("__Concrete", StringComparison.Ordinal) &&
                                                     !part.name.EndsWith("__SteelDark", StringComparison.Ordinal)))
                throw new InvalidOperationException("Bench stair lost its two shared surface roles.");
            Bounds bounds = parts[0].bounds;
            foreach (var part in parts) bounds.Encapsulate(part.bounds);
            if (Mathf.Abs(bounds.min.x + 5.2f) > .025f || Mathf.Abs(bounds.max.x - 5.2f) > .025f ||
                Mathf.Abs(bounds.min.y + .25f) > .025f || Mathf.Abs(bounds.min.z) > .025f ||
                Mathf.Abs(bounds.max.z - 2.16f) > .025f)
                throw new InvalidOperationException("Bench stair FBX axis/unit conversion changed: " + bounds);
            Anchor("ANCHOR_Lower", Vector3.zero);
            Anchor("ANCHOR_Upper", new Vector3(0f, .96f, 2.16f));
            if (measured.GetComponentsInChildren<Collider>(true).Length > 0)
                throw new InvalidOperationException("Passive stair source must not duplicate runtime contacts.");

            void Anchor(string name, Vector3 expected)
            {
                Transform point = measured.GetComponentsInChildren<Transform>(true).Single(value => value.name == name);
                if (Vector3.Distance(point.position, expected) > .015f)
                    throw new InvalidOperationException("Bench stair anchor/unit mismatch: " + name);
            }
        }
    }

    public sealed class CityBenchAccessStairModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != CityBenchAccessStairAssetSetup.ModelPath || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
