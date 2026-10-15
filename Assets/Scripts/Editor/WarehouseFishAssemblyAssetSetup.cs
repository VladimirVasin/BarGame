using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>The single animated fish has a separate import recipe from passive warehouse modules.</summary>
    public sealed class WarehouseFishAssemblyModelImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/CombatWarehouse/FishAssembly/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer)) return;
            bool animated = assetPath == Folder + "AssembledFish.fbx";
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;
            importer.animationType = animated ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.avatarSetup = animated ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = animated;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.generateSecondaryUV = false;
            importer.weldVertices = false;
            importer.keepQuads = false;
        }
    }
}
