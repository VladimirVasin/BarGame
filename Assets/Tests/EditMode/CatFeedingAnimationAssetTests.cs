using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CatFeedingAnimationAssetTests
    {
        private const string CatAtlasResourcePath =
            "Stairwell/Cat/StairwellCatFeedingAtlas";

        [TestCase(CatAtlasResourcePath, 512, 128)]
        public void Atlas_HasExpectedLayoutAndPixelArtImportContract(
            string resourcePath,
            int expectedWidth,
            int expectedHeight)
        {
            Texture2D atlas = Resources.Load<Texture2D>(resourcePath);

            Assert.That(
                atlas,
                Is.Not.Null,
                $"Missing feeding atlas Resources/{resourcePath}.png.");
            Assert.That(atlas.width, Is.EqualTo(expectedWidth));
            Assert.That(atlas.height, Is.EqualTo(expectedHeight));
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

        [TestCase(CatAtlasResourcePath, 64, 64, 8, 2)]
        public void Atlas_EveryFrameIsNonEmptyWithBinaryAlpha(
            string resourcePath,
            int frameWidth,
            int frameHeight,
            int columns,
            int rows)
        {
            Texture2D imported = Resources.Load<Texture2D>(resourcePath);
            Texture2D readable = LoadReadableCopy(imported);

            try
            {
                Assert.That(
                    readable.width,
                    Is.EqualTo(frameWidth * columns));
                Assert.That(
                    readable.height,
                    Is.EqualTo(frameHeight * rows));

                Color32[] pixels = readable.GetPixels32();
                int nonEmptyFrames = 0;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0;
                         column < columns;
                         column++)
                    {
                        int opaquePixels =
                            AssertFrameHasBinaryAlpha(
                                pixels,
                                readable.width,
                                column,
                                row,
                                frameWidth,
                                frameHeight);
                        Assert.That(
                            opaquePixels,
                            Is.GreaterThan(0),
                            $"Physical atlas cell " +
                            $"({column}, {row}) is empty.");
                        nonEmptyFrames++;
                    }
                }

                Assert.That(
                    nonEmptyFrames,
                    Is.EqualTo(columns * rows));
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        private static Texture2D LoadReadableCopy(Texture2D imported)
        {
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

            bool loaded = ImageConversion.LoadImage(
                readable,
                pngBytes,
                false);
            Assert.That(
                loaded,
                Is.True,
                $"Could not decode {assetPath}.");
            Assert.That(readable.isReadable, Is.True);
            return readable;
        }

        private static int AssertFrameHasBinaryAlpha(
            Color32[] pixels,
            int atlasWidth,
            int column,
            int row,
            int frameWidth,
            int frameHeight)
        {
            int xMin = column * frameWidth;
            int yMin = row * frameHeight;
            int opaquePixels = 0;
            for (int localY = 0;
                 localY < frameHeight;
                 localY++)
            {
                int rowStart =
                    ((yMin + localY) * atlasWidth) + xMin;
                for (int localX = 0;
                     localX < frameWidth;
                     localX++)
                {
                    byte alpha = pixels[rowStart + localX].a;
                    if (alpha != 0 && alpha != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Physical atlas cell ({column}, {row}), " +
                            $"pixel ({localX}, {localY}) has " +
                            $"non-binary alpha {alpha}.");
                    }

                    if (alpha == byte.MaxValue)
                    {
                        opaquePixels++;
                    }
                }
            }

            return opaquePixels;
        }
    }
}
