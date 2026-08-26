using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ChurchAssetImportTests
    {
        private const string ManifestPath =
            "Assets/Church/Models/Church3D.json";

        [Test]
        public void CatholicBasilica_ImportsAsTwoPassiveResourcePrefabs()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(source, Is.Not.Null);
            ChurchManifest manifest =
                JsonUtility.FromJson<ChurchManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(
                manifest.design_id,
                Is.EqualTo("provincial_catholic_gothic_basilica_v1"));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.dimensions_m.width, Is.EqualTo(23f));
            Assert.That(manifest.dimensions_m.length, Is.EqualTo(44f));
            Assert.That(manifest.dimensions_m.height, Is.EqualTo(32f));
            Assert.That(manifest.assets, Has.Length.EqualTo(2));

            ChurchManifestAsset exterior = manifest.assets.Single(
                asset => asset.kind == "Exterior");
            ChurchManifestAsset interior = manifest.assets.Single(
                asset => asset.kind == "Interior");
            Assert.That(exterior.renderer_count, Is.LessThanOrEqualTo(18));
            Assert.That(exterior.triangle_count, Is.LessThanOrEqualTo(12000));
            Assert.That(interior.renderer_count, Is.LessThanOrEqualTo(24));
            Assert.That(interior.triangle_count, Is.LessThanOrEqualTo(22000));
            Assert.That(exterior.runtime_wrapper_yaw_degrees, Is.EqualTo(180f));
            Assert.That(interior.runtime_wrapper_yaw_degrees, Is.Zero);

            AssertImporter("Assets/Church/Models/ChurchExterior3D.fbx");
            AssertImporter("Assets/Church/Models/ChurchInterior3D.fbx");
            AssertTextureImporters();
            AssertPrefab(
                ChurchAssetKind.Exterior,
                ChurchResources.LoadExteriorPrefab(),
                exterior);
            AssertPrefab(
                ChurchAssetKind.Interior,
                ChurchResources.LoadInteriorPrefab(),
                interior);
        }

        private static void AssertTextureImporters()
        {
            string[] tileable =
            {
                "ChurchPlasterAlbedo.png",
                "ChurchStoneAlbedo.png",
                "ChurchWoodAlbedo.png",
                "ChurchMetalAlbedo.png",
                "ChurchFloorAlbedo.png",
                "ChurchTextileAlbedo.png"
            };
            string[] atlases =
            {
                "ChurchSacredArtAtlasAlbedo.png",
                "ChurchMuralAtlasAlbedo.png",
                "ChurchGlassAtlasAlbedo.png"
            };

            foreach (string name in tileable)
            {
                AssertTextureImporter(
                    name,
                    TextureWrapMode.Repeat,
                    true);
            }

            foreach (string name in atlases)
            {
                AssertTextureImporter(
                    name,
                    TextureWrapMode.Clamp,
                    false);
            }
        }

        private static void AssertTextureImporter(
            string name,
            TextureWrapMode expectedWrap,
            bool expectedMipmaps)
        {
            string path = $"Assets/Church/Textures/{name}";
            TextureImporter importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.wrapMode, Is.EqualTo(expectedWrap), path);
            Assert.That(
                importer.mipmapEnabled,
                Is.EqualTo(expectedMipmaps),
                path);
        }

        private static void AssertImporter(string path)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        private static void AssertPrefab(
            ChurchAssetKind expectedKind,
            GameObject prefab,
            ChurchManifestAsset source)
        {
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ChurchAssetRegistry registry =
                    instance.GetComponent<ChurchAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.Kind, Is.EqualTo(expectedKind));
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(
                    registry.Renderers.Count,
                    Is.EqualTo(source.renderer_count));
                Assert.That(
                    registry.RendererBindings.Count,
                    Is.EqualTo(source.renderer_count));
                Assert.That(
                    registry.SourceTriangleCount,
                    Is.EqualTo(source.triangle_count));
                Assert.That(
                    registry.RendererBindings.All(
                        binding =>
                            binding != null &&
                            binding.Renderer != null &&
                            !string.IsNullOrWhiteSpace(binding.Role) &&
                            binding.Renderer.sharedMaterials.Length == 1),
                    Is.True);
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);

                if (expectedKind == ChurchAssetKind.Exterior)
                {
                    Vector3 entrance = instance.transform
                        .InverseTransformPoint(
                            registry.EntranceAnchor.position);
                    Assert.That(entrance.x, Is.EqualTo(0f).Within(.001f));
                    Assert.That(
                        entrance.z,
                        Is.EqualTo(22.05f).Within(.001f));
                    Assert.That(registry.ApproachAnchor, Is.Not.Null);
                    Assert.That(registry.ReturnAnchor, Is.Not.Null);
                }
                else
                {
                    Assert.That(registry.SpawnAnchor, Is.Not.Null);
                    Assert.That(registry.ExitAnchor, Is.Not.Null);
                    Assert.That(registry.NarthexLightAnchor, Is.Not.Null);
                    Assert.That(registry.NaveLightAnchor, Is.Not.Null);
                    Assert.That(registry.SanctuaryLightAnchor, Is.Not.Null);
                    Assert.That(
                        registry.RendererBindings.Any(
                            binding => binding.Role ==
                                "twelve_nave_pew_halves"),
                        Is.True);
                    Assert.That(
                        registry.RendererBindings.Any(
                            binding => binding.Role ==
                                "choir_loft_and_pipe_organ_case"),
                        Is.True);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Serializable]
        private sealed class ChurchManifest
        {
            public string design_id;
            public ChurchDimensionsManifest dimensions_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public ChurchManifestAsset[] assets;
        }

        [Serializable]
        private sealed class ChurchDimensionsManifest
        {
            public float width;
            public float length;
            public float height;
        }

        [Serializable]
        private sealed class ChurchManifestAsset
        {
            public string kind;
            public float runtime_wrapper_yaw_degrees;
            public int renderer_count;
            public int triangle_count;
        }
    }
}
