using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    public sealed class CityBusModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!string.Equals(
                    assetPath,
                    CityBusAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

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
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (CityBusAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        CityBusAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CityBusAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CityBusAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
