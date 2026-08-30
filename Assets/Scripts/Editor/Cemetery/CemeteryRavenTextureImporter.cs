using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks the cemetery raven's detail atlas to the same import
    /// contract every character atlas already obeys: Point, Clamp,
    /// sRGB, 256 px, no mipmaps, uncompressed. It deliberately
    /// configures nothing itself — the whole contract lives in
    /// <see cref="Player3DV2TextureImporter.ConfigureAtlas"/>, and
    /// this class only decides that the raven's one path is an atlas.
    /// The pedestrian importer's own whitelist is a foreign domain
    /// and stays untouched.
    /// </summary>
    public sealed class CemeteryRavenTextureImporter :
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
                    CemeteryRavenAssetSetup.AtlasPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Player3DV2TextureImporter.ConfigureAtlas(importer);
        }
    }
}
