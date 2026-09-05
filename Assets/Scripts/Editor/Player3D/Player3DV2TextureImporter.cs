using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks the production face/clothing atlases and inventory portrait to
    /// deterministic import contracts.
    /// </summary>
    public sealed class Player3DV2TextureImporter : AssetPostprocessor
    {
        /// <summary>
        /// Every atlas in the game is 256 px square except the hero's face
        /// atlas, which grew to 8x4 cells (512x256) when each face got a
        /// soiled twin; a 256 cap would have halved it back down silently.
        /// </summary>
        private const int DefaultMaxTextureSize = 256;
        internal const int FaceAtlasMaxTextureSize = 512;

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
                ConfigureAtlas(importer, FaceAtlasMaxTextureSize);
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
            ConfigureAtlas(importer, DefaultMaxTextureSize);
        }

        internal static void ConfigureAtlas(
            TextureImporter importer,
            int maxTextureSize)
        {
            ConfigureCommon(importer, maxTextureSize);
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

        internal static void ConfigureCommon(
            TextureImporter importer,
            int maxTextureSize = DefaultMaxTextureSize)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 1;
            importer.maxTextureSize = maxTextureSize;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.compressionQuality = 100;

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = maxTextureSize;
            standalone.format = TextureImporterFormat.Automatic;
            standalone.textureCompression =
                TextureImporterCompression.Uncompressed;
            standalone.compressionQuality = 100;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
