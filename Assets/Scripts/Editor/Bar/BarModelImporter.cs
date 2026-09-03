using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Import settings for the Blender bar model. These are a contract,
    /// not a preference, and they are copied verbatim from
    /// `ChurchModelImporter`; two Blender assets in one project that
    /// import differently are two conventions.
    ///
    /// The two that everything else rests on:
    /// `materialImportMode = None`, so district tints stay the runtime's
    /// business rather than something baked into the FBX, and
    /// `addCollider = false`, so collision keeps coming from the layout
    /// plan's footprints.
    /// </summary>
    public sealed class BarModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!BarAssetSetup.IsModelPath(assetPath) ||
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
            if (!BarAssetSetup.IsTexturePath(assetPath) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            // The FBX carries metre-scaled UVs. Match the house-surface
            // contract so detailed grain survives while large faces repeat
            // at the generator's measured material pitch.
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (BarAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                string path = importedAssets[index];
                if (BarAssetSetup.IsModelPath(path) ||
                    BarAssetSetup.IsManifestPath(path))
                {
                    BarAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
