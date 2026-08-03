using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Keeps the runtime-sliced fall atlases crisp and free of generated
    /// mipmaps or platform compression.
    /// </summary>
    public sealed class PlayerFallAtlasImporter : AssetPostprocessor
    {
        public const string AssetFolder =
            "Assets/Resources/Player/Falls/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(
                    AssetFolder,
                    System.StringComparison.Ordinal))
            {
                return;
            }

            ConfigureImporter((TextureImporter)assetImporter);
        }

        public static void ConfigureAll()
        {
            string[] textureGuids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { AssetFolder.TrimEnd('/') });
            for (int index = 0;
                 index < textureGuids.Length;
                 index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(
                    textureGuids[index]);
                if (AssetImporter.GetAtPath(path) is not
                    TextureImporter importer)
                {
                    continue;
                }

                ConfigureImporter(importer);
                importer.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Configured {textureGuids.Length} player fall atlases.");
        }

        private static void ConfigureImporter(
            TextureImporter importer)
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
            importer.maxTextureSize = 2048;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.compressionQuality = 100;

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = 2048;
            standalone.format = TextureImporterFormat.Automatic;
            standalone.textureCompression =
                TextureImporterCompression.Uncompressed;
            standalone.compressionQuality = 100;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
