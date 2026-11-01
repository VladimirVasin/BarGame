using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class StairwellLabyrinthModelImporter : AssetPostprocessor
    {
        public const string ModelPath = "Assets/Resources/Stairwell/Labyrinth/Labyrinth3D.fbx";
        private void OnPreprocessModel()
        {
            if(assetPath!=ModelPath)return;
            var importer=(ModelImporter)assetImporter;
            importer.globalScale=1f;importer.useFileScale=true;importer.bakeAxisConversion=true;
            importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
            importer.animationType=ModelImporterAnimationType.None;importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.addCollider=false;importer.preserveHierarchy=true;
            // StairwellLabyrinthWorldBuilder cooks MeshColliders at runtime.
            // Players require the source mesh CPU data; Editor access can mask its absence.
            importer.isReadable=true;
            importer.meshCompression=ModelImporterMeshCompression.Off;importer.importNormals=ModelImporterNormals.Import;
        }
    }

    public static class StairwellLabyrinthAssetSetup
    {
        [Serializable]
        private sealed class Manifest
        {
            public Part[] parts;
        }

        [Serializable]
        private sealed class Part
        {
            public string name, role;
        }

        public static void EnsureAssets()
        {
            string path=StairwellLabyrinthModelImporter.ModelPath;
            if(!File.Exists(path)||!File.Exists(Path.ChangeExtension(path,"json")))
                throw new InvalidOperationException("Missing generated Stairwell labyrinth model/manifest.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        /// <summary>Read-only player contract: editor mesh access must not hide a disabled importer flag.</summary>
        public static void ValidateOrThrow()
        {
            string path = StairwellLabyrinthModelImporter.ModelPath;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.ChangeExtension(path, "json"));
            ValidateModel(AssetDatabase.LoadAssetAtPath<GameObject>(path), manifest != null ? manifest.text : null,
                importer != null && importer.isReadable);
        }

        // Kept separate so negative fixtures never reimport or mutate production assets.
        public static void ValidateModel(GameObject model, string manifestJson, bool importerReadable)
        {
            if (!importerReadable)
                throw new InvalidOperationException("Stairwell labyrinth importer must keep Read/Write enabled for runtime MeshCollider cooking in players.");
            if (model == null || string.IsNullOrWhiteSpace(manifestJson))
                throw new InvalidOperationException("Missing imported Stairwell labyrinth model/manifest.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(manifestJson);
            if (manifest?.parts == null)
                throw new InvalidOperationException("Stairwell labyrinth manifest has no parts.");
            var meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!meshes.TryAdd(filter.name, filter))
                    throw new InvalidOperationException("Stairwell labyrinth has duplicate mesh part: " + filter.name);
            }
            int required = 0;
            foreach (Part part in manifest.parts)
            {
                if (part == null || (part.role != "Collision" && part.role != "ShellCollision" && part.role != "DecorCollision")) continue;
                required++;
                if (string.IsNullOrWhiteSpace(part.name) || !meshes.TryGetValue(part.name, out MeshFilter filter) ||
                    filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh.vertexCount == 0)
                    throw new InvalidOperationException("Stairwell labyrinth runtime collision mesh is missing, unreadable or empty: " + part.name);
            }
            if (required == 0)
                throw new InvalidOperationException("Stairwell labyrinth manifest has no required runtime collision meshes.");
        }
    }
}
