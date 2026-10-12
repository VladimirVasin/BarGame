using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPortSurfaceImportTests
    {
        [TestCase("PortConcreteAlbedo.png", true)]
        [TestCase("PortTimberAlbedo.png", true)]
        [TestCase("PortWarehousePlasterAlbedo.png", true)]
        [TestCase("PortPaintedSteelAlbedo.png", false)]
        public void Surface_UsesSelectedWindowsFormatWithoutChangingSampling(
            string filename, bool compressed)
        {
            string path = "Assets/Resources/City/Port/Textures/" + filename;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(texture, Is.Not.Null, path);
            Assert.That(texture.width, Is.EqualTo(512), path);
            Assert.That(texture.height, Is.EqualTo(512), path);
            Assert.That(texture.mipmapCount, Is.EqualTo(10), path);
            Assert.That(importer.sRGBTexture, Is.True, path);
            Assert.That(importer.mipmapEnabled, Is.True, path);
            Assert.That(importer.isReadable, Is.False, path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), path);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), path);
            Assert.That(importer.anisoLevel, Is.EqualTo(4), path);

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            if (compressed)
            {
                Assert.That(standalone.overridden, Is.True, path);
                Assert.That(standalone.maxTextureSize, Is.EqualTo(512), path);
                Assert.That(standalone.format, Is.EqualTo(TextureImporterFormat.BC7), path);
                Assert.That(standalone.textureCompression,
                    Is.EqualTo(TextureImporterCompression.CompressedHQ), path);
                Assert.That(standalone.compressionQuality, Is.EqualTo(100), path);
                Assert.That(standalone.crunchedCompression, Is.False, path);
                // Check Unity's imported resource, not only its requested policy.
                Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7), path);
            }
            else
            {
                Assert.That(standalone.overridden, Is.False, path);
                Assert.That(importer.textureCompression,
                    Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                Assert.That(texture.format, Is.Not.EqualTo(TextureFormat.BC7), path);
            }
        }
    }
}
