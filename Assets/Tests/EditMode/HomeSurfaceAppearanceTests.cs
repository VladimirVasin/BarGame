using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeSurfaceAppearanceTests
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
        // tools/build-home-textures.py: below this channel value the sRGB
        // toe makes relative error meaningless, so only the clamp check
        // applies there.
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour the home builders pass per surface kind,
        // transcribed from the seven Home* builders. The generator derives
        // its compensation constants from the same values, so a palette
        // edit in a builder fails here rather than silently shifting the
        // room's brightness.
        private static readonly Dictionary<HomeSurfaceKind, Color[]>
            BuilderTints = new Dictionary<HomeSurfaceKind, Color[]>
            {
                [HomeSurfaceKind.Wallpaper] = new[]
                {
                    new Color(0.255f, 0.225f, 0.175f),
                    new Color(0.37f, 0.27f, 0.16f),
                    new Color(0.255f, 0.235f, 0.205f),
                },
                [HomeSurfaceKind.CeilingPlaster] = new[]
                {
                    new Color(0.16f, 0.14f, 0.11f),
                },
                [HomeSurfaceKind.PlankFloor] = new[]
                {
                    new Color(0.115f, 0.085f, 0.065f),
                    new Color(0.22f, 0.27f, 0.25f),
                },
                [HomeSurfaceKind.DarkWood] = new[]
                {
                    new Color(0.115f, 0.055f, 0.036f),
                    new Color(0.19f, 0.105f, 0.065f),
                    new Color(0.08f, 0.16f, 0.15f),
                    new Color(0.12f, 0.14f, 0.12f),
                    new Color(0.25f, 0.14f, 0.08f),
                    new Color(0.20f, 0.11f, 0.065f),
                    new Color(0.10f, 0.095f, 0.08f),
                    new Color(0.10f, 0.055f, 0.035f),
                    new Color(0.19f, 0.105f, 0.060f),
                    new Color(0.16f, 0.12f, 0.075f),
                },
                [HomeSurfaceKind.WornLaminate] = new[]
                {
                    new Color(0.37f, 0.27f, 0.16f),
                    new Color(0.16f, 0.18f, 0.16f),
                },
                [HomeSurfaceKind.Upholstery] = new[]
                {
                    new Color(0.14f, 0.20f, 0.18f),
                    new Color(0.17f, 0.255f, 0.23f),
                    new Color(0.18f, 0.235f, 0.20f),
                    new Color(0.105f, 0.14f, 0.13f),
                },
                [HomeSurfaceKind.BedLinen] = new[]
                {
                    new Color(0.43f, 0.39f, 0.29f),
                    new Color(0.47f, 0.43f, 0.34f),
                    new Color(0.38f, 0.36f, 0.22f),
                    new Color(0.42f, 0.40f, 0.26f),
                },
                [HomeSurfaceKind.BathroomTile] = new[]
                {
                    new Color(0.28f, 0.31f, 0.28f),
                    new Color(0.17f, 0.21f, 0.18f),
                },
                [HomeSurfaceKind.Enamel] = new[]
                {
                    new Color(0.49f, 0.50f, 0.42f),
                    new Color(0.31f, 0.33f, 0.29f),
                    new Color(0.33f, 0.31f, 0.24f),
                    new Color(0.54f, 0.55f, 0.43f),
                    new Color(0.69f, 0.70f, 0.57f),
                    new Color(0.27f, 0.29f, 0.23f),
                    new Color(0.70f, 0.73f, 0.64f),
                    new Color(0.34f, 0.38f, 0.34f),
                    new Color(0.49f, 0.57f, 0.55f),
                    new Color(0.22f, 0.27f, 0.25f),
                    new Color(0.32f, 0.38f, 0.36f),
                },
                [HomeSurfaceKind.PaintedMetal] = new[]
                {
                    new Color(0.18f, 0.25f, 0.25f),
                    new Color(0.40f, 0.17f, 0.075f),
                    new Color(0.38f, 0.16f, 0.075f),
                    new Color(0.30f, 0.31f, 0.27f),
                },
                [HomeSurfaceKind.Concrete] = new[]
                {
                    new Color(0.20f, 0.22f, 0.19f),
                    new Color(0.23f, 0.23f, 0.20f),
                    new Color(0.255f, 0.225f, 0.175f),
                },
                [HomeSurfaceKind.Rug] = new[]
                {
                    new Color(0.22f, 0.075f, 0.065f),
                },
            };

        // Deliberately flat-tinted renderers: decal overlays that paint
        // variation onto a textured parent, and props too small to read a
        // 512 albedo. Exact object names, or prefixes for numbered runs.
        private static readonly HashSet<string> FlatTintExemptNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Home Exit Door Patch",
                "Home Main Lamp Shade",
                "Home Main Lamp Socket",
                "Home Main Lamp Wire",
                "Home Main Lamp Ceiling Rose",
                "Home Bathroom Cold Fixture",
                "Home Entry Door Lamp Housing",
                "Home Entry Door Lamp Hood",
                "Home Camera Corner Suitcase",
                "Home Sofa Tear",
                "Home Bathroom Toilet Flush",
                "Home Bathroom Shower Head",
                "Home Bathroom Shower Head Neck",
                "Home Bathroom Shower Head Face",
                "Home Bathroom Shower Drain",
                "Home Bathroom Shower Mixer Handle Hot",
                "Home Bathroom Shower Mixer Handle Cold",
                "Home Bathroom Shower Soap",
                "Home Bathroom Sink Hollow",
                "Home Bathroom Cracked Mirror",
                "Home Bathroom Mirror Crack",
                "Home Bathroom Leak Stain",
                "Home Bathroom Floor Drain",
                "Home Dead Window",
                "Home Back Wall Damp",
                "Home Left Wall Peel",
                "Home Table Crushed Can",
                "Home Table Ashtray",
                "Home Table Stale Plate",
                "Home Kitchen Dirty Dishes",
                "Home Kitchen Wet Rag",
                "Home Kitchen Tin",
                "Home Radio Dial",
                "Home Faded Photograph",
                "Home Old Calendar",
                "Home Pill Blister",
                "Home Cardboard Box",
                "Home Floor Can",
                "Home Lower Facade Damp Stain",
                "Home Balcony Drain",
                "Home Balcony Door Handle",
                "Home Refrigerator Lower Rust Bloom",
                "Home Refrigerator Lower Grime Line",
                "Home Refrigerator Side Rust Run",
                "Home Refrigerator Freezer Plate",
                "Home Refrigerator Freezer Lip",
                "Home Refrigerator Lower Drawer Bottom",
                "Home Refrigerator Lower Drawer Left",
                "Home Refrigerator Lower Drawer Right",
                "Home Refrigerator Lower Drawer Handle",
                "Home Refrigerator Interior Spill",
                "Home Refrigerator Door Vertical Rust Run",
                "Home Refrigerator Door Chipped Corner",
                "Home Refrigerator Yellowed Note",
                "Home Refrigerator Note Pencil Mark",
                "Home Refrigerator Crooked Magnet",
                "Home Refrigerator Upper Hinge",
                "Home Refrigerator Lower Hinge",
            };

        private static readonly string[] FlatTintExemptPrefixes =
        {
            "Home Table Green Bottle",
            "Home Table Brown Bottle",
            "Home Kitchen Empty Bottle",
            "Home Hidden Bed Bottle",
            "Home Worn Slipper",
            "Home Balcony Ashtray",
            "Home Refrigerator Foot",
            "Home Refrigerator Frost Patch",
            "Home Refrigerator Interior Drip",
            "Home Refrigerator Door Gasket",
            "Home Refrigerator Vent Slat",
            "Home Refrigerator Handle",
            "Home Refrigerator Door Bin",
        };

        [TestCase(
            (int)HomeSurfaceKind.Wallpaper,
            "Home/Textures/HomeWallpaperAlbedo",
            1.9f,
            0.05f,
            0f,
            1.414f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.CeilingPlaster,
            "Home/Textures/HomeCeilingPlasterAlbedo",
            2.8f,
            0.04f,
            0f,
            1.401f,
            24)]
        [TestCase(
            (int)HomeSurfaceKind.PlankFloor,
            "Home/Textures/HomePlankFloorAlbedo",
            1.6f,
            0.10f,
            0f,
            1.5225f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.DarkWood,
            "Home/Textures/HomeDarkWoodAlbedo",
            1.1f,
            0.12f,
            0f,
            1.43f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.WornLaminate,
            "Home/Textures/HomeWornLaminateAlbedo",
            1.2f,
            0.18f,
            0f,
            1.4425f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.Upholstery,
            "Home/Textures/HomeUpholsteryAlbedo",
            0.85f,
            0.03f,
            0f,
            1.5325f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.BedLinen,
            "Home/Textures/HomeBedLinenAlbedo",
            0.9f,
            0.03f,
            0f,
            1.3005f,
            24)]
        [TestCase(
            (int)HomeSurfaceKind.BathroomTile,
            "Home/Textures/HomeBathroomTileAlbedo",
            1.2f,
            0.35f,
            0f,
            1.39f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.Enamel,
            "Home/Textures/HomeEnamelAlbedo",
            1.0f,
            0.45f,
            0.05f,
            1.2545f,
            24)]
        [TestCase(
            (int)HomeSurfaceKind.PaintedMetal,
            "Home/Textures/HomePaintedMetalAlbedo",
            1.0f,
            0.22f,
            0.30f,
            1.4855f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.Concrete,
            "Home/Textures/HomeConcreteAlbedo",
            2.4f,
            0.06f,
            0f,
            1.4985f,
            40)]
        [TestCase(
            (int)HomeSurfaceKind.Rug,
            "Home/Textures/HomeRugAlbedo",
            1.5f,
            0.02f,
            0f,
            1.5445f,
            40)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation,
            int contrastFloor)
        {
            HomeSurfaceKind kind = (HomeSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                HomeSurfaceAppearance.GetRecipe(kind);
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
                HomeSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(
                HomeSurfaceAppearance.GetTexture(kind),
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
            AssertTextureSourceHonoursContract(
                assetPath,
                kind,
                recipe.AlbedoCompensation,
                contrastFloor);
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void Apply_CompensatesTintAndAddsSharedMaterialProperties(
            int kindValue)
        {
            HomeSurfaceKind kind = (HomeSurfaceKind)kindValue;
            Color tint = new Color(0.12f, 0.16f, 0.10f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Stable Home Surface",
                null,
                new Vector3(2.25f, 1.5f, -0.75f),
                new Vector3(3f, 2.5f, 0.2f),
                tint,
                false);

            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                int preservedId =
                    Shader.PropertyToID("_HomePreservedTest");
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(preservedId, 0.42f);
                renderer.SetPropertyBlock(properties);
                Vector4 expectedTransform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        kind,
                        SurfaceProjection.BoxXY);

                HomeSurfaceAppearance.Apply(
                    renderer,
                    kind,
                    SurfaceProjection.BoxXY,
                    tint);

                properties.Clear();
                renderer.GetPropertyBlock(properties);
                HomeSurfaceRecipe recipe =
                    HomeSurfaceAppearance.GetRecipe(kind);
                Color displayTint =
                    HomeSurfaceAppearance.CreateDisplayTint(tint, kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(HomeSurfaceAppearance.GetTexture(kind)));
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

        [Test]
        public void BaseMapTransform_IsStableForMatchingObjects()
        {
            GameObject first = RuntimePrimitiveFactory.CreateBox(
                "Matching Home Surface",
                null,
                new Vector3(1.2f, 3.4f, 5.6f),
                new Vector3(6f, 2f, 0.2f),
                Color.white,
                false);
            GameObject second = RuntimePrimitiveFactory.CreateBox(
                "Matching Home Surface",
                null,
                new Vector3(1.2f, 3.4f, 5.6f),
                new Vector3(6f, 2f, 0.2f),
                Color.white,
                false);

            try
            {
                Vector4 firstTransform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        first.GetComponent<Renderer>(),
                        HomeSurfaceKind.Wallpaper,
                        SurfaceProjection.BoxXY);
                Vector4 secondTransform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        second.GetComponent<Renderer>(),
                        HomeSurfaceKind.Wallpaper,
                        SurfaceProjection.BoxXY);

                Assert.That(secondTransform, Is.EqualTo(firstTransform));
                Assert.That(
                    firstTransform.x,
                    Is.EqualTo(6f / 1.9f).Within(0.0001f));
                Assert.That(
                    firstTransform.y,
                    Is.EqualTo(2f / 1.9f).Within(0.0001f));
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
        public void BaseMapTransform_DiffersFromStairwellForSameTransform()
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
                Vector4 homeTransform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        HomeSurfaceKind.Wallpaper,
                        SurfaceProjection.BoxXY);
                Vector4 stairwellTransform =
                    StairwellSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        StairwellSurfaceKind.WallPaint,
                        StairwellSurfaceProjection.BoxXY);

                Assert.That(
                    new Vector2(homeTransform.z, homeTransform.w),
                    Is.Not.EqualTo(
                        new Vector2(
                            stairwellTransform.z,
                            stairwellTransform.w)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void CylinderSideProjection_UsesCircumferenceAndLength()
        {
            GameObject cylinder = RuntimePrimitiveFactory.CreateCylinder(
                "Home Tall Pipe",
                null,
                Vector3.zero,
                new Vector3(0.5f, 3f, 0.5f),
                Color.white,
                false);

            try
            {
                Vector4 transform =
                    HomeSurfaceAppearance.CreateBaseMapTransform(
                        cylinder.GetComponent<Renderer>(),
                        HomeSurfaceKind.Enamel,
                        SurfaceProjection.CylinderSide);

                Assert.That(
                    transform.x,
                    Is.EqualTo(Mathf.PI * 0.5f).Within(0.0001f));
                Assert.That(
                    transform.y,
                    Is.EqualTo(6f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cylinder);
            }
        }

        [Test]
        public void DitherOcclusionShader_SamplesTheSurfaceAlbedo()
        {
            Shader shader = HomeOcclusionResources.DitherMaterial.shader;
            Assert.That(
                shader.FindPropertyIndex("_BaseMap"),
                Is.GreaterThanOrEqualTo(0),
                "The home occlusion dither shader must sample _BaseMap, " +
                "or textured furniture flashes to its brightened flat " +
                "tint during visibility fades.");
            Assert.That(
                shader.FindPropertyIndex("_Smoothness"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                shader.FindPropertyIndex("_Metallic"),
                Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void WorldBuilder_TexturesEveryOrdinaryRendererOrExemptsIt()
        {
            var parent = new GameObject("Home Appearance Test");
            try
            {
                HomeInteriorLayoutPlan plan =
                    HomeInteriorLayoutPlanner.Generate();
                HomeBalconyLayoutPlan balcony =
                    HomeBalconyLayoutPlanner.Generate(plan);
                Transform root = HomeInteriorWorldBuilder.Build(
                    parent.transform,
                    plan,
                    balcony);

                var expectedTextures = new HashSet<Texture>();
                foreach (HomeSurfaceKind kind in AllSurfaceKinds())
                {
                    expectedTextures.Add(
                        HomeSurfaceAppearance.GetTexture(kind));
                }

                var seenTextures = new HashSet<Texture>();
                int texturedRendererCount = 0;
                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    if (renderer.GetComponentInParent<
                            HomeRefrigeratorSlotView>() != null)
                    {
                        continue;
                    }

                    if (renderer.sharedMaterial !=
                        RuntimePrimitiveFactory.DefaultMaterial)
                    {
                        Assert.That(
                            renderer.gameObject.name,
                            Does.Contain("Halo")
                                .Or.Contain("Bulb")
                                .Or.Contain("Tube")
                                .Or.Contain("Glow")
                                .Or.Contain("Glass")
                                .Or.Contain("Lower Facade Window")
                                .Or.Contain("Light Strip"),
                            "Unexpected non-default material on " +
                            $"{renderer.gameObject.name}.");
                        continue;
                    }

                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    if (texture == null)
                    {
                        Assert.That(
                            IsFlatTintExempt(renderer.gameObject.name),
                            Is.True,
                            $"{renderer.gameObject.name} carries no home " +
                            "surface texture and is not on the flat-tint " +
                            "exemption list.");
                        continue;
                    }

                    texturedRendererCount++;
                    Vector4 transform =
                        properties.GetVector(BaseMapTransformId);
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

                Assert.That(
                    texturedRendererCount,
                    Is.GreaterThanOrEqualTo(60));
                Assert.That(
                    seenTextures,
                    Is.EquivalentTo(expectedTextures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static bool IsFlatTintExempt(string objectName)
        {
            if (FlatTintExemptNames.Contains(objectName))
            {
                return true;
            }

            foreach (string prefix in FlatTintExemptPrefixes)
            {
                if (objectName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // The numbered shelf hardware: "Home Refrigerator Shelf N
            // Front Rail" and "... Shelf N Stain" stay flat while the
            // shelf plane itself is textured, so the prefix alone must
            // not exempt.
            if (objectName.StartsWith(
                    "Home Refrigerator Shelf",
                    StringComparison.Ordinal) &&
                (objectName.EndsWith("Front Rail", StringComparison.Ordinal) ||
                 objectName.EndsWith("Stain", StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
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

        private static IEnumerable<HomeSurfaceKind> AllSurfaceKinds()
        {
            foreach (HomeSurfaceKind kind in Enum.GetValues(
                         typeof(HomeSurfaceKind)))
            {
                yield return kind;
            }
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (HomeSurfaceKind kind in AllSurfaceKinds())
            {
                yield return (int)kind;
            }
        }

        private static void AssertTextureSourceHonoursContract(
            string assetPath,
            HomeSurfaceKind kind,
            float albedoCompensation,
            int contrastFloor)
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

                double meanChannelDelta =
                    edgeDelta /
                    ((source.width + source.height) * 3.0);
                Assert.That(
                    meanChannelDelta,
                    Is.LessThanOrEqualTo(16.0),
                    $"{kind} albedo edges diverge too much " +
                    "for Repeat sampling.");

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
                    Is.GreaterThanOrEqualTo(contrastFloor),
                    $"{kind} albedo contrast is too subtle " +
                    "for the PS1 downsample.");

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
        // multiplied by the compensated sheet must keep the brightness the
        // flat colour had, measured through the same linear multiply URP
        // performs. Channels in the sRGB toe are held to the clamp check
        // only.
        private static void AssertCompensationPreservesBuilderTints(
            HomeSurfaceKind kind,
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
                foreach (float channel in new[] { tint.r, tint.g, tint.b })
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
