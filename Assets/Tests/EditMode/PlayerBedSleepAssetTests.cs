using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class PlayerBedSleepAssetTests
    {
        private const string AtlasResourcePath =
            "Player/PlayerBedSleepAtlas";
        private const string IdleAtlasResourcePath =
            "Player/PlayerDirectionalAtlas";
        private const string AtlasAssetPath =
            "Assets/Resources/Player/PlayerBedSleepAtlas.png";
        private const string SourceFrameDirectory =
            "ArtSource/Player/BedSleep";
        private const string ExpectedAtlasFileSha256 =
            "80B08BA6782019C12DE87B5D57130D34B5D13CD350428864D7BBB2747B612572";
        private const string ExpectedSourceFrameSequenceSha256 =
            "741D4BDFB51163FF039DBD4B7CD996310477203706232F898BF65380F2FD0507";
        private const string ExpectedAuthoredMiddleSequenceSha256 =
            "6182B5AD8FFE02DFCF8B20532BD4C16EB2009844F9C82E2E9B8CCB29B28BDECF";
        private const int IdleDirectionIndex =
            (int)PlayerViewDirection.FrontLeft;
        private const int EnterFirstFrame = 0;
        private const int ExitLastFrame =
            PlayerAnimatedInteractionController.AtlasFrameCount - 1;
        private const string OverlayShaderAssetPath =
            "Assets/Resources/Shaders/" +
            "PlayerAnimatedInteractionOverlay.shader";

        [Test]
        public void Atlas_HasExpectedLayoutAndImportSettings()
        {
            Texture2D atlas = Resources.Load<Texture2D>(
                AtlasResourcePath);

            Assert.That(atlas, Is.Not.Null);
            Assert.That(
                PlayerAnimatedInteractionController.AtlasFrameCount,
                Is.EqualTo(64));
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
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.alphaIsTransparency, Is.True);
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
        public void Atlas_HasNonEmptyBinaryAlphaFramesInBottomRowOrder()
        {
            Texture2D imported = Resources.Load<Texture2D>(
                AtlasResourcePath);
            Texture2D readable = LoadReadableCopy(imported);

            try
            {
                RectInt frameZero = GetFrameRect(0);
                Assert.That(
                    frameZero,
                    Is.EqualTo(
                        new RectInt(
                            0,
                            0,
                            PlayerAnimatedInteractionController.FrameWidth,
                            PlayerAnimatedInteractionController.FrameHeight)),
                    "Logical frame 0 must occupy the lower-left atlas cell.");

                RectInt finalFrame = GetFrameRect(
                    PlayerAnimatedInteractionController.AtlasFrameCount - 1);
                Assert.That(
                    finalFrame.x,
                    Is.EqualTo(
                        readable.width -
                        PlayerAnimatedInteractionController.FrameWidth));
                Assert.That(
                    finalFrame.y,
                    Is.EqualTo(
                        readable.height -
                        PlayerAnimatedInteractionController.FrameHeight));

                Color32[] pixels = readable.GetPixels32();
                for (int frameIndex = 0;
                     frameIndex <
                     PlayerAnimatedInteractionController.AtlasFrameCount;
                     frameIndex++)
                {
                    AssertFrameIsNonEmptyWithBinaryAlpha(
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
        public void Atlas_StartAndEndMatchPreflippedOrdinaryFrontLeftExactly()
        {
            Texture2D importedSleep = Resources.Load<Texture2D>(
                AtlasResourcePath);
            Texture2D importedIdle = Resources.Load<Texture2D>(
                IdleAtlasResourcePath);
            Texture2D sleep = LoadReadableCopy(importedSleep);
            Texture2D idle = LoadReadableCopy(importedIdle);

            try
            {
                Assert.That(
                    IdleDirectionIndex,
                    Is.EqualTo(7),
                    "The bed dock handoff must use FrontLeft cell 7.");
                AssertFrameMatchesPreflippedIdle(
                    sleep,
                    idle,
                    EnterFirstFrame);
                AssertFrameMatchesPreflippedIdle(
                    sleep,
                    idle,
                    ExitLastFrame);
            }
            finally
            {
                Object.DestroyImmediate(sleep);
                Object.DestroyImmediate(idle);
            }
        }

        [Test]
        public void GeneratedFiles_MatchEndpointOnlyAuthoredSequence()
        {
            Assert.That(
                HashFile(AtlasAssetPath),
                Is.EqualTo(ExpectedAtlasFileSha256),
                "The runtime bed atlas must be rebuilt from the exact " +
                "preflipped idle endpoints and untouched authored middle.");

            var allFrameHashes = new StringBuilder();
            var authoredMiddleHashes = new StringBuilder();
            for (int frameIndex = 0;
                 frameIndex <
                 PlayerAnimatedInteractionController.AtlasFrameCount;
                 frameIndex++)
            {
                string framePath = Path.Combine(
                    SourceFrameDirectory,
                    $"frame-{frameIndex:000}.png");
                string frameHash = HashFile(framePath);
                allFrameHashes.Append(frameHash);
                if (frameIndex > EnterFirstFrame &&
                    frameIndex < ExitLastFrame)
                {
                    authoredMiddleHashes.Append(frameHash);
                }
            }

            Assert.That(
                HashBytes(
                    Encoding.ASCII.GetBytes(
                        allFrameHashes.ToString())),
                Is.EqualTo(ExpectedSourceFrameSequenceSha256));
            Assert.That(
                HashBytes(
                    Encoding.ASCII.GetBytes(
                        authoredMiddleHashes.ToString())),
                Is.EqualTo(ExpectedAuthoredMiddleSequenceSha256),
                "Source frames 001..062 must stay byte-identical while " +
                "only endpoints 000 and 063 use ordinary idle.");
        }

        [Test]
        public void InteractionOverlay_IsSharedAndIgnoresSceneDepth()
        {
            Shader shader = Resources.Load<Shader>(
                PlayerAnimatedInteractionResources
                    .OverlayShaderResourcePath);

            Assert.That(shader, Is.Not.Null);
            Assert.That(
                shader.name,
                Is.EqualTo(
                    "Bar Promenade/" +
                    "Player Animated Interaction Overlay"));
            Assert.That(shader.isSupported, Is.True);

            Material first =
                PlayerAnimatedInteractionResources
                    .OverlayMaterial;
            Material second =
                PlayerAnimatedInteractionResources
                    .OverlayMaterial;
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.shader, Is.SameAs(shader));
            Assert.That(
                first.FindPass("InteractionOverlay"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(first.enableInstancing, Is.True);
            Assert.That(first.renderQueue, Is.EqualTo(3100));

            string shaderSource = File.ReadAllText(
                Path.GetFullPath(
                    OverlayShaderAssetPath));
            StringAssert.Contains(
                "ZWrite Off",
                shaderSource);
            StringAssert.Contains(
                "ZTest Always",
                shaderSource);
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

        private static void AssertFrameMatchesPreflippedIdle(
            Texture2D sleep,
            Texture2D idle,
            int frameIndex)
        {
            Color32[] sleepPixels = sleep.GetPixels32();
            Color32[] idlePixels = idle.GetPixels32();
            RectInt frame = GetFrameRect(frameIndex);
            int idleX = IdleDirectionIndex * PlayerSpriteRig.FrameWidth;
            int canvasOffsetX =
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
                    int sleepIndex =
                        (frame.y + localY) * sleep.width +
                        frame.x + localX;
                    bool insideIdle =
                        localX >= canvasOffsetX &&
                        localX <
                            canvasOffsetX +
                            PlayerSpriteRig.FrameWidth;
                    int idleLocalX = localX - canvasOffsetX;
                    Color32 expected = insideIdle
                        ? idlePixels[
                            localY * idle.width +
                            idleX +
                            PlayerSpriteRig.FrameWidth - 1 -
                            idleLocalX]
                        : new Color32(0, 0, 0, 0);

                    if (!sleepPixels[sleepIndex].Equals(expected))
                    {
                        Assert.Fail(
                            $"Bed frame {frameIndex} does not match " +
                            "preflipped ordinary FrontLeft idle at " +
                            $"({localX}, {localY}).");
                    }
                }
            }
        }

        private static RectInt GetFrameRect(int frameIndex)
        {
            Rect runtimeRect =
                PlayerAnimatedInteractionController.GetAtlasFrameRect(
                    frameIndex);
            return new RectInt(
                Mathf.RoundToInt(runtimeRect.x),
                Mathf.RoundToInt(runtimeRect.y),
                Mathf.RoundToInt(runtimeRect.width),
                Mathf.RoundToInt(runtimeRect.height));
        }

        private static void AssertFrameIsNonEmptyWithBinaryAlpha(
            Color32[] pixels,
            int atlasWidth,
            int frameIndex)
        {
            RectInt frame = GetFrameRect(frameIndex);
            int opaquePixels = 0;

            for (int localY = 0;
                 localY < frame.height;
                 localY++)
            {
                int rowStart =
                    ((frame.y + localY) * atlasWidth) + frame.x;
                for (int localX = 0;
                     localX < frame.width;
                     localX++)
                {
                    byte alpha = pixels[rowStart + localX].a;
                    if (alpha != 0 && alpha != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Logical frame {frameIndex}, pixel " +
                            $"({localX}, {localY}) has non-binary alpha " +
                            $"{alpha}.");
                    }

                    if (alpha == byte.MaxValue)
                    {
                        opaquePixels++;
                    }
                }
            }

            Assert.That(
                opaquePixels,
                Is.GreaterThan(0),
                $"Logical frame {frameIndex} is empty.");
        }
    }
}
