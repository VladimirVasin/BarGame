using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the deterministic player-home exterior at its Blender/Unity
    /// hand-off: authored metre dimensions, semantic surfaces, passive import
    /// and the street-door anchor shared by City and Home presentation.
    /// </summary>
    public sealed class PlayerHomeExteriorModelContractTests
    {
        private const string ManifestPath =
            "Assets/PlayerHome/Models/PlayerHomeExterior3D.json";
        private const string ModelPath =
            "Assets/PlayerHome/Models/PlayerHomeExterior3D.fbx";
        private const float Tolerance = 0.01f;

        private static readonly string[] RequiredSheets =
        {
            "StuccoPrimary",
            "StuccoRepair",
            "BrickPlinth",
            "RoofSlate",
            "PaintedWood",
            "PaintedMetal",
            "WindowFrame",
            "WindowGlass",
            "Concrete"
        };

        [Test]
        public void
            DedicatedExterior_IsPassiveSemanticAndKeepsTheHomeDoorFixed()
        {
            PlayerHomeManifest manifest = LoadManifest();

            Assert.That(
                manifest.design_id,
                Is.EqualTo("player_home_exterior_v1"));
            Assert.That(
                manifest.dimensions_m.width,
                Is.EqualTo(13f).Within(0.0001f));
            Assert.That(
                manifest.dimensions_m.depth,
                Is.EqualTo(12f).Within(0.0001f));
            Assert.That(
                manifest.dimensions_m.height,
                Is.EqualTo(8.8f).Within(0.0001f));
            Assert.That(
                manifest.bounds_min,
                Is.EqualTo(new[] { -6.5f, -6f, 0f }));
            Assert.That(
                manifest.bounds_max,
                Is.EqualTo(new[] { 6.5f, 8.3f, 8.8f }));

            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.triangle_count, Is.GreaterThan(0));
            Assert.That(manifest.build_signature, Is.Not.Empty);
            Assert.That(manifest.generator_version, Is.Not.Empty);

            PlayerHomeManifestAnchor door = manifest.anchors.Single(
                anchor => anchor.role == "exterior_door");
            Assert.That(door.name, Is.Not.Empty);
            Assert.That(
                door.local_position,
                Is.EqualTo(new[] { 0f, 6f, 0f }));
            Assert.That(
                door.unity_local_position,
                Is.EqualTo(new[] { 0f, 0f, 6f }));

            Assert.That(
                manifest.parts.Select(part => part.sheet).Distinct(),
                Is.EquivalentTo(RequiredSheets));
            Assert.That(
                manifest.parts.All(part =>
                    !string.IsNullOrWhiteSpace(part.role) &&
                    !string.IsNullOrWhiteSpace(part.group)),
                Is.True);
            for (int index = 0; index < RequiredSheets.Length; index++)
            {
                Assert.That(
                    PlayerHomeExteriorSurfaceAppearance.TryResolveSheet(
                        RequiredSheets[index],
                        out _),
                    Is.True,
                    $"No runtime surface resolves " +
                    $"'{RequiredSheets[index]}'.");
            }

            PlayerHomeManifestPart emissive = manifest.parts.Single(
                part => part.emissive);
            Assert.That(
                emissive.name,
                Is.EqualTo("Front Lit Window Glass"));
            Assert.That(emissive.sheet, Is.EqualTo("WindowGlass"));
            Assert.That(emissive.role, Is.EqualTo("exterior_glass"));
            Assert.That(
                manifest.parts
                    .Where(part =>
                        part.sheet == "WindowGlass" &&
                        part.name != "Front Lit Window Glass")
                    .All(part => !part.emissive),
                Is.True);

            AssertSurfaceClearanceContract(manifest);
            AssertPassiveImporter();
            AssertPrefabRegistry(manifest, door);
        }

        private static void AssertSurfaceClearanceContract(
            PlayerHomeManifest manifest)
        {
            Assert.That(
                manifest.surface_clearance_contract,
                Is.Not.Null);
            Assert.That(
                manifest.surface_clearance_contract
                    .opaque_overlay_min_clearance_m,
                Is.EqualTo(0.03f).Within(0.0001f));
            Assert.That(
                manifest.surface_clearance_contract
                    .runtime_foundation_inset_m,
                Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(
                manifest.surface_clearance_contract.facade_uv,
                Is.EqualTo(
                    "authored_per_elevation_no_whole_building_stretch"));
            Assert.That(
                manifest.surface_clearance_contract.openings,
                Is.EqualTo("separate_geometry_not_baked"));
        }

        private static void AssertPassiveImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the exterior did not import");
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        private static void AssertPrefabRegistry(
            PlayerHomeManifest manifest,
            PlayerHomeManifestAnchor door)
        {
            GameObject prefab = PlayerHomeExteriorModelResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                PlayerHomeExteriorAssetRegistry registry =
                    instance.GetComponent<PlayerHomeExteriorAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(
                    instance.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(
                    registry.DesignId,
                    Is.EqualTo(manifest.design_id));
                Assert.That(
                    registry.BuildSignature,
                    Is.EqualTo(manifest.build_signature));
                Assert.That(
                    registry.SourceGeneratorVersion,
                    Is.EqualTo(manifest.generator_version));
                Assert.That(
                    registry.SourceTriangleCount,
                    Is.EqualTo(manifest.triangle_count));
                Assert.That(
                    registry.Dimensions.Width,
                    Is.EqualTo(13f).Within(0.0001f));
                Assert.That(
                    registry.Dimensions.Depth,
                    Is.EqualTo(12f).Within(0.0001f));
                Assert.That(
                    registry.Dimensions.Height,
                    Is.EqualTo(8.8f).Within(0.0001f));

                Assert.That(
                    registry.Parts.Count,
                    Is.EqualTo(manifest.parts.Length));
                var expectedParts =
                    new Dictionary<string, PlayerHomeManifestPart>(
                        StringComparer.Ordinal);
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    expectedParts.Add(
                        manifest.parts[index].name,
                        manifest.parts[index]);
                }

                for (int index = 0;
                     index < registry.Parts.Count;
                     index++)
                {
                    PlayerHomeExteriorPartBinding binding =
                        registry.Parts[index];
                    Assert.That(binding, Is.Not.Null);
                    Assert.That(
                        expectedParts.TryGetValue(
                            binding.SourceName,
                            out PlayerHomeManifestPart expected),
                        Is.True);
                    Assert.That(binding.Renderer, Is.Not.Null);
                    Assert.That(binding.Role, Is.EqualTo(expected.role));
                    Assert.That(binding.Group, Is.EqualTo(expected.group));
                    Assert.That(binding.Sheet, Is.EqualTo(expected.sheet));
                    Assert.That(
                        binding.Emissive,
                        Is.EqualTo(expected.emissive));
                    Assert.That(
                        binding.CastsShadows,
                        Is.EqualTo(expected.shadows));
                }

                PlayerHomeExteriorPartBinding emissiveBinding =
                    registry.Parts.Single(binding => binding.Emissive);
                Assert.That(
                    emissiveBinding.SourceName,
                    Is.EqualTo("Front Lit Window Glass"));
                Assert.That(
                    emissiveBinding.Sheet,
                    Is.EqualTo("WindowGlass"));
                Assert.That(
                    emissiveBinding.Role,
                    Is.EqualTo("exterior_glass"));
                Assert.That(
                    registry.Parts
                        .Where(binding =>
                            binding.Sheet == "WindowGlass" &&
                            binding.SourceName != "Front Lit Window Glass")
                        .All(binding => !binding.Emissive),
                    Is.True);

                Assert.That(
                    registry.TryGetAnchor(
                        "exterior_door",
                        out Transform doorAnchor),
                    Is.True);
                Vector3 measured = instance.transform
                    .InverseTransformPoint(doorAnchor.position);
                AssertVectorNear(
                    measured,
                    ToVector3(door.unity_local_position));

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
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static PlayerHomeManifest LoadManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                $"'{ManifestPath}' is missing; run the Blender generator");
            PlayerHomeManifest manifest =
                JsonUtility.FromJson<PlayerHomeManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.dimensions_m, Is.Not.Null);
            Assert.That(manifest.anchors, Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.parts, Is.Not.Null.And.Not.Empty);
            return manifest;
        }

        private static Vector3 ToVector3(float[] values)
        {
            Assert.That(values, Has.Length.EqualTo(3));
            return new Vector3(values[0], values[1], values[2]);
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                actual.x,
                Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(
                actual.y,
                Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(
                actual.z,
                Is.EqualTo(expected.z).Within(Tolerance));
        }

        [Serializable]
        private sealed class PlayerHomeManifest
        {
            public string generator_version;
            public string design_id;
            public float[] bounds_min;
            public float[] bounds_max;
            public PlayerHomeManifestDimensions dimensions_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int triangle_count;
            public PlayerHomeManifestAnchor[] anchors;
            public PlayerHomeManifestPart[] parts;
            public PlayerHomeSurfaceClearanceContract
                surface_clearance_contract;
            public string build_signature;
        }

        [Serializable]
        private sealed class PlayerHomeManifestDimensions
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class PlayerHomeManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
            public float[] unity_local_position;
        }

        [Serializable]
        private sealed class PlayerHomeManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public bool emissive;
            public bool shadows;
        }

        [Serializable]
        private sealed class PlayerHomeSurfaceClearanceContract
        {
            public float opaque_overlay_min_clearance_m;
            public float runtime_foundation_inset_m;
            public string facade_uv;
            public string openings;
        }
    }
}
