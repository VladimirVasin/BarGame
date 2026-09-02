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
    /// Locks the six-item Blender catalog to passive, grounded Resources
    /// prefabs and covers the scale-safe factory wrapper used by world,
    /// refrigerator and inventory presentations.
    /// </summary>
    public sealed class SupermarketProductModelContractTests
    {
        private const string ModelPath =
            "Assets/Supermarket/Products/Models/SupermarketProducts3D.fbx";
        private const string ManifestPath =
            "Assets/Supermarket/Products/Models/SupermarketProducts3D.json";
        private const string ExpectedDesignId =
            "supermarket_product_pack_v1";
        private const string ExpectedRootName =
            "ROOT_SupermarketProducts3D";
        private const float PivotTolerance = 0.002f;
        private const float CentreTolerance = 0.012f;
        private const float BoundsTolerance = 0.012f;

        private static readonly ProductContract[] Contracts =
        {
            new ProductContract(
                "instant_noodles",
                "ITEM_instant_noodles",
                InventoryItemId.InstantNoodles,
                "Instant Noodles",
                new[] { "packet", "seal", "label" }),
            new ProductContract(
                "day_old_loaf",
                "ITEM_day_old_loaf",
                InventoryItemId.DayOldLoaf,
                "Day Old Loaf",
                new[] { "bread", "crumb", "score" }),
            new ProductContract(
                "vodka_bottle",
                "ITEM_vodka_bottle",
                InventoryItemId.VodkaBottle,
                "Vodka Bottle",
                new[] { "glass", "label", "liquid", "cap" }),
            new ProductContract(
                "closed_stew_can",
                "ITEM_closed_stew_can",
                InventoryItemId.ClosedStewCan,
                "Closed Stew Can",
                new[] { "can_body", "label", "rim", "pull_tab" }),
            new ProductContract(
                "open_stew_can",
                "ITEM_open_stew_can",
                InventoryItemId.OpenStewCan,
                "Open Stew Can",
                new[]
                {
                    "can_body", "label", "rim", "stew", "lid", "pull_tab"
                }),
            new ProductContract(
                "chicken_egg",
                "ITEM_chicken_egg",
                InventoryItemId.ChickenEgg,
                "Chicken Egg",
                new[] { "carton", "shell", "shell_mark" }),
        };

        [Test]
        public void Manifest_DeclaresSixGroundedPassiveBudgetedItems()
        {
            ProductManifest manifest = LoadManifest();

            Assert.That(manifest.design_id, Is.EqualTo(ExpectedDesignId));
            Assert.That(manifest.root_name, Is.EqualTo(ExpectedRootName));
            Assert.That(
                manifest.layout_mode,
                Is.EqualTo("coincident_identity_item_roots_for_extraction"));
            Assert.That(manifest.pivot_contract,
                Is.EqualTo("bottom_centre"));
            Assert.That(manifest.generator_version,
                Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.blender_version,
                Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.build_signature,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(manifest.authored_text, Is.Empty);
            Assert.That(manifest.brands, Is.Empty);
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.materials, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.rigidbodies, Is.False);
            Assert.That(manifest.audio_sources, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            Assert.That(manifest.item_count, Is.EqualTo(Contracts.Length));
            Assert.That(manifest.items,
                Has.Length.EqualTo(Contracts.Length));
            Assert.That(manifest.mesh_count,
                Is.Positive.And.LessThanOrEqualTo(64));
            Assert.That(manifest.triangle_count,
                Is.Positive.And.LessThanOrEqualTo(12000));
            Assert.That(manifest.budgets, Is.Not.Null);
            Assert.That(manifest.budgets.maximum_renderers,
                Is.Positive.And.LessThanOrEqualTo(64));
            Assert.That(manifest.budgets.maximum_triangles,
                Is.Positive.And.LessThanOrEqualTo(12000));
            Assert.That(manifest.mesh_count,
                Is.LessThanOrEqualTo(manifest.budgets.maximum_renderers));
            Assert.That(manifest.triangle_count,
                Is.LessThanOrEqualTo(manifest.budgets.maximum_triangles));

            Assert.That(
                manifest.items.Select(item => item.id),
                Is.EqualTo(Contracts.Select(contract => contract.StableId)));
            Assert.That(
                manifest.items.Select(item => item.source_name),
                Is.EqualTo(Contracts.Select(contract => contract.SourceName)));
            Assert.That(manifest.parts,
                Has.Length.EqualTo(manifest.mesh_count));
            Assert.That(
                manifest.parts.Select(part => part.name).Distinct().Count(),
                Is.EqualTo(manifest.mesh_count));

            int itemTriangles = 0;
            int partTriangles = 0;
            for (int index = 0; index < Contracts.Length; index++)
            {
                ProductContract contract = Contracts[index];
                ProductManifestItem item = manifest.items[index];
                Assert.That(item.id, Is.EqualTo(contract.StableId));
                Assert.That(item.source_name, Is.EqualTo(contract.SourceName));
                Assert.That(item.role, Is.EqualTo(contract.StableId));
                Assert.That(item.pivot.kind, Is.EqualTo("bottom_centre"));
                AssertZeroVector(item.pivot.source_position, item.id);
                AssertZeroVector(item.pivot.unity_position, item.id);
                Assert.That(item.mesh_count,
                    Is.Positive.And.LessThanOrEqualTo(64));
                Assert.That(item.triangle_count,
                    Is.Positive.And.LessThanOrEqualTo(4000));
                Assert.That(item.parts,
                    Has.Length.EqualTo(item.mesh_count));
                Assert.That(item.parts.Distinct().Count(),
                    Is.EqualTo(item.parts.Length));
                AssertGroundedBounds(item);

                ProductManifestPart[] parts = manifest.parts
                    .Where(part => part.item_id == item.id)
                    .ToArray();
                Assert.That(parts, Has.Length.EqualTo(item.mesh_count));
                Assert.That(
                    parts.Select(part => part.name),
                    Is.EquivalentTo(item.parts));
                Assert.That(
                    parts.Select(part => part.role).Distinct(),
                    Is.EquivalentTo(contract.RequiredRoles));
                Assert.That(parts.Sum(part => part.triangles),
                    Is.EqualTo(item.triangle_count));
                foreach (ProductManifestPart part in parts)
                {
                    Assert.That(part.group, Is.EqualTo("render"), part.name);
                    Assert.That(part.surface,
                        Is.Not.Null.And.Not.Empty, part.name);
                    Assert.That(part.sheet, Is.Empty, part.name);
                    Assert.That(part.base_color,
                        Has.Length.EqualTo(4), part.name);
                    Assert.That(part.casts_shadows, Is.True, part.name);
                    Assert.That(part.shadows,
                        Is.EqualTo(part.casts_shadows), part.name);
                    Assert.That(part.vertices, Is.Positive, part.name);
                    Assert.That(part.triangles, Is.Positive, part.name);
                    AssertSourceUnityBoundsAgree(part);
                    partTriangles += part.triangles;
                }

                itemTriangles += item.triangle_count;
            }

            Assert.That(itemTriangles, Is.EqualTo(manifest.triangle_count));
            Assert.That(partTriangles, Is.EqualTo(manifest.triangle_count));
        }

        [Test]
        public void ImportedCatalog_UsesPassiveFixedMetreImportContract()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;

            Assert.That(importer, Is.Not.Null);
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
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        [Test]
        public void ResourcesPrefabs_BindEveryPartAndRemainPassive()
        {
            ProductManifest manifest = LoadManifest();
            for (int index = 0; index < Contracts.Length; index++)
            {
                ProductContract contract = Contracts[index];
                ProductManifestItem item = manifest.items[index];
                GameObject prefab =
                    SupermarketProductModelResources.LoadPrefab(
                        contract.ItemId);
                Assert.That(prefab, Is.Not.Null, contract.StableId);
                Assert.That(
                    AssetDatabase.GetAssetPath(prefab),
                    Is.EqualTo(
                        "Assets/Resources/" +
                        SupermarketProductModelResources.GetResourcePath(
                            contract.ItemId) +
                        ".prefab"));

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    SupermarketProductAssetRegistry registry =
                        instance.GetComponent<
                            SupermarketProductAssetRegistry>();
                    Assert.That(registry, Is.Not.Null, item.id);
                    registry.ValidateOrThrow();
                    Assert.That(registry.ItemId,
                        Is.EqualTo(contract.ItemId));
                    Assert.That(registry.ModelRoot,
                        Is.SameAs(instance.transform));
                    Assert.That(registry.DesignId,
                        Is.EqualTo(ExpectedDesignId));
                    Assert.That(registry.BuildSignature,
                        Is.EqualTo(manifest.build_signature));
                    Assert.That(registry.SourceGeneratorVersion,
                        Is.EqualTo(manifest.generator_version));
                    Assert.That(registry.SourceTriangleCount,
                        Is.EqualTo(item.triangle_count));
                    Assert.That(
                        registry.Parts.Count,
                        Is.EqualTo(item.parts.Length));
                    AssertPassive(instance, item.id);

                    Bounds expected = BoundsFromArrays(
                        item.unity_bounds_min,
                        item.unity_bounds_max);
                    AssertBoundsNear(registry.LocalBounds, expected, item.id);
                    AssertBoundsNear(
                        CalculateLocalRendererBounds(instance.transform),
                        expected,
                        item.id);
                    Assert.That(
                        instance.GetComponentsInChildren<Transform>(true)
                            .Count(transform =>
                                transform.name == contract.SourceName),
                        Is.EqualTo(1),
                        item.id);

                    Dictionary<string, ProductManifestPart> parts =
                        manifest.parts
                            .Where(part => part.item_id == item.id)
                            .ToDictionary(part => part.name);
                    registry.ApplyAppearance();
                    var renderers = new HashSet<Renderer>();
                    foreach (SupermarketProductPartBinding binding in
                             registry.Parts)
                    {
                        Assert.That(parts.TryGetValue(
                            binding.SourceName,
                            out ProductManifestPart source), Is.True);
                        Assert.That(binding.Role, Is.EqualTo(source.role));
                        Assert.That(binding.Renderer, Is.Not.Null);
                        Assert.That(renderers.Add(binding.Renderer), Is.True);
                        Assert.That(binding.Renderer.sharedMaterials,
                            Has.Length.EqualTo(1));
                        Assert.That(binding.Renderer.sharedMaterial,
                            Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                        Assert.That(binding.Renderer.shadowCastingMode,
                            Is.EqualTo(ShadowCastingMode.On));
                        Assert.That(binding.Renderer.receiveShadows, Is.True);
                        AssertColor(binding.Color, source.base_color,
                            binding.SourceName);

                        var block = new MaterialPropertyBlock();
                        binding.Renderer.GetPropertyBlock(block);
                        AssertColor(
                            block.GetColor(Shader.PropertyToID("_BaseColor")),
                            source.base_color,
                            binding.SourceName + " property block");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Factory_UsesAllSixPrefabsAndKeepsSmallOpenCanFitSafe()
        {
            var parent = new GameObject("Product Factory Contract Parent");
            try
            {
                for (int index = 0; index < Contracts.Length; index++)
                {
                    ProductContract contract = Contracts[index];
                    Transform preview =
                        InventoryItemModelFactory.BuildPreviewModel(
                            contract.ItemId,
                            parent.transform);
                    Assert.That(
                        preview.name,
                        Is.EqualTo(
                            $"Inventory Preview {contract.DisplaySuffix}"));
                    Assert.That(preview.localScale,
                        Is.EqualTo(Vector3.one), contract.StableId);
                    SupermarketProductAssetRegistry registry = preview
                        .GetComponentInChildren<
                            SupermarketProductAssetRegistry>(true);
                    Assert.That(registry, Is.Not.Null, contract.StableId);
                    Assert.That(registry.ItemId,
                        Is.EqualTo(contract.ItemId));
                    Assert.That(preview.GetComponentsInChildren<Renderer>(true),
                        Is.Not.Empty, contract.StableId);
                    Assert.That(preview.GetComponentsInChildren<Collider>(true),
                        Is.Empty, contract.StableId);
                }

                Transform smallCan = InventoryItemModelFactory.BuildWorldModel(
                    InventoryItemId.OpenStewCan,
                    parent.transform,
                    Vector3.one * 0.12f);
                Assert.That(smallCan.localScale, Is.EqualTo(Vector3.one));
                Assert.That(smallCan.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Bounds bounds = CalculateLocalRendererBounds(smallCan);
                Assert.That(bounds.size.x, Is.LessThanOrEqualTo(0.1205f));
                Assert.That(bounds.size.y, Is.LessThanOrEqualTo(0.1205f));
                Assert.That(bounds.size.z, Is.LessThanOrEqualTo(0.1205f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static ProductManifest LoadManifest()
        {
            Assert.That(File.Exists(ManifestPath), Is.True);
            ProductManifest manifest = JsonUtility.FromJson<ProductManifest>(
                File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            return manifest;
        }

        private static void AssertGroundedBounds(ProductManifestItem item)
        {
            Bounds bounds = BoundsFromArrays(
                item.unity_bounds_min,
                item.unity_bounds_max);
            Vector3 dimensions = ToVector(item.dimensions_m);
            Vector3 available = ToVector(item.available_size_m);
            Assert.That(Mathf.Abs(bounds.min.y),
                Is.LessThanOrEqualTo(PivotTolerance), item.id);
            Assert.That(Mathf.Abs(bounds.center.x),
                Is.LessThanOrEqualTo(CentreTolerance), item.id);
            Assert.That(Mathf.Abs(bounds.center.z),
                Is.LessThanOrEqualTo(CentreTolerance), item.id);
            Assert.That(Vector3.Distance(bounds.size, dimensions),
                Is.LessThanOrEqualTo(BoundsTolerance), item.id);
            Assert.That(bounds.size.x,
                Is.LessThanOrEqualTo(available.x + BoundsTolerance), item.id);
            Assert.That(bounds.size.y,
                Is.LessThanOrEqualTo(available.y + BoundsTolerance), item.id);
            Assert.That(bounds.size.z,
                Is.LessThanOrEqualTo(available.z + BoundsTolerance), item.id);
            Bounds source = BoundsFromArrays(
                item.bounds_min,
                item.bounds_max);
            Bounds converted = ConvertSourceBounds(source);
            AssertBoundsNear(bounds, converted, item.id + " axes");
            if (item.id == "vodka_bottle")
            {
                Assert.That(bounds.size.y,
                    Is.LessThanOrEqualTo(0.4701f));
            }
        }

        private static void AssertSourceUnityBoundsAgree(
            ProductManifestPart part)
        {
            Bounds source = BoundsFromArrays(
                part.bounds_min,
                part.bounds_max);
            Bounds unity = BoundsFromArrays(
                part.unity_bounds_min,
                part.unity_bounds_max);
            AssertBoundsNear(unity, ConvertSourceBounds(source), part.name);
        }

        private static Bounds ConvertSourceBounds(Bounds source)
        {
            var result = new Bounds();
            result.SetMinMax(
                new Vector3(source.min.x, source.min.z, source.min.y),
                new Vector3(source.max.x, source.max.z, source.max.y));
            return result;
        }

        private static Bounds BoundsFromArrays(float[] minimum, float[] maximum)
        {
            Assert.That(minimum, Has.Length.EqualTo(3));
            Assert.That(maximum, Has.Length.EqualTo(3));
            var result = new Bounds();
            result.SetMinMax(ToVector(minimum), ToVector(maximum));
            Assert.That(result.size.x, Is.Positive);
            Assert.That(result.size.y, Is.Positive);
            Assert.That(result.size.z, Is.Positive);
            return result;
        }

        private static Bounds CalculateLocalRendererBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            bool started = false;
            Bounds result = default;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Bounds local = renderer.localBounds;
                Matrix4x4 rendererToRoot =
                    worldToRoot * renderer.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = rendererToRoot.MultiplyPoint3x4(
                                local.center + Vector3.Scale(
                                    local.extents,
                                    new Vector3(x, y, z)));
                            if (!started)
                            {
                                result = new Bounds(point, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Bounds expected,
            string owner)
        {
            Assert.That(Vector3.Distance(actual.center, expected.center),
                Is.LessThanOrEqualTo(BoundsTolerance), owner);
            Assert.That(Vector3.Distance(actual.size, expected.size),
                Is.LessThanOrEqualTo(BoundsTolerance), owner);
        }

        private static void AssertPassive(GameObject root, string owner)
        {
            Assert.That(root.GetComponentsInChildren<Collider>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<Light>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<Camera>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<AudioSource>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<Animator>(true),
                Is.Empty, owner);
            Assert.That(root.GetComponentsInChildren<Animation>(true),
                Is.Empty, owner);
        }

        private static void AssertZeroVector(float[] values, string owner)
        {
            Assert.That(values, Has.Length.EqualTo(3), owner);
            Assert.That(values.All(value => Mathf.Abs(value) <= PivotTolerance),
                Is.True, owner);
        }

        private static void AssertColor(
            Color actual,
            float[] expected,
            string owner)
        {
            Assert.That(expected, Has.Length.EqualTo(4), owner);
            Assert.That(actual.r,
                Is.EqualTo(expected[0]).Within(0.0001f), owner);
            Assert.That(actual.g,
                Is.EqualTo(expected[1]).Within(0.0001f), owner);
            Assert.That(actual.b,
                Is.EqualTo(expected[2]).Within(0.0001f), owner);
            Assert.That(actual.a,
                Is.EqualTo(expected[3]).Within(0.0001f), owner);
        }

        private static Vector3 ToVector(float[] values)
        {
            Assert.That(values, Has.Length.EqualTo(3));
            return new Vector3(values[0], values[1], values[2]);
        }

        private sealed class ProductContract
        {
            public ProductContract(
                string stableId,
                string sourceName,
                InventoryItemId itemId,
                string displaySuffix,
                string[] requiredRoles)
            {
                StableId = stableId;
                SourceName = sourceName;
                ItemId = itemId;
                DisplaySuffix = displaySuffix;
                RequiredRoles = requiredRoles;
            }

            public string StableId { get; }
            public string SourceName { get; }
            public InventoryItemId ItemId { get; }
            public string DisplaySuffix { get; }
            public string[] RequiredRoles { get; }
        }

        [Serializable]
        private sealed class ProductManifest
        {
            public string generator_version;
            public string blender_version;
            public string design_id;
            public string root_name;
            public string layout_mode;
            public string pivot_contract;
            public string[] authored_text;
            public string[] brands;
            public bool colliders;
            public bool materials;
            public bool lights;
            public bool cameras;
            public bool rigidbodies;
            public bool audio_sources;
            public int animation_count;
            public int item_count;
            public int mesh_count;
            public int triangle_count;
            public ProductManifestBudgets budgets;
            public ProductManifestItem[] items;
            public ProductManifestPart[] parts;
            public string build_signature;
        }

        [Serializable]
        private sealed class ProductManifestBudgets
        {
            public int maximum_renderers;
            public int maximum_triangles;
        }

        [Serializable]
        private sealed class ProductManifestItem
        {
            public string id;
            public string source_name;
            public string role;
            public ProductManifestPivot pivot;
            public float[] available_size_m;
            public float[] bounds_min;
            public float[] bounds_max;
            public float[] unity_bounds_min;
            public float[] unity_bounds_max;
            public float[] dimensions_m;
            public int mesh_count;
            public int triangle_count;
            public string[] parts;
        }

        [Serializable]
        private sealed class ProductManifestPivot
        {
            public string kind;
            public float[] source_position;
            public float[] unity_position;
        }

        [Serializable]
        private sealed class ProductManifestPart
        {
            public string name;
            public string item_id;
            public string role;
            public string group;
            public string surface;
            public string sheet;
            public float[] base_color;
            public bool casts_shadows;
            public bool shadows;
            public int vertices;
            public int triangles;
            public float[] bounds_min;
            public float[] bounds_max;
            public float[] unity_bounds_min;
            public float[] unity_bounds_max;
        }
    }
}
