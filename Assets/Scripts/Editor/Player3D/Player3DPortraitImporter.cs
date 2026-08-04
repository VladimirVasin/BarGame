using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks the generated inventory portrait to its crisp transparent UI
    /// contract without coupling it to the model/prefab build pipeline.
    /// </summary>
    public sealed class Player3DPortraitImporter : AssetPostprocessor
    {
        public const string AssetPath =
            "Assets/Resources/Player/Player3DPortrait.png";

        private void OnPreprocessTexture()
        {
            if (!string.Equals(
                    assetPath,
                    AssetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Configure((TextureImporter)assetImporter);
        }

        private static void Configure(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 256;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.compressionQuality = 100;

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 256;
            standalone.format = TextureImporterFormat.Automatic;
            standalone.textureCompression =
                TextureImporterCompression.Uncompressed;
            standalone.compressionQuality = 100;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
