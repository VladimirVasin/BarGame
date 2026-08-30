using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the measured hand-off from the deterministic Blender exterior to
    /// the passive Resources prefab used by City and the Home reconstruction.
    /// </summary>
    public sealed class SupermarketExteriorModelContractTests
    {
        private const string ManifestPath =
            "Assets/Supermarket/Models/SupermarketExterior3D.json";
        private const string ModelPath =
            "Assets/Supermarket/Models/SupermarketExterior3D.fbx";
        private const float Tolerance = 0.01f;

        private static readonly string[] RequiredSheets =
        {
            "ExteriorWallAtlas",
            "ExteriorFasciaAtlas",
            "ExteriorBrick",
            "ExteriorRoof",
            "ExteriorMetal",
            "ExteriorGlass",
            "ExteriorInteriorDark",
            "ExteriorInteriorLight",
            "ExteriorSignHousing",
            "ExteriorSignGlow",
            "ExteriorMat"
        };

        [Test]
        public void DedicatedExterior_IsFixedMetrePassiveAndSemanticallyBound()
        {
            SupermarketManifest manifest = LoadManifest();

            Assert.That(
                manifest.design_id,
                Is.EqualTo("supermarket_exterior_v1"));
            Assert.That(manifest.dimensions_m.width,
                Is.EqualTo(15.5f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.depth,
                Is.EqualTo(15.5f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.height,
                Is.EqualTo(6.4f).Within(0.0001f));

            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            SupermarketManifestAnchor door = manifest.anchors.Single(
                anchor => anchor.role == "exterior_door");
            Assert.That(door.name, Is.EqualTo("ExteriorDoor"));
            Assert.That(
                door.local_position,
                Is.EqualTo(new[] { 0f, 7.75f, 0f }));
            Assert.That(
                door.unity_local_position,
                Is.EqualTo(new[] { 0f, 0f, 7.75f }));

            Assert.That(
                manifest.yard_spotlight_mount_zones,
                Has.Length.EqualTo(2));
            foreach (SupermarketSpotlightMountZone zone in
                     manifest.yard_spotlight_mount_zones)
            {
                float side = zone.side == "left" ? -1f : 1f;
                Assert.That(
                    zone.side,
                    Is.EqualTo("left").Or.EqualTo("right"));
                Assert.That(
                    zone.unity_center[0],
                    Is.EqualTo(
                        side * (
                            manifest.dimensions_m.width * 0.5f -
                            SupermarketEntranceGeometry
                                .ExteriorWallInset))
                        .Within(0.0001f));
            }

            Assert.That(
                manifest.parts.Select(part => part.sheet).Distinct(),
                Is.EquivalentTo(RequiredSheets));
            for (int index = 0; index < RequiredSheets.Length; index++)
            {
                Assert.That(
                    SupermarketExteriorSurfaceAppearance.TryResolveSheet(
                        RequiredSheets[index],
                        out _),
                    Is.True,
                    $"No runtime surface resolves '{RequiredSheets[index]}'.");
            }

            Assert.That(
                manifest.parts.Any(part =>
                    part.group == "frontage" &&
                    part.role == "exterior_glass"),
                Is.True,
                "The authored storefront has no semantic glass part.");
            AssertRole(manifest, "exterior_door");
            AssertRole(manifest, "exterior_roof");
            AssertRole(manifest, "exterior_roof_equipment");
            AssertRole(manifest, "exterior_service");

            Assert.That(manifest.surface_clearance_contract, Is.Not.Null);
            Assert.That(
                manifest.surface_clearance_contract
                    .opaque_overlay_min_clearance_m,
                Is.EqualTo(
                    SupermarketEntranceGeometry.MinimumOpaqueClearance)
                    .Within(0.0001f));
            Assert.That(
                manifest.surface_clearance_contract
                    .runtime_foundation_inset_m,
                Is.EqualTo(
                    SupermarketEntranceGeometry.FoundationInset)
                    .Within(0.0001f));
            Assert.That(
                CitySpecialBuildingWorldBuilder
                    .SupermarketFoundationInset,
                Is.EqualTo(
                    manifest.surface_clearance_contract
                        .runtime_foundation_inset_m)
                    .Within(0.0001f));
            AssertFoundationClearsVisibleWalls(manifest);
            Assert.That(
                manifest.surface_clearance_contract.fascia_bands,
                Is.EqualTo("authored_uv_atlas_no_overlay_geometry"));

            AssertPassiveImporter();
            AssertPrefabRegistry(manifest, door);
        }

        private static void AssertFoundationClearsVisibleWalls(
            SupermarketManifest manifest)
        {
            float halfWidth = manifest.dimensions_m.width * 0.5f -
                manifest.surface_clearance_contract
                    .runtime_foundation_inset_m;
            float halfDepth = manifest.dimensions_m.depth * 0.5f -
                manifest.surface_clearance_contract
                    .runtime_foundation_inset_m;
            float minimum = manifest.surface_clearance_contract
                .opaque_overlay_min_clearance_m;

            SupermarketManifestPart left = manifest.parts.Single(
                part => part.name == "Left Rendered Wall");
            SupermarketManifestPart right = manifest.parts.Single(
                part => part.name == "Right Rendered Wall");
            SupermarketManifestPart rear = manifest.parts.Single(
                part => part.name == "Rear Rendered Wall");
            SupermarketManifestPart front = manifest.parts.Single(
                part => part.name == "Front Brick Wings");
            float[] clearances =
            {
                Mathf.Abs(left.bounds_min[0]) - halfWidth,
                right.bounds_max[0] - halfWidth,
                Mathf.Abs(rear.bounds_min[1]) - halfDepth,
                front.bounds_max[1] - halfDepth,
            };

            for (int index = 0; index < clearances.Length; index++)
            {
                Assert.That(
                    clearances[index],
                    Is.GreaterThanOrEqualTo(minimum - 0.0001f),
                    $"Foundation clearance {index} is too small.");
            }
        }

        private static void AssertPassiveImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the exterior did not import");
            Assert.That(importer.animationType,
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
            SupermarketManifest manifest,
            SupermarketManifestAnchor door)
        {
            GameObject prefab = SupermarketExteriorModelResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                SupermarketExteriorAssetRegistry registry =
                    instance.GetComponent<SupermarketExteriorAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(instance.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(registry.DesignId,
                    Is.EqualTo(manifest.design_id));
                Assert.That(registry.BuildSignature,
                    Is.EqualTo(manifest.build_signature));
                Assert.That(registry.BuildSignature, Is.Not.Empty);
                Assert.That(registry.SourceGeneratorVersion,
                    Is.EqualTo(manifest.generator_version));
                Assert.That(registry.SourceTriangleCount,
                    Is.EqualTo(manifest.triangle_count));
                Assert.That(registry.Dimensions.Width,
                    Is.EqualTo(15.5f).Within(0.0001f));
                Assert.That(registry.Dimensions.Depth,
                    Is.EqualTo(15.5f).Within(0.0001f));
                Assert.That(registry.Dimensions.Height,
                    Is.EqualTo(6.4f).Within(0.0001f));

                Assert.That(registry.Parts.Count,
                    Is.EqualTo(manifest.parts.Length));
                var parts = new Dictionary<string, SupermarketManifestPart>(
                    StringComparer.Ordinal);
                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    parts.Add(manifest.parts[index].name, manifest.parts[index]);
                }

                for (int index = 0; index < registry.Parts.Count; index++)
                {
                    SupermarketExteriorPartBinding binding =
                        registry.Parts[index];
                    Assert.That(binding, Is.Not.Null);
                    Assert.That(
                        parts.TryGetValue(
                            binding.SourceName,
                            out SupermarketManifestPart expected),
                        Is.True);
                    Assert.That(binding.Renderer, Is.Not.Null);
                    Assert.That(binding.Role, Is.EqualTo(expected.role));
                    Assert.That(binding.Group, Is.EqualTo(expected.group));
                    Assert.That(binding.Sheet, Is.EqualTo(expected.sheet));
                }

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

        private static SupermarketManifest LoadManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                $"'{ManifestPath}' is missing; run the Blender generator");
            SupermarketManifest manifest =
                JsonUtility.FromJson<SupermarketManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.dimensions_m, Is.Not.Null);
            Assert.That(manifest.anchors, Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.parts, Is.Not.Null.And.Not.Empty);
            return manifest;
        }

        private static void AssertRole(
            SupermarketManifest manifest,
            string role)
        {
            Assert.That(
                manifest.parts.Any(part => part.role == role),
                Is.True,
                $"The exterior has no semantic '{role}' part.");
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
            Assert.That(actual.x,
                Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y,
                Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(actual.z,
                Is.EqualTo(expected.z).Within(Tolerance));
        }

        [Serializable]
        private sealed class SupermarketManifest
        {
            public string generator_version;
            public string design_id;
            public SupermarketManifestDimensions dimensions_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int triangle_count;
            public SupermarketManifestAnchor[] anchors;
            public SupermarketSpotlightMountZone[]
                yard_spotlight_mount_zones;
            public SupermarketManifestPart[] parts;
            public SupermarketSurfaceClearanceContract
                surface_clearance_contract;
            public string build_signature;
        }

        [Serializable]
        private sealed class SupermarketManifestDimensions
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class SupermarketManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
            public float[] unity_local_position;
        }

        [Serializable]
        private sealed class SupermarketManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public float[] bounds_min;
            public float[] bounds_max;
        }

        [Serializable]
        private sealed class SupermarketSpotlightMountZone
        {
            public string side;
            public float[] unity_center;
        }

        [Serializable]
        private sealed class SupermarketSurfaceClearanceContract
        {
            public float opaque_overlay_min_clearance_m;
            public float runtime_foundation_inset_m;
            public string fascia_bands;
        }
    }
}
