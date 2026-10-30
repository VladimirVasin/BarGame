using System;
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
            importer.addCollider=false;importer.preserveHierarchy=true;importer.isReadable=false;
            importer.meshCompression=ModelImporterMeshCompression.Off;importer.importNormals=ModelImporterNormals.Import;
        }
    }

    public static class StairwellLabyrinthAssetSetup
    {
        public static void EnsureAssets()
        {
            string path=StairwellLabyrinthModelImporter.ModelPath;
            if(!File.Exists(path)||!File.Exists(Path.ChangeExtension(path,"json")))
                throw new InvalidOperationException("Missing generated Stairwell labyrinth model/manifest.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)
                throw new InvalidOperationException("Stairwell labyrinth FBX failed to import.");
        }
    }
}
