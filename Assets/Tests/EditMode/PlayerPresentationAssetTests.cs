using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class PlayerPresentationAssetTests
    {
        private const string ExpressionAtlasSha256 =
            "6FDFB6744B9F74F0EFE67BC30C528B8C654ABEC3444EA815FA5F94DD034A7688";
        private const int NeutralExpression =
            (int)PlayerFacialExpression.Neutral;

        private static readonly PlayerViewDirection[] VisibleFaceDirections =
        {
            PlayerViewDirection.Front,
            PlayerViewDirection.FrontRight,
            PlayerViewDirection.Right,
            PlayerViewDirection.Left,
            PlayerViewDirection.FrontLeft
        };

        // Local frame coordinates measured from the top-left of the PNG.
        private static readonly RectInt[] OpaqueFaceCoreRegions =
        {
            new RectInt(28, 17, 8, 7),
            new RectInt(27, 17, 8, 7),
            new RectInt(27, 16, 5, 8),
            new RectInt(31, 16, 6, 8),
            new RectInt(29, 17, 7, 7)
        };

        private static readonly Vector2Int[] BrightFaceSamples =
        {
            new Vector2Int(34, 20),
            new Vector2Int(35, 18),
            new Vector2Int(28, 18),
            new Vector2Int(35, 18),
            new Vector2Int(35, 18)
        };

        // Indexed by PlayerViewDirection and measured from the top-left of
        // each 64x96 frame. Rear views intentionally have empty masks.
        private static readonly RectInt[] ExpressionFaceEditRegions =
        {
            new RectInt(27, 16, 9, 8),
            new RectInt(26, 16, 10, 8),
            new RectInt(27, 15, 6, 9),
            new RectInt(0, 0, 0, 0),
            new RectInt(0, 0, 0, 0),
            new RectInt(0, 0, 0, 0),
            new RectInt(31, 15, 8, 9),
            new RectInt(28, 16, 12, 8)
        };

        private static readonly int[] ExpectedHalfBlinkChangedPixelCounts =
        {
            6,
            6,
            4,
            0,
            0,
            0,
            4,
            4
        };

        private static readonly int[] ExpectedClosedBlinkChangedPixelCounts =
        {
            8,
            8,
            5,
            0,
            0,
            0,
            4,
            6
        };

        private static readonly int[] ExpectedWatchfulChangedPixelCounts =
        {
            6,
            6,
            4,
            0,
            0,
            0,
            4,
            4
        };

        private static readonly int[] ExpectedTenseChangedPixelCounts =
        {
            11,
            11,
            6,
            0,
            0,
            0,
            6,
            9
        };

        private static readonly int[][] ExpectedExpressionChangedPixelCounts =
        {
            ExpectedHalfBlinkChangedPixelCounts,
            ExpectedClosedBlinkChangedPixelCounts,
            ExpectedWatchfulChangedPixelCounts,
            ExpectedTenseChangedPixelCounts
        };

        [Test]
        public void DirectionalAtlases_HaveExpectedLayoutsAndImportSettings()
        {
            Texture2D reference = Resources.Load<Texture2D>(
                PlayerSpriteRig.ReferenceAtlasResourcePath);
            Texture2D parts = Resources.Load<Texture2D>(
                PlayerSpriteRig.AtlasResourcePath);

            Assert.That(reference, Is.Not.Null);
            Assert.That(parts, Is.Not.Null);

            Assert.That(
                reference.width,
                Is.EqualTo(
                    PlayerSpriteRig.FrameWidth *
                    PlayerSpriteRig.DirectionCount));
            Assert.That(
                reference.height,
                Is.EqualTo(PlayerSpriteRig.FrameHeight));
            Assert.That(parts.width, Is.EqualTo(reference.width));
            Assert.That(
                parts.height,
                Is.EqualTo(
                    PlayerSpriteRig.FrameHeight *
                    PlayerSpriteRig.PartCount));

            TextureImporter referenceImporter =
                AssertImportContract(reference);
            TextureImporter partsImporter =
                AssertImportContract(parts);

            AssertMatchingImportSettings(
                reference,
                referenceImporter,
                parts,
                partsImporter);
        }

        [Test]
        public void ReferenceAtlas_HasBinaryAlphaAndOpaqueVisibleFaces()
        {
            Texture2D imported = Resources.Load<Texture2D>(
                PlayerSpriteRig.ReferenceAtlasResourcePath);
            Texture2D readable = LoadReadableCopy(imported);

            try
            {
                Color32[] pixels = readable.GetPixels32();
                for (int index = 0; index < pixels.Length; index++)
                {
                    byte alpha = pixels[index].a;
                    if (alpha != 0 && alpha != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Reference atlas pixel {index} has non-binary " +
                            $"alpha {alpha}.");
                    }
                }

                for (int faceIndex = 0;
                     faceIndex < VisibleFaceDirections.Length;
                     faceIndex++)
                {
                    PlayerViewDirection direction =
                        VisibleFaceDirections[faceIndex];
                    RectInt region = OpaqueFaceCoreRegions[faceIndex];

                    for (int topY = region.yMin;
                         topY < region.yMax;
                         topY++)
                    {
                        for (int localX = region.xMin;
                             localX < region.xMax;
                             localX++)
                        {
                            Color32 pixel = GetTopLeftPixel(
                                pixels,
                                readable.width,
                                readable.height,
                                direction,
                                localX,
                                topY);
                            Assert.That(
                                pixel.a,
                                Is.EqualTo(byte.MaxValue),
                                $"{direction} face is transparent at " +
                                $"({localX}, {topY}).");
                        }
                    }

                    Vector2Int sample = BrightFaceSamples[faceIndex];
                    Color32 facePixel = GetTopLeftPixel(
                        pixels,
                        readable.width,
                        readable.height,
                        direction,
                        sample.x,
                        sample.y);
                    int brightness =
                        facePixel.r + facePixel.g + facePixel.b;

                    Assert.That(
                        facePixel.a,
                        Is.EqualTo(byte.MaxValue),
                        $"{direction} repaired face sample is transparent.");
                    Assert.That(
                        brightness,
                        Is.GreaterThan(300),
                        $"{direction} repaired face sample is not readable.");
                }
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        [Test]
        public void PartsAtlas_HasAllCellsAndRecomposesReferenceExactly()
        {
            Texture2D importedReference = Resources.Load<Texture2D>(
                PlayerSpriteRig.ReferenceAtlasResourcePath);
            Texture2D importedParts = Resources.Load<Texture2D>(
                PlayerSpriteRig.AtlasResourcePath);
            Texture2D reference = LoadReadableCopy(importedReference);
            Texture2D parts = LoadReadableCopy(importedParts);

            try
            {
                Color32[] referencePixels = reference.GetPixels32();
                Color32[] partPixels = parts.GetPixels32();

                for (int partIndex = 0;
                     partIndex < PlayerSpriteRig.PartCount;
                     partIndex++)
                {
                    for (int directionIndex = 0;
                         directionIndex < PlayerSpriteRig.DirectionCount;
                         directionIndex++)
                    {
                        int opaquePixels = CountOpaqueCellPixels(
                            partPixels,
                            parts.width,
                            partIndex,
                            directionIndex);
                        Assert.That(
                            opaquePixels,
                            Is.GreaterThan(0),
                            $"Part {partIndex}, direction {directionIndex} " +
                            "must contain visible pixels.");
                    }
                }

                AssertNeutralCompositeMatchesReference(
                    referencePixels,
                    reference.width,
                    partPixels,
                    parts.width);
            }
            finally
            {
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(parts);
            }
        }

        [Test]
        public void BodyExpressionsAtlas_HasExpectedLayoutAndImportSettings()
        {
            Texture2D parts = Resources.Load<Texture2D>(
                PlayerSpriteRig.AtlasResourcePath);
            Texture2D expressions = Resources.Load<Texture2D>(
                PlayerSpriteRig.ExpressionAtlasResourcePath);

            Assert.That(parts, Is.Not.Null);
            Assert.That(expressions, Is.Not.Null);
            Assert.That(expressions.width, Is.EqualTo(parts.width));
            Assert.That(
                expressions.height,
                Is.EqualTo(
                    PlayerSpriteRig.FrameHeight *
                    PlayerSpriteRig.ExpressionCount));

            TextureImporter partsImporter = AssertImportContract(parts);
            TextureImporter expressionsImporter =
                AssertImportContract(expressions);

            AssertMatchingImportSettings(
                parts,
                partsImporter,
                expressions,
                expressionsImporter);
        }

        [Test]
        public void BodyExpressionsAtlas_MatchesDeterministicBuilderOutput()
        {
            string atlasPath = Path.Combine(
                Application.dataPath,
                "Resources",
                "Player",
                "PlayerDirectionalBodyExpressionsAtlas.png");
            byte[] bytes = File.ReadAllBytes(atlasPath);
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(bytes);
            }

            string actualHash = System.BitConverter
                .ToString(hash)
                .Replace("-", string.Empty);
            Assert.That(actualHash, Is.EqualTo(ExpressionAtlasSha256));
        }

        [Test]
        public void BodyExpressionsAtlas_PreservesBodyAndOnlyEditsFaceMasks()
        {
            Texture2D importedParts = Resources.Load<Texture2D>(
                PlayerSpriteRig.AtlasResourcePath);
            Texture2D importedExpressions = Resources.Load<Texture2D>(
                PlayerSpriteRig.ExpressionAtlasResourcePath);
            Texture2D parts = LoadReadableCopy(importedParts);
            Texture2D expressions =
                LoadReadableCopy(importedExpressions);

            try
            {
                Color32[] partPixels = parts.GetPixels32();
                Color32[] expressionPixels = expressions.GetPixels32();

                for (int directionIndex = 0;
                     directionIndex < PlayerSpriteRig.DirectionCount;
                     directionIndex++)
                {
                    RectInt faceMask =
                        ExpressionFaceEditRegions[directionIndex];
                    int[] expressionChanges =
                        new int[PlayerSpriteRig.ExpressionCount];
                    int[,] pairDifferences = new int[
                        PlayerSpriteRig.ExpressionCount,
                        PlayerSpriteRig.ExpressionCount];

                    for (int localY = 0;
                         localY < PlayerSpriteRig.FrameHeight;
                         localY++)
                    {
                        for (int localX = 0;
                             localX < PlayerSpriteRig.FrameWidth;
                             localX++)
                        {
                            Color32 body = GetAtlasCellPixel(
                                partPixels,
                                parts.width,
                                (int)PlayerPuppetPart.Body,
                                directionIndex,
                                localX,
                                localY);
                            Color32 neutral = GetAtlasCellPixel(
                                expressionPixels,
                                expressions.width,
                                NeutralExpression,
                                directionIndex,
                                localX,
                                localY);

                            if (!ColorsEqual(body, neutral))
                            {
                                Assert.Fail(
                                    $"Neutral expression differs from Body " +
                                    $"at direction {directionIndex}, pixel " +
                                    $"({localX}, {localY}).");
                            }

                            Color32[] facialPixels =
                                new Color32[
                                    PlayerSpriteRig.ExpressionCount];
                            facialPixels[NeutralExpression] = neutral;
                            for (int expressionIndex = 1;
                                 expressionIndex <
                                 PlayerSpriteRig.ExpressionCount;
                                 expressionIndex++)
                            {
                                Color32 facial = GetAtlasCellPixel(
                                    expressionPixels,
                                    expressions.width,
                                    expressionIndex,
                                    directionIndex,
                                    localX,
                                    localY);
                                facialPixels[expressionIndex] = facial;

                                if (facial.a != neutral.a)
                                {
                                    Assert.Fail(
                                        $"Expression {expressionIndex} " +
                                        $"changed alpha at direction " +
                                        $"{directionIndex}, pixel " +
                                        $"({localX}, {localY}).");
                                }

                                AssertFacialChangeIsInsideMask(
                                    neutral,
                                    facial,
                                    faceMask,
                                    directionIndex,
                                    expressionIndex,
                                    localX,
                                    localY);

                                if (!ColorsEqual(neutral, facial))
                                {
                                    expressionChanges[expressionIndex]++;
                                }
                            }

                            for (int leftExpression = 0;
                                 leftExpression <
                                 PlayerSpriteRig.ExpressionCount;
                                 leftExpression++)
                            {
                                for (int rightExpression =
                                         leftExpression + 1;
                                     rightExpression <
                                     PlayerSpriteRig.ExpressionCount;
                                     rightExpression++)
                                {
                                    if (!ColorsEqual(
                                            facialPixels[leftExpression],
                                            facialPixels[rightExpression]))
                                    {
                                        pairDifferences[
                                            leftExpression,
                                            rightExpression]++;
                                    }
                                }
                            }
                        }
                    }

                    for (int expressionIndex = 1;
                         expressionIndex <
                         PlayerSpriteRig.ExpressionCount;
                         expressionIndex++)
                    {
                        int expectedChanges =
                            ExpectedExpressionChangedPixelCounts[
                                expressionIndex - 1][directionIndex];
                        Assert.That(
                            expressionChanges[expressionIndex],
                            Is.EqualTo(expectedChanges),
                            $"Expression {expressionIndex}, direction " +
                            $"{directionIndex} changed an unexpected " +
                            "pixel count.");
                    }

                    bool visibleFace = faceMask.width > 0;
                    for (int leftExpression = 0;
                         leftExpression <
                         PlayerSpriteRig.ExpressionCount;
                         leftExpression++)
                    {
                        for (int rightExpression = leftExpression + 1;
                             rightExpression <
                             PlayerSpriteRig.ExpressionCount;
                             rightExpression++)
                        {
                            Assert.That(
                                pairDifferences[
                                    leftExpression,
                                    rightExpression],
                                visibleFace
                                    ? Is.GreaterThan(0)
                                    : Is.EqualTo(0),
                                $"Expressions {leftExpression} and " +
                                $"{rightExpression}, direction " +
                                $"{directionIndex} violate the facial " +
                                "visibility contract.");
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parts);
                Object.DestroyImmediate(expressions);
            }
        }

        [Test]
        public void DirectionalAtlas_PreservesDirectionPpuAndPivotContract()
        {
            Assert.That(
                System.Enum.GetValues(typeof(PlayerViewDirection)),
                Has.Length.EqualTo(PlayerSpriteRig.DirectionCount));
            Assert.That(
                System.Enum.GetValues(typeof(PlayerPuppetPart)),
                Has.Length.EqualTo(PlayerSpriteRig.PartCount));
            Assert.That(PlayerSpriteRig.DirectionCount, Is.EqualTo(8));
            Assert.That(PlayerSpriteRig.PartCount, Is.EqualTo(9));
            Assert.That(PlayerSpriteRig.FrameWidth, Is.EqualTo(64));
            Assert.That(PlayerSpriteRig.FrameHeight, Is.EqualTo(96));
            Assert.That(PlayerSpriteRig.PixelsPerUnit, Is.EqualTo(48f));
            Assert.That(PlayerSpriteRig.FeetPivotXPixels, Is.EqualTo(32f));
            Assert.That(PlayerSpriteRig.FeetPivotPixels, Is.EqualTo(4f));
        }

        private static TextureImporter AssertImportContract(Texture2D atlas)
        {
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(atlas.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));
            Assert.That(atlas.isReadable, Is.False);

            string assetPath = AssetDatabase.GetAssetPath(atlas);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;

            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
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
                importer.spritePixelsPerUnit,
                Is.EqualTo(PlayerSpriteRig.PixelsPerUnit));
            Assert.That(
                importer.spritePivot.x,
                Is.EqualTo(
                    PlayerSpriteRig.FeetPivotXPixels /
                    PlayerSpriteRig.FrameWidth).Within(0.0001f));
            Assert.That(
                importer.spritePivot.y,
                Is.EqualTo(
                    PlayerSpriteRig.FeetPivotPixels /
                    PlayerSpriteRig.FrameHeight).Within(0.0001f));

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            Assert.That(standalone.overridden, Is.True);
            Assert.That(
                standalone.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(standalone.crunchedCompression, Is.False);

            return importer;
        }

        private static void AssertMatchingImportSettings(
            Texture2D reference,
            TextureImporter referenceImporter,
            Texture2D parts,
            TextureImporter partsImporter)
        {
            Assert.That(parts.filterMode, Is.EqualTo(reference.filterMode));
            Assert.That(parts.wrapMode, Is.EqualTo(reference.wrapMode));
            Assert.That(
                parts.mipmapCount,
                Is.EqualTo(reference.mipmapCount));
            Assert.That(parts.isReadable, Is.EqualTo(reference.isReadable));
            Assert.That(
                partsImporter.mipmapEnabled,
                Is.EqualTo(referenceImporter.mipmapEnabled));
            Assert.That(
                partsImporter.textureCompression,
                Is.EqualTo(referenceImporter.textureCompression));
            Assert.That(
                partsImporter.crunchedCompression,
                Is.EqualTo(referenceImporter.crunchedCompression));
            Assert.That(
                partsImporter.isReadable,
                Is.EqualTo(referenceImporter.isReadable));
        }

        private static Texture2D LoadReadableCopy(Texture2D imported)
        {
            Assert.That(imported, Is.Not.Null);
            string assetPath = AssetDatabase.GetAssetPath(imported);
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Texture2D readable = new Texture2D(
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

        private static Color32 GetTopLeftPixel(
            Color32[] pixels,
            int atlasWidth,
            int atlasHeight,
            PlayerViewDirection direction,
            int localX,
            int topY)
        {
            int x =
                ((int)direction * PlayerSpriteRig.FrameWidth) +
                localX;
            int y = atlasHeight - 1 - topY;
            return pixels[(y * atlasWidth) + x];
        }

        private static int CountOpaqueCellPixels(
            Color32[] pixels,
            int atlasWidth,
            int partIndex,
            int directionIndex)
        {
            int opaquePixels = 0;
            int cellX = directionIndex * PlayerSpriteRig.FrameWidth;
            int cellY = partIndex * PlayerSpriteRig.FrameHeight;

            for (int localY = 0;
                 localY < PlayerSpriteRig.FrameHeight;
                 localY++)
            {
                int rowStart =
                    ((cellY + localY) * atlasWidth) + cellX;
                for (int localX = 0;
                     localX < PlayerSpriteRig.FrameWidth;
                     localX++)
                {
                    byte alpha = pixels[rowStart + localX].a;
                    if (alpha != 0 && alpha != byte.MaxValue)
                    {
                        Assert.Fail(
                            $"Part {partIndex}, direction {directionIndex} " +
                            $"has non-binary alpha {alpha}.");
                    }

                    if (alpha == byte.MaxValue)
                    {
                        opaquePixels++;
                    }
                }
            }

            return opaquePixels;
        }

        private static Color32 GetAtlasCellPixel(
            Color32[] pixels,
            int atlasWidth,
            int rowIndex,
            int directionIndex,
            int localX,
            int localY)
        {
            int x =
                (directionIndex * PlayerSpriteRig.FrameWidth) +
                localX;
            int y =
                (rowIndex * PlayerSpriteRig.FrameHeight) +
                localY;
            return pixels[(y * atlasWidth) + x];
        }

        private static void AssertFacialChangeIsInsideMask(
            Color32 neutral,
            Color32 expression,
            RectInt faceMask,
            int directionIndex,
            int expressionIndex,
            int localX,
            int localY)
        {
            if (ColorsEqual(neutral, expression))
            {
                return;
            }

            int topY =
                PlayerSpriteRig.FrameHeight - 1 - localY;
            Assert.That(
                faceMask.Contains(new Vector2Int(localX, topY)),
                Is.True,
                $"Expression {expressionIndex}, direction " +
                $"{directionIndex} changed pixel " +
                $"({localX}, {topY}) outside its face mask.");
        }

        private static void AssertNeutralCompositeMatchesReference(
            Color32[] referencePixels,
            int referenceWidth,
            Color32[] partPixels,
            int partsWidth)
        {
            for (int directionIndex = 0;
                 directionIndex < PlayerSpriteRig.DirectionCount;
                 directionIndex++)
            {
                int frameX =
                    directionIndex * PlayerSpriteRig.FrameWidth;
                for (int localY = 0;
                     localY < PlayerSpriteRig.FrameHeight;
                     localY++)
                {
                    for (int localX = 0;
                         localX < PlayerSpriteRig.FrameWidth;
                         localX++)
                    {
                        Color32 composite =
                            new Color32(0, 0, 0, 0);
                        for (int partIndex = 0;
                             partIndex < PlayerSpriteRig.PartCount;
                             partIndex++)
                        {
                            int partX = frameX + localX;
                            int partY =
                                (partIndex *
                                 PlayerSpriteRig.FrameHeight) +
                                localY;
                            Color32 partPixel =
                                partPixels[
                                    (partY * partsWidth) + partX];
                            if (partPixel.a != 0)
                            {
                                composite = partPixel;
                            }
                        }

                        Color32 expected =
                            referencePixels[
                                (localY * referenceWidth) +
                                frameX +
                                localX];
                        if (!ColorsEqual(composite, expected))
                        {
                            Assert.Fail(
                                $"Neutral puppet composite differs at " +
                                $"direction {directionIndex}, " +
                                $"pixel ({localX}, {localY}). " +
                                $"Expected {expected}, got {composite}.");
                        }
                    }
                }
            }
        }

        private static bool ColorsEqual(Color32 left, Color32 right)
        {
            return left.r == right.r &&
                   left.g == right.g &&
                   left.b == right.b &&
                   left.a == right.a;
        }
    }
}
