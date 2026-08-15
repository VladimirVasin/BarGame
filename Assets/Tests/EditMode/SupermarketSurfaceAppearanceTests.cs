using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketSurfaceAppearanceTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        // The compensation contract is the linear rule from
        // tools/build-supermarket-textures.py: below this channel value
        // the sRGB toe makes relative error meaningless, so only the
        // clamp check applies there.
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour SupermarketInteriorWorldBuilder passes per
        // surface kind. The generator derives its compensation from the
        // same values, so a palette edit in the builder fails here
        // rather than silently shifting the hall's brightness.
        private static readonly Dictionary<SupermarketSurfaceKind, Color[]>
            BuilderTints =
                new Dictionary<SupermarketSurfaceKind, Color[]>
                {
                    [SupermarketSurfaceKind.Linoleum] = new[]
                    {
                        new Color(0.255f, 0.285f, 0.265f),
                        new Color(0.165f, 0.185f, 0.170f),
                    },
                    [SupermarketSurfaceKind.WallPaint] = new[]
                    {
                        new Color(0.56f, 0.57f, 0.47f),
                        new Color(0.275f, 0.305f, 0.265f),
                    },
                    [SupermarketSurfaceKind.Ceiling] = new[]
                    {
                        new Color(0.40f, 0.43f, 0.38f),
                    },
                    [SupermarketSurfaceKind.ShelfMetal] = new[]
                    {
                        new Color(0.34f, 0.36f, 0.31f),
                        new Color(0.47f, 0.48f, 0.39f),
                        new Color(0.38f, 0.47f, 0.45f),
                        new Color(0.245f, 0.315f, 0.305f),
                    },
                    [SupermarketSurfaceKind.Counter] = new[]
                    {
                        new Color(0.31f, 0.38f, 0.32f),
                        new Color(0.67f, 0.61f, 0.38f),
                    },
                    [SupermarketSurfaceKind.Cardboard] = new[]
                    {
                        new Color(0.44f, 0.34f, 0.20f),
                        new Color(0.38f, 0.29f, 0.17f),
                    },
                };

        [TestCase(
            (int)SupermarketSurfaceKind.Linoleum,
            "Supermarket/Textures/SupermarketLinoleumAlbedo",
            2.4f,
            0.16f,
            0f,
            1.421f)]
        [TestCase(
            (int)SupermarketSurfaceKind.WallPaint,
            "Supermarket/Textures/SupermarketWallPaintAlbedo",
            2.6f,
            0.05f,
            0f,
            1.36f)]
        [TestCase(
            (int)SupermarketSurfaceKind.Ceiling,
            "Supermarket/Textures/SupermarketCeilingAlbedo",
            3.0f,
            0.04f,
            0f,
            1.3555f)]
        [TestCase(
            (int)SupermarketSurfaceKind.ShelfMetal,
            "Supermarket/Textures/SupermarketShelfMetalAlbedo",
            1.3f,
            0.24f,
            0.25f,
            1.39f)]
        [TestCase(
            (int)SupermarketSurfaceKind.Counter,
            "Supermarket/Textures/SupermarketCounterAlbedo",
            1.2f,
            0.20f,
            0f,
            1.3775f)]
        [TestCase(
            (int)SupermarketSurfaceKind.Cardboard,
            "Supermarket/Textures/SupermarketCardboardAlbedo",
            0.9f,
            0.03f,
            0f,
            1.407f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            SupermarketSurfaceKind kind =
                (SupermarketSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                SupermarketSurfaceAppearance.GetRecipe(kind);
            Assert.That(
                recipe.ResourcePath,
                Is.EqualTo(expectedResourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(expectedMetersPerTile));
            Assert.That(
                recipe.Smoothness,
                Is.EqualTo(expectedSmoothness));
            Assert.That(
                recipe.Metallic,
                Is.EqualTo(expectedMetallic));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(expectedAlbedoCompensation));

            Texture2D resource = Resources.Load<Texture2D>(
                expectedResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                SupermarketSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));
            Assert.That(resource.isReadable, Is.False);

            string assetPath = AssetDatabase.GetAssetPath(resource);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(
                importer.filterMode,
                Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.anisoLevel, Is.EqualTo(4));
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.isReadable, Is.False);
            AssertTextureSourceHonoursContract(
                assetPath,
                kind,
                recipe.AlbedoCompensation);
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void Apply_CompensatesTintAndAddsSharedMaterialProperties(
            int kindValue)
        {
            SupermarketSurfaceKind kind =
                (SupermarketSurfaceKind)kindValue;
            Color tint = new Color(0.24f, 0.28f, 0.22f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Stable Supermarket Surface",
                null,
                new Vector3(1.25f, 1.5f, -0.5f),
                new Vector3(3f, 2.5f, 0.2f),
                tint,
                false);

            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                Vector4 expectedTransform =
                    SupermarketSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        kind,
                        SurfaceProjection.BoxXY);

                SupermarketSurfaceAppearance.Apply(
                    renderer,
                    kind,
                    SurfaceProjection.BoxXY,
                    tint);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                HomeSurfaceRecipe recipe =
                    SupermarketSurfaceAppearance.GetRecipe(kind);
                Color displayTint =
                    SupermarketSurfaceAppearance.CreateDisplayTint(
                        tint,
                        kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        SupermarketSurfaceAppearance.GetTexture(kind)));
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(expectedTransform));
                Assert.That(expectedTransform.x, Is.GreaterThan(0f));
                Assert.That(expectedTransform.y, Is.GreaterThan(0f));
                AssertColorApproximatelyEqual(
                    properties.GetColor(BaseColorId),
                    displayTint);
                AssertColorApproximatelyEqual(
                    properties.GetColor(ColorId),
                    displayTint);
                Assert.That(displayTint.r, Is.GreaterThan(tint.r));
                Assert.That(displayTint.g, Is.GreaterThan(tint.g));
                Assert.That(displayTint.b, Is.GreaterThan(tint.b));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(recipe.Smoothness).Within(0.0001f));
                Assert.That(
                    properties.GetFloat(MetallicId),
                    Is.EqualTo(recipe.Metallic).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void BaseMapTransform_DiffersFromHomeForSameTransform()
        {
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Shared Hash Surface",
                null,
                new Vector3(0.5f, 1.5f, 2.5f),
                new Vector3(2f, 2f, 0.2f),
                Color.white,
                false);

            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                Vector4 marketTransform =
                    SupermarketSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        SupermarketSurfaceKind.WallPaint,
                        SurfaceProjection.BoxXY);
                Vector4 homeTransform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        HomeSurfaceKind.Wallpaper,
                        SurfaceProjection.BoxXY);

                Assert.That(
                    new Vector2(marketTransform.z, marketTransform.w),
                    Is.Not.EqualTo(
                        new Vector2(homeTransform.z, homeTransform.w)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void WorldBuilder_TexturesTheHallSurfaces()
        {
            var parent = new GameObject("Supermarket Appearance Test");
            try
            {
                SupermarketInteriorLayoutPlan plan =
                    SupermarketInteriorLayoutPlanner.Generate(20260815);
                SupermarketInteriorWorldBuilder.Build(
                    parent.transform,
                    plan,
                    null);

                var expectedTextures = new HashSet<Texture>();
                foreach (SupermarketSurfaceKind kind in
                         Enum.GetValues(typeof(SupermarketSurfaceKind)))
                {
                    expectedTextures.Add(
                        SupermarketSurfaceAppearance.GetTexture(kind));
                }

                var seenTextures = new HashSet<Texture>();
                int texturedRendererCount = 0;
                Renderer[] renderers = parent
                    .GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture =
                        properties.GetTexture(BaseMapId);
                    if (texture == null ||
                        !expectedTextures.Contains(texture))
                    {
                        continue;
                    }

                    texturedRendererCount++;
                    Vector4 transform =
                        properties.GetVector(BaseMapTransformId);
                    Assert.That(
                        transform.x,
                        Is.GreaterThan(0f),
                        $"Invalid U tiling on {renderer.name}.");
                    Assert.That(
                        transform.y,
                        Is.GreaterThan(0f),
                        $"Invalid V tiling on {renderer.name}.");
                    seenTextures.Add(texture);
                }

                Assert.That(
                    texturedRendererCount,
                    Is.GreaterThanOrEqualTo(30),
                    "The hall's big surfaces must carry the packaged " +
                    "sheets.");
                Assert.That(
                    seenTextures,
                    Is.EquivalentTo(expectedTextures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void AssertColorApproximatelyEqual(
            Color actual,
            Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (SupermarketSurfaceKind kind in Enum.GetValues(
                         typeof(SupermarketSurfaceKind)))
            {
                yield return (int)kind;
            }
        }

        private static void AssertTextureSourceHonoursContract(
            string assetPath,
            SupermarketSurfaceKind kind,
            float albedoCompensation)
        {
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                $"{kind} albedo must use opaque RGB PNG storage.");

            var source = new Texture2D(
                2,
                2,
                TextureFormat.RGB24,
                false,
                true);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(source, pngBytes, false),
                    Is.True);
                Assert.That(source.width, Is.EqualTo(source.height));
                Assert.That(
                    source.width,
                    Is.GreaterThanOrEqualTo(512));

                Color32[] pixels = source.GetPixels32();
                long edgeDelta = 0L;
                for (int y = 0; y < source.height; y++)
                {
                    Color32 left = pixels[y * source.width];
                    Color32 right = pixels[
                        y * source.width + source.width - 1];
                    edgeDelta += ChannelDelta(left, right);
                }

                for (int x = 0; x < source.width; x++)
                {
                    Color32 top = pixels[x];
                    Color32 bottom = pixels[
                        (source.height - 1) * source.width + x];
                    edgeDelta += ChannelDelta(top, bottom);
                }

                double meanChannelDelta =
                    edgeDelta /
                    ((source.width + source.height) * 3.0);
                Assert.That(
                    meanChannelDelta,
                    Is.LessThanOrEqualTo(16.0),
                    $"{kind} albedo edges diverge too much for Repeat " +
                    "sampling.");

                var redHistogram = new int[256];
                var greenHistogram = new int[256];
                var blueHistogram = new int[256];
                var luminanceHistogram = new int[256];
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 sample = pixels[index];
                    redHistogram[sample.r]++;
                    greenHistogram[sample.g]++;
                    blueHistogram[sample.b]++;
                    int luminance = Mathf.Clamp(
                        Mathf.RoundToInt(
                            sample.r * 0.2126f +
                            sample.g * 0.7152f +
                            sample.b * 0.0722f),
                        0,
                        255);
                    luminanceHistogram[luminance]++;
                }

                int lowLuminance = FindHistogramPercentile(
                    luminanceHistogram,
                    pixels.Length,
                    0.05f);
                int highLuminance = FindHistogramPercentile(
                    luminanceHistogram,
                    pixels.Length,
                    0.95f);
                Assert.That(
                    highLuminance - lowLuminance,
                    Is.GreaterThanOrEqualTo(40),
                    $"{kind} albedo contrast is too subtle for the PS1 " +
                    "downsample.");

                double meanLinearLuminance =
                    HistogramLinearMean(redHistogram, pixels.Length) *
                    0.2126 +
                    HistogramLinearMean(greenHistogram, pixels.Length) *
                    0.7152 +
                    HistogramLinearMean(blueHistogram, pixels.Length) *
                    0.0722;
                AssertCompensationPreservesBuilderTints(
                    kind,
                    albedoCompensation,
                    meanLinearLuminance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        // The linear rule shared with the generator: a builder tint
        // multiplied by the compensated sheet must keep the brightness
        // the flat colour had, measured through the same linear multiply
        // URP performs. Channels in the sRGB toe are held to the clamp
        // check only.
        private static void AssertCompensationPreservesBuilderTints(
            SupermarketSurfaceKind kind,
            float compensation,
            double meanLinearLuminance)
        {
            Assert.That(
                BuilderTints.ContainsKey(kind),
                Is.True,
                $"No builder tint table entry for {kind}.");

            bool sawEligibleChannel = false;
            foreach (Color tint in BuilderTints[kind])
            {
                foreach (float channel in
                         new[] { tint.r, tint.g, tint.b })
                {
                    Assert.That(
                        channel * compensation,
                        Is.LessThanOrEqualTo(1.0001f),
                        $"{kind} compensation clamps a builder tint " +
                        "channel and would crush its hue.");
                    if (channel < TintChannelFloor)
                    {
                        continue;
                    }

                    sawEligibleChannel = true;
                    double compensated = SrgbToLinear(
                        Math.Min(1.0, channel * (double)compensation));
                    double error = Math.Abs(
                        compensated *
                        meanLinearLuminance /
                        SrgbToLinear(channel) -
                        1.0);
                    Assert.That(
                        error,
                        Is.LessThanOrEqualTo(BrightnessErrorLimit),
                        $"{kind} compensation shifts a builder tint's " +
                        $"brightness by {error * 100.0:F1}%.");
                }
            }

            Assert.That(sawEligibleChannel, Is.True);
        }

        private static double HistogramLinearMean(
            int[] histogram,
            int pixelCount)
        {
            double total = 0.0;
            for (int value = 0; value < histogram.Length; value++)
            {
                total += histogram[value] * SrgbToLinear(value / 255.0);
            }

            return total / pixelCount;
        }

        private static double SrgbToLinear(double value)
        {
            if (value <= 0.04045)
            {
                return value / 12.92;
            }

            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static int ChannelDelta(
            Color32 first,
            Color32 second)
        {
            return Mathf.Abs(first.r - second.r) +
                   Mathf.Abs(first.g - second.g) +
                   Mathf.Abs(first.b - second.b);
        }

        private static int FindHistogramPercentile(
            int[] histogram,
            int sampleCount,
            float percentile)
        {
            int target = Mathf.CeilToInt(sampleCount * percentile);
            int cumulative = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                cumulative += histogram[value];
                if (cumulative >= target)
                {
                    return value;
                }
            }

            return histogram.Length - 1;
        }
    }
}
