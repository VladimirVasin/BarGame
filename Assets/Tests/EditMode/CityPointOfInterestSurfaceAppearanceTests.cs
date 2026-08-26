using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPointOfInterestSurfaceAppearanceTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

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
        // tools/build-city-poi-textures.py: below this channel value
        // the sRGB toe makes relative error meaningless, so only the
        // clamp check applies there.
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;

        // Every flat colour CityDistrictPointOfInterestWorldBuilder
        // passes per surface kind. The generator derives its
        // compensation from the same values, so a palette edit in the
        // builder fails here rather than silently shifting a site's
        // brightness.
        private static readonly
            Dictionary<CityPointOfInterestSurfaceKind, Color[]>
            BuilderTints =
                new Dictionary<CityPointOfInterestSurfaceKind, Color[]>
                {
                    [CityPointOfInterestSurfaceKind.Paving] = new[]
                    {
                        new Color(0.255f, 0.235f, 0.190f),
                        new Color(0.235f, 0.275f, 0.270f),
                        new Color(0.200f, 0.220f, 0.210f),
                        new Color(0.170f, 0.175f, 0.215f),
                        new Color(0.245f, 0.235f, 0.285f),
                    },
                    [CityPointOfInterestSurfaceKind.PaintedMetal] = new[]
                    {
                        new Color(0.145f, 0.190f, 0.185f),
                        new Color(0.085f, 0.090f, 0.125f),
                        new Color(0.115f, 0.135f, 0.145f),
                    },
                    [CityPointOfInterestSurfaceKind.Cloth] = new[]
                    {
                        new Color(0.405f, 0.245f, 0.185f),
                        new Color(0.225f, 0.350f, 0.375f),
                        new Color(0.600f, 0.500f, 0.285f),
                        new Color(0.335f, 0.305f, 0.245f),
                        new Color(0.295f, 0.140f, 0.150f),
                        new Color(0.140f, 0.205f, 0.245f),
                        new Color(0.355f, 0.135f, 0.165f),
                    },
                    [CityPointOfInterestSurfaceKind.Timber] = new[]
                    {
                        new Color(0.405f, 0.245f, 0.185f),
                        new Color(0.225f, 0.145f, 0.245f),
                    },
                    [CityPointOfInterestSurfaceKind.Paper] = new[]
                    {
                        new Color(0.385f, 0.335f, 0.255f),
                        new Color(0.355f, 0.135f, 0.165f),
                        new Color(0.145f, 0.245f, 0.295f),
                    },
                };

        [TestCase(
            (int)CityPointOfInterestSurfaceKind.Paving,
            "Textures/CityPoiPavingAlbedo",
            3.0f,
            0.06f,
            0f,
            1.4205f)]
        [TestCase(
            (int)CityPointOfInterestSurfaceKind.PaintedMetal,
            "Textures/CityPoiPaintedMetalAlbedo",
            1.4f,
            0.22f,
            0.25f,
            1.479f)]
        [TestCase(
            (int)CityPointOfInterestSurfaceKind.Cloth,
            "Textures/CityPoiClothAlbedo",
            1.6f,
            0f,
            0f,
            1.4105f)]
        [TestCase(
            (int)CityPointOfInterestSurfaceKind.Timber,
            "Textures/CityPoiTimberAlbedo",
            1.6f,
            0.08f,
            0f,
            1.445f)]
        [TestCase(
            (int)CityPointOfInterestSurfaceKind.Paper,
            "Textures/CityPoiPaperAlbedo",
            0.9f,
            0.02f,
            0f,
            1.4215f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int kindValue,
            string expectedResourcePath,
            float expectedMetersPerTile,
            float expectedSmoothness,
            float expectedMetallic,
            float expectedAlbedoCompensation)
        {
            CityPointOfInterestSurfaceKind kind =
                (CityPointOfInterestSurfaceKind)kindValue;
            HomeSurfaceRecipe recipe =
                CityPointOfInterestSurfaceAppearance.GetRecipe(kind);
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
                CityPointOfInterestSurfaceAppearance.GetTexture(kind),
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
            CityPointOfInterestSurfaceKind kind =
                (CityPointOfInterestSurfaceKind)kindValue;
            Color tint = new Color(0.24f, 0.28f, 0.22f, 1f);
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "Stable Point Of Interest Surface",
                null,
                new Vector3(1.25f, 1.5f, -0.5f),
                new Vector3(3f, 2.5f, 0.2f),
                tint,
                false);

            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                Vector4 expectedTransform =
                    CityPointOfInterestSurfaceAppearance
                        .CreateBaseMapTransform(
                            renderer,
                            kind,
                            SurfaceProjection.BoxXY);

                CityPointOfInterestSurfaceAppearance.Apply(
                    renderer,
                    kind,
                    SurfaceProjection.BoxXY,
                    tint);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                HomeSurfaceRecipe recipe =
                    CityPointOfInterestSurfaceAppearance.GetRecipe(kind);
                Color displayTint =
                    CityPointOfInterestSurfaceAppearance
                        .CreateDisplayTint(tint, kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityPointOfInterestSurfaceAppearance
                            .GetTexture(kind)));
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
        public void BaseMapTransform_DiffersFromSupermarketForSameTransform()
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
                Vector4 poiTransform =
                    CityPointOfInterestSurfaceAppearance
                        .CreateBaseMapTransform(
                            renderer,
                            CityPointOfInterestSurfaceKind.Paving,
                            SurfaceProjection.BoxXY);
                Vector4 marketTransform =
                    SupermarketSurfaceAppearance.CreateBaseMapTransform(
                        renderer,
                        SupermarketSurfaceKind.Linoleum,
                        SurfaceProjection.BoxXY);

                Assert.That(
                    new Vector2(poiTransform.z, poiTransform.w),
                    Is.Not.EqualTo(
                        new Vector2(
                            marketTransform.z,
                            marketTransform.w)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void Build_TexturesEverySiteGroundAndTheDryingYard()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Poi Surface Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                Texture pavingTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Paving);
                int pavedGroundCount = 0;
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name !=
                        CityDistrictPointOfInterestWorldBuilder
                            .PublicGroundName)
                    {
                        continue;
                    }

                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Assert.That(
                        properties.GetTexture(BaseMapId),
                        Is.SameAs(pavingTexture),
                        "Every public ground carries the paving sheet.");
                    pavedGroundCount++;
                }

                Assert.That(pavedGroundCount, Is.EqualTo(4));

                Transform dryingYard = FindDryingYardRecipe(root);
                Texture metalTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.PaintedMetal);
                Texture timberTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Timber);
                Texture clothTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Cloth);
                int metalCount = 0;
                int timberCount = 0;
                int clothCount = 0;
                foreach (Renderer renderer in
                         dryingYard.GetComponentsInChildren<Renderer>(
                             true))
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    if (texture == null)
                    {
                        continue;
                    }

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
                    if (texture == metalTexture)
                    {
                        metalCount++;
                    }
                    else if (texture == timberTexture)
                    {
                        timberCount++;
                    }
                    else if (texture == clothTexture)
                    {
                        clothCount++;
                        Assert.That(
                            renderer,
                            Is.TypeOf<SkinnedMeshRenderer>(),
                            "City laundry is simulated cloth.");
                        Assert.That(
                            renderer.sharedMaterial,
                            Is.SameAs(ClothPanelFactory.TwoSidedMaterial),
                            "Laundry keeps the shared two-sided panel " +
                            "material.");
                        Assert.That(
                            properties.GetFloat(SmoothnessId),
                            Is.EqualTo(0f).Within(0.0001f),
                            "Laundry stays matte under the street " +
                            "lights.");
                    }
                }

                Assert.That(
                    metalCount,
                    Is.EqualTo(1),
                    "The authored drying frames and fixture shells share " +
                    "one painted-metal role mesh.");
                Assert.That(timberCount, Is.EqualTo(1));
                Assert.That(clothCount, Is.EqualTo(4));
                Assert.That(
                    dryingYard.Find(
                        "Imported Residential Drying Yard " +
                        "Residential_PaintedMetal"),
                    Is.Not.Null);
                Assert.That(
                    dryingYard.Find(
                        "Imported Residential Drying Yard " +
                        "Residential_Timber"),
                    Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_TexturesTheLastRouteIsland()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Poi Island Surface Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                Transform island = FindRecipe(
                    root,
                    CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland);
                Texture pavingTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Paving);
                Texture metalTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.PaintedMetal);
                Texture paperTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Paper);
                Texture timberTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Timber);
                Texture clothTexture =
                    CityPointOfInterestSurfaceAppearance.GetTexture(
                        CityPointOfInterestSurfaceKind.Cloth);
                int pavingCount = 0;
                int metalCount = 0;
                int paperCount = 0;
                int timberCount = 0;
                int clothCount = 0;
                int skinnedClothCount = 0;
                foreach (Renderer renderer in
                         island.GetComponentsInChildren<Renderer>(true))
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    if (texture == null)
                    {
                        continue;
                    }

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
                    if (texture == pavingTexture)
                    {
                        pavingCount++;
                    }
                    else if (texture == metalTexture)
                    {
                        metalCount++;
                    }
                    else if (texture == paperTexture)
                    {
                        paperCount++;
                    }
                    else if (texture == timberTexture)
                    {
                        timberCount++;
                    }
                    else if (texture == clothTexture)
                    {
                        clothCount++;
                        if (renderer is SkinnedMeshRenderer)
                        {
                            skinnedClothCount++;
                            Assert.That(
                                renderer.sharedMaterial,
                                Is.SameAs(
                                    ClothPanelFactory.TwoSidedMaterial),
                                "Canopy rags keep the shared two-sided " +
                                "panel material.");
                        }
                    }
                }

                Assert.That(
                    pavingCount,
                    Is.EqualTo(1),
                    "The authored island masonry shares one paving role " +
                    "mesh.");
                Assert.That(
                    metalCount,
                    Is.GreaterThanOrEqualTo(1),
                    "The imported island fixtures must carry the " +
                    "painted-metal sheet; the Ferryman lamp may add " +
                    "runtime renderers.");
                Assert.That(
                    paperCount,
                    Is.EqualTo(1),
                    "All static island paper is one authored role mesh.");
                Assert.That(timberCount, Is.EqualTo(1));
                // Six simulated canopy rags plus the lost scarf.
                Assert.That(clothCount, Is.EqualTo(7));
                Assert.That(skinnedClothCount, Is.EqualTo(6));
                foreach (string component in new[]
                         {
                             "Masonry", "Street", "Residential", "Timber"
                         })
                {
                    Assert.That(
                        island.Find(
                            "Imported Nightlife Last Route Island " +
                            component),
                        Is.Not.Null,
                        $"Missing imported island {component} role mesh.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_DryingYardCarriesNightScaledPoleFloodlight()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Poi Floodlight Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                Light light = FindSiteLight(
                    root,
                    "Drying Yard Floodlight Light");
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(
                    light.shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(light.range, Is.EqualTo(16f).Within(0.001f));
                Assert.That(
                    light.spotAngle,
                    Is.EqualTo(72f).Within(0.001f));
                Assert.That(
                    light.GetComponentInChildren<CityLightHalo>(true),
                    Is.Not.Null,
                    "Every city realtime light owns a fog halo.");
                Transform dryingYard = FindDryingYardRecipe(root);
                Assert.That(
                    light.transform.IsChildOf(dryingYard),
                    Is.True);
                Assert.That(
                    light.transform.position.y,
                    Is.GreaterThan(3.5f),
                    "The floodlight sits on its own tall pole above " +
                    "the drying frames.");

                float nightIntensity = light.intensity /
                    Mathf.Max(
                        0.0001f,
                        CityNightSiteLightRegistry.NightFactor);
                try
                {
                    CityNightSiteLightRegistry.SetNightFactor(0.5f);
                    Assert.That(
                        light.intensity,
                        Is.EqualTo(nightIntensity * 0.5f)
                            .Within(0.001f));
                    Assert.That(light.enabled, Is.True);

                    CityNightSiteLightRegistry.SetNightFactor(0f);
                    Assert.That(
                        light.enabled,
                        Is.False,
                        "Nothing electric burns under the day sky.");
                }
                finally
                {
                    CityNightSiteLightRegistry.SetNightFactor(1f);
                }

                Assert.That(light.enabled, Is.True);
                AssertPoleColliderClearsApproaches(root, layout);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_IslandMastCarriesNightScaledFloodlight()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Poi Island Floodlight Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                // The island mast carries a SECOND head, turned on the
                // Ferryman's car - and only when there is a car to turn it
                // on, because the bay is fitted per seed and some cities
                // have nowhere to park.
                bool hasCar = false;
                foreach (CityDistrictPointOfInterestDescriptor descriptor in
                         layout.DistrictPointsOfInterest)
                {
                    if (descriptor.Kind !=
                        CityDistrictPointOfInterestKind
                            .NightlifeLastRouteIsland)
                    {
                        continue;
                    }

                    hasCar = CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanCarStance(descriptor, out _);
                }

                Assert.That(
                    root.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(hasCar ? 3 : 2),
                    "The district points of interest carry the drying " +
                    "yard floodlight, the island's service head, and the " +
                    "Ferryman's car head when there is a car.");
                Light light = FindSiteLight(
                    root,
                    "Island Mast Floodlight Light");
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(
                    light.shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(light.range, Is.EqualTo(16f).Within(0.001f));
                Assert.That(
                    light.spotAngle,
                    Is.EqualTo(72f).Within(0.001f));
                Assert.That(
                    light.GetComponentInChildren<CityLightHalo>(true),
                    Is.Not.Null,
                    "Every city realtime light owns a fog halo.");
                Transform island = FindRecipe(
                    root,
                    CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland);
                Assert.That(
                    light.transform.IsChildOf(island),
                    Is.True);
                Assert.That(
                    light.transform.position.y,
                    Is.GreaterThan(4.0f),
                    "The floodlight hangs from the route mast under " +
                    "the broken totem.");

                float nightIntensity = light.intensity /
                    Mathf.Max(
                        0.0001f,
                        CityNightSiteLightRegistry.NightFactor);
                try
                {
                    CityNightSiteLightRegistry.SetNightFactor(0.5f);
                    Assert.That(
                        light.intensity,
                        Is.EqualTo(nightIntensity * 0.5f)
                            .Within(0.001f));
                    Assert.That(light.enabled, Is.True);

                    CityNightSiteLightRegistry.SetNightFactor(0f);
                    Assert.That(
                        light.enabled,
                        Is.False,
                        "Nothing electric burns under the day sky.");
                }
                finally
                {
                    CityNightSiteLightRegistry.SetNightFactor(1f);
                }

                Assert.That(light.enabled, Is.True);

                // The mast's service head adds no collider of its own:
                // the mast base already owns the island's obstacle. Its
                // neighbour is not on the mast any more - it stands on a
                // post of its own in front of the car - and that post does
                // own one, because it is a thing to walk into.
                foreach (Collider collider in
                         island.GetComponentsInChildren<Collider>(true))
                {
                    Assert.That(
                        collider.name.Contains("Island Floodlight"),
                        Is.False,
                        "The island floodlight must reuse the mast " +
                        "collider instead of adding its own.");
                }

                if (!hasCar)
                {
                    return;
                }

                // The Ferryman's own lamp: same recipe, its own post in
                // front of his car, and two deliberate differences from
                // the service head above. A narrow cone, because it has to
                // land as a pool on one man rather than wash a lot, and a
                // daytime floor, because he has to be lit whenever anybody
                // walks up to him. That floor is inherited from the light
                // he used to carry around with him.
                Light carLight = FindSiteLight(
                    root,
                    "Ferryman Car Floodlight Light");
                Assert.That(carLight.type, Is.EqualTo(LightType.Spot));
                Assert.That(
                    carLight.shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(
                    carLight.spotAngle,
                    Is.LessThan(light.spotAngle),
                    "A pool, not a wash: the car head's cone has to be " +
                    "tighter than the service head's.");
                Assert.That(
                    carLight.GetComponentInChildren<CityLightHalo>(true),
                    Is.Not.Null,
                    "Every city realtime light owns a fog halo.");
                Assert.That(
                    carLight.transform.IsChildOf(island),
                    Is.True,
                    "It hangs off a fixture on the island, not off the " +
                    "man - that is the whole difference from the light he " +
                    "used to carry.");

                try
                {
                    CityNightSiteLightRegistry.SetNightFactor(0f);
                    Assert.That(
                        carLight.enabled,
                        Is.True,
                        "Unlike its neighbour this one keeps a daytime " +
                        "floor - he has to be lit whenever anybody walks " +
                        "up, not only after dark.");
                }
                finally
                {
                    CityNightSiteLightRegistry.SetNightFactor(1f);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BuildHomeExterior_KeepsFloodlightGeometryOnly()
        {
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(Seed);
            var parent = new GameObject("Poi Vista Floodlight Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder
                        .BuildHomeExterior(parent.transform, context);

                Assert.That(
                    root.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "The balcony vista adds no realtime lights.");
                Assert.That(
                    root.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "The balcony vista stays collision-free.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static Transform FindDryingYardRecipe(GameObject root)
        {
            return FindRecipe(
                root,
                CityDistrictPointOfInterestKind.ResidentialDryingYard);
        }

        private static Transform FindRecipe(
            GameObject root,
            CityDistrictPointOfInterestKind kind)
        {
            string recipeName =
                CityDistrictPointOfInterestWorldBuilder.GetRecipeName(
                    kind);
            foreach (Transform transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == recipeName)
                {
                    return transform;
                }
            }

            Assert.Fail($"The default city builds a {recipeName}.");
            return null;
        }

        private static Light FindSiteLight(
            GameObject root,
            string lightName)
        {
            foreach (Light light in
                     root.GetComponentsInChildren<Light>(true))
            {
                if (light.name == lightName)
                {
                    return light;
                }
            }

            Assert.Fail($"Missing site light '{lightName}'.");
            return null;
        }

        private static void AssertPoleColliderClearsApproaches(
            GameObject root,
            CityLayout layout)
        {
            BoxCollider pole = null;
            foreach (Collider collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name ==
                    "Drying Yard Floodlight Pole Collider")
                {
                    pole = (BoxCollider)collider;
                    break;
                }
            }

            Assert.That(
                pole,
                Is.Not.Null,
                "The floodlight pole blocks movement like every lamp " +
                "post.");

            Physics.SyncTransforms();
            Bounds bounds = pole.bounds;
            var footprint = Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[index];
                if (descriptor.Kind !=
                    CityDistrictPointOfInterestKind
                        .ResidentialDryingYard)
                {
                    continue;
                }

                for (int accessIndex = 0;
                     accessIndex < descriptor.Accesses.Count;
                     accessIndex++)
                {
                    Assert.That(
                        footprint.Overlaps(
                            descriptor.Accesses[accessIndex]
                                .ApproachBounds),
                        Is.False,
                        "The floodlight pole must not block a public " +
                        "access approach.");
                }
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
            foreach (CityPointOfInterestSurfaceKind kind in
                     Enum.GetValues(
                         typeof(CityPointOfInterestSurfaceKind)))
            {
                yield return (int)kind;
            }
        }

        private static void AssertTextureSourceHonoursContract(
            string assetPath,
            CityPointOfInterestSurfaceKind kind,
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
            CityPointOfInterestSurfaceKind kind,
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
