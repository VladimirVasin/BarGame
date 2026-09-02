using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the measured seam between the deterministic Blender interior and
    /// the existing data-first supermarket plan. The model owns passive visual
    /// geometry and product support anchors; the plan continues to own
    /// traversal, product placement/lifetime, lights and interaction state.
    /// </summary>
    public sealed class SupermarketInteriorModelContractTests
    {
        private const string ModelPath =
            "Assets/Supermarket/Interior/Models/SupermarketInterior3D.fbx";
        private const string ManifestPath =
            "Assets/Supermarket/Interior/Models/SupermarketInterior3D.json";
        private const string ExpectedDesignId = "supermarket_interior_v1";
        private const float PositionTolerance = 0.01f;
        private const float BoundsTolerance = 0.05f;

        private static readonly string[] RequiredAnchorRoles =
        {
            "entrance",
            "room_centre",
            "shelf_dry",
            "shelf_pantry",
            "shelf_cold",
            "checkout",
            "stockroom",
            "cashier",
            "cctv_mount_01",
            "cctv_head_01",
            "cctv_mount_02",
            "cctv_head_02",
            "cctv_mount_03",
            "cctv_head_03",
            "cctv_mount_04",
            "cctv_head_04",
            "tube_01",
            "tube_02",
            "tube_03",
            "tube_04",
            "product_instant_noodles",
            "product_day_old_loaf",
            "product_vodka_bottle",
            "product_closed_stew_can",
            "product_chicken_egg",
        };

        private static readonly string[] RequiredPartRoles =
        {
            "floor",
            "ceiling",
            "wall",
            "entrance_frame",
            "ceiling_grid",
            "fluorescent_housing",
            "fluorescent_tube",
            "shelf_dry",
            "shelf_pantry",
            "shelf_cold",
            "checkout",
            "register",
            "bag_rack",
            "stockroom_facade",
            "carton",
            "cctv_mount",
            "cctv_head",
            "trim",
            "grime",
        };

        private static readonly string[] SurfaceSheets =
        {
            "Linoleum",
            "WallPaint",
            "Ceiling",
            "ShelfMetal",
            "Counter",
            "Cardboard",
        };

        [Test]
        public void Manifest_DeclaresFixedMetrePassiveSemanticInterior()
        {
            SupermarketInteriorManifest manifest = LoadManifest();

            Assert.That(manifest.design_id, Is.EqualTo(ExpectedDesignId));
            Assert.That(manifest.generator_version, Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.dimensions_m, Is.Not.Null);
            Assert.That(manifest.dimensions_m.width, Is.EqualTo(16f));
            Assert.That(manifest.dimensions_m.depth, Is.EqualTo(11f));
            Assert.That(manifest.dimensions_m.height, Is.EqualTo(3.6f));
            Assert.That(manifest.wall_thickness_m, Is.EqualTo(0.25f));
            Assert.That(manifest.entrance_opening_m, Is.Not.Null);
            Assert.That(manifest.entrance_opening_m.width, Is.EqualTo(2.4f));
            Assert.That(manifest.entrance_opening_m.height, Is.EqualTo(2.94f));

            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.rigidbodies, Is.False);
            Assert.That(manifest.audio_sources, Is.False);
            Assert.That(manifest.materials, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.build_signature, Is.Not.Null.And.Not.Empty);

            Assert.That(manifest.anchors, Is.Not.Null);
            Assert.That(
                manifest.anchors.Select(anchor => anchor.name),
                Is.EquivalentTo(RequiredAnchorRoles));
            Assert.That(
                manifest.anchors.Select(anchor => anchor.role),
                Is.EquivalentTo(RequiredAnchorRoles));
            foreach (SupermarketInteriorManifestAnchor anchor in
                     manifest.anchors)
            {
                Assert.That(anchor.name, Is.EqualTo(anchor.role));
                AssertVectorArray(anchor.local_position, anchor.role);
                AssertVectorArray(anchor.unity_local_position, anchor.role);
            }

            Assert.That(manifest.parts, Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.mesh_count, Is.EqualTo(manifest.parts.Length));
            Assert.That(
                manifest.triangle_count,
                Is.EqualTo(manifest.parts.Sum(part => part.triangles)));
            Assert.That(
                manifest.parts.Select(part => part.name).Distinct().Count(),
                Is.EqualTo(manifest.parts.Length),
                "Every manifest part needs a unique renderer name.");

            var allowedSheets = new HashSet<string>(
                SurfaceSheets,
                StringComparer.Ordinal);
            var seenSheets = new HashSet<string>(StringComparer.Ordinal);
            var seenRoles = new HashSet<string>(StringComparer.Ordinal);
            foreach (SupermarketInteriorManifestPart part in manifest.parts)
            {
                Assert.That(part.name, Is.Not.Null.And.Not.Empty);
                Assert.That(part.role, Is.Not.Null.And.Not.Empty, part.name);
                Assert.That(part.group, Is.EqualTo("fixed"), part.name);
                Assert.That(
                    string.IsNullOrEmpty(part.sheet) ||
                    allowedSheets.Contains(part.sheet),
                    Is.True,
                    $"Part '{part.name}' uses unknown sheet '{part.sheet}'.");
                Assert.That(part.base_color, Has.Length.EqualTo(4), part.name);
                Assert.That(part.vertices, Is.GreaterThan(0), part.name);
                Assert.That(part.triangles, Is.GreaterThan(0), part.name);
                Assert.That(
                    part.shadows,
                    Is.EqualTo(part.casts_shadows),
                    $"Part '{part.name}' has two shadow declarations.");
                AssertVectorArray(part.bounds_min, part.name);
                AssertVectorArray(part.bounds_max, part.name);
                seenRoles.Add(part.role);
                if (!string.IsNullOrEmpty(part.sheet))
                {
                    seenSheets.Add(part.sheet);
                }
            }

            Assert.That(seenSheets, Is.EquivalentTo(SurfaceSheets));
            foreach (string role in RequiredPartRoles)
            {
                Assert.That(
                    seenRoles,
                    Does.Contain(role),
                    $"The authored room has no semantic '{role}' part.");
            }
        }

        [Test]
        public void ImportedModel_UsesThePassiveInteriorImportContract()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;

            Assert.That(importer, Is.Not.Null, "The interior model did not import.");
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

        [Test]
        public void RuntimePrefab_BindsEveryPartAndCarriesOnlyPassiveGeometry()
        {
            SupermarketInteriorManifest manifest = LoadManifest();
            GameObject prefab = SupermarketInteriorModelResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                SupermarketInteriorAssetRegistry registry =
                    instance.GetComponent<SupermarketInteriorAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.DesignId,
                    Is.EqualTo(ExpectedDesignId));
                Assert.That(
                    SupermarketInteriorAssetRegistry.ExpectedDesignId,
                    Is.EqualTo(ExpectedDesignId));
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(registry.Dimensions.Width, Is.EqualTo(16f));
                Assert.That(registry.Dimensions.Depth, Is.EqualTo(11f));
                Assert.That(registry.Dimensions.Height, Is.EqualTo(3.6f));
                Assert.That(registry.Dimensions.WallThickness,
                    Is.EqualTo(0.25f));
                Assert.That(registry.Dimensions.EntranceWidth,
                    Is.EqualTo(2.4f));
                Assert.That(registry.Dimensions.EntranceHeight,
                    Is.EqualTo(2.94f));
                Assert.That(registry.SourceGeneratorVersion,
                    Is.EqualTo(manifest.generator_version));
                Assert.That(registry.SourceTriangleCount,
                    Is.EqualTo(manifest.triangle_count));
                Assert.That(registry.BuildSignature,
                    Is.EqualTo(manifest.build_signature));

                Assert.That(instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(instance.GetComponentsInChildren<AudioSource>(true),
                    Is.Empty);
                Assert.That(instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);

                Assert.That(registry.Parts,
                    Has.Count.EqualTo(manifest.parts.Length));
                Assert.That(
                    registry.Parts.Select(part => part.SourceName),
                    Is.EquivalentTo(manifest.parts.Select(part => part.name)));

                Dictionary<string, SupermarketInteriorManifestPart>
                    manifestParts = manifest.parts.ToDictionary(
                        part => part.name,
                        StringComparer.Ordinal);
                var boundRenderers = new HashSet<Renderer>();
                var seenSheets = new HashSet<string>(StringComparer.Ordinal);
                foreach (SupermarketInteriorPartBinding part in registry.Parts)
                {
                    Assert.That(part, Is.Not.Null);
                    Assert.That(
                        manifestParts.TryGetValue(
                            part.SourceName,
                            out SupermarketInteriorManifestPart authored),
                        Is.True,
                        part.SourceName);
                    Assert.That(part.Role, Is.EqualTo(authored.role),
                        part.SourceName);
                    Assert.That(part.Group, Is.EqualTo(authored.group),
                        part.SourceName);
                    Assert.That(part.Sheet, Is.EqualTo(authored.sheet),
                        part.SourceName);
                    Assert.That(part.SurfaceKind, Is.EqualTo(authored.sheet),
                        part.SourceName);
                    Assert.That(part.Emissive, Is.EqualTo(authored.emissive),
                        part.SourceName);
                    Assert.That(
                        part.CastsShadows,
                        Is.EqualTo(authored.casts_shadows),
                        part.SourceName);
                    AssertColorApproximatelyEqual(
                        part.BaseColor,
                        ToColor(authored.base_color),
                        part.SourceName);
                    Assert.That(part.Renderer, Is.Not.Null, part.SourceName);
                    Assert.That(boundRenderers.Add(part.Renderer), Is.True,
                        $"Renderer '{part.Renderer.name}' is bound twice.");
                    Assert.That(part.Renderer.sharedMaterials,
                        Has.Length.EqualTo(1), part.SourceName);
                    Material expectedMaterial = part.Emissive
                        ? CityNightResources.EmissiveMaterial
                        : RuntimePrimitiveFactory.DefaultMaterial;
                    Assert.That(part.Renderer.sharedMaterial,
                        Is.SameAs(expectedMaterial), part.SourceName);
                    Assert.That(
                        part.Renderer.shadowCastingMode,
                        Is.EqualTo(part.CastsShadows
                            ? ShadowCastingMode.On
                            : ShadowCastingMode.Off),
                        part.SourceName);
                    Assert.That(
                        part.Renderer.receiveShadows,
                        Is.EqualTo(part.CastsShadows),
                        part.SourceName);
                    Assert.That(
                        registry.TryGetPart(part.SourceName, out var resolved),
                        Is.True,
                        part.SourceName);
                    Assert.That(resolved, Is.SameAs(part));
                    if (!string.IsNullOrEmpty(part.Sheet))
                    {
                        seenSheets.Add(part.Sheet);
                    }
                }

                Assert.That(
                    boundRenderers,
                    Is.EquivalentTo(
                        instance.GetComponentsInChildren<Renderer>(true)));
                Assert.That(seenSheets, Is.EquivalentTo(SurfaceSheets));
                AssertBoundsApproximatelyEqual(
                    registry.LocalBounds,
                    CalculateLocalRendererBounds(instance.transform));
                AssertBoundsApproximatelyEqual(
                    registry.LocalBounds,
                    ManifestBoundsInUnity(manifest));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RuntimePrefab_AnchorsMatchThePublishedLayoutPlan()
        {
            SupermarketInteriorLayoutPlan plan =
                SupermarketInteriorLayoutPlanner.Generate(20260902);
            GameObject prefab = SupermarketInteriorModelResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                SupermarketInteriorAssetRegistry registry =
                    instance.GetComponent<SupermarketInteriorAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(
                    registry.Anchors.Select(anchor => anchor.Role),
                    Is.EquivalentTo(RequiredAnchorRoles));

                AssertAnchor(registry, instance.transform, "entrance",
                    new Vector3(0f, 0f, -plan.RoomSize.y * 0.5f));
                AssertAnchor(registry, instance.transform, "room_centre",
                    Vector3.zero);
                AssertAnchor(registry, instance.transform, "shelf_dry",
                    FindShelf(plan, SupermarketInteriorLayoutPlanner
                        .DryGoodsShelfId).RootPosition);
                AssertAnchor(registry, instance.transform, "shelf_pantry",
                    FindShelf(plan, SupermarketInteriorLayoutPlanner
                        .PantryShelfId).RootPosition);
                AssertAnchor(registry, instance.transform, "shelf_cold",
                    FindShelf(plan, SupermarketInteriorLayoutPlanner
                        .ColdShelfId).RootPosition);
                AssertAnchor(registry, instance.transform, "checkout",
                    FixtureRoot(plan, SupermarketFixtureKind.Checkout));
                AssertAnchor(registry, instance.transform, "stockroom",
                    FixtureRoot(plan, SupermarketFixtureKind.StockroomFacade));
                AssertAnchor(registry, instance.transform, "cashier",
                    plan.Cashier.Position);

                for (int shelfIndex = 0;
                     shelfIndex < plan.Shelves.Count;
                     shelfIndex++)
                {
                    SupermarketShelfPlan shelf = plan.Shelves[shelfIndex];
                    for (int productIndex = 0;
                         productIndex < shelf.Products.Count;
                         productIndex++)
                    {
                        SupermarketProductSlotPlan product =
                            shelf.Products[productIndex];
                        AssertAnchor(
                            registry,
                            instance.transform,
                            ProductAnchorRole(product.ItemId),
                            shelf.RootPosition + product.LocalPosition);
                    }
                }

                Vector3[] heads = SupermarketSecurityCameraWorldBuilder
                    .ResolveHeadPositions(plan);
                for (int index = 0; index < heads.Length; index++)
                {
                    string suffix = (index + 1).ToString("00");
                    AssertAnchor(
                        registry,
                        instance.transform,
                        $"cctv_head_{suffix}",
                        heads[index]);
                    AssertAnchor(
                        registry,
                        instance.transform,
                        $"cctv_mount_{suffix}",
                        new Vector3(
                            heads[index].x,
                            plan.RoomHeight,
                            heads[index].z));
                }

                float[] tubeXs = { -5.2f, -1.75f, 1.75f, 5.2f };
                for (int index = 0; index < tubeXs.Length; index++)
                {
                    AssertAnchor(
                        registry,
                        instance.transform,
                        $"tube_{index + 1:00}",
                        new Vector3(tubeXs[index], plan.RoomHeight - 0.19f, 0f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WorldBuilder_MakesEachPriceTagAProductDescendant()
        {
            var parent = new GameObject("Supermarket Product Tag Test");
            try
            {
                SupermarketInteriorLayoutPlan plan =
                    SupermarketInteriorLayoutPlanner.Generate(20260902);
                SupermarketInteriorWorldResult result =
                    SupermarketInteriorWorldBuilder.Build(
                        parent.transform,
                        plan);
                int productCount = 0;
                var ownedTags = new HashSet<Transform>();

                foreach (SupermarketShelfView shelf in result.Shelves)
                {
                    foreach (SupermarketProductView product in shelf.Products)
                    {
                        productCount++;
                        Transform[] tags = product.OriginalRoot
                            .GetComponentsInChildren<Transform>(true)
                            .Where(candidate => candidate.name.StartsWith(
                                "Product Price Tag ",
                                StringComparison.Ordinal))
                            .ToArray();
                        Assert.That(
                            tags,
                            Has.Length.EqualTo(1),
                            $"Product '{product.SourceId}' must own exactly " +
                            "one price tag.");
                        Assert.That(
                            ownedTags.Add(tags[0]),
                            Is.True,
                            $"Price tag '{tags[0].name}' is shared by more " +
                            "than one product.");
                    }
                }

                Assert.That(productCount, Is.EqualTo(5));
                Assert.That(
                    result.Root.GetComponentsInChildren<Transform>(true)
                        .Count(candidate => candidate.name.StartsWith(
                            "Product Price Tag ",
                            StringComparison.Ordinal)),
                    Is.EqualTo(productCount));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static SupermarketShelfPlan FindShelf(
            SupermarketInteriorLayoutPlan plan,
            string shelfId)
        {
            Assert.That(plan.TryGetShelf(shelfId, out var shelf), Is.True);
            return shelf;
        }

        private static Vector3 FixtureRoot(
            SupermarketInteriorLayoutPlan plan,
            SupermarketFixtureKind kind)
        {
            Assert.That(plan.TryGetFixture(kind, out var fixture), Is.True);
            return new Vector3(
                fixture.Bounds.center.x,
                0f,
                fixture.Bounds.center.y);
        }

        private static void AssertAnchor(
            SupermarketInteriorAssetRegistry registry,
            Transform instanceRoot,
            string role,
            Vector3 expectedLocalPosition)
        {
            Assert.That(registry.TryGetAnchor(role, out Transform anchor),
                Is.True, $"Missing authored anchor '{role}'.");
            Assert.That(anchor, Is.Not.Null);
            Vector3 actual = instanceRoot.InverseTransformPoint(anchor.position);
            Assert.That(
                Vector3.Distance(actual, expectedLocalPosition),
                Is.LessThanOrEqualTo(PositionTolerance),
                $"Anchor '{role}' is at {actual}, expected " +
                $"{expectedLocalPosition}.");
        }

        private static string ProductAnchorRole(InventoryItemId itemId)
        {
            switch (itemId)
            {
                case InventoryItemId.InstantNoodles:
                    return "product_instant_noodles";
                case InventoryItemId.DayOldLoaf:
                    return "product_day_old_loaf";
                case InventoryItemId.VodkaBottle:
                    return "product_vodka_bottle";
                case InventoryItemId.ClosedStewCan:
                    return "product_closed_stew_can";
                case InventoryItemId.ChickenEgg:
                    return "product_chicken_egg";
                default:
                    throw new ArgumentOutOfRangeException(nameof(itemId));
            }
        }

        private static SupermarketInteriorManifest LoadManifest()
        {
            Assert.That(File.Exists(ManifestPath), Is.True,
                $"Missing supermarket interior manifest '{ManifestPath}'.");
            SupermarketInteriorManifest manifest = JsonUtility.FromJson<
                SupermarketInteriorManifest>(File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            return manifest;
        }

        private static Bounds ManifestBoundsInUnity(
            SupermarketInteriorManifest manifest)
        {
            AssertVectorArray(manifest.bounds_min, "manifest bounds_min");
            AssertVectorArray(manifest.bounds_max, "manifest bounds_max");
            var bounds = new Bounds();
            bounds.SetMinMax(
                new Vector3(
                    manifest.bounds_min[0],
                    manifest.bounds_min[2],
                    manifest.bounds_min[1]),
                new Vector3(
                    manifest.bounds_max[0],
                    manifest.bounds_max[2],
                    manifest.bounds_max[1]));
            return bounds;
        }

        private static Bounds CalculateLocalRendererBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            bool started = false;
            Bounds combined = default;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Bounds localBounds = renderer.localBounds;
                Vector3 center = localBounds.center;
                Vector3 extents = localBounds.extents;
                Matrix4x4 rendererToRoot =
                    worldToRoot * renderer.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 local = rendererToRoot.MultiplyPoint3x4(
                                center + Vector3.Scale(
                                    extents,
                                    new Vector3(x, y, z)));
                            if (!started)
                            {
                                combined = new Bounds(local, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                combined.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            return combined;
        }

        private static void AssertBoundsApproximatelyEqual(
            Bounds actual,
            Bounds expected)
        {
            Assert.That(
                Vector3.Distance(actual.center, expected.center),
                Is.LessThanOrEqualTo(BoundsTolerance),
                $"Bounds centers differ: {actual} / {expected}.");
            Assert.That(
                Vector3.Distance(actual.size, expected.size),
                Is.LessThanOrEqualTo(BoundsTolerance),
                $"Bounds sizes differ: {actual} / {expected}.");
        }

        private static Color ToColor(float[] values)
        {
            Assert.That(values, Has.Length.EqualTo(4));
            return new Color(values[0], values[1], values[2], values[3]);
        }

        private static void AssertColorApproximatelyEqual(
            Color actual,
            Color expected,
            string owner)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f), owner);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f), owner);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f), owner);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f), owner);
        }

        private static void AssertVectorArray(float[] values, string owner)
        {
            Assert.That(values, Has.Length.EqualTo(3), owner);
            for (int index = 0; index < values.Length; index++)
            {
                Assert.That(float.IsNaN(values[index]), Is.False, owner);
                Assert.That(float.IsInfinity(values[index]), Is.False, owner);
            }
        }

        [Serializable]
        private sealed class SupermarketInteriorManifest
        {
            public string generator_version;
            public string design_id;
            public SupermarketInteriorManifestDimensions dimensions_m;
            public float wall_thickness_m;
            public SupermarketInteriorManifestOpening entrance_opening_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public bool rigidbodies;
            public bool audio_sources;
            public bool materials;
            public int animation_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public int mesh_count;
            public int triangle_count;
            public SupermarketInteriorManifestAnchor[] anchors;
            public SupermarketInteriorManifestPart[] parts;
            public string build_signature;
        }

        [Serializable]
        private sealed class SupermarketInteriorManifestDimensions
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class SupermarketInteriorManifestOpening
        {
            public float width;
            public float height;
        }

        [Serializable]
        private sealed class SupermarketInteriorManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
            public float[] unity_local_position;
        }

        [Serializable]
        private sealed class SupermarketInteriorManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public bool emissive;
            public bool casts_shadows;
            public bool shadows;
            public float[] base_color;
            public int vertices;
            public int triangles;
            public float[] bounds_min;
            public float[] bounds_max;
        }
    }
}
