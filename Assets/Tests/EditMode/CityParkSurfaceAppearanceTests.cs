using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityParkSurfaceAppearanceTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

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

        // The compensation contract is the linear rule from
        // tools/build-city-park-textures.py: below this channel value
        // the sRGB toe makes relative error meaningless, so only the
        // clamp check applies there.
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // The three CityDecorationWorldBuilder batch colours the four
        // park landmarks are drawn in. Texturing them changed no
        // colour, so these are the same values the flat parts had.
        // Declared before the table: static initialisers run in
        // declaration order, and a colour read too early is black.
        private static readonly Color Masonry =
            new Color(0.270f, 0.220f, 0.170f);
        private static readonly Color Residential =
            new Color(0.200f, 0.290f, 0.300f);
        private static readonly Color Street =
            new Color(0.100f, 0.120f, 0.130f);

        // Every flat colour the park builder passes per surface kind.
        // The generator derives its compensation from the same values,
        // so a palette edit in CityWorldBuilder or the decoration
        // builder fails here rather than silently shifting the park's
        // brightness.
        private static readonly
            Dictionary<CityParkSurfaceKind, Color[]> BuilderTints =
                new Dictionary<CityParkSurfaceKind, Color[]>
                {
                    [CityParkSurfaceKind.Lawn] = new[]
                    {
                        new Color(0.160f, 0.300f, 0.180f),
                    },
                    [CityParkSurfaceKind.Path] = new[]
                    {
                        new Color(0.390f, 0.340f, 0.240f),
                    },
                    [CityParkSurfaceKind.Plaza] = new[]
                    {
                        new Color(0.380f, 0.350f, 0.290f),
                    },
                    [CityParkSurfaceKind.Bark] = new[]
                    {
                        new Color(0.200f, 0.120f, 0.070f),
                    },
                    [CityParkSurfaceKind.Foliage] = new[]
                    {
                        new Color(0.120f, 0.270f, 0.150f),
                        new Color(0.100f, 0.240f, 0.130f),
                    },
                    [CityParkSurfaceKind.Timber] = new[]
                    {
                        new Color(0.380f, 0.220f, 0.100f),
                        Masonry,
                        Residential,
                        Street,
                    },
                    [CityParkSurfaceKind.Stone] = new[]
                    {
                        Masonry,
                        Street,
                    },
                    [CityParkSurfaceKind.PaintedMetal] = new[]
                    {
                        Residential,
                        Street,
                    },
                };

        [TestCase(
            (int)CityParkSurfaceKind.Lawn,
            "Textures/CityParkLawnAlbedo",
            3.0f,
            0.03f,
            0f,
            1.394f)]
        [TestCase(
            (int)CityParkSurfaceKind.Path,
            "Textures/CityParkPathAlbedo",
            2.2f,
            0.05f,
            0f,
            1.3965f)]
        [TestCase(
            (int)CityParkSurfaceKind.Plaza,
            "Textures/CityParkPlazaAlbedo",
            2.8f,
            0.06f,
            0f,
            1.391f)]
        [TestCase(
            (int)CityParkSurfaceKind.Bark,
            "Textures/CityParkBarkAlbedo",
            1.2f,
            0.04f,
            0f,
            1.486f)]
        [TestCase(
            (int)CityParkSurfaceKind.Foliage,
            "Textures/CityParkFoliageAlbedo",
            1.6f,
            0.04f,
            0f,
            1.3825f)]
        [TestCase(
            (int)CityParkSurfaceKind.Timber,
            "Textures/CityParkTimberAlbedo",
            1.4f,
            0.08f,
            0f,
            1.3345f)]
        [TestCase(
            (int)CityParkSurfaceKind.Stone,
            "Textures/CityParkStoneAlbedo",
            1.5f,
            0.05f,
            0f,
            1.3445f)]
        [TestCase(
            (int)CityParkSurfaceKind.PaintedMetal,
            "Textures/CityParkPaintedMetalAlbedo",
            1.2f,
            0.16f,
            0.15f,
            1.341f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            var kind = (CityParkSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                CityParkSurfaceAppearance.GetRecipe(kind);
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
                CityParkSurfaceAppearance.GetTexture(kind),
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
                "World-planar park UVs run far past 0..1.");
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            AssertTextureSourceHonoursContract(
                assetPath,
                kind,
                recipe.AlbedoCompensation);
        }

        /// <summary>
        /// Flat park ground projects straight down; upright park
        /// objects project per face. Getting this backwards is exactly
        /// the bug that would smear a hedge run into one stretched
        /// line of leaves.
        /// </summary>
        [TestCase(
            (int)CityParkSurfaceKind.Lawn,
            (int)RuntimeWorldUvMode.XZPlanar)]
        [TestCase(
            (int)CityParkSurfaceKind.Path,
            (int)RuntimeWorldUvMode.XZPlanar)]
        [TestCase(
            (int)CityParkSurfaceKind.Plaza,
            (int)RuntimeWorldUvMode.XZPlanar)]
        [TestCase(
            (int)CityParkSurfaceKind.Bark,
            (int)RuntimeWorldUvMode.BoxProjected)]
        [TestCase(
            (int)CityParkSurfaceKind.Foliage,
            (int)RuntimeWorldUvMode.BoxProjected)]
        [TestCase(
            (int)CityParkSurfaceKind.Timber,
            (int)RuntimeWorldUvMode.BoxProjected)]
        [TestCase(
            (int)CityParkSurfaceKind.Stone,
            (int)RuntimeWorldUvMode.BoxProjected)]
        [TestCase(
            (int)CityParkSurfaceKind.PaintedMetal,
            (int)RuntimeWorldUvMode.BoxProjected)]
        public void UvMode_MatchesSurfaceOrientation(
            int kindValue,
            int expectedModeValue)
        {
            Assert.That(
                CityParkSurfaceAppearance.GetUvMode(
                    (CityParkSurfaceKind)kindValue),
                Is.EqualTo((RuntimeWorldUvMode)expectedModeValue));
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void ApplyCombined_TintsWithoutCloningTheSharedMaterial(
            int kindValue)
        {
            var kind = (CityParkSurfaceKind)kindValue;
            var tint = new Color(0.19f, 0.27f, 0.17f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Park Surface Test Box",
                null,
                Vector3.zero,
                Vector3.one,
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CityParkSurfaceAppearance.ApplyCombined(
                    renderer,
                    kind,
                    tint);

                HomeSurfaceRecipe recipe =
                    CityParkSurfaceAppearance.GetRecipe(kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityParkSurfaceAppearance.GetTexture(kind)));
                Color display = properties.GetColor(BaseColorId);
                Assert.That(
                    display.r,
                    Is.EqualTo(tint.r * recipe.AlbedoCompensation)
                        .Within(0.0001f));
                Assert.That(
                    display.g,
                    Is.EqualTo(tint.g * recipe.AlbedoCompensation)
                        .Within(0.0001f));
                Assert.That(
                    display.b,
                    Is.EqualTo(tint.b * recipe.AlbedoCompensation)
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
        public void BuildParkLawn_CarriesTheTurfSheetAtItsOwnPitch()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Park Lawn Surface Test");
            try
            {
                GameObject lawn = CityWorldBuilder.BuildParkLawn(
                    parent.transform,
                    layout);
                Assert.That(
                    lawn,
                    Is.Not.Null,
                    "The standard city has a Central Park.");

                Renderer renderer = lawn.GetComponent<Renderer>();
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityParkSurfaceAppearance.GetTexture(
                            CityParkSurfaceKind.Lawn)));

                // The mesh, not a UV transform, carries the pitch:
                // one tile per lawn recipe metre of world XZ.
                Mesh mesh = lawn.GetComponent<MeshFilter>().sharedMesh;
                float metersPerTile = CityParkSurfaceAppearance
                    .GetRecipe(CityParkSurfaceKind.Lawn)
                    .MetersPerTile;
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                Assert.That(uvs, Has.Length.EqualTo(vertices.Length));
                for (int index = 0; index < vertices.Length; index++)
                {
                    Assert.That(
                        uvs[index].x,
                        Is.EqualTo(vertices[index].x / metersPerTile)
                            .Within(0.0005f));
                    Assert.That(
                        uvs[index].y,
                        Is.EqualTo(vertices[index].z / metersPerTile)
                            .Within(0.0005f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BuildPark_TexturesEveryPlazaTreeAndBench()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Park Surface Test");
            try
            {
                GameObject park = CityWorldBuilder.BuildPark(
                    parent.transform,
                    layout,
                    null);
                Assert.That(park, Is.Not.Null);

                var seen = new HashSet<CityParkSurfaceKind>();
                foreach (Renderer renderer in
                         park.GetComponentsInChildren<Renderer>(true))
                {
                    CityParkSurfaceKind kind = ResolveExpectedKind(
                        renderer.name);
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Assert.That(
                        properties.GetTexture(BaseMapId),
                        Is.SameAs(
                            CityParkSurfaceAppearance.GetTexture(kind)),
                        $"{renderer.name} must carry the {kind} sheet.");
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            RuntimePrimitiveFactory.DefaultMaterial),
                        "Park surfaces share one material.");
                    seen.Add(kind);
                }

                Assert.That(
                    seen,
                    Is.SupersetOf(
                        new[]
                        {
                            CityParkSurfaceKind.Plaza,
                            CityParkSurfaceKind.Bark,
                            CityParkSurfaceKind.Foliage,
                            CityParkSurfaceKind.Timber,
                        }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The four park landmarks live in the city-wide decoration
        /// layer, which is otherwise flat. Only their parts may opt
        /// into a park sheet: if a batch named "Park ..." appears with
        /// the wrong sheet, or a neighbouring district's batch picks
        /// one up, this fails.
        /// </summary>
        [Test]
        public void BuildDecorations_TexturesOnlyTheParkLandmarks()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            CityDecorationPlan plan = CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
            var parent = new GameObject("Park Decoration Surface Test");
            try
            {
                GameObject root = CityDecorationWorldBuilder.Build(
                    parent.transform,
                    layout,
                    plan);

                var parkTextures =
                    new Dictionary<Texture, CityParkSurfaceKind>();
                foreach (CityParkSurfaceKind kind in
                         new[]
                         {
                             CityParkSurfaceKind.Stone,
                             CityParkSurfaceKind.Timber,
                             CityParkSurfaceKind.PaintedMetal,
                         })
                {
                    parkTextures.Add(
                        CityParkSurfaceAppearance.GetTexture(kind),
                        kind);
                }

                var seen = new HashSet<CityParkSurfaceKind>();
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    bool named = renderer.name.StartsWith(
                        "Park ",
                        StringComparison.Ordinal) ||
                        renderer.name.StartsWith(
                            "Imported Park ",
                            StringComparison.Ordinal);
                    if (texture == null)
                    {
                        Assert.That(
                            named,
                            Is.False,
                            $"'{renderer.name}' is a park batch with " +
                            "no sheet.");
                        continue;
                    }

                    Assert.That(
                        named,
                        Is.True,
                        $"'{renderer.name}' is not a park landmark " +
                        "batch but carries a texture; the rest of the " +
                        "decoration layer stays flat.");
                    Assert.That(
                        parkTextures.ContainsKey(texture),
                        Is.True,
                        $"'{renderer.name}' carries a sheet that is " +
                        "not one of the park's.");
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            RuntimePrimitiveFactory.DefaultMaterial));
                    seen.Add(parkTextures[texture]);
                }

                // The standard layout plants both park landmarks, so
                // stone (fountain basin, pedestal, figure, bandstand
                // plinth) and the bandstand's timber and painted metal
                // must all be present.
                Assert.That(
                    seen,
                    Is.SupersetOf(
                        new[]
                        {
                            CityParkSurfaceKind.Stone,
                            CityParkSurfaceKind.Timber,
                            CityParkSurfaceKind.PaintedMetal,
                        }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// Every renderer the park builder can produce, mapped to the
        /// sheet it must carry. An unknown name fails the test: a new
        /// park object has to join a sheet rather than ship flat.
        /// </summary>
        private static CityParkSurfaceKind ResolveExpectedKind(
            string rendererName)
        {
            if (rendererName.StartsWith(
                    "Park Plaza ",
                    StringComparison.Ordinal))
            {
                return CityParkSurfaceKind.Plaza;
            }

            switch (rendererName)
            {
                case "Park Tree Trunks":
                case "Imported Park Tree Trunks":
                    return CityParkSurfaceKind.Bark;
                case "Park Tree Canopies":
                case "Imported Park Tree Canopies":
                case "Park Boundary Hedges":
                    return CityParkSurfaceKind.Foliage;
                case "Park Benches":
                case "Imported Park Benches":
                    return CityParkSurfaceKind.Timber;
                default:
                    Assert.Fail(
                        $"Untextured park object '{rendererName}'; " +
                        "every park surface belongs to a sheet.");
                    return CityParkSurfaceKind.Lawn;
            }
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (object value in
                     Enum.GetValues(typeof(CityParkSurfaceKind)))
            {
                yield return (int)value;
            }
        }

        private static void AssertTextureSourceHonoursContract(
            string assetPath,
            CityParkSurfaceKind kind,
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
                    edgeDelta += ChannelDelta(
                        pixels[y * source.width],
                        pixels[y * source.width + source.width - 1]);
                }

                for (int x = 0; x < source.width; x++)
                {
                    edgeDelta += ChannelDelta(
                        pixels[x],
                        pixels[
                            (source.height - 1) * source.width + x]);
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
                    $"{kind} albedo contrast is too subtle for the " +
                    "PS1 downsample.");

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
        // the flat colour had, measured through the same linear
        // multiply URP performs. Channels in the sRGB toe are held to
        // the clamp check only.
        private static void AssertCompensationPreservesBuilderTints(
            CityParkSurfaceKind kind,
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
