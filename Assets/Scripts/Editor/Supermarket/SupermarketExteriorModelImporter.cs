using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Fixed import contract for the deterministic supermarket exterior.
    /// The FBX remains passive; collision, lights and interaction continue to
    /// belong to the city plans. Texture addressing follows the authored UV
    /// contract: atlases clamp while metre-aware brick and metal sheets repeat.
    /// </summary>
    public sealed class SupermarketExteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!SupermarketExteriorAssetSetup.IsModelPath(assetPath) ||
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

        private void OnPreprocessTexture()
        {
            if (!SupermarketExteriorAssetSetup.IsTexturePath(assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode =
                SupermarketExteriorAssetSetup.IsAtlasTexturePath(assetPath)
                    ? TextureWrapMode.Clamp
                    : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (SupermarketExteriorAssetSetup.IsBuilding)
            {
                return;
            }

            if (ContainsSource(importedAssets) || ContainsSource(movedAssets))
            {
                SupermarketExteriorAssetSetup.QueueBuildWhenSourcesExist();
            }
        }

        private static bool ContainsSource(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int index = 0; index < paths.Length; index++)
            {
                if (SupermarketExteriorAssetSetup.IsSourcePath(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
