using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatPresentationAssetTests
    {
        private const int AtlasWidth = 512;
        private const int AtlasHeight = 256;
        private const int CellSize = 64;
        private const int Columns = 8;
        private const int Rows = 4;

        [Test]
        public void Atlas_IsLoadableWithPixelArtImportContract()
        {
            Texture2D atlas = Resources.Load<Texture2D>(
                StairwellCatSpriteLibrary.DefaultResourcePath);

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(AtlasWidth));
            Assert.That(atlas.height, Is.EqualTo(AtlasHeight));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));
            Assert.That(atlas.isReadable, Is.False);

            string assetPath = AssetDatabase.GetAssetPath(atlas);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;

            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(
                importer.npotScale,
                Is.EqualTo(TextureImporterNPOTScale.None));

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            Assert.That(standalone.overridden, Is.True);
            Assert.That(
                standalone.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(standalone.crunchedCompression, Is.False);
        }

        [Test]
        public void Atlas_AllIdleAndGroomingCellsAreVisible()
        {
            Texture2D imported = Resources.Load<Texture2D>(
                StairwellCatSpriteLibrary.DefaultResourcePath);
            Assert.That(imported, Is.Not.Null);
            string assetPath = AssetDatabase.GetAssetPath(imported);
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            var readable = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false);

            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        readable,
                        pngBytes,
                        false),
                    Is.True);
                Assert.That(readable.width, Is.EqualTo(AtlasWidth));
                Assert.That(readable.height, Is.EqualTo(AtlasHeight));

                Color32[] pixels = readable.GetPixels32();
                for (int row = 0; row < Rows; row++)
                {
                    for (int column = 0;
                         column < Columns;
                         column++)
                    {
                        int opaquePixels = CountOpaquePixels(
                            pixels,
                            readable.width,
                            column * CellSize,
                            row * CellSize);
                        Assert.That(
                            opaquePixels,
                            Is.GreaterThan(80),
                            $"Atlas cell ({column}, {row}) is empty.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        private static int CountOpaquePixels(
            Color32[] pixels,
            int atlasWidth,
            int xMin,
            int yMin)
        {
            int opaquePixels = 0;
            for (int y = yMin; y < yMin + CellSize; y++)
            {
                int rowStart = y * atlasWidth;
                for (int x = xMin; x < xMin + CellSize; x++)
                {
                    byte alpha = pixels[rowStart + x].a;
                    if (alpha != 0 && alpha != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Pixel ({x}, {y}) has non-binary alpha " +
                            $"{alpha}.");
                    }

                    if (alpha == byte.MaxValue)
                    {
                        int localX = x - xMin;
                        Assert.That(
                            localX,
                            Is.InRange(2, CellSize - 3),
                            $"Pixel ({x}, {y}) touches a cell edge.");
                        opaquePixels++;
                    }
                }
            }

            return opaquePixels;
        }
    }
}
