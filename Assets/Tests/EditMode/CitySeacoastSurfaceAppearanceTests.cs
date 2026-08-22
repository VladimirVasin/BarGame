using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySeacoastSurfaceAppearanceTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour the seacoast builder passes per surface
        // kind, plus the beach's own sand tint. The generator derives
        // its compensation from the same values, so a palette edit in
        // CitySeacoastWorldBuilder fails here rather than silently
        // shifting the shore's brightness.
        private static readonly
            Dictionary<CitySeacoastSurfaceKind, Color[]> BuilderTints =
                new Dictionary<CitySeacoastSurfaceKind, Color[]>
                {
                    [CitySeacoastSurfaceKind.Sand] = new[]
                    {
                        new Color(0.520f, 0.450f, 0.300f),
                    },
                    [CitySeacoastSurfaceKind.Concrete] = new[]
                    {
                        new Color(0.290f, 0.290f, 0.270f),
                    },
                    [CitySeacoastSurfaceKind.Granite] = new[]
                    {
                        new Color(0.340f, 0.340f, 0.320f),
                    },
                    [CitySeacoastSurfaceKind.Plank] = new[]
                    {
                        new Color(0.310f, 0.280f, 0.240f),
                        new Color(0.160f, 0.140f, 0.120f),
                    },
                    [CitySeacoastSurfaceKind.Hull] = new[]
                    {
                        new Color(0.260f, 0.320f, 0.310f),
                        new Color(0.130f, 0.120f, 0.110f),
                    },
                };

        [TestCase(
            (int)CitySeacoastSurfaceKind.Sand,
            "Textures/CitySeacoastSandAlbedo",
            2.6f,
            0.03f,
            0f,
            1.3825f)]
        [TestCase(
            (int)CitySeacoastSurfaceKind.Concrete,
            "Textures/CitySeacoastConcreteAlbedo",
            2.2f,
            0.05f,
            0f,
            1.401f)]
        [TestCase(
            (int)CitySeacoastSurfaceKind.Granite,
            "Textures/CityRiverQuayAlbedo",
            2.2f,
            0.06f,
            0f,
            1.404f)]
        [TestCase(
            (int)CitySeacoastSurfaceKind.Plank,
            "Textures/CitySeacoastPlankAlbedo",
            1.2f,
            0.06f,
            0f,
            1.4355f)]
        [TestCase(
            (int)CitySeacoastSurfaceKind.Hull,
            "Textures/CitySeacoastHullAlbedo",
            1.6f,
            0.12f,
            0f,
            1.439f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            var kind = (CitySeacoastSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                CitySeacoastSurfaceAppearance.GetRecipe(kind);
            Assert.That(
                recipe.ResourcePath,
                Is.EqualTo(expectedResourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(expectedMetersPerTile));
            Assert.That(
                recipe.Smoothness,
                Is.EqualTo(expectedSmoothness));
            Assert.That(recipe.Metallic, Is.EqualTo(expectedMetallic));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(expectedAlbedoCompensation));

            Texture2D resource = Resources.Load<Texture2D>(
                expectedResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                CitySeacoastSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));

            string assetPath = AssetDatabase.GetAssetPath(resource);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat),
                "World-planar seacoast UVs run far past 0..1.");
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            AssertCompensationPreservesBuilderTints(
                assetPath,
                kind,
                recipe.AlbedoCompensation);
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void ApplyCombined_TintsWithoutCloningTheSharedMaterial(
            int kindValue)
        {
            var kind = (CitySeacoastSurfaceKind)kindValue;
            var tint = new Color(0.31f, 0.28f, 0.24f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Seacoast Surface Test Box",
                null,
                Vector3.zero,
                Vector3.one,
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CitySeacoastSurfaceAppearance.ApplyCombined(
                    renderer,
                    kind,
                    tint);

                HomeSurfaceRecipe recipe =
                    CitySeacoastSurfaceAppearance.GetRecipe(kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CitySeacoastSurfaceAppearance.GetTexture(kind)));
                Color display = properties.GetColor(BaseColorId);
                Assert.That(
                    display.r,
                    Is.EqualTo(tint.r * recipe.AlbedoCompensation)
                        .Within(0.0001f));
                Assert.That(
                    properties.GetColor(ColorId),
                    Is.EqualTo(display));
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
        public void SeaWaterNormal_ExistsAndImportsLinear()
        {
            Texture2D normal = Resources.Load<Texture2D>(
                CitySeaResources.RippleTextureResourcePath);
            Assert.That(normal, Is.Not.Null);

            string assetPath = AssetDatabase.GetAssetPath(normal);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.sRGBTexture,
                Is.False,
                "A derivative map read as sRGB tilts every slope.");
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat));
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (object value in
                     Enum.GetValues(typeof(CitySeacoastSurfaceKind)))
            {
                yield return (int)value;
            }
        }

        private static void AssertCompensationPreservesBuilderTints(
            string assetPath,
            CitySeacoastSurfaceKind kind,
            float compensation)
        {
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
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
                Color32[] pixels = source.GetPixels32();
                var redHistogram = new int[256];
                var greenHistogram = new int[256];
                var blueHistogram = new int[256];
                for (int index = 0; index < pixels.Length; index++)
                {
                    redHistogram[pixels[index].r]++;
                    greenHistogram[pixels[index].g]++;
                    blueHistogram[pixels[index].b]++;
                }

                double meanLinearLuminance =
                    HistogramLinearMean(redHistogram, pixels.Length) *
                    0.2126 +
                    HistogramLinearMean(greenHistogram, pixels.Length) *
                    0.7152 +
                    HistogramLinearMean(blueHistogram, pixels.Length) *
                    0.0722;

                bool sawEligibleChannel = false;
                foreach (Color tint in BuilderTints[kind])
                {
                    foreach (float channel in
                             new[] { tint.r, tint.g, tint.b })
                    {
                        Assert.That(
                            channel * compensation,
                            Is.LessThanOrEqualTo(1.0001f),
                            $"{kind} compensation clamps a builder " +
                            "tint channel and would crush its hue.");
                        if (channel < TintChannelFloor)
                        {
                            continue;
                        }

                        sawEligibleChannel = true;
                        double compensated = SrgbToLinear(
                            Math.Min(
                                1.0,
                                channel * (double)compensation));
                        double error = Math.Abs(
                            compensated *
                            meanLinearLuminance /
                            SrgbToLinear(channel) -
                            1.0);
                        Assert.That(
                            error,
                            Is.LessThanOrEqualTo(BrightnessErrorLimit),
                            $"{kind} compensation shifts a builder " +
                            $"tint's brightness by {error * 100.0:F1}%.");
                    }
                }

                Assert.That(sawEligibleChannel, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
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
    }
}
