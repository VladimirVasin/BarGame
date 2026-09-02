using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks the shared cashier detail atlas to the same import
    /// contract every character atlas already obeys: Point, Clamp, sRGB,
    /// 256 px, no mipmaps, uncompressed. It configures nothing itself —
    /// the whole contract lives in
    /// <see cref="Player3DV2TextureImporter.ConfigureAtlas"/>, and this
    /// class only decides that the cashier's one path is an atlas. The
    /// cemetery raven's importer is the model for this; the pedestrian
    /// importer's own whitelist is a foreign domain and stays untouched.
    ///
    /// Point and Clamp are not taste. The sheet is a 4x4 grid of 64 px
    /// cells with a one-pixel UV inset, so bilinear filtering would drag
    /// a neighbouring cell's ink across a seam, and repeat wrapping would
    /// do it at the sheet's edge.
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
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Player3DV2TextureImporter.ConfigureAtlas(importer);
        }
    }
}
