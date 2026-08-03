using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class PlayerDetailedFallAssetTests
    {
        private static readonly PlayerViewDirection[] Directions =
        {
            PlayerViewDirection.Front,
            PlayerViewDirection.FrontRight,
            PlayerViewDirection.Right,
            PlayerViewDirection.BackRight,
            PlayerViewDirection.Back,
            PlayerViewDirection.BackLeft,
            PlayerViewDirection.Left,
            PlayerViewDirection.FrontLeft
        };

        [Test]
        public void Atlases_CoverEveryViewAndFallSideWithCrispImports()
        {
            Assert.That(
                Enum.GetValues(typeof(PlayerViewDirection)),
                Is.EqualTo(Directions));

            for (int directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                AssertAtlasContract(Directions[directionIndex], -1f);
                AssertAtlasContract(Directions[directionIndex], 1f);
            }
        }

        [Test]
        public void Atlases_HaveEightyNonEmptyTopToBottomFrames()
        {
            for (int directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                AssertAllFramesAreNonEmpty(
                    Directions[directionIndex],
                    -1f);
                AssertAllFramesAreNonEmpty(
                    Directions[directionIndex],
                    1f);
            }
        }

        [Test]
        public void FrameRects_ReadChronologicallyFromTopLeft()
        {
            Assert.That(
                PlayerSpriteRig.GetFallAtlasFrameRect(0),
                Is.EqualTo(new Rect(0f, 672f, 128f, 96f)));
            Assert.That(
                PlayerSpriteRig.GetFallAtlasFrameRect(9),
                Is.EqualTo(new Rect(1152f, 672f, 128f, 96f)));
            Assert.That(
                PlayerSpriteRig.GetFallAtlasFrameRect(10),
                Is.EqualTo(new Rect(0f, 576f, 128f, 96f)));
            Assert.That(
                PlayerSpriteRig.GetFallAtlasFrameRect(79),
                Is.EqualTo(new Rect(1152f, 0f, 128f, 96f)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerSpriteRig.GetFallAtlasFrameRect(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerSpriteRig.GetFallAtlasFrameRect(80));
        }

        private static void AssertAtlasContract(
            PlayerViewDirection direction,
            float signedDirection)
        {
            string resourcePath =
                PlayerSpriteRig.GetFallAtlasResourcePath(
                    direction,
                    signedDirection);
            Texture2D atlas = Resources.Load<Texture2D>(resourcePath);

            Assert.That(atlas, Is.Not.Null, resourcePath);
            Assert.That(atlas.width, Is.EqualTo(1280), resourcePath);
            Assert.That(atlas.height, Is.EqualTo(768), resourcePath);
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));
            Assert.That(atlas.isReadable, Is.False);

            string assetPath = AssetDatabase.GetAssetPath(atlas);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, assetPath);
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
                importer.npotScale,
                Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            Assert.That(standalone.overridden, Is.True);
            Assert.That(
                standalone.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(standalone.crunchedCompression, Is.False);
        }

        private static void AssertAllFramesAreNonEmpty(
            PlayerViewDirection direction,
            float signedDirection)
        {
            string resourcePath =
                PlayerSpriteRig.GetFallAtlasResourcePath(
                    direction,
                    signedDirection);
            Texture2D imported = Resources.Load<Texture2D>(resourcePath);
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
                    Is.True,
                    assetPath);
                Color32[] pixels = readable.GetPixels32();
                for (int frameIndex = 0;
                     frameIndex <
                     PlayerFallAnimationTimeline.FrameCount;
                     frameIndex++)
                {
                    Rect rect = PlayerSpriteRig.GetFallAtlasFrameRect(
                        frameIndex);
                    int visiblePixelCount = 0;
                    for (int y = 0;
                         y < PlayerSpriteRig.FallFrameHeight;
                         y++)
                    {
                        int rowStart =
                            (Mathf.RoundToInt(rect.y) + y) *
                            readable.width +
                            Mathf.RoundToInt(rect.x);
                        for (int x = 0;
                             x < PlayerSpriteRig.FallFrameWidth;
                             x++)
                        {
                            if (pixels[rowStart + x].a > 8)
                            {
                                visiblePixelCount++;
                            }
                        }
                    }

                    Assert.That(
                        visiblePixelCount,
                        Is.GreaterThan(100),
                        $"{assetPath} frame {frameIndex} is empty.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }
    }
}
