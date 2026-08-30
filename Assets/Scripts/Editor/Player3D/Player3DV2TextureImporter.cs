using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks the V2 face/clothing atlases and portrait to deterministic import
    /// contracts. The V2 portrait is the live production inventory portrait;
    /// the original V1 portrait remains packaged beside it as a fallback.
    /// </summary>
    public sealed class Player3DV2TextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer))
            {
                return;
            }

            if (string.Equals(
                    assetPath,
                    Player3DV2AssetSetup.AtlasPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureAtlas(importer);
            }
            else if (string.Equals(
                         assetPath,
                         Player3DV2AssetSetup.ClothingAtlasPath,
                         StringComparison.OrdinalIgnoreCase))
            {
                ConfigureAtlas(importer);
            }
            else if (string.Equals(
                         assetPath,
                         Player3DV2AssetSetup.PortraitPath,
                         StringComparison.OrdinalIgnoreCase))
            {
                ConfigurePortrait(importer);
            }
        }

        /// <summary>
        /// The flat pixel-art atlas contract, shared with the pedestrian
        /// detail atlas importer so a second texture family cannot drift
        /// from the first by a single flag.
        /// </summary>
        internal static void ConfigureAtlas(TextureImporter importer)
        {
            ConfigureCommon(importer);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
        }

        private static void ConfigurePortrait(TextureImporter importer)
        {
            ConfigureCommon(importer);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
        }

        internal static void ConfigureCommon(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.isReadable = false;
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
