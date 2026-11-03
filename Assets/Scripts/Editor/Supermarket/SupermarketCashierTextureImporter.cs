using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// The retained Watcher keeps its original 256 px point-filtered sheet.
    /// The ordinary clerk uses 512 px surface islands, including a 256 px
    /// cylindrical head, with a three-pixel inset and bilinear filtering.
    /// Both reuse the shared sRGB/Clamp/uncompressed/no-mipmap import policy.
    /// </summary>
    public sealed class SupermarketCashierTextureImporter :
        AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer))
            {
                return;
            }

            if (!string.Equals(
                    assetPath,
                    SupermarketCashierAssetSetup.DetailAtlasPath,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(assetPath, SupermarketCashierAssetSetup.NormalDetailAtlasPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool normal = string.Equals(assetPath,
                SupermarketCashierAssetSetup.NormalDetailAtlasPath,
                StringComparison.OrdinalIgnoreCase);
            Player3DV2TextureImporter.ConfigureAtlas(importer, normal ? 512 : 256);
            // The normal clerk has padded high-density surface islands. Linear
            // filtering resolves fabric threads without hard pixel noise.
            if (normal) importer.filterMode = FilterMode.Bilinear;
        }
    }
}
