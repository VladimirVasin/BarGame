using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarSurfaceAppearanceTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");

        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour BarInteriorWorldBuilder passes per worn
        // surface kind; the generator solved its compensation against
        // these same values.
        private static readonly Dictionary<BarSurfaceKind, Color[]>
            BuilderTints = new Dictionary<BarSurfaceKind, Color[]>
            {
                [BarSurfaceKind.WornPlank] = new[]
                {
                    new Color(0.14f, 0.06f, 0.042f),
                    new Color(0.075f, 0.024f, 0.017f),
                },
                [BarSurfaceKind.Wallpaper] = new[]
                {
                    new Color(0.29f, 0.075f, 0.075f),
                },
                [BarSurfaceKind.DarkWood] = new[]
                {
                    new Color(0.075f, 0.024f, 0.017f),
                    new Color(0.16f, 0.055f, 0.028f),
                },
                [BarSurfaceKind.WornLeather] = new[]
                {
                    new Color(0.30f, 0.035f, 0.045f),
                },
            };

        [TestCase(
            (int)BarSurfaceKind.WornPlank,
            "Bar/Textures/BarWornPlankAlbedo",
            1.5f,
            1.4575f)]
        [TestCase(
            (int)BarSurfaceKind.Wallpaper,
            "Bar/Textures/BarWallpaperAlbedo",
            1.8f,
            1.433f)]
        [TestCase(
            (int)BarSurfaceKind.DarkWood,
            "Bar/Textures/BarDarkWoodAlbedo",
            1.1f,
            1.4495f)]
        [TestCase(
            (int)BarSurfaceKind.WornLeather,
            "Bar/Textures/BarWornLeatherAlbedo",
            0.9f,
            1.396f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedAlbedoCompensation)
        {
            BarSurfaceKind kind = (BarSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                BarSurfaceAppearance.GetRecipe(kind);
            Assert.That(
                recipe.ResourcePath,
                Is.EqualTo(expectedResourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(expectedMetersPerTile));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(expectedAlbedoCompensation));

            Texture2D resource = Resources.Load<Texture2D>(
                expectedResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                BarSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));

            string assetPath = AssetDatabase.GetAssetPath(resource);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            AssertCompensationPreservesBuilderTints(
                assetPath,
                kind,
                recipe.AlbedoCompensation);
        }

        [Test]
        public void WorldBuilder_TexturesOnlyTheWornIdentity()
        {
            var parent = new GameObject("Bar Surface Test");
            try
            {
                BarInteriorLayoutPlan worn =
                    BarInteriorLayoutPlanner.Generate(
                        20260816,
                        "bar-worn-test",
                        BarActivityKind.Cocktail,
                        CityDistrictKind.Residential);
                Transform wornRoom =
                    BarInteriorWorldBuilder.Build(
                        parent.transform,
                        worn);
                Assert.That(
                    CountTexturedRenderers(wornRoom),
                    Is.GreaterThanOrEqualTo(12),
                    "The worn identity must dress the big surfaces.");

                BarInteriorLayoutPlan shared =
                    BarInteriorLayoutPlanner.Generate(
                        20260816,
                        "bar-shared-test",
                        BarActivityKind.Cocktail,
                        CityDistrictKind.Nightlife);
                Transform sharedRoom =
                    BarInteriorWorldBuilder.Build(
                        parent.transform,
                        shared);
                Assert.That(
                    CountTexturedRenderers(sharedRoom),
                    Is.Zero,
                    "Identities without the worn set keep flat tints.");

                foreach (Transform room in
                         new[] { wornRoom, sharedRoom })
                {
                    Transform jukebox = room.Find("Bar Jukebox");
                    Assert.That(
                        jukebox,
                        Is.Not.Null,
                        "Every bar carries the jukebox.");
                    BarJukeboxInteraction interaction =
                        jukebox.GetComponentInChildren<
                            BarJukeboxInteraction>();
                    Assert.That(interaction, Is.Not.Null);
                    Assert.That(
                        interaction.PromptKey,
                        Is.EqualTo(
                            BarJukeboxInteraction.PromptKeyName));
                    Assert.That(
                        jukebox.GetComponentsInChildren<Collider>(true),
                        Has.Length.GreaterThanOrEqualTo(2),
                        "The jukebox needs its solid and trigger.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static int CountTexturedRenderers(Transform room)
        {
            var expected = new HashSet<Texture>();
            foreach (BarSurfaceKind kind in
                     Enum.GetValues(typeof(BarSurfaceKind)))
            {
                expected.Add(BarSurfaceAppearance.GetTexture(kind));
            }

            int count = 0;
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in
                     room.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(properties);
                Texture texture = properties.GetTexture(BaseMapId);
                if (texture != null && expected.Contains(texture))
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertCompensationPreservesBuilderTints(
            string assetPath,
            BarSurfaceKind kind,
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
                double linearSum = 0.0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 sample = pixels[index];
                    linearSum +=
                        SrgbToLinear(sample.r / 255.0) * 0.2126 +
                        SrgbToLinear(sample.g / 255.0) * 0.7152 +
                        SrgbToLinear(sample.b / 255.0) * 0.0722;
                }

                double mean = linearSum / pixels.Length;
                bool sawEligibleChannel = false;
                foreach (Color tint in BuilderTints[kind])
                {
                    foreach (float channel in
                             new[] { tint.r, tint.g, tint.b })
                    {
                        Assert.That(
                            channel * compensation,
                            Is.LessThanOrEqualTo(1.0001f));
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
                            compensated * mean /
                            SrgbToLinear(channel) -
                            1.0);
                        Assert.That(
                            error,
                            Is.LessThanOrEqualTo(
                                BrightnessErrorLimit),
                            $"{kind} shifts a builder tint by " +
                            $"{error * 100.0:F1}%.");
                    }
                }

                Assert.That(sawEligibleChannel, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
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
    }
}
