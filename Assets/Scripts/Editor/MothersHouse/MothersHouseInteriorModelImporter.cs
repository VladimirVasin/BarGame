using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Fixed-metre import contract for the deterministic mother's-house FBX.
    /// Materials, collision, camera, lights and animation stay runtime-owned.
    /// </summary>
    public sealed class MothersHouseInteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!MothersHouseInteriorAssetSetup.IsModelPath(assetPath) ||
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
            // The setup serializes exact per-renderer UV bounds and validates
            // them again after prefab reload, so this small room mesh keeps
            // CPU-readable UV0 as part of its authoring contract.
            importer.isReadable = true;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessTexture()
        {
            if (!MothersHouseInteriorAssetSetup.IsPositiveAtlasPath(
                    assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.isReadable = false;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (MothersHouseInteriorAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string path = importedAssets[index];
                if (MothersHouseInteriorAssetSetup.IsModelPath(path) ||
                    MothersHouseInteriorAssetSetup.IsManifestPath(path) ||
                    MothersHouseInteriorAssetSetup.IsPositiveAtlasPath(path))
                {
                    MothersHouseInteriorAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }

            for (int index = 0; index < movedAssets.Length; index++)
            {
                if (MothersHouseInteriorAssetSetup.IsPositiveAtlasPath(
                        movedAssets[index]))
                {
                    MothersHouseInteriorAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
