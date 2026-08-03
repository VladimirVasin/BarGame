using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class PlayerBalconySmokingAssetTests
    {
        private const string AtlasResourcePath =
            "Player/PlayerBalconySmokingAtlas";
        private const string AtlasAssetPath =
            "Assets/Resources/Player/" +
            "PlayerBalconySmokingAtlas.png";
        private const string SourceFrameDirectory =
            "ArtSource/Player/BalconySmoking";
        private const string ExpectedSourceFrameSequenceSha256 =
            "8401B729B85E9C6E5D6BD766DBA0BEAFDC2D0E9C0B46609A07335E5E32E4EA55";
        private const string ExpectedAtlasFileSha256 =
            "FDF40A07AC6C3BCC366E9A71B09A5F875F76F2BB358AB6CADF544E23504D09DA";
        private const string IdleAtlasResourcePath =
            "Player/PlayerDirectionalAtlas";
        private const int IdleDirectionIndex =
            (int)PlayerViewDirection.BackRight;
        private const int EnterFirstFrame = 0;
        private const int EnterLastFrame = 23;
        private const int LoopFirstFrame = 24;
        private const int LoopLastFrame = 47;
        private const int ExitFirstFrame = 48;
        private const int ExitLastFrame = 63;
        private const int MinimumOpaquePixelsPerFrame = 256;

        [Test]
        public void Atlas_HasExpectedResourceLayoutAndImportSettings()
        {
            Texture2D atlas = Resources.Load<Texture2D>(
                AtlasResourcePath);

            Assert.That(
                atlas,
                Is.Not.Null,
                $"Missing smoking atlas resource " +
                $"{AtlasResourcePath}. The test requires final art; " +
                "do not satisfy it with a placeholder texture.");
            Assert.That(
                AssetDatabase.GetAssetPath(atlas),
                Is.EqualTo(AtlasAssetPath));

            Assert.That(
                PlayerAnimatedInteractionController.AtlasFrameCount,
                Is.EqualTo(64));
            Assert.That(
                PlayerAnimatedInteractionController.AtlasColumnCount,
                Is.EqualTo(8));
            Assert.That(
                PlayerAnimatedInteractionController.AtlasRowCount,
                Is.EqualTo(8));
            Assert.That(
                PlayerAnimatedInteractionController.FrameWidth,
                Is.EqualTo(128));
            Assert.That(
                PlayerAnimatedInteractionController.FrameHeight,
                Is.EqualTo(96));
            Assert.That(
                PlayerAnimatedInteractionController.PixelsPerUnit,
                Is.EqualTo(48f));
            Assert.That(
                PlayerAnimatedInteractionController.HipPivotXPixels,
                Is.EqualTo(64f));
            Assert.That(
                PlayerAnimatedInteractionController.HipPivotYPixels,
                Is.EqualTo(40f));
            Assert.That(
                PlayerAnimatedInteractionController.AuthoredTextureFlipX,
                Is.True,
                "The generic default remains available for existing " +
                "interactions; smoking overrides it per definition.");

            Assert.That(
                atlas.width,
                Is.EqualTo(
                    PlayerAnimatedInteractionController.FrameWidth *
                    PlayerAnimatedInteractionController.AtlasColumnCount));
            Assert.That(
                atlas.height,
                Is.EqualTo(
                    PlayerAnimatedInteractionController.FrameHeight *
                    PlayerAnimatedInteractionController.AtlasRowCount));
            Assert.That(atlas.width, Is.EqualTo(1024));
            Assert.That(atlas.height, Is.EqualTo(768));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));
            Assert.That(atlas.isReadable, Is.False);

            TextureImporter importer =
                AssetImporter.GetAtPath(AtlasAssetPath) as
                    TextureImporter;

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
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(
                importer.npotScale,
                Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(
                importer.maxTextureSize,
                Is.GreaterThanOrEqualTo(1024));

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            Assert.That(standalone.overridden, Is.True);
            Assert.That(
                standalone.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(standalone.crunchedCompression, Is.False);
            Assert.That(
                standalone.maxTextureSize,
                Is.GreaterThanOrEqualTo(1024));
        }

        [Test]
        public void Atlas_HasNonPlaceholderBinaryFramesInLogicalOrder()
        {
            Texture2D imported = Resources.Load<Texture2D>(
                AtlasResourcePath);
            Texture2D readable = LoadReadableCopy(imported);

            try
            {
                AssertPhaseContract();
                AssertFrameRect(
                    EnterFirstFrame,
                    0,
                    0);
                AssertFrameRect(
                    EnterLastFrame,
                    7,
                    2);
                AssertFrameRect(
                    LoopFirstFrame,
                    0,
                    3);
                AssertFrameRect(
                    LoopLastFrame,
                    7,
                    5);
                AssertFrameRect(
                    ExitFirstFrame,
                    0,
                    6);
                AssertFrameRect(
                    ExitLastFrame,
                    7,
                    7);

                Color32[] pixels = readable.GetPixels32();
                for (int frameIndex = 0;
                     frameIndex <
                     PlayerAnimatedInteractionController.AtlasFrameCount;
                     frameIndex++)
                {
                    int expectedColumn =
                        frameIndex %
                        PlayerAnimatedInteractionController.AtlasColumnCount;
                    int expectedRow =
                        frameIndex /
                        PlayerAnimatedInteractionController.AtlasColumnCount;
                    AssertFrameRect(
                        frameIndex,
                        expectedColumn,
                        expectedRow);
                    AssertFrameHasCharacterWithBinaryAlpha(
                        pixels,
                        readable.width,
                        frameIndex);
                }
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        [Test]
        public void Atlas_StartAndEndMatchOrdinaryBackRightIdleExactly()
        {
            Texture2D importedSmoking = Resources.Load<Texture2D>(
                AtlasResourcePath);
            Texture2D importedIdle = Resources.Load<Texture2D>(
                IdleAtlasResourcePath);
            Texture2D smoking = LoadReadableCopy(
                importedSmoking,
                1024,
                768);
            Texture2D idle = LoadReadableCopy(
                importedIdle,
                PlayerSpriteRig.FrameWidth *
                    PlayerSpriteRig.DirectionCount,
                PlayerSpriteRig.FrameHeight);

            try
            {
                AssertFrameMatchesIdle(
                    smoking,
                    idle,
                    EnterFirstFrame);
                AssertFrameMatchesIdle(
                    smoking,
                    idle,
                    ExitLastFrame);
            }
            finally
            {
                Object.DestroyImmediate(smoking);
                Object.DestroyImmediate(idle);
            }
        }

        [Test]
        public void GeneratedFiles_MatchEndpointOnlyAuthoredSequence()
        {
            Assert.That(
                HashFile(AtlasAssetPath),
                Is.EqualTo(ExpectedAtlasFileSha256),
                "The runtime atlas must be regenerated from the exact idle " +
                "endpoints and complete authored keyed poses without the " +
                "retired dither blend.");

            var frameHashes = new StringBuilder();
            for (int frameIndex = 0;
                 frameIndex <
                 PlayerAnimatedInteractionController.AtlasFrameCount;
                 frameIndex++)
            {
                string framePath = Path.Combine(
                    SourceFrameDirectory,
                    $"frame-{frameIndex:000}.png");
                frameHashes.Append(HashFile(framePath));
            }

            string sequenceHash = HashBytes(
                Encoding.ASCII.GetBytes(frameHashes.ToString()));
            Assert.That(
                sequenceHash,
                Is.EqualTo(ExpectedSourceFrameSequenceSha256),
                "Source frames 001..062 must remain the normalized keyed " +
                "poses while only 000 and 063 use exact ordinary idle.");
        }

        private static Texture2D LoadReadableCopy(
            Texture2D imported,
            int expectedWidth = 1024,
            int expectedHeight = 768)
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
            Assert.That(readable.width, Is.EqualTo(expectedWidth));
            Assert.That(readable.height, Is.EqualTo(expectedHeight));
            return readable;
        }

        private static string HashFile(string projectRelativePath)
        {
            return HashBytes(
                File.ReadAllBytes(
                    Path.GetFullPath(projectRelativePath)));
        }

        private static string HashBytes(byte[] bytes)
        {
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(bytes);
            }

            return System.BitConverter
                .ToString(hash)
                .Replace("-", string.Empty);
        }

        private static void AssertFrameMatchesIdle(
            Texture2D smoking,
            Texture2D idle,
            int frameIndex)
        {
            Color32[] smokingPixels = smoking.GetPixels32();
            Color32[] idlePixels = idle.GetPixels32();
            Rect frameRect =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    frameIndex);
            int frameX = Mathf.RoundToInt(frameRect.x);
            int frameY = Mathf.RoundToInt(frameRect.y);
            int idleX =
                IdleDirectionIndex * PlayerSpriteRig.FrameWidth;
            int idleCanvasOffsetX =
                (PlayerAnimatedInteractionController.FrameWidth -
                 PlayerSpriteRig.FrameWidth) / 2;

            for (int localY = 0;
                 localY < PlayerSpriteRig.FrameHeight;
                 localY++)
            {
                for (int localX = 0;
                     localX <
                     PlayerAnimatedInteractionController.FrameWidth;
                     localX++)
                {
                    int smokingIndex =
                        (frameY + localY) * smoking.width +
                        frameX + localX;
                    bool insideIdle =
                        localX >= idleCanvasOffsetX &&
                        localX <
                            idleCanvasOffsetX +
                            PlayerSpriteRig.FrameWidth;
                    Color32 expected = insideIdle
                        ? idlePixels[
                            localY * idle.width +
                            idleX +
                            localX - idleCanvasOffsetX]
                        : new Color32(0, 0, 0, 0);

                    if (!smokingPixels[smokingIndex].Equals(expected))
                    {
                        Assert.Fail(
                            $"Smoking frame {frameIndex} does not match " +
                            $"ordinary BackRight idle at ({localX}, " +
                            $"{localY}).");
                    }
                }
            }
        }

        private static void AssertPhaseContract()
        {
            Assert.That(EnterFirstFrame, Is.EqualTo(0));
            Assert.That(EnterLastFrame + 1, Is.EqualTo(LoopFirstFrame));
            Assert.That(LoopLastFrame + 1, Is.EqualTo(ExitFirstFrame));
            Assert.That(
                ExitLastFrame,
                Is.EqualTo(
                    PlayerAnimatedInteractionController.AtlasFrameCount -
                    1));
            Assert.That(
                EnterLastFrame - EnterFirstFrame + 1,
                Is.EqualTo(24));
            Assert.That(
                LoopLastFrame - LoopFirstFrame + 1,
                Is.EqualTo(24));
            Assert.That(
                ExitLastFrame - ExitFirstFrame + 1,
                Is.EqualTo(16));
        }

        private static void AssertFrameRect(
            int frameIndex,
            int expectedColumn,
            int expectedRowFromBottom)
        {
            Rect runtimeRect =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    frameIndex);
            var expected = new Rect(
                expectedColumn *
                    PlayerAnimatedInteractionController.FrameWidth,
                expectedRowFromBottom *
                    PlayerAnimatedInteractionController.FrameHeight,
                PlayerAnimatedInteractionController.FrameWidth,
                PlayerAnimatedInteractionController.FrameHeight);

            Assert.That(
                runtimeRect,
                Is.EqualTo(expected),
                $"Logical frame {frameIndex} is not in its expected " +
                "bottom-up atlas cell.");
        }

        private static void AssertFrameHasCharacterWithBinaryAlpha(
            Color32[] pixels,
            int atlasWidth,
            int frameIndex)
        {
            Rect runtimeRect =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    frameIndex);
            int frameX = Mathf.RoundToInt(runtimeRect.x);
            int frameY = Mathf.RoundToInt(runtimeRect.y);
            int frameWidth = Mathf.RoundToInt(runtimeRect.width);
            int frameHeight = Mathf.RoundToInt(runtimeRect.height);
            int opaquePixels = 0;

            for (int localY = 0; localY < frameHeight; localY++)
            {
                int rowStart =
                    ((frameY + localY) * atlasWidth) + frameX;
                for (int localX = 0; localX < frameWidth; localX++)
                {
                    Color32 pixel = pixels[rowStart + localX];
                    if (pixel.a != 0 && pixel.a != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Logical frame {frameIndex}, pixel " +
                            $"({localX}, {localY}) has non-binary alpha " +
                            $"{pixel.a}.");
                    }

                    if (pixel.a == 0 &&
                        (pixel.r != 0 || pixel.g != 0 || pixel.b != 0))
                    {
                        Assert.Fail(
                            $"Logical frame {frameIndex}, transparent " +
                            $"pixel ({localX}, {localY}) is not black.");
                    }

                    if (pixel.a == byte.MaxValue)
                    {
                        opaquePixels++;
                    }
                }
            }

            Assert.That(
                opaquePixels,
                Is.GreaterThanOrEqualTo(MinimumOpaquePixelsPerFrame),
                $"Logical frame {frameIndex} is empty or resembles a " +
                "placeholder instead of a complete character frame.");
        }
    }
}
