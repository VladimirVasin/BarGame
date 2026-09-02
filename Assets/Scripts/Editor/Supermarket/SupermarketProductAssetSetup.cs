using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Extracts the six passive product roots from the deterministic Blender
    /// catalog and emits one Resources prefab for each inventory item.
    /// Selection collision and purchase lifetime remain runtime-owned.
    /// </summary>
    [InitializeOnLoad]
    public static class SupermarketProductAssetSetup
    {
        public const string ModelPath =
            "Assets/Supermarket/Products/Models/SupermarketProducts3D.fbx";
        public const string ManifestPath =
            "Assets/Supermarket/Products/Models/SupermarketProducts3D.json";
        public const string PrefabFolder =
            "Assets/Resources/Supermarket/Products";
        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";

        private const string ExpectedRootName =
            "ROOT_SupermarketProducts3D";
        private const string ExpectedLayoutMode =
            "coincident_identity_item_roots_for_extraction";
        private const string ExpectedPivotKind = "bottom_centre";
        private const int ExpectedItemCount = 6;
        private const int MaximumRenderers = 64;
        private const int MaximumTriangles = 12000;
        private const int MaximumItemTriangles = 4000;
        private const float PivotTolerance = 0.002f;
        private const float HorizontalCentreTolerance = 0.012f;
        private const float BoundsTolerance = 0.012f;
        private const float VodkaMaximumHeight = 0.4701f;

        private static readonly ProductContract[] Contracts =
        {
            new ProductContract(
                "instant_noodles",
                "ITEM_instant_noodles",
                InventoryItemId.InstantNoodles,
                "InstantNoodles3D"),
            new ProductContract(
                "day_old_loaf",
                "ITEM_day_old_loaf",
                InventoryItemId.DayOldLoaf,
                "DayOldLoaf3D"),
            new ProductContract(
                "vodka_bottle",
                "ITEM_vodka_bottle",
                InventoryItemId.VodkaBottle,
                "VodkaBottle3D"),
            new ProductContract(
                "closed_stew_can",
                "ITEM_closed_stew_can",
                InventoryItemId.ClosedStewCan,
                "ClosedStewCan3D"),
            new ProductContract(
                "open_stew_can",
                "ITEM_open_stew_can",
                InventoryItemId.OpenStewCan,
                "OpenStewCan3D"),
            new ProductContract(
                "chicken_egg",
                "ITEM_chicken_egg",
                InventoryItemId.ChickenEgg,
                "ChickenEgg3D"),
        };

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static SupermarketProductAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Build Product Runtime Prefabs")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
            Debug.Log("Supermarket product prefabs rebuilt.");
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Validate Product Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Supermarket product model contract is valid.");
        }

        /// <summary>
        /// Batch entry point for the complete supermarket art refresh. The
        /// interior follows the product pack so its measured shelf anchors are
        /// rebuilt and validated in the same deterministic import pass.
        /// </summary>
        public static void RunBatch()
        {
            try
            {
                BuildOrThrow();
                SupermarketInteriorAssetSetup.BuildOrThrow();
                ValidateOrThrow();
                SupermarketInteriorAssetSetup.ValidateOrThrow();
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "Supermarket product and interior prefab contracts are valid.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError(error);
                EditorApplication.Exit(1);
            }
        }

        public static bool IsModelPath(string path)
        {
            return PathsEqual(path, ModelPath);
        }

        public static bool IsManifestPath(string path)
        {
            return PathsEqual(path, ManifestPath);
        }

        public static bool IsSourcePath(string path)
        {
            return IsModelPath(path) || IsManifestPath(path);
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
        }

        public static string GetPrefabPath(InventoryItemId itemId)
        {
            RequireContract(itemId);
            return "Assets/Resources/" +
                SupermarketProductModelResources.GetResourcePath(itemId) +
                ".prefab";
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (buildQueued || IsBuilding || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        public static void BuildOrThrow()
        {
            if (IsBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Supermarket product sources are missing. Run " +
                    "tools/build-supermarket-products-3d-model.py through " +
                    "Blender first.");
            }

            IsBuilding = true;
            try
            {
                EnsureAssetFolder(PrefabFolder);
                ImportSources();
                SupermarketProductManifest manifest =
                    LoadAndValidateManifest();
                ValidateImportedModel(manifest);
                for (int index = 0; index < Contracts.Length; index++)
                {
                    ProductContract contract = Contracts[index];
                    SupermarketProductManifestItem item = manifest.items
                        .Single(candidate => candidate.id == contract.StableId);
                    BuildPrefab(manifest, item, contract);
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                IsBuilding = false;
            }

            AssetDatabase.Refresh();
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            SupermarketProductManifest manifest =
                LoadAndValidateManifest();
            ValidateModelImporter();
            ValidateImportedModel(manifest);
            for (int index = 0; index < Contracts.Length; index++)
            {
                ProductContract contract = Contracts[index];
                SupermarketProductManifestItem item = manifest.items
                    .Single(candidate => candidate.id == contract.StableId);
                ValidatePrefab(manifest, item, contract);
            }
        }

        private static void ImportSources()
        {
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void BuildPrefab(
            SupermarketProductManifest manifest,
            SupermarketProductManifestItem item,
            ProductContract contract)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            if (modelAsset == null || sharedLit == null)
            {
                throw new InvalidOperationException(
                    "The product catalog or shared runtime material did not " +
                    "import.");
            }

            var wrapper = new GameObject(contract.PrefabName);
            GameObject sourceInstance = null;
            try
            {
                sourceInstance = PrefabUtility.InstantiatePrefab(
                    modelAsset) as GameObject;
                if (sourceInstance == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{ModelPath}'.");
                }

                PrefabUtility.UnpackPrefabInstance(
                    sourceInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                Transform catalogRoot = FindUniqueTransform(
                    sourceInstance.transform,
                    ExpectedRootName);
                Transform itemRoot = FindDirectChild(
                    catalogRoot,
                    item.source_name);
                // Identity is an authoring-space contract relative to the
                // catalog root. Detaching must preserve Unity's imported FBX
                // axis carrier, which may make the extracted child transform
                // non-identity while keeping wrapper-space geometry exact.
                AssertIdentityItemRoot(itemRoot, item.id);
                itemRoot.SetParent(wrapper.transform, true);
                Object.DestroyImmediate(sourceInstance);
                sourceInstance = null;

                AssertPassive(wrapper);
                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(itemRoot.gameObject);
                EnsureExactRendererSet(item, renderers);
                Dictionary<string, SupermarketProductManifestPart> parts =
                    manifest.parts
                        .Where(part => part.item_id == item.id)
                        .ToDictionary(part => part.name, StringComparer.Ordinal);
                var bindings = new SupermarketProductPartBinding[
                    item.parts.Length];
                for (int index = 0; index < item.parts.Length; index++)
                {
                    string partName = item.parts[index];
                    SupermarketProductManifestPart source = parts[partName];
                    Renderer renderer = renderers[partName];
                    renderer.sharedMaterials = new[] { sharedLit };
                    renderer.shadowCastingMode = source.casts_shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = source.casts_shadows;
                    renderer.enabled = true;
                    bindings[index] = new SupermarketProductPartBinding(
                        source.name,
                        source.role,
                        ToColor(source.base_color),
                        renderer);
                }

                Bounds localBounds = CalculateLocalBounds(
                    wrapper.transform,
                    renderers.Values);
                AssertBoundsNear(
                    localBounds,
                    BoundsFromArrays(
                        item.unity_bounds_min,
                        item.unity_bounds_max),
                    item.id + " imported bounds");

                SupermarketProductAssetRegistry registry =
                    wrapper.AddComponent<
                        SupermarketProductAssetRegistry>();
                registry.Configure(
                    contract.ItemId,
                    wrapper.transform,
                    bindings,
                    localBounds,
                    item.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);
                registry.ApplyAppearance();
                registry.ValidateOrThrow();

                string prefabPath = GetPrefabPath(contract.ItemId);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    wrapper,
                    prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save '{prefabPath}'.");
                }
            }
            finally
            {
                if (sourceInstance != null)
                {
                    Object.DestroyImmediate(sourceInstance);
                }

                Object.DestroyImmediate(wrapper);
            }
        }

        private static SupermarketProductManifest
            LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Missing product manifest '{ManifestPath}'.");
            }

            SupermarketProductManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<
                    SupermarketProductManifest>(
                        File.ReadAllText(ManifestPath));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "The supermarket product manifest is invalid JSON.",
                    error);
            }

            ValidateManifestHeader(manifest);
            ValidateManifestItems(manifest);
            ValidateManifestParts(manifest);
            return manifest;
        }

        private static void ValidateManifestHeader(
            SupermarketProductManifest manifest)
        {
            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.generator) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.blender_version) ||
                !string.Equals(
                    manifest.design_id,
                    SupermarketProductAssetRegistry.ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.root_name,
                    ExpectedRootName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.layout_mode,
                    ExpectedLayoutMode,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.pivot_contract,
                    ExpectedPivotKind,
                    StringComparison.Ordinal) ||
                !IsSha256(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The supermarket product manifest identity is invalid.");
            }

            if (manifest.source_axes == null ||
                manifest.unity_axes == null ||
                manifest.source_axes.right != "+X" ||
                manifest.source_axes.forward != "+Y" ||
                manifest.source_axes.up != "+Z" ||
                manifest.unity_axes.right != "+X" ||
                manifest.unity_axes.forward != "+Z" ||
                manifest.unity_axes.up != "+Y" ||
                manifest.unity_axes.fbx_axis_forward != "-Z" ||
                manifest.unity_axes.fbx_axis_up != "Y" ||
                manifest.unity_axes.bake_space_transform)
            {
                throw new InvalidOperationException(
                    "The supermarket product axis contract changed.");
            }

            if (manifest.colliders || manifest.materials || manifest.lights ||
                manifest.cameras || manifest.rigidbodies ||
                manifest.audio_sources || manifest.animation_count != 0 ||
                manifest.authored_text == null ||
                manifest.authored_text.Length != 0 ||
                manifest.brands == null || manifest.brands.Length != 0)
            {
                throw new InvalidOperationException(
                    "The supermarket product source must remain passive, " +
                    "unbranded and text-free.");
            }

            if (manifest.budgets == null ||
                manifest.budgets.maximum_renderers <= 0 ||
                manifest.budgets.maximum_renderers > MaximumRenderers ||
                manifest.budgets.maximum_triangles <= 0 ||
                manifest.budgets.maximum_triangles > MaximumTriangles ||
                manifest.item_count != ExpectedItemCount ||
                manifest.mesh_count <= 0 ||
                manifest.mesh_count > manifest.budgets.maximum_renderers ||
                manifest.triangle_count <= 0 ||
                manifest.triangle_count >
                    manifest.budgets.maximum_triangles ||
                !TryBounds(
                    manifest.unity_bounds_min,
                    manifest.unity_bounds_max,
                    out _))
            {
                throw new InvalidOperationException(
                    "The supermarket product count, bounds or budget is " +
                    "invalid.");
            }
        }

        private static void ValidateManifestItems(
            SupermarketProductManifest manifest)
        {
            if (manifest.items == null ||
                manifest.items.Length != ExpectedItemCount)
            {
                throw new InvalidOperationException(
                    "The product manifest must contain exactly six items.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            int rendererTotal = 0;
            int triangleTotal = 0;
            for (int index = 0; index < Contracts.Length; index++)
            {
                ProductContract contract = Contracts[index];
                SupermarketProductManifestItem item = manifest.items[index];
                if (item == null || !ids.Add(item.id) ||
                    !string.Equals(
                        item.id,
                        contract.StableId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        item.source_name,
                        contract.SourceName,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        item.role,
                        contract.StableId,
                        StringComparison.Ordinal) ||
                    item.pivot == null ||
                    item.pivot.kind != ExpectedPivotKind ||
                    !IsZeroVector(item.pivot.source_position) ||
                    !IsZeroVector(item.pivot.unity_position))
                {
                    throw new InvalidOperationException(
                        $"Product item {index} has invalid identity or pivot.");
                }

                ValidateItemBounds(item);
                if (item.parts == null || item.parts.Length == 0 ||
                    item.mesh_count != item.parts.Length ||
                    item.mesh_count > MaximumRenderers ||
                    item.triangle_count <= 0 ||
                    item.triangle_count > MaximumItemTriangles ||
                    item.parts.Distinct(StringComparer.Ordinal).Count() !=
                        item.parts.Length)
                {
                    throw new InvalidOperationException(
                        $"Product '{item.id}' has invalid mesh metadata.");
                }

                rendererTotal += item.mesh_count;
                triangleTotal += item.triangle_count;
            }

            if (rendererTotal != manifest.mesh_count ||
                triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Product item totals differ from the manifest header.");
            }
        }

        private static void ValidateItemBounds(
            SupermarketProductManifestItem item)
        {
            if (!TryBounds(
                    item.unity_bounds_min,
                    item.unity_bounds_max,
                    out Bounds unityBounds) ||
                !TryBounds(
                    item.bounds_min,
                    item.bounds_max,
                    out Bounds sourceBounds) ||
                !IsPositiveVector(item.available_size_m) ||
                !IsPositiveVector(item.dimensions_m))
            {
                throw new InvalidOperationException(
                    $"Product '{item.id}' has invalid bounds.");
            }

            Bounds converted = ConvertSourceBoundsToUnity(
                item.bounds_min,
                item.bounds_max);
            AssertBoundsNear(
                unityBounds,
                converted,
                item.id + " source/Unity bounds");
            Vector3 dimensions = ToVector(item.dimensions_m);
            Vector3 available = ToVector(item.available_size_m);
            if (Vector3.Distance(dimensions, unityBounds.size) >
                    BoundsTolerance ||
                unityBounds.size.x > available.x + BoundsTolerance ||
                unityBounds.size.y > available.y + BoundsTolerance ||
                unityBounds.size.z > available.z + BoundsTolerance ||
                Mathf.Abs(unityBounds.min.y) > PivotTolerance ||
                Mathf.Abs(unityBounds.center.x) >
                    HorizontalCentreTolerance ||
                Mathf.Abs(unityBounds.center.z) >
                    HorizontalCentreTolerance)
            {
                throw new InvalidOperationException(
                    $"Product '{item.id}' is not fit-safe on its " +
                    "bottom-centre pivot.");
            }

            if (item.id == "vodka_bottle" &&
                unityBounds.size.y > VodkaMaximumHeight)
            {
                throw new InvalidOperationException(
                    "The vodka bottle no longer fits beneath the next shelf.");
            }
        }

        private static void ValidateManifestParts(
            SupermarketProductManifest manifest)
        {
            if (manifest.parts == null ||
                manifest.parts.Length != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "The product part table is missing or stale.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int triangleTotal = 0;
            foreach (SupermarketProductManifestPart part in manifest.parts)
            {
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    !names.Add(part.name) ||
                    !Contracts.Any(contract =>
                        contract.StableId == part.item_id) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    string.IsNullOrWhiteSpace(part.surface) ||
                    part.group != "render" ||
                    !string.IsNullOrEmpty(part.sheet) ||
                    !IsFiniteColor(part.base_color) ||
                    !part.casts_shadows ||
                    part.shadows != part.casts_shadows ||
                    part.vertices <= 0 || part.triangles <= 0 ||
                    !TryBounds(
                        part.unity_bounds_min,
                        part.unity_bounds_max,
                        out Bounds unityBounds) ||
                    !TryBounds(
                        part.bounds_min,
                        part.bounds_max,
                        out _))
                {
                    throw new InvalidOperationException(
                        "A supermarket product part has invalid semantic or " +
                        "mesh metadata.");
                }

                AssertBoundsNear(
                    unityBounds,
                    ConvertSourceBoundsToUnity(
                        part.bounds_min,
                        part.bounds_max),
                    part.name + " source/Unity bounds");
                triangleTotal += part.triangles;
            }

            if (triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Product part triangles differ from the manifest total.");
            }

            for (int index = 0; index < manifest.items.Length; index++)
            {
                SupermarketProductManifestItem item = manifest.items[index];
                SupermarketProductManifestPart[] itemParts = manifest.parts
                    .Where(part => part.item_id == item.id)
                    .ToArray();
                if (itemParts.Length != item.mesh_count ||
                    !item.parts.ToHashSet(StringComparer.Ordinal).SetEquals(
                        itemParts.Select(part => part.name)) ||
                    itemParts.Sum(part => part.triangles) !=
                        item.triangle_count)
                {
                    throw new InvalidOperationException(
                        $"Product '{item.id}' part table is stale.");
                }
            }
        }

        private static void ValidateImportedModel(
            SupermarketProductManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}'.");
            }

            AssertPassive(model);
            Transform catalog = FindUniqueTransform(
                model.transform,
                ExpectedRootName);
            for (int index = 0; index < Contracts.Length; index++)
            {
                ProductContract contract = Contracts[index];
                SupermarketProductManifestItem item = manifest.items[index];
                Transform itemRoot = FindDirectChild(
                    catalog,
                    contract.SourceName);
                AssertIdentityItemRoot(itemRoot, contract.StableId);
                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(itemRoot.gameObject);
                EnsureExactRendererSet(item, renderers);
            }
        }

        private static void ValidateModelImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                Mathf.Abs(importer.globalScale - 1f) > 0.0001f ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy || importer.optimizeGameObjects ||
                importer.importCameras || importer.importLights ||
                importer.addCollider || importer.importBlendShapes ||
                importer.isReadable ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "The supermarket product importer contract drifted.");
            }
        }

        private static void ValidatePrefab(
            SupermarketProductManifest manifest,
            SupermarketProductManifestItem item,
            ProductContract contract)
        {
            string path = GetPrefabPath(contract.ItemId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing generated product prefab '{path}'.");
            }

            AssertPassive(prefab);
            SupermarketProductAssetRegistry[] registries =
                prefab.GetComponentsInChildren<
                    SupermarketProductAssetRegistry>(true);
            if (registries.Length != 1 ||
                registries[0].gameObject != prefab)
            {
                throw new InvalidOperationException(
                    $"Product prefab '{path}' needs one root registry.");
            }

            SupermarketProductAssetRegistry registry = registries[0];
            registry.ValidateOrThrow();
            if (registry.ItemId != contract.ItemId ||
                registry.ModelRoot != prefab.transform ||
                registry.SourceTriangleCount != item.triangle_count ||
                registry.DesignId != manifest.design_id ||
                registry.SourceGeneratorVersion !=
                    manifest.generator_version ||
                registry.BuildSignature != manifest.build_signature ||
                registry.Parts.Count != item.parts.Length)
            {
                throw new InvalidOperationException(
                    $"Product prefab '{path}' has stale registry metadata.");
            }

            FindUniqueTransform(prefab.transform, contract.SourceName);
            AssertBoundsNear(
                registry.LocalBounds,
                BoundsFromArrays(
                    item.unity_bounds_min,
                    item.unity_bounds_max),
                item.id + " registry bounds");
            AssertBoundsNear(
                CalculateLocalBounds(
                    prefab.transform,
                    registry.Parts.Select(part => part.Renderer)),
                registry.LocalBounds,
                item.id + " prefab renderer bounds");

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Dictionary<string, SupermarketProductManifestPart> parts =
                manifest.parts
                    .Where(part => part.item_id == item.id)
                    .ToDictionary(part => part.name, StringComparer.Ordinal);
            var rendererSet = new HashSet<Renderer>();
            foreach (SupermarketProductPartBinding binding in registry.Parts)
            {
                if (binding == null || binding.Renderer == null ||
                    !parts.TryGetValue(
                        binding.SourceName,
                        out SupermarketProductManifestPart source) ||
                    binding.Role != source.role ||
                    binding.Color != ToColor(source.base_color) ||
                    !rendererSet.Add(binding.Renderer) ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial != sharedLit ||
                    binding.Renderer.shadowCastingMode != ShadowCastingMode.On ||
                    !binding.Renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"Product '{item.id}' has an invalid renderer binding.");
                }
            }
        }

        private static void AssertPassive(GameObject root)
        {
            var problems = new List<string>();
            AppendForbidden<Collider>(root, problems, "Collider");
            AppendForbidden<Light>(root, problems, "Light");
            AppendForbidden<Camera>(root, problems, "Camera");
            AppendForbidden<AudioSource>(root, problems, "AudioSource");
            AppendForbidden<Rigidbody>(root, problems, "Rigidbody");
            AppendForbidden<Animator>(root, problems, "Animator");
            AppendForbidden<Animation>(root, problems, "Animation");
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "The supermarket product catalog is not passive: " +
                    string.Join("; ", problems));
            }
        }

        private static void AppendForbidden<T>(
            GameObject root,
            ICollection<string> problems,
            string label)
            where T : Component
        {
            T[] found = root.GetComponentsInChildren<T>(true);
            if (found.Length > 0)
            {
                problems.Add($"found {found.Length} {label}(s)");
            }
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject root)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!(renderer is MeshRenderer) ||
                    !result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"Product renderer '{renderer.name}' is duplicated " +
                        "or is not a static mesh.");
                }
            }

            return result;
        }

        private static void EnsureExactRendererSet(
            SupermarketProductManifestItem item,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            var expected = new HashSet<string>(
                item.parts,
                StringComparer.Ordinal);
            if (expected.Count != renderers.Count ||
                renderers.Keys.Any(name => !expected.Contains(name)))
            {
                throw new InvalidOperationException(
                    $"Imported renderer set for '{item.id}' differs from " +
                    "its manifest.");
            }
        }

        private static Transform FindUniqueTransform(
            Transform root,
            string name)
        {
            Transform[] matches = root
                .GetComponentsInChildren<Transform>(true)
                .Where(candidate => string.Equals(
                    candidate.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one imported transform '{name}', found " +
                    $"{matches.Length}.");
            }

            return matches[0];
        }

        private static Transform FindDirectChild(
            Transform parent,
            string name)
        {
            Transform result = null;
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (!string.Equals(
                        child.name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"Imported root '{name}' is duplicated.");
                }

                result = child;
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    $"Imported catalog lacks direct child '{name}'.");
            }

            return result;
        }

        private static void AssertIdentityItemRoot(
            Transform root,
            string itemId)
        {
            if (Vector3.Distance(root.localPosition, Vector3.zero) >
                    PivotTolerance ||
                Quaternion.Angle(root.localRotation, Quaternion.identity) >
                    0.01f ||
                Vector3.Distance(root.localScale, Vector3.one) >
                    PivotTolerance)
            {
                throw new InvalidOperationException(
                    $"Product '{itemId}' lost its identity source pivot.");
            }
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool started = false;
            Bounds result = default;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    throw new InvalidOperationException(
                        "A product registry contains a missing renderer.");
                }

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

            if (!started)
            {
                throw new InvalidOperationException(
                    "A supermarket product has no renderer bounds.");
            }

            return result;
        }

        private static bool TryBounds(
            float[] minimum,
            float[] maximum,
            out Bounds bounds)
        {
            if (!IsFiniteVector(minimum) || !IsFiniteVector(maximum))
            {
                bounds = default;
                return false;
            }

            Vector3 min = ToVector(minimum);
            Vector3 max = ToVector(maximum);
            if (min.x > max.x || min.y > max.y || min.z > max.z)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds.size.x > 0f && bounds.size.y > 0f &&
                bounds.size.z > 0f;
        }

        private static Bounds BoundsFromArrays(
            float[] minimum,
            float[] maximum)
        {
            if (!TryBounds(minimum, maximum, out Bounds bounds))
            {
                throw new InvalidOperationException(
                    "A product bounds array is invalid.");
            }

            return bounds;
        }

        private static Bounds ConvertSourceBoundsToUnity(
            float[] minimum,
            float[] maximum)
        {
            return BoundsFromArrays(
                new[] { minimum[0], minimum[2], minimum[1] },
                new[] { maximum[0], maximum[2], maximum[1] });
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Bounds expected,
            string label)
        {
            if (Vector3.Distance(actual.center, expected.center) >
                    BoundsTolerance ||
                Vector3.Distance(actual.size, expected.size) >
                    BoundsTolerance)
            {
                throw new InvalidOperationException(
                    $"{label} differ: {actual} / {expected}.");
            }
        }

        private static ProductContract RequireContract(
            InventoryItemId itemId)
        {
            for (int index = 0; index < Contracts.Length; index++)
            {
                if (Contracts[index].ItemId == itemId)
                {
                    return Contracts[index];
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(itemId),
                itemId,
                "The inventory item is not part of the authored product pack.");
        }

        private static Color ToColor(float[] values)
        {
            return new Color(values[0], values[1], values[2], values[3]);
        }

        private static Vector3 ToVector(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool IsFiniteColor(float[] values)
        {
            if (values == null || values.Length != 4)
            {
                return false;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFiniteVector(float[] values)
        {
            return values != null && values.Length == 3 &&
                IsFinite(values[0]) && IsFinite(values[1]) &&
                IsFinite(values[2]);
        }

        private static bool IsPositiveVector(float[] values)
        {
            return IsFiniteVector(values) && values[0] > 0f &&
                values[1] > 0f && values[2] > 0f;
        }

        private static bool IsZeroVector(float[] values)
        {
            return IsFiniteVector(values) &&
                Mathf.Abs(values[0]) <= PivotTolerance &&
                Mathf.Abs(values[1]) <= PivotTolerance &&
                Mathf.Abs(values[2]) <= PivotTolerance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathsEqual(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && string.Equals(
                left,
                right,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist() || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception error)
            {
                Debug.LogError(error);
            }
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private sealed class ProductContract
        {
            public ProductContract(
                string stableId,
                string sourceName,
                InventoryItemId itemId,
                string prefabName)
            {
                StableId = stableId;
                SourceName = sourceName;
                ItemId = itemId;
                PrefabName = prefabName;
            }

            public string StableId { get; }
            public string SourceName { get; }
            public InventoryItemId ItemId { get; }
            public string PrefabName { get; }
        }
    }

    /// <summary>
    /// The catalog is exported in metre scale. Preserve its extraction
    /// hierarchy while excluding every authored runtime feature and material.
    /// </summary>
    public sealed class SupermarketProductModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!SupermarketProductAssetSetup.IsModelPath(assetPath) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (SupermarketProductAssetSetup.IsBuilding)
            {
                return;
            }

            if (ContainsSource(importedAssets) || ContainsSource(movedAssets))
            {
                SupermarketProductAssetSetup.QueueBuildWhenSourcesExist();
            }
        }

        private static bool ContainsSource(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int index = 0; index < paths.Length; index++)
            {
                if (SupermarketProductAssetSetup.IsSourcePath(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    internal sealed class SupermarketProductManifest
    {
        public string generator;
        public string generator_version;
        public string blender_version;
        public string design_id;
        public string display_name;
        public string root_name;
        public string layout_mode;
        public SupermarketProductManifestAxes source_axes;
        public SupermarketProductManifestUnityAxes unity_axes;
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
        public float[] bounds_min;
        public float[] bounds_max;
        public float[] unity_bounds_min;
        public float[] unity_bounds_max;
        public SupermarketProductManifestBudgets budgets;
        public SupermarketProductManifestItem[] items;
        public SupermarketProductManifestPart[] parts;
        public string build_signature;
    }

    [Serializable]
    internal sealed class SupermarketProductManifestAxes
    {
        public string right;
        public string forward;
        public string up;
    }

    [Serializable]
    internal sealed class SupermarketProductManifestUnityAxes
    {
        public string right;
        public string forward;
        public string up;
        public string fbx_axis_forward;
        public string fbx_axis_up;
        public bool bake_space_transform;
    }

    [Serializable]
    internal sealed class SupermarketProductManifestBudgets
    {
        public int maximum_renderers;
        public int maximum_triangles;
    }

    [Serializable]
    internal sealed class SupermarketProductManifestItem
    {
        public string id;
        public string source_name;
        public string role;
        public SupermarketProductManifestPivot pivot;
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
    internal sealed class SupermarketProductManifestPivot
    {
        public string kind;
        public float[] source_position;
        public float[] unity_position;
    }

    [Serializable]
    internal sealed class SupermarketProductManifestPart
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
