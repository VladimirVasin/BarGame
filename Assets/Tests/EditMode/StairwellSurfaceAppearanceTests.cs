using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellSurfaceAppearanceTests
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

        [TestCase(
            (int)StairwellSurfaceKind.WallPaint,
            "Stairwell/Textures/StairwellWallPaintAlbedoV2",
            2.4f,
            0.07f,
            0f,
            2.52f)]
        [TestCase(
            (int)StairwellSurfaceKind.Concrete,
            "Stairwell/Textures/StairwellConcreteAlbedo",
            2.8f,
            0.06f,
            0f,
            2.51f)]
        [TestCase(
            (int)StairwellSurfaceKind.StairConcrete,
            "Stairwell/Textures/StairwellStairConcreteAlbedo",
            1.6f,
            0.10f,
            0f,
            2.69f)]
        [TestCase(
            (int)StairwellSurfaceKind.CorrodedMetal,
            "Stairwell/Textures/StairwellCorrodedMetalAlbedoV2",
            1.2f,
            0.16f,
            0.28f,
            3.98f)]
        [TestCase(
            (int)StairwellSurfaceKind.DoorPaint,
            "Stairwell/Textures/StairwellDoorPaintAlbedoV2",
            1.5f,
            0.12f,
            0.12f,
            3.13f)]
        [TestCase(
            (int)StairwellSurfaceKind.Damage,
            "Stairwell/Textures/StairwellDamageAlbedo",
            1.25f,
            0.05f,
            0f,
            2.17f)]
        [TestCase(
            (int)StairwellSurfaceKind.DirtyWood,
            "Stairwell/Textures/StairwellDirtyWoodAlbedo",
            1.1f,
            0.07f,
            0f,
            3.54f)]
        [TestCase(
            (int)StairwellSurfaceKind.Debris,
            "Stairwell/Textures/StairwellDebrisAlbedo",
            0.8f,
            0.08f,
            0f,
            3.50f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            StairwellSurfaceKind kind =
                (StairwellSurfaceKind)kindValue;
            StairwellSurfaceRecipe recipe =
                StairwellSurfaceAppearance.GetRecipe(kind);
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
                StairwellSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(
                StairwellSurfaceAppearance.GetTexture(kind),
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
            Assert.That(
                importer.npotScale,
                Is.EqualTo(TextureImporterNPOTScale.None));
            AssertTextureSourceIsOpaqueAndTileable(
                assetPath,
                kind.ToString(),
                recipe.AlbedoCompensation);
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void Apply_CompensatesTintAndAddsSharedMaterialProperties(
            int kindValue)
        {
            StairwellSurfaceKind kind =
                (StairwellSurfaceKind)kindValue;
            Color tint = new Color(0.08f, 0.12f, 0.06f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Stable Surface",
                null,
                new Vector3(1.25f, 2.5f, -3.75f),
                new Vector3(4f, 2f, 0.25f),
                tint,
                false);

            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                int preservedId =
                    Shader.PropertyToID("_StairwellPreservedTest");
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(preservedId, 0.42f);
                renderer.SetPropertyBlock(properties);
                Vector4 expectedTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        kind,
                        StairwellSurfaceProjection.BoxXY);

                StairwellSurfaceAppearance.Apply(
                    renderer,
                    kind,
                    StairwellSurfaceProjection.BoxXY,
                    tint);

                properties.Clear();
                renderer.GetPropertyBlock(properties);
                StairwellSurfaceRecipe recipe =
                    StairwellSurfaceAppearance.GetRecipe(kind);
                Color displayTint =
                    StairwellSurfaceAppearance.CreateDisplayTint(
                        tint,
                        kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        StairwellSurfaceAppearance.GetTexture(kind)));
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
                Assert.That(
                    properties.GetFloat(preservedId),
                    Is.EqualTo(0.42f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
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

        [Test]
        public void BaseMapTransform_IsStableForMatchingObjects()
        {
            GameObject first = RuntimePrimitiveFactory.CreateBox(
                "Matching Surface",
                null,
                new Vector3(1.2f, 3.4f, 5.6f),
                new Vector3(6f, 2f, 0.2f),
                Color.white,
                false);
            GameObject second = RuntimePrimitiveFactory.CreateBox(
                "Matching Surface",
                null,
                new Vector3(1.2f, 3.4f, 5.6f),
                new Vector3(6f, 2f, 0.2f),
                Color.white,
                false);

            try
            {
                Vector4 firstTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        first.GetComponent<Renderer>(),
                        StairwellSurfaceKind.WallPaint,
                        StairwellSurfaceProjection.BoxXY);
                Vector4 secondTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        second.GetComponent<Renderer>(),
                        StairwellSurfaceKind.WallPaint,
                        StairwellSurfaceProjection.BoxXY);

                Assert.That(secondTransform, Is.EqualTo(firstTransform));
                Assert.That(firstTransform.x, Is.EqualTo(2.5f));
                Assert.That(
                    firstTransform.y,
                    Is.EqualTo(2f / 2.4f).Within(0.0001f));
                Assert.That(firstTransform.z, Is.InRange(0f, 1f));
                Assert.That(firstTransform.w, Is.InRange(0f, 1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CylinderSideProjection_UsesCircumferenceAndLength()
        {
            GameObject cylinder = RuntimePrimitiveFactory.CreateCylinder(
                "Tall Pipe",
                null,
                Vector3.zero,
                new Vector3(0.5f, 3f, 0.5f),
                Color.white,
                false);

            try
            {
                Vector4 transform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        cylinder.GetComponent<Renderer>(),
                        StairwellSurfaceKind.CorrodedMetal,
                        StairwellSurfaceProjection.CylinderSide);

                Assert.That(
                    transform.x,
                    Is.EqualTo(Mathf.PI * 0.5f / 1.2f)
                        .Within(0.0001f));
                Assert.That(
                    transform.y,
                    Is.EqualTo(6f / 1.2f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cylinder);
            }
        }

        [Test]
        public void StairTopProjection_IgnoresCumulativeStepHeight()
        {
            StairwellFlightPlan flight =
                StairwellLayoutPlanner.Generate().LowerFlight;
            GameObject early = RuntimePrimitiveFactory.CreateBox(
                "Early Stair",
                null,
                flight.GetStepCenter(0),
                flight.GetStepSize(0),
                Color.white,
                false);
            GameObject late = RuntimePrimitiveFactory.CreateBox(
                "Late Stair",
                null,
                flight.GetStepCenter(flight.StepCount - 1),
                flight.GetStepSize(flight.StepCount - 1),
                Color.white,
                false);

            try
            {
                Vector4 earlyTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        early.GetComponent<Renderer>(),
                        StairwellSurfaceKind.StairConcrete,
                        StairwellSurfaceProjection.BoxXZ);
                Vector4 lateTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        late.GetComponent<Renderer>(),
                        StairwellSurfaceKind.StairConcrete,
                        StairwellSurfaceProjection.BoxXZ);

                Assert.That(
                    lateTransform.x,
                    Is.EqualTo(earlyTransform.x).Within(0.0001f));
                Assert.That(
                    lateTransform.y,
                    Is.EqualTo(earlyTransform.y).Within(0.0001f));
                Assert.That(
                    earlyTransform.x,
                    Is.EqualTo(flight.Width / 1.6f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(early);
                UnityEngine.Object.DestroyImmediate(late);
            }
        }

        [Test]
        public void WorldBuilder_TexturesEveryEnabledOrdinaryRenderer()
        {
            var parent = new GameObject("Stairwell Appearance Test");
            try
            {
                StairwellWorldResult world = StairwellWorldBuilder.Build(
                    parent.transform,
                    StairwellLayoutPlanner.Generate());
                var expectedTextures = new HashSet<Texture>();
                foreach (StairwellSurfaceKind kind in AllSurfaceKinds())
                {
                    expectedTextures.Add(
                        StairwellSurfaceAppearance.GetTexture(kind));
                }

                var seenTextures = new HashSet<Texture>();
                int ordinaryRendererCount = 0;
                Renderer[] renderers =
                    world.Root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    if (renderer.sharedMaterial !=
                        RuntimePrimitiveFactory.DefaultMaterial)
                    {
                        Assert.That(
                            renderer.gameObject.name,
                            Does.Contain("Fluorescent Tube")
                                .Or.Contain("Fluorescent Halo"));
                        continue;
                    }

                    ordinaryRendererCount++;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    Vector4 transform =
                        properties.GetVector(BaseMapTransformId);
                    Assert.That(
                        texture,
                        Is.Not.Null,
                        $"Missing surface texture on {renderer.name}.");
                    Assert.That(
                        expectedTextures.Contains(texture),
                        Is.True,
                        $"Unexpected surface texture on {renderer.name}.");
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

                Assert.That(ordinaryRendererCount, Is.GreaterThan(0));
                Assert.That(
                    seenTextures,
                    Is.EquivalentTo(expectedTextures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static IEnumerable<StairwellSurfaceKind> AllSurfaceKinds()
        {
            foreach (StairwellSurfaceKind kind in Enum.GetValues(
                         typeof(StairwellSurfaceKind)))
            {
                yield return kind;
            }
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (StairwellSurfaceKind kind in AllSurfaceKinds())
            {
                yield return (int)kind;
            }
        }

        private static void AssertTextureSourceIsOpaqueAndTileable(
            string assetPath,
            string surfaceName,
            float albedoCompensation)
        {
            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                $"{surfaceName} albedo must use opaque RGB PNG storage.");

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
                Assert.That(source.width, Is.GreaterThanOrEqualTo(512));

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

                var luminanceHistogram = new int[256];
                double linearLuminanceSum = 0.0;
                int sampleCount = 0;
                const int sampleStride = 8;
                for (int y = 0; y < source.height; y += sampleStride)
                {
                    for (int x = 0; x < source.width; x += sampleStride)
                    {
                        Color32 sample = pixels[y * source.width + x];
                        int luminance = Mathf.Clamp(
                            Mathf.RoundToInt(
                                sample.r * 0.2126f +
                                sample.g * 0.7152f +
                                sample.b * 0.0722f),
                            0,
                            255);
                        luminanceHistogram[luminance]++;
                        linearLuminanceSum +=
                            Mathf.GammaToLinearSpace(sample.r / 255f) *
                            0.2126 +
                            Mathf.GammaToLinearSpace(sample.g / 255f) *
                            0.7152 +
                            Mathf.GammaToLinearSpace(sample.b / 255f) *
                            0.0722;
                        sampleCount++;
                    }
                }

                int lowLuminance = FindHistogramPercentile(
                    luminanceHistogram,
                    sampleCount,
                    0.05f);
                int highLuminance = FindHistogramPercentile(
                    luminanceHistogram,
                    sampleCount,
                    0.95f);
                Assert.That(
                    highLuminance - lowLuminance,
                    Is.GreaterThanOrEqualTo(24),
                    $"{surfaceName} albedo contrast is too subtle " +
                    "for the PS1 downsample.");
                double compensatedMean =
                    linearLuminanceSum /
                    sampleCount *
                    albedoCompensation;
                Assert.That(
                    compensatedMean,
                    Is.EqualTo(1.0).Within(0.08),
                    $"{surfaceName} tint compensation no longer " +
                    "preserves the pre-texture mean brightness.");

                double meanChannelDelta =
                    edgeDelta /
                    ((source.width + source.height) * 3.0);
                Assert.That(
                    meanChannelDelta,
                    Is.LessThanOrEqualTo(16.0),
                    $"{surfaceName} albedo edges diverge too much " +
                    "for Repeat sampling.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
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
