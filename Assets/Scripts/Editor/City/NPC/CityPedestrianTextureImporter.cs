using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Locks every pedestrian detail atlas to the same import contract the
    /// Hero V2 atlases already obey: Point, Clamp, sRGB, 256 px, no
    /// mipmaps, uncompressed, alpha read as data rather than transparency.
    ///
    /// It exists because an atlas that Unity is left to import with its
    /// defaults arrives bilinear and mipmapped, and a grey seam authored
    /// one pixel wide on a 64 px cell then bleeds into the neighbouring
    /// cell the moment the walker is more than a few metres away. The
    /// pedestrian pipeline validates the manifest's declared filter and
    /// wrap modes against these values, so this importer is what makes
    /// that declaration true rather than aspirational.
    ///
    /// It deliberately configures nothing itself: the whole contract lives
    /// in <see cref="Player3DV2TextureImporter.ConfigureAtlas"/>, and this
    /// class only decides which paths are pedestrian atlases. Two copies of
    /// the same flag list would be two places for them to disagree.
    /// </summary>
    public sealed class CityPedestrianTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer))
            {
                return;
            }

            if (!CityPedestrianAssetSetup.IsDetailAtlasPath(assetPath))
            {
                return;
            }

            Player3DV2TextureImporter.ConfigureAtlas(importer);
        }
    }
}
