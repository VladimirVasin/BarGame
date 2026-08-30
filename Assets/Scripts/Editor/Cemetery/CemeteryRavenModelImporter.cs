using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    public sealed class CemeteryRavenModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!string.Equals(
                    assetPath,
                    CemeteryRavenAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            // No armature and no clips: the raven is plain meshes and
            // pivot empties, the stairwell cat's contract. The
            // preserveHierarchy flag is what keeps those empties
            // importable for the runtime adopt.
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
            importer.importTangents = ModelImporterTangents.None;
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
            if (CemeteryRavenAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string importedPath = importedAssets[index];
                if (string.Equals(
                        importedPath,
                        CemeteryRavenAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CemeteryRavenAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CemeteryRavenAssetSetup.SharedMaterialPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedPath,
                        CemeteryRavenAssetSetup.AtlasPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CemeteryRavenAssetSetup
                        .QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
