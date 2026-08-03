using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CatFeedingAnimationAssetTests
    {
        private const string CatAtlasResourcePath =
            "Stairwell/Cat/StairwellCatFeedingAtlas";
        private const string PlayerAtlasResourcePath =
            "Player/PlayerCatFeedingAtlas";
        private const string PlayerAtlasAssetPath =
            "Assets/Resources/Player/PlayerCatFeedingAtlas.png";
        private const string PlayerSourceAssetPath =
            "ArtSource/Player/CatFeeding/" +
            "PlayerCatFeedingSource-alpha.png";
        private const string IdleAtlasResourcePath =
            "Player/PlayerDirectionalAtlas";
        private const string ExpectedPlayerSourceFileSha256 =
            "BEA27B553C611E75DB14C14ECFE509B1044DEBBFF5AAE73DDF7CFE85A24D2118";
        private const string ExpectedPlayerAtlasFileSha256 =
            "BFD959B610B5807AA22516C78D42941A14BCBC0AE06BE3E34F833504FC2C361B";
        private const int IdleDirectionIndex =
            (int)PlayerViewDirection.FrontLeft;
        private const int IdleFrameWidth = 64;
        private const int IdleFrameHeight = 96;
        private const int InteractionFrameWidth = 128;
        private const int InteractionFrameHeight = 96;
        private const int EndpointPaddingX = 32;

        [TestCase(CatAtlasResourcePath, 512, 128)]
        [TestCase(PlayerAtlasResourcePath, 1024, 768)]
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
        [TestCase(PlayerAtlasResourcePath, 128, 96, 8, 8)]
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

        [Test]
        public void
            PlayerAtlas_EndpointsMatchPreflippedOrdinaryFrontLeftExactly()
        {
            Texture2D importedPlayer = Resources.Load<Texture2D>(
                PlayerAtlasResourcePath);
            Texture2D importedIdle = Resources.Load<Texture2D>(
                IdleAtlasResourcePath);
            Texture2D player = LoadReadableCopy(importedPlayer);
            Texture2D idle = LoadReadableCopy(importedIdle);

            try
            {
                Assert.That(IdleDirectionIndex, Is.EqualTo(7));
                Assert.That(
                    idle.width,
                    Is.EqualTo(
                        PlayerSpriteRig.DirectionCount *
                        IdleFrameWidth));
                Assert.That(idle.height, Is.EqualTo(IdleFrameHeight));
                Assert.That(player.width, Is.EqualTo(1024));
                Assert.That(player.height, Is.EqualTo(768));

                Color32[] playerPixels = player.GetPixels32();
                Color32[] idlePixels = idle.GetPixels32();
                AssertPlayerEndpointMatchesPreflippedIdle(
                    playerPixels,
                    player.width,
                    idlePixels,
                    idle.width,
                    0);
                AssertPlayerEndpointMatchesPreflippedIdle(
                    playerPixels,
                    player.width,
                    idlePixels,
                    idle.width,
                    PlayerAnimatedInteractionController
                        .AtlasFrameCount - 1);
                AssertPlayerEndpointsMatchEachOther(
                    playerPixels,
                    player.width);
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(idle);
            }
        }

        [Test]
        public void
            PlayerAtlas_FilesMatchApprovedEndpointNormalizedArtifact()
        {
            Assert.That(
                HashFile(PlayerSourceAssetPath),
                Is.EqualTo(ExpectedPlayerSourceFileSha256),
                "The approved 8x8 source sheet changed; frames 1..62 " +
                "must remain source-derived from the reviewed artifact.");
            Assert.That(
                HashFile(PlayerAtlasAssetPath),
                Is.EqualTo(ExpectedPlayerAtlasFileSha256),
                "Regenerate the player cat-feeding atlas with exact " +
                "preflipped FrontLeft endpoints and unchanged normalized " +
                "source poses in frames 1..62.");
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

        private static string HashFile(string projectRelativePath)
        {
            byte[] bytes = File.ReadAllBytes(
                Path.GetFullPath(projectRelativePath));
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(bytes);
            }

            return System.BitConverter
                .ToString(hash)
                .Replace("-", string.Empty);
        }

        private static void AssertPlayerEndpointMatchesPreflippedIdle(
            Color32[] playerPixels,
            int playerAtlasWidth,
            Color32[] idlePixels,
            int idleAtlasWidth,
            int logicalFrameIndex)
        {
            Rect frameRect =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    logicalFrameIndex);
            int frameX = Mathf.RoundToInt(frameRect.x);
            int frameY = Mathf.RoundToInt(frameRect.y);
            var transparent = new Color32(0, 0, 0, 0);

            for (int localY = 0;
                 localY < InteractionFrameHeight;
                 localY++)
            {
                for (int localX = 0;
                     localX < InteractionFrameWidth;
                     localX++)
                {
                    Color32 expected = transparent;
                    int idleLocalX =
                        localX - EndpointPaddingX;
                    if (idleLocalX >= 0 &&
                        idleLocalX < IdleFrameWidth)
                    {
                        int preflippedIdleLocalX =
                            IdleFrameWidth - 1 - idleLocalX;
                        int idleX =
                            IdleDirectionIndex * IdleFrameWidth +
                            preflippedIdleLocalX;
                        expected = idlePixels[
                            localY * idleAtlasWidth + idleX];
                        if (expected.a == 0)
                        {
                            expected = transparent;
                        }
                    }

                    Color32 actual = playerPixels[
                        (frameY + localY) * playerAtlasWidth +
                        frameX + localX];
                    Assert.That(
                        actual,
                        Is.EqualTo(expected),
                        $"Logical endpoint {logicalFrameIndex}, pixel " +
                        $"({localX}, {localY}) differs from the centered " +
                        "preflipped ordinary FrontLeft idle.");
                }
            }
        }

        private static void AssertPlayerEndpointsMatchEachOther(
            Color32[] pixels,
            int atlasWidth)
        {
            Rect first =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(0);
            Rect last =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    PlayerAnimatedInteractionController
                        .AtlasFrameCount - 1);
            int firstX = Mathf.RoundToInt(first.x);
            int firstY = Mathf.RoundToInt(first.y);
            int lastX = Mathf.RoundToInt(last.x);
            int lastY = Mathf.RoundToInt(last.y);

            for (int localY = 0;
                 localY < InteractionFrameHeight;
                 localY++)
            {
                for (int localX = 0;
                     localX < InteractionFrameWidth;
                     localX++)
                {
                    Color32 firstPixel = pixels[
                        (firstY + localY) * atlasWidth +
                        firstX + localX];
                    Color32 lastPixel = pixels[
                        (lastY + localY) * atlasWidth +
                        lastX + localX];
                    Assert.That(
                        lastPixel,
                        Is.EqualTo(firstPixel),
                        $"Endpoint frames differ at " +
                        $"({localX}, {localY}).");
                }
            }
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
