using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public sealed class SurfaceDamageTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool micro = assetPath.StartsWith("Assets/Resources/SurfaceDamage/Micro/", StringComparison.Ordinal);
            if ((!micro && !assetPath.StartsWith("Assets/Resources/SurfaceDamage/SurfaceDamageAtlas", StringComparison.Ordinal)) ||
                !assetPath.EndsWith(".png", StringComparison.Ordinal)) return;
            var texture = (TextureImporter)assetImporter;
            texture.textureType = TextureImporterType.Default;
            texture.sRGBTexture = micro;
            texture.alphaSource = micro ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            texture.alphaIsTransparency = false;
            texture.mipmapEnabled = true;
            texture.isReadable = false;
            texture.wrapMode = micro ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.maxTextureSize = micro ? 512 : 2048;
            texture.textureCompression = TextureImporterCompression.CompressedHQ;
            texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            { name = "Standalone", overridden = true, maxTextureSize = texture.maxTextureSize, format = TextureImporterFormat.BC7, compressionQuality = 100 });
        }
    }
}
