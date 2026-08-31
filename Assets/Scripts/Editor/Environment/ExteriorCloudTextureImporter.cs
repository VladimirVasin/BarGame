using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the packed RGB channels as linear repeatable data. Compression
    /// is deliberately disabled: block cross-talk changes coverage thresholds
    /// and costs more visually than this one 256 px texture saves in memory.
    /// </summary>
    public sealed class ExteriorCloudTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!string.Equals(
                    assetPath,
                    ExteriorCloudAssetSetup.TexturePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 256;
            standalone.format = TextureImporterFormat.RGBA32;
            standalone.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
