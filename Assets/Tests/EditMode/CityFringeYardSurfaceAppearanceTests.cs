using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the fringe-yard appearance chain from generated PNG through its
    /// measured manifest and Unity importer to the shared-material property
    /// block used by separate and combined geometry.
    /// </summary>
    public sealed class CityFringeYardSurfaceAppearanceTests
    {
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.08;

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

        private static readonly Dictionary<
            CityFringeYardSurfaceKind,
            Color> SourceTints =
                new Dictionary<CityFringeYardSurfaceKind, Color>
                {
                    [CityFringeYardSurfaceKind.ServiceTrack] =
                        new Color(0.300f, 0.275f, 0.215f),
                    [CityFringeYardSurfaceKind.Concrete] =
                        new Color(0.285f, 0.315f, 0.305f),
                    [CityFringeYardSurfaceKind.Masonry] =
                        new Color(0.335f, 0.350f, 0.325f),
                };

        private static SheetManifest manifest;

        [OneTimeSetUp]
        public void LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "ArtSource",
                "City",
                "fringe-textures.json"));
            Assert.That(
                File.Exists(path),
                Is.True,
                $"Missing the measured fringe contract at {path}.");
            manifest = JsonUtility.FromJson<SheetManifest>(
                File.ReadAllText(path));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.sheetSize, Is.EqualTo(1024));
            Assert.That(manifest.runtimeImportSize, Is.EqualTo(512));
            Assert.That(manifest.sheets, Has.Length.EqualTo(3));
        }

        [TestCase(
            (int)CityFringeYardSurfaceKind.ServiceTrack,
            "CityFringeServiceTrackAlbedo")]
        [TestCase(
            (int)CityFringeYardSurfaceKind.Concrete,
            "CityFringeConcreteAlbedo")]
        [TestCase(
            (int)CityFringeYardSurfaceKind.Masonry,
            "CityFringeMasonryAlbedo")]
        public void Recipe_MatchesMeasuredSheetAndRuntimeImport(
            int kindValue,
            string expectedKey)
        {
            var kind = (CityFringeYardSurfaceKind)kindValue;
            SheetRecord record = FindRecord(expectedKey);
            HomeSurfaceRecipe recipe =
                CityFringeYardSurfaceAppearance.GetRecipe(kind);

            Assert.That(recipe.ResourcePath, Is.EqualTo(record.resourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(record.metersPerTile).Within(0.0001f));
            Assert.That(
                recipe.Smoothness,
                Is.EqualTo(record.smoothness).Within(0.0001f));
            Assert.That(
                recipe.Metallic,
                Is.EqualTo(record.metallic).Within(0.0001f));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(record.albedoCompensation).Within(0.0001f),
                $"{expectedKey} was regenerated without updating its recipe.");

            Texture2D resource = Resources.Load<Texture2D>(
                recipe.ResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                CityFringeYardSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));

            string assetPath = AssetDatabase.GetAssetPath(resource);
            var importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                $"{expectedKey} must use opaque RGB PNG storage.");
            Assert.That(
                Sha256(pngBytes),
                Is.EqualTo(record.sha256),
                $"{expectedKey} differs from its measured manifest.");

            AssertCompensationPreservesSourceTint(kind, record);
        }

        [TestCase((int)CityFringeYardSurfaceKind.ServiceTrack, 4f)]
        [TestCase((int)CityFringeYardSurfaceKind.Concrete, 3f)]
        [TestCase((int)CityFringeYardSurfaceKind.Masonry, 2.4f)]
        public void Apply_UsesMetreTilingAndSharedMaterial(
            int kindValue,
            float expectedPitch)
        {
            var kind = (CityFringeYardSurfaceKind)kindValue;
            Color tint = SourceTints[kind];
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Fringe Surface Test Box",
                null,
                Vector3.zero,
                new Vector3(4f, 0.18f, 12f),
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CityFringeYardSurfaceAppearance.Apply(renderer, kind, tint);

                AssertCommonProperties(renderer, kind, tint);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Vector4 transform =
                    properties.GetVector(BaseMapTransformId);
                Assert.That(
                    transform.x,
                    Is.EqualTo(4f / expectedPitch).Within(0.0005f));
                Assert.That(
                    transform.y,
                    Is.EqualTo(12f / expectedPitch).Within(0.0005f));
                Assert.That(transform.z, Is.InRange(0f, 1f));
                Assert.That(transform.w, Is.InRange(0f, 1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        [TestCase((int)CityFringeYardSurfaceKind.ServiceTrack)]
        [TestCase((int)CityFringeYardSurfaceKind.Concrete)]
        [TestCase((int)CityFringeYardSurfaceKind.Masonry)]
        public void ApplyCombined_UsesBakedUvsAndSharedMaterial(int kindValue)
        {
            var kind = (CityFringeYardSurfaceKind)kindValue;
            Color tint = SourceTints[kind];
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Combined Fringe Surface Test Box",
                null,
                Vector3.zero,
                new Vector3(7f, 0.2f, 16f),
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CityFringeYardSurfaceAppearance.ApplyCombined(
                    renderer,
                    kind,
                    tint);

                AssertCommonProperties(renderer, kind, tint);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(Vector4.zero),
                    "Combined meshes keep their baked metre-scale UVs.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        private static void AssertCommonProperties(
            Renderer renderer,
            CityFringeYardSurfaceKind kind,
            Color tint)
        {
            HomeSurfaceRecipe recipe =
                CityFringeYardSurfaceAppearance.GetRecipe(kind);
            Assert.That(
                renderer.sharedMaterial,
                Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(CityFringeYardSurfaceAppearance.GetTexture(kind)));

            Color display = properties.GetColor(BaseColorId);
            Color expected =
                CityFringeYardSurfaceAppearance.CreateDisplayTint(tint, kind);
            AssertColor(display, expected);
            AssertColor(properties.GetColor(ColorId), expected);
            Assert.That(
                properties.GetFloat(SmoothnessId),
                Is.EqualTo(recipe.Smoothness).Within(0.0001f));
            Assert.That(
                properties.GetFloat(MetallicId),
                Is.EqualTo(recipe.Metallic).Within(0.0001f));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static void AssertCompensationPreservesSourceTint(
            CityFringeYardSurfaceKind kind,
            SheetRecord record)
        {
            Color tint = SourceTints[kind];
            bool sawEligibleChannel = false;
            foreach (float channel in new[] { tint.r, tint.g, tint.b })
            {
                Assert.That(
                    channel * record.albedoCompensation,
                    Is.LessThanOrEqualTo(1.0001f),
                    $"{kind} compensation clamps its authored tint.");
                if (channel < TintChannelFloor)
                {
                    continue;
                }

                sawEligibleChannel = true;
                double compensated = SrgbToLinear(Math.Min(
                    1.0,
                    channel * (double)record.albedoCompensation));
                double error = Math.Abs(
                    compensated *
                    record.meanLinearLuminance /
                    SrgbToLinear(channel) -
                    1.0);
                Assert.That(
                    error,
                    Is.LessThanOrEqualTo(BrightnessErrorLimit),
                    $"{kind} shifts brightness by {error * 100.0:F1}%.");
            }

            Assert.That(sawEligibleChannel, Is.True);
        }

        private static SheetRecord FindRecord(string key)
        {
            foreach (SheetRecord record in manifest.sheets)
            {
                if (string.Equals(record.key, key, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            Assert.Fail($"The fringe contract has no sheet '{key}'.");
            return null;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                var text = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString(
                        "X2",
                        CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        private static double SrgbToLinear(double value)
        {
            if (value <= 0.04045)
            {
                return value / 12.92;
            }

            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        [Serializable]
        private sealed class SheetManifest
        {
            public int sheetSize;
            public int runtimeImportSize;
            public SheetRecord[] sheets;
        }

        [Serializable]
        private sealed class SheetRecord
        {
            public string key;
            public string resourcePath;
            public float meanLinearLuminance;
            public float albedoCompensation;
            public float metersPerTile;
            public float smoothness;
            public float metallic;
            public string sha256;
        }
    }
}
