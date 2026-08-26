using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    public sealed class ChurchModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!ChurchAssetSetup.IsModelPath(assetPath) ||
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
            if (!ChurchAssetSetup.IsTexturePath(assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            bool isAtlas = ChurchAssetSetup.IsAtlasTexturePath(assetPath);
            importer.mipmapEnabled = !isAtlas;
            importer.wrapMode = isAtlas
                ? UnityEngine.TextureWrapMode.Clamp
                : UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ChurchAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string path = importedAssets[index];
                if (ChurchAssetSetup.IsModelPath(path) ||
                    ChurchAssetSetup.IsTexturePath(path) ||
                    string.Equals(
                        path,
                        ChurchAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ChurchAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
