using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Only the village house sheets; shared mountain textures stay untouched.</summary>
    public static class VillageFacadeTextureSetup
    {
        public const string TextureFolder = "Assets/Resources/Village/Textures/";
        public const string ManifestPath = TextureFolder + "VillageFacadeTextures.json";

        [MenuItem("Bar Promenade/Village/Build And Validate Facade Textures")]
        public static void BuildOrThrow()
        {
            LoadManifest();
            for (int index = 0; index < VillageFacadeAppearance.TextureCount; index++)
            {
                AssetDatabase.ImportAsset(TexturePath(index),
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.ImportAsset(ManifestPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            VillageFacadeTextureManifest manifest = LoadManifest();
            for (int index = 0; index < VillageFacadeAppearance.TextureCount; index++)
            {
                string path = TexturePath(index);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null || importer == null ||
                    texture.width != VillageFacadeAppearance.TextureSize ||
                    texture.height != VillageFacadeAppearance.TextureSize ||
                    texture.mipmapCount < 2 || importer.textureType != TextureImporterType.Default ||
                    importer.textureShape != TextureImporterShape.Texture2D ||
                    !importer.sRGBTexture || !importer.mipmapEnabled || importer.isReadable ||
                    importer.streamingMipmaps || importer.alphaSource != TextureImporterAlphaSource.None ||
                    importer.filterMode != FilterMode.Bilinear || importer.anisoLevel != 4 ||
                    importer.wrapMode != TextureWrapMode.Repeat ||
                    importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.maxTextureSize != VillageFacadeAppearance.TextureSize)
                    throw new InvalidOperationException("Village facade import contract drift: " + path);
                using (SHA256 hash = SHA256.Create())
                {
                    string measured = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)))
                        .Replace("-", string.Empty).ToLowerInvariant();
                    if (!string.Equals(measured, manifest.sheets[index].sha256, StringComparison.Ordinal))
                        throw new InvalidOperationException("Village facade PNG differs from measured manifest: " + path);
                }
            }
        }

        public static string TexturePath(int index) =>
            TextureFolder + VillageFacadeAppearance.GetTextureName(index) + ".png";

        private static VillageFacadeTextureManifest LoadManifest()
        {
            if (!File.Exists(ManifestPath))
                throw new InvalidOperationException("Run tools/build-village-facade-textures.py first.");
            return VillageFacadeAppearance.ParseManifestOrThrow(File.ReadAllText(ManifestPath));
        }
    }

    public sealed class VillageFacadeTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer)) return;
            bool owned = false;
            for (int index = 0; index < VillageFacadeAppearance.TextureCount; index++)
                owned |= string.Equals(assetPath, VillageFacadeTextureSetup.TexturePath(index),
                    StringComparison.OrdinalIgnoreCase);
            if (!owned) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = VillageFacadeAppearance.TextureSize;
        }
    }
}
