using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBuildingSurfaceAppearanceTests
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

        private static readonly CityDistrictKind[] Districts =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife
        };

        private static readonly CityBuildingSurfaceKind[] Surfaces =
        {
            CityBuildingSurfaceKind.FacadePrimary,
            CityBuildingSurfaceKind.FacadeSecondary,
            CityBuildingSurfaceKind.Plinth,
            CityBuildingSurfaceKind.Roof,
            CityBuildingSurfaceKind.Metal,
            CityBuildingSurfaceKind.WindowFrame
        };

        [Test]
        public void Resolver_AcceptsV2RolesAndOnlySafeLegacyAliases()
        {
            for (int index = 0; index < Surfaces.Length; index++)
            {
                CityBuildingSurfaceKind expected = Surfaces[index];
                Assert.That(
                    CityBuildingSurfaceAppearance.TryResolveSurface(
                        CityDistrictKind.OldTown,
                        expected.ToString(),
                        out CityBuildingSurfaceKind direct),
                    Is.True);
                Assert.That(direct, Is.EqualTo(expected));
                Assert.That(
                    CityBuildingSurfaceAppearance.TryResolveSurface(
                        CityDistrictKind.OldTown,
                        "prototype__" + expected,
                        out CityBuildingSurfaceKind sourced),
                    Is.True);
                Assert.That(sourced, Is.EqualTo(expected));
            }

            Assert.That(
                CityBuildingSurfaceAppearance.TryResolveSurface(
                    CityDistrictKind.Residential,
                    "Shell",
                    out CityBuildingSurfaceKind shell),
                Is.True);
            Assert.That(
                shell,
                Is.EqualTo(CityBuildingSurfaceKind.FacadePrimary));
            Assert.That(
                CityBuildingSurfaceAppearance.TryResolveSurface(
                    CityDistrictKind.Residential,
                    "Trim",
                    out CityBuildingSurfaceKind trim),
                Is.True);
            Assert.That(
                trim,
                Is.EqualTo(CityBuildingSurfaceKind.FacadeSecondary));

            Assert.That(
                CityBuildingSurfaceAppearance.TryResolveSurface(
                    CityDistrictKind.OldTown,
                    "WindowGlass",
                    out _),
                Is.False,
                "Window glass must remain on its custom shader path.");
            Assert.That(
                CityBuildingSurfaceAppearance.TryResolveSurface(
                    CityDistrictKind.CentralPark,
                    "FacadePrimary",
                    out _),
                Is.False);
            Assert.That(
                CityBuildingSurfaceAppearance.TryResolveSurface(
                    CityDistrictKind.OldTown,
                    "../FacadePrimary",
                    out _),
                Is.False);
        }

        [Test]
        public void Recipes_LoadEveryDistrictSurfaceFromItsStablePath()
        {
            string manifestPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "ArtSource",
                "City",
                "BuildingSurfaces",
                "city-building-surface-textures.json"));
            Assert.That(File.Exists(manifestPath), Is.True);
            SheetManifest manifest = JsonUtility.FromJson<SheetManifest>(
                File.ReadAllText(manifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.sheets, Has.Length.EqualTo(24));

            for (int districtIndex = 0;
                 districtIndex < Districts.Length;
                 districtIndex++)
            {
                CityDistrictKind district = Districts[districtIndex];
                for (int surfaceIndex = 0;
                     surfaceIndex < Surfaces.Length;
                     surfaceIndex++)
                {
                    CityBuildingSurfaceKind surface =
                        Surfaces[surfaceIndex];
                    CityBuildingSurfaceRecipe recipe =
                        CityBuildingSurfaceAppearance.GetRecipe(
                            district,
                            surface);
                    string expectedPath =
                        CityBuildingSurfaceAppearance
                            .TextureResourceRoot +
                        "/" + district + "/" + surface;
                    SheetRecord record = Array.Find(
                        manifest.sheets,
                        candidate =>
                            candidate.district == district.ToString() &&
                            candidate.surface == surface.ToString());
                    Assert.That(record, Is.Not.Null);
                    Assert.That(
                        recipe.ResourcePath,
                        Is.EqualTo(expectedPath));
                    Assert.That(
                        recipe.AlbedoCompensation,
                        Is.EqualTo(record.albedoCompensation)
                            .Within(0.000001f));
                    Assert.That(
                        recipe.Smoothness,
                        Is.EqualTo(record.smoothness)
                            .Within(0.000001f));
                    Assert.That(
                        recipe.Metallic,
                        Is.EqualTo(record.metallic)
                            .Within(0.000001f));
                    Assert.That(
                        recipe.MetersPerTile,
                        Is.EqualTo(record.metersPerTile)
                            .Within(0.000001f));
                    Assert.That(
                        record.resourcePath,
                        Is.EqualTo(expectedPath));

                    bool atlas =
                        surface ==
                            CityBuildingSurfaceKind.FacadePrimary ||
                        surface ==
                            CityBuildingSurfaceKind.FacadeSecondary;
                    bool fullFace =
                        surface == CityBuildingSurfaceKind.Plinth;
                    Assert.That(
                        recipe.UvLayout,
                        Is.EqualTo(
                            atlas
                                ? CityBuildingSurfaceUvLayout
                                    .BuildingSideAtlas
                                : fullFace
                                    ? CityBuildingSurfaceUvLayout.FullFace
                                    : CityBuildingSurfaceUvLayout
                                        .WorldMetreProjected));
                    string expectedLayout = atlas
                        ? "building-side-atlas"
                        : fullFace
                            ? "full-face"
                            : "meter-tile";
                    Assert.That(
                        record.layout.kind,
                        Is.EqualTo(expectedLayout));
                    Assert.That(
                        recipe.MetersPerTile,
                        atlas || fullFace
                            ? Is.EqualTo(0f)
                            : Is.GreaterThan(0f));

                    Texture2D resource = Resources.Load<Texture2D>(
                        expectedPath);
                    Assert.That(
                        resource,
                        Is.Not.Null,
                        $"Missing generated sheet {expectedPath}.");
                    Assert.That(
                        CityBuildingSurfaceAppearance.GetTexture(
                            district,
                            surface),
                        Is.SameAs(resource));
                    bool fullResolution = atlas || fullFace;
                    int expectedRuntimeSize = fullResolution
                        ? 1024
                        : 512;
                    Assert.That(
                        resource.width,
                        Is.EqualTo(expectedRuntimeSize));
                    Assert.That(
                        resource.height,
                        Is.EqualTo(expectedRuntimeSize));
                    Assert.That(resource.isReadable, Is.False);

                    string assetPath = AssetDatabase.GetAssetPath(resource);
                    TextureImporter importer =
                        AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    Assert.That(importer, Is.Not.Null);
                    Assert.That(importer.sRGBTexture, Is.True);
                    Assert.That(importer.mipmapEnabled, Is.True);
                    Assert.That(importer.isReadable, Is.False);
                    Assert.That(importer.anisoLevel, Is.EqualTo(4));
                    Assert.That(
                        importer.wrapMode,
                        Is.EqualTo(
                            fullResolution
                                ? TextureWrapMode.Clamp
                                : TextureWrapMode.Repeat));
                    Assert.That(
                        importer.maxTextureSize,
                        Is.EqualTo(expectedRuntimeSize));
                    Assert.That(
                        importer.textureCompression,
                        Is.EqualTo(
                            TextureImporterCompression.Uncompressed));
                }
            }
        }

        [Test]
        public void Apply_BindsSharedMaterialAndCompletePropertyBlock()
        {
            Color sourceTint = new Color(
                0.24f,
                0.28f,
                0.22f,
                1f);
            var root = new GameObject(
                "City Building Surface Appearance Test");
            try
            {
                for (int districtIndex = 0;
                     districtIndex < Districts.Length;
                     districtIndex++)
                {
                    CityDistrictKind district = Districts[districtIndex];
                    for (int surfaceIndex = 0;
                         surfaceIndex < Surfaces.Length;
                         surfaceIndex++)
                    {
                        CityBuildingSurfaceKind surface =
                            Surfaces[surfaceIndex];
                        GameObject part =
                            RuntimePrimitiveFactory.CreateBox(
                                district + " " + surface,
                                root.transform,
                                Vector3.zero,
                                Vector3.one,
                                sourceTint,
                                false);
                        Renderer renderer =
                            part.GetComponent<Renderer>();

                        CityBuildingSurfaceAppearance.Apply(
                            renderer,
                            district,
                            surface,
                            sourceTint);

                        var properties = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(properties);
                        CityBuildingSurfaceRecipe recipe =
                            CityBuildingSurfaceAppearance.GetRecipe(
                                district,
                                surface);
                        Color displayTint =
                            CityBuildingSurfaceAppearance
                                .CreateDisplayTint(
                                    sourceTint,
                                    district,
                                    surface);
                        Assert.That(
                            renderer.sharedMaterial,
                            Is.SameAs(
                                RuntimePrimitiveFactory.DefaultMaterial));
                        Assert.That(
                            properties.GetTexture(BaseMapId),
                            Is.SameAs(
                                CityBuildingSurfaceAppearance.GetTexture(
                                    district,
                                    surface)));
                        Assert.That(
                            properties.GetTexture(BaseMapId),
                            Is.Not.SameAs(Texture2D.whiteTexture));
                        Assert.That(
                            properties.GetVector(BaseMapTransformId),
                            Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
                        AssertColorNear(
                            properties.GetColor(BaseColorId),
                            displayTint);
                        AssertColorNear(
                            properties.GetColor(ColorId),
                            displayTint);
                        Assert.That(
                            properties.GetFloat(SmoothnessId),
                            Is.EqualTo(recipe.Smoothness).Within(0.0001f));
                        Assert.That(
                            properties.GetFloat(MetallicId),
                            Is.EqualTo(recipe.Metallic).Within(0.0001f));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertColorNear(Color actual, Color expected)
        {
            Assert.That(
                actual.r,
                Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(
                actual.g,
                Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(
                actual.b,
                Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(
                actual.a,
                Is.EqualTo(expected.a).Within(0.0001f));
        }

        [Serializable]
        private sealed class SheetManifest
        {
            public SheetRecord[] sheets;
        }

        [Serializable]
        private sealed class SheetRecord
        {
            public string district;
            public string surface;
            public string resourcePath;
            public SheetLayout layout;
            public float metersPerTile;
            public float albedoCompensation;
            public float smoothness;
            public float metallic;
        }

        [Serializable]
        private sealed class SheetLayout
        {
            public string kind;
        }
    }
}
