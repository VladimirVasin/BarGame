using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Shared Blender import convention for the bar. The isolated native GI
    /// pilot additionally needs CPU buffers on its two contributor models.
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
#if SURFACE_CACHE
            // The pinned native GI provider creates Raw buffers at runtime and
            // requires the CPU source data to do so. Bound that copy to the two
            // interior contributor assets; the City facade keeps GPU-only data.
            importer.isReadable = string.Equals(assetPath, BarAssetSetup.InteriorModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, BarAssetSetup.ServicePropsModelPath, StringComparison.OrdinalIgnoreCase);
#else
            importer.isReadable = false;
#endif
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
