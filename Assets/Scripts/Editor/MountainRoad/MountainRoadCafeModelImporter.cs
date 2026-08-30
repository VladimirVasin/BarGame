using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Fixed passive import contract for the authored cafe.</summary>
    public sealed class MountainRoadCafeModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!MountainRoadCafeAssetSetup.IsModelPath(assetPath) ||
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
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessTexture()
        {
            if (!MountainRoadCafeAssetSetup.IsTexturePath(assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            bool glass = MountainRoadCafeAssetSetup.IsGlassTexturePath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = glass
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = glass;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = MountainRoadCafeAssetSetup.IsClampTexturePath(assetPath)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (MountainRoadCafeAssetSetup.IsBuilding)
            {
                return;
            }

            if (ContainsSource(importedAssets) || ContainsSource(movedAssets))
            {
                MountainRoadCafeAssetSetup.QueueBuildWhenSourcesExist();
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
                if (MountainRoadCafeAssetSetup.IsSourcePath(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
