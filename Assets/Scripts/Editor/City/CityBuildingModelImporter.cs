using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Passive import contract for the fixed-metre building prototype kit.
    /// Read/write stays disabled because wrappers instantiate whole meshes;
    /// no runtime mesh combining or deformation is part of this slice.
    /// </summary>
    public sealed class CityBuildingModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!CityBuildingAssetSetup.IsModelPath(assetPath) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
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
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (CityBuildingAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (CityBuildingAssetSetup.IsOwnedSourcePath(
                        importedAssets[index]))
                {
                    CityBuildingAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
