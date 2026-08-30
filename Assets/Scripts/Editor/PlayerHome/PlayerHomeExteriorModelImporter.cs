using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Fixed import contract for the deterministic player-home exterior.
    /// The FBX remains passive; collision, lights and interaction stay owned
    /// by the City and Home plans.
    /// </summary>
    public sealed class PlayerHomeExteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!PlayerHomeExteriorAssetSetup.IsModelPath(assetPath) ||
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
            if (!PlayerHomeExteriorAssetSetup.IsTexturePath(assetPath) ||
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
            importer.wrapMode = TextureWrapMode.Repeat;
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
            if (PlayerHomeExteriorAssetSetup.IsBuilding)
            {
                return;
            }

            if (ContainsSource(importedAssets) || ContainsSource(movedAssets))
            {
                PlayerHomeExteriorAssetSetup.QueueBuildWhenSourcesExist();
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
                if (PlayerHomeExteriorAssetSetup.IsSourcePath(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
