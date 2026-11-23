using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Living-room sheets and the wall painting retain independent import settings.</summary>
    public sealed class HomeRoomSurfaceTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool painting = assetPath == "Assets/Resources/Home/Textures/HomeWeaverPaintingAlbedo.png";
            bool household = assetPath == "Assets/Resources/Home/Textures/HomeHouseholdFinishes.png";
            bool furniture = household || assetPath == "Assets/Resources/Home/Textures/HomeFurnitureFinishes.png";
            if (!painting && !furniture && (!assetPath.StartsWith("Assets/Resources/Home/Textures/HomeRoom",
                    System.StringComparison.Ordinal) || !assetPath.EndsWith("Albedo.png",
                    System.StringComparison.Ordinal))) return;
            var texture = (TextureImporter)assetImporter;
            texture.textureType = TextureImporterType.Default;
            texture.textureShape = TextureImporterShape.Texture2D;
            texture.sRGBTexture = true;
            texture.alphaSource = furniture ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            texture.alphaIsTransparency = false;
            texture.mipmapEnabled = true;
            texture.isReadable = false;
            texture.wrapMode = (painting || furniture) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.maxTextureSize = (painting || (furniture && !household)) ? 2048 : 1024;
            texture.textureCompression = TextureImporterCompression.CompressedHQ;
            texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, maxTextureSize = texture.maxTextureSize,
                format = TextureImporterFormat.BC7, compressionQuality = 100
            });
        }
    }
}
