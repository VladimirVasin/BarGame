using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Preserve the authored 64-pixel facial drawings through import.</summary>
    public sealed class DialogueFaceTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Dialogue/Faces/", StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false; importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
        }

        public static void ValidateOrThrow()
        {
            foreach (string resource in new[] { SpeechFaceAtlasResources.HeroPath, SpeechFaceAtlasResources.ForemanPath })
            {
                string path = "Assets/Resources/" + resource + ".png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                int height = resource == SpeechFaceAtlasResources.HeroPath ? 512 : 256;
                if (texture == null || texture.width != 512 || texture.height != height || importer == null ||
                    texture.filterMode != FilterMode.Point || importer.mipmapEnabled ||
                    importer.textureCompression != TextureImporterCompression.Uncompressed)
                    throw new InvalidOperationException("Dialogue face import contract failed: " + path);
            }
        }
    }
}
