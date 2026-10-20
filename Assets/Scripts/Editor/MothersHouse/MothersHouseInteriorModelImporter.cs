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
            if (!MothersHouseInteriorAssetSetup.IsTexturePath(
                    assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            bool response = MothersHouseInteriorAssetSetup.IsSurfaceResponsePath(assetPath);
            bool auxiliary = MothersHouseInteriorAssetSetup.IsPositiveAtlasPath(assetPath);
            int size = auxiliary ? 1024 : response ? 2048 : 4096;
            importer.sRGBTexture = !response;
            importer.alphaSource = response ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = auxiliary ? FilterMode.Bilinear : FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = size;
            importer.isReadable = false;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, maxTextureSize = size,
                format = TextureImporterFormat.BC7, textureCompression = TextureImporterCompression.CompressedHQ,
                compressionQuality = 100, crunchedCompression = false
            });
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
                    MothersHouseInteriorAssetSetup.IsTexturePath(path))
                {
                    MothersHouseInteriorAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }

            for (int index = 0; index < movedAssets.Length; index++)
            {
                if (MothersHouseInteriorAssetSetup.IsTexturePath(
                        movedAssets[index]))
                {
                    MothersHouseInteriorAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
