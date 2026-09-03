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
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour named by the generated surface manifest; the
        // generator solved each compensation against these same values.
        private static readonly Dictionary<BarSurfaceKind, Color[]>
            BuilderTints = new Dictionary<BarSurfaceKind, Color[]>
            {
                [BarSurfaceKind.WornPlank] = new[]
                {
                    new Color(0.14f, 0.06f, 0.042f),
                    new Color(0.16f, 0.055f, 0.028f),
                },
                [BarSurfaceKind.Wallpaper] = new[]
                {
                    new Color(0.29f, 0.075f, 0.075f),
                    new Color(0.13f, 0.042f, 0.032f),
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
                [BarSurfaceKind.CeilingPlaster] = new[]
                {
                    new Color(0.18f, 0.14f, 0.11f),
                },
                [BarSurfaceKind.PolishedWood] = new[]
                {
                    new Color(0.16f, 0.055f, 0.028f),
                    new Color(0.22f, 0.095f, 0.045f),
                },
                [BarSurfaceKind.AgedBrass] = new[]
                {
                    new Color(0.62f, 0.34f, 0.13f),
                    new Color(0.86f, 0.46f, 0.14f),
                    new Color(0.32f, 0.40f, 0.38f),
                    new Color(0.52f, 0.18f, 0.44f),
                },
                [BarSurfaceKind.MirrorGlass] = new[]
                {
                    new Color(0.22f, 0.34f, 0.38f),
                },
                [BarSurfaceKind.PatternedGlass] = new[]
                {
                    new Color(0.18f, 0.28f, 0.30f),
                },
                [BarSurfaceKind.PubCarpet] = new[]
                {
                    new Color(0.22f, 0.055f, 0.052f),
                    new Color(0.36f, 0.27f, 0.10f),
                    new Color(0.16f, 0.065f, 0.055f),
                },
                [BarSurfaceKind.WornFabric] = new[]
                {
                    new Color(0.30f, 0.035f, 0.045f),
                    new Color(0.32f, 0.18f, 0.14f),
                },
                [BarSurfaceKind.PaintedMetal] = new[]
                {
                    new Color(0.18f, 0.20f, 0.19f),
                    new Color(0.12f, 0.12f, 0.13f),
                },
                [BarSurfaceKind.Paper] = new[]
                {
                    new Color(0.74f, 0.66f, 0.47f),
                    new Color(0.72f, 0.66f, 0.48f),
                    new Color(0.62f, 0.38f, 0.20f),
                },
                [BarSurfaceKind.BottleGlass] = new[]
                {
                    new Color(0.28f, 0.20f, 0.11f),
                    new Color(0.62f, 0.82f, 0.86f),
                    new Color(0.90f, 0.58f, 0.18f),
                },
                [BarSurfaceKind.Ceramic] = new[]
                {
                    new Color(0.82f, 0.12f, 0.10f),
                    new Color(0.74f, 0.62f, 0.42f),
                },
            };

        [TestCase(
            (int)BarSurfaceKind.WornPlank,
            "WornPlank",
            "Bar/Textures/BarWornPlankAlbedo",
            1.5f,
            0.08f,
            0f,
            1.4575f)]
        [TestCase(
            (int)BarSurfaceKind.Wallpaper,
            "Wallpaper",
            "Bar/Textures/BarWallpaperAlbedo",
            1.8f,
            0.04f,
            0f,
            1.433f)]
        [TestCase(
            (int)BarSurfaceKind.DarkWood,
            "DarkWood",
            "Bar/Textures/BarDarkWoodAlbedo",
            1.1f,
            0.12f,
            0f,
            1.4495f)]
        [TestCase(
            (int)BarSurfaceKind.WornLeather,
            "WornLeather",
            "Bar/Textures/BarWornLeatherAlbedo",
            0.9f,
            0.06f,
            0f,
            1.396f)]
        [TestCase(
            (int)BarSurfaceKind.CeilingPlaster,
            "CeilingPlaster",
            "Bar/Textures/BarCeilingPlasterAlbedo",
            2.4f,
            0.025f,
            0f,
            1.408f)]
        [TestCase(
            (int)BarSurfaceKind.PolishedWood,
            "PolishedWood",
            "Bar/Textures/BarPolishedWoodAlbedo",
            0.75f,
            0.34f,
            0f,
            1.436f)]
        [TestCase(
            (int)BarSurfaceKind.AgedBrass,
            "AgedBrass",
            "Bar/Textures/BarAgedBrassAlbedo",
            0.42f,
            0.42f,
            0.72f,
            1.1625f)]
        [TestCase(
            (int)BarSurfaceKind.MirrorGlass,
            "MirrorGlass",
            "Bar/Textures/BarMirrorGlassAlbedo",
            1.35f,
            0.78f,
            0.12f,
            1.263f)]
        [TestCase(
            (int)BarSurfaceKind.PatternedGlass,
            "PatternedGlass",
            "Bar/Textures/BarPatternedGlassAlbedo",
            0.72f,
            0.64f,
            0.04f,
            1.316f)]
        [TestCase(
            (int)BarSurfaceKind.PubCarpet,
            "PubCarpet",
            "Bar/Textures/BarPubCarpetAlbedo",
            1.15f,
            0.015f,
            0f,
            1.3605f)]
        [TestCase(
            (int)BarSurfaceKind.WornFabric,
            "WornFabric",
            "Bar/Textures/BarWornFabricAlbedo",
            0.68f,
            0.02f,
            0f,
            1.455f)]
        [TestCase(
            (int)BarSurfaceKind.PaintedMetal,
            "PaintedMetal",
            "Bar/Textures/BarPaintedMetalAlbedo",
            0.82f,
            0.20f,
            0.30f,
            1.3715f)]
        [TestCase(
            (int)BarSurfaceKind.Paper,
            "Paper",
            "Bar/Textures/BarPaperAlbedo",
            0.55f,
            0.025f,
            0f,
            1.1865f)]
        [TestCase(
            (int)BarSurfaceKind.BottleGlass,
            "BottleGlass",
            "Bar/Textures/BarBottleGlassAlbedo",
            0.36f,
            0.68f,
            0.02f,
            1.111f)]
        [TestCase(
            (int)BarSurfaceKind.Ceramic,
            "Ceramic",
            "Bar/Textures/BarCeramicAlbedo",
            0.42f,
            0.48f,
            0.04f,
            1.202f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string sheet,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            BarSurfaceKind kind = (BarSurfaceKind)kindValue;
            Assert.That(
                BarSurfaceAppearance.TryResolveSheet(
                    sheet,
                    out BarSurfaceKind resolvedKind),
                Is.True);
            Assert.That(resolvedKind, Is.EqualTo(kind));
            HomeSurfaceRecipe recipe =
                BarSurfaceAppearance.GetRecipe(kind);
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
        public void ApplyAuthored_PreservesBakedUvsAndRecipeResponse()
        {
            GameObject target = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            try
            {
                Renderer renderer = target.GetComponent<Renderer>();
                Color sourceTint = new Color(0.18f, 0.28f, 0.30f);
                BarSurfaceAppearance.ApplyAuthored(
                    renderer,
                    BarSurfaceKind.PatternedGlass,
                    sourceTint);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        BarSurfaceAppearance.GetTexture(
                            BarSurfaceKind.PatternedGlass)));
                Assert.That(
                    properties.GetVector(BaseMapTransformId),
                    Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
                Color actualTint = properties.GetColor(BaseColorId);
                Color expectedTint =
                    BarSurfaceAppearance.CreateDisplayTint(
                        sourceTint,
                        BarSurfaceKind.PatternedGlass);
                Assert.That(
                    actualTint.r,
                    Is.EqualTo(expectedTint.r).Within(0.00001f));
                Assert.That(
                    actualTint.g,
                    Is.EqualTo(expectedTint.g).Within(0.00001f));
                Assert.That(
                    actualTint.b,
                    Is.EqualTo(expectedTint.b).Within(0.00001f));
                Assert.That(
                    actualTint.a,
                    Is.EqualTo(expectedTint.a).Within(0.00001f));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(0.64f));
                Assert.That(
                    properties.GetFloat(MetallicId),
                    Is.EqualTo(0.04f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [TestCase(CityDistrictKind.OldTown)]
        [TestCase(CityDistrictKind.Residential)]
        [TestCase(CityDistrictKind.Industrial)]
        [TestCase(CityDistrictKind.Nightlife)]
        public void WorldBuilder_TexturesEveryDistrictIdentity(
            CityDistrictKind district)
        {
            var parent = new GameObject("Bar Surface Test");
            try
            {
                BarInteriorLayoutPlan plan =
                    BarInteriorLayoutPlanner.Generate(
                        20260816,
                        $"bar-surface-{district}",
                        BarActivityKind.Cocktail,
                        district);
                Transform room =
                    BarInteriorWorldBuilder.Build(
                        parent.transform,
                        plan);
                Assert.That(
                    CountTexturedRenderers(room),
                    Is.GreaterThanOrEqualTo(12),
                    $"The {district} identity must retain authored sheets.");
                for (int lightIndex = 0;
                     lightIndex < plan.LightAnchors.Count;
                     lightIndex++)
                {
                    AssertTransformTexture(
                        room.Find($"Practical Cable {lightIndex + 1}"),
                        BarSurfaceKind.PaintedMetal);
                    AssertTransformTexture(
                        room.Find($"Practical Shade {lightIndex + 1}"),
                        BarSurfaceKind.PaintedMetal);
                }

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
                    Is.EqualTo(BarJukeboxInteraction.PromptKeyName));
                Assert.That(
                    jukebox.GetComponentsInChildren<Collider>(true),
                    Has.Length.GreaterThanOrEqualTo(2),
                    "The jukebox needs its solid and trigger.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ServicePropFactory_BindsAuthoredMenuSheets()
        {
            var parent = new GameObject("Bar Service Surface Test");
            try
            {
                BarServicePropInstance menu =
                    BarServicePropFactory.CreateMenu(parent.transform);
                AssertRoleTexture(
                    menu,
                    "service_menu_cover",
                    BarSurfaceKind.DarkWood);
                AssertRoleTexture(
                    menu,
                    "service_menu_pages",
                    BarSurfaceKind.Paper);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void AssertRoleTexture(
            BarServicePropInstance instance,
            string role,
            BarSurfaceKind expectedKind)
        {
            Assert.That(
                instance.TryGetRenderer(role, out Renderer renderer),
                Is.True,
                $"The service pack must publish '{role}'.");
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(BarSurfaceAppearance.GetTexture(expectedKind)));
            Assert.That(
                properties.GetVector(BaseMapTransformId),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
        }

        private static void AssertTransformTexture(
            Transform target,
            BarSurfaceKind expectedKind)
        {
            Assert.That(target, Is.Not.Null);
            Renderer renderer = target.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null, target.name);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(BarSurfaceAppearance.GetTexture(expectedKind)),
                target.name);
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
