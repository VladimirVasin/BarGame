using UnityEditor;
using UnityEngine;

namespace BarPromenade.EditorTools
{
    /// <summary>
    /// Forces the Last Route car's import contract and rebuilds its prefab
    /// whenever the generated sources land. Identical in shape to the bus
    /// importer: the model carries no rig, no animation and no imported
    /// materials, and its hierarchy is preserved because every anchor the
    /// runtime binds is an empty in that hierarchy.
    /// </summary>
    public sealed class LastRouteCarModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != LastRouteCarAssetSetup.ModelPath)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importConstraints = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.weldVertices = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (LastRouteCarAssetSetup.IsBuilding)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (path == LastRouteCarAssetSetup.ModelPath ||
                    path == LastRouteCarAssetSetup.ManifestPath)
                {
                    LastRouteCarAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
