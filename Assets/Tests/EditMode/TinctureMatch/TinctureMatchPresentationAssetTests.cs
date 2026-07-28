using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class TinctureMatchPresentationAssetTests
    {
        [Test]
        public void BackgroundAndAtlas_AreLoadablePixelArtResources()
        {
            Texture2D background = Resources.Load<Texture2D>(
                TinctureMatchSpriteLibrary.BackgroundResourcePath);
            Texture2D atlas = Resources.Load<Texture2D>(
                TinctureMatchSpriteLibrary.AtlasResourcePath);

            Assert.That(background, Is.Not.Null);
            Assert.That(background.width, Is.EqualTo(640));
            Assert.That(background.height, Is.EqualTo(360));
            Assert.That(background.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                background.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(background.mipmapCount, Is.EqualTo(1));

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(512));
            Assert.That(atlas.height, Is.EqualTo(512));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(atlas.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));
        }

        [Test]
        public void TextureImporters_AreClampPointNoMipAndUncompressed()
        {
            Texture2D background = Resources.Load<Texture2D>(
                TinctureMatchSpriteLibrary.BackgroundResourcePath);
            Texture2D atlas = Resources.Load<Texture2D>(
                TinctureMatchSpriteLibrary.AtlasResourcePath);

            AssertImportContract(background, false);
            AssertImportContract(atlas, true);
        }

        [Test]
        public void SpriteUvCells_CoverAtlasExactlyOnce()
        {
            Array values = Enum.GetValues(
                typeof(TinctureMatchSpriteId));
            Assert.That(
                values.Length,
                Is.EqualTo(
                    TinctureMatchSpriteLibrary.AtlasColumns *
                    TinctureMatchSpriteLibrary.AtlasRows));

            var occupied = new bool[
                TinctureMatchSpriteLibrary.AtlasColumns,
                TinctureMatchSpriteLibrary.AtlasRows];

            foreach (TinctureMatchSpriteId sprite in values)
            {
                Rect uv = TinctureMatchSpriteLibrary.GetUv(sprite);
                Assert.That(uv.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(uv.yMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(uv.xMax, Is.LessThanOrEqualTo(1f));
                Assert.That(uv.yMax, Is.LessThanOrEqualTo(1f));
                Assert.That(
                    uv.width,
                    Is.EqualTo(
                        1f /
                        TinctureMatchSpriteLibrary.AtlasColumns));
                Assert.That(
                    uv.height,
                    Is.EqualTo(
                        1f /
                        TinctureMatchSpriteLibrary.AtlasRows));

                int column = Mathf.RoundToInt(
                    uv.x *
                    TinctureMatchSpriteLibrary.AtlasColumns);
                int row = Mathf.RoundToInt(
                    uv.y *
                    TinctureMatchSpriteLibrary.AtlasRows);
                Assert.That(occupied[column, row], Is.False);
                occupied[column, row] = true;
            }

            for (int column = 0;
                 column < TinctureMatchSpriteLibrary.AtlasColumns;
                 column++)
            {
                for (int row = 0;
                     row < TinctureMatchSpriteLibrary.AtlasRows;
                     row++)
                {
                    Assert.That(occupied[column, row], Is.True);
                }
            }
        }

        [Test]
        public void AtlasCells_AreVisibleAndVisuallyUnique()
        {
            Texture2D importedAtlas = Resources.Load<Texture2D>(
                TinctureMatchSpriteLibrary.AtlasResourcePath);
            string assetPath =
                AssetDatabase.GetAssetPath(importedAtlas);
            byte[] bytes = File.ReadAllBytes(assetPath);
            var readableAtlas = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);

            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        readableAtlas,
                        bytes,
                        false),
                    Is.True);
                Color32[] pixels = readableAtlas.GetPixels32();
                int cellWidth =
                    readableAtlas.width /
                    TinctureMatchSpriteLibrary.AtlasColumns;
                int cellHeight =
                    readableAtlas.height /
                    TinctureMatchSpriteLibrary.AtlasRows;
                var signatures = new HashSet<ulong>();

                foreach (TinctureMatchSpriteId sprite in
                         Enum.GetValues(
                             typeof(TinctureMatchSpriteId)))
                {
                    Rect uv =
                        TinctureMatchSpriteLibrary.GetUv(sprite);
                    int xMin = Mathf.RoundToInt(
                        uv.x * readableAtlas.width);
                    int yMin = Mathf.RoundToInt(
                        uv.y * readableAtlas.height);
                    int visiblePixels = 0;
                    ulong signature = 1469598103934665603UL;

                    for (int y = yMin;
                         y < yMin + cellHeight;
                         y++)
                    {
                        for (int x = xMin;
                             x < xMin + cellWidth;
                             x++)
                        {
                            Color32 pixel =
                                pixels[y * readableAtlas.width + x];
                            if (pixel.a > 0)
                            {
                                visiblePixels++;
                            }

                            signature ^= pixel.r;
                            signature *= 1099511628211UL;
                            signature ^= pixel.g;
                            signature *= 1099511628211UL;
                            signature ^= pixel.b;
                            signature *= 1099511628211UL;
                            signature ^= pixel.a;
                            signature *= 1099511628211UL;
                        }
                    }

                    Assert.That(
                        visiblePixels,
                        Is.GreaterThan(cellWidth * cellHeight / 80),
                        sprite.ToString());
                    Assert.That(
                        signatures.Add(signature),
                        Is.True,
                        sprite.ToString());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readableAtlas);
            }
        }

        [Test]
        public void InvalidSpriteId_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TinctureMatchSpriteLibrary.GetUv(
                    (TinctureMatchSpriteId)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TinctureMatchSpriteLibrary.GetUv(
                    (TinctureMatchSpriteId)16));
        }

        private static void AssertImportContract(
            Texture2D texture,
            bool alphaIsTransparency)
        {
            Assert.That(texture, Is.Not.Null);
            string assetPath = AssetDatabase.GetAssetPath(texture);
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
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(
                importer.alphaIsTransparency,
                Is.EqualTo(alphaIsTransparency));
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
    }
}
