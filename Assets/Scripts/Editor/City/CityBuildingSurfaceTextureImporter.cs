using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Import contract for the generated semantic building-surface sheets.
    /// Facade atlases clamp at their authored four-side boundary; metric
    /// materials repeat only when geometry genuinely exceeds their metre span.
    /// </summary>
    public sealed class CityBuildingSurfaceTextureImporter : AssetPostprocessor
    {
        private const string Prefix =
            "Assets/Resources/Textures/CityBuildingSurfaces/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(
                    Prefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is TextureImporter importer))
            {
                return;
            }

            bool sideAtlas =
                assetPath.EndsWith(
                    "/FacadePrimary.png",
                    StringComparison.OrdinalIgnoreCase) ||
                assetPath.EndsWith(
                    "/FacadeSecondary.png",
                    StringComparison.OrdinalIgnoreCase);
            bool plinth = assetPath.EndsWith(
                "/Plinth.png",
                StringComparison.OrdinalIgnoreCase);

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
            importer.wrapMode = sideAtlas || plinth
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = sideAtlas || plinth ? 1024 : 512;
        }
    }
}
