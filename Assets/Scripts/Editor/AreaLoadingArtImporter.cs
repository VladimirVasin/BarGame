using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Import and read-only validation for the four loading pictures only.</summary>
    public sealed class AreaLoadingArtImporter : AssetPostprocessor
    {
        private const int MaximumSize = 2048;

        public static string GetAssetPath(string resourcePath)
        {
            return $"Assets/Resources/{resourcePath}.png";
        }

        public static bool IsArtworkPath(string path)
        {
            foreach (string resource in AreaLoadingArtCatalog.ResourcePaths)
            {
                if (string.Equals(path, GetAssetPath(resource),
                    StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private void OnPreprocessTexture()
        {
            if (!IsArtworkPath(assetPath) ||
                !(assetImporter is TextureImporter importer)) return;

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaximumSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.crunchedCompression = false;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = MaximumSize,
                format = TextureImporterFormat.BC7,
                textureCompression = TextureImporterCompression.CompressedHQ,
                compressionQuality = 100,
                crunchedCompression = false
            });
        }

        public static void ValidateOrThrow()
        {
            foreach (string resource in AreaLoadingArtCatalog.ResourcePaths)
            {
                string path = GetAssetPath(resource);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                    throw new InvalidOperationException($"Required loading illustration is missing: '{path}'.");

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null ||
                    importer.textureType != TextureImporterType.Default ||
                    importer.textureShape != TextureImporterShape.Texture2D ||
                    !importer.sRGBTexture ||
                    importer.alphaSource != TextureImporterAlphaSource.None ||
                    importer.alphaIsTransparency || importer.mipmapEnabled ||
                    importer.isReadable || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.filterMode != FilterMode.Bilinear ||
                    importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.maxTextureSize != MaximumSize ||
                    importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                    importer.compressionQuality != 100 || importer.crunchedCompression)
                    throw new InvalidOperationException($"Loading illustration import settings are stale: '{path}'.");

                TextureImporterPlatformSettings standalone =
                    importer.GetPlatformTextureSettings("Standalone");
                if (!standalone.overridden || standalone.maxTextureSize != MaximumSize ||
                    standalone.format != TextureImporterFormat.BC7 ||
                    standalone.textureCompression != TextureImporterCompression.CompressedHQ ||
                    standalone.compressionQuality != 100 || standalone.crunchedCompression)
                    throw new InvalidOperationException($"Loading illustration needs the Windows BC7 profile: '{path}'.");
            }
        }

        [MenuItem("Bar Promenade/Presentation/Reimport Loading Illustrations")]
        public static void ReimportAll()
        {
            foreach (string resource in AreaLoadingArtCatalog.ResourcePaths)
            {
                AssetDatabase.ImportAsset(GetAssetPath(resource),
                    ImportAssetOptions.ForceUpdate);
            }

            ValidateOrThrow();
        }
    }
}
