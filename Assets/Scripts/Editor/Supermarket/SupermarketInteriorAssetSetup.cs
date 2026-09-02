using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the deterministic Blender supermarket interior and emits the
    /// passive Resources prefab consumed by runtime composition. Gameplay
    /// collision, products, lights and behaviour are intentionally excluded.
    /// </summary>
    [InitializeOnLoad]
    public static class SupermarketInteriorAssetSetup
    {
        public const string ModelPath =
            "Assets/Supermarket/Interior/Models/" +
            "SupermarketInterior3D.fbx";
        public const string ManifestPath =
            "Assets/Supermarket/Interior/Models/" +
            "SupermarketInterior3D.json";
        public const string PrefabPath =
            "Assets/Resources/Supermarket/SupermarketInterior3D.prefab";
        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedRootName =
            "ROOT_SupermarketInterior3D";
        private const float ExpectedWidth = 16f;
        private const float ExpectedDepth = 11f;
        private const float ExpectedHeight = 3.6f;
        private const float ExpectedWallThickness = 0.25f;
        private const float ExpectedEntranceWidth = 2.4f;
        private const float ExpectedEntranceHeight = 2.94f;
        private const float MeasureTolerance = 0.05f;
        private const int MaximumRenderers = 180;
        private const int MaximumTriangles = 30000;

        private static readonly string[] RequiredAnchorRoles =
        {
            "entrance",
            "room_centre",
            "shelf_dry",
            "shelf_pantry",
            "shelf_cold",
            "checkout",
            "stockroom",
            "cctv_mount_01",
            "cctv_mount_02",
            "cctv_mount_03",
            "cctv_mount_04",
            "cctv_head_01",
            "cctv_head_02",
            "cctv_head_03",
            "cctv_head_04",
            "tube_01",
            "tube_02",
            "tube_03",
            "tube_04",
            "cashier",
            "product_instant_noodles",
            "product_day_old_loaf",
            "product_vodka_bottle",
            "product_closed_stew_can",
            "product_chicken_egg"
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
            "grime"
        };

        private static readonly HashSet<string> AllowedSheets =
            new HashSet<string>(StringComparer.Ordinal)
            {
                string.Empty,
                "Linoleum",
                "WallPaint",
                "Ceiling",
                "ShelfMetal",
                "Counter",
                "Cardboard"
            };

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static SupermarketInteriorAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Build Interior Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Validate Interior Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Supermarket interior model contract is valid.");
        }

        public static void RunBatch()
        {
            try
            {
                BuildOrThrow();
                AssetDatabase.SaveAssets();
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
            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Supermarket interior sources are missing. Run " +
                    "tools/build-supermarket-interior-3d-model.py through " +
                    "Blender first.");
            }

            IsBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                ImportSources();
                SupermarketInteriorManifest manifest =
                    LoadAndValidateManifest();
                BuildPrefab(manifest);
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
            SupermarketInteriorManifest manifest =
                LoadAndValidateManifest();
            ValidateModelImporterOrThrow();
            ValidatePrefabOrThrow(manifest);
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
            SupermarketInteriorManifest manifest)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}'.");
            }

            Material sharedLit = AssetDatabase.LoadAssetAtPath<Material>(
                SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "Supermarket interior shared materials failed to load.");
            }

            var wrapper = new GameObject("SupermarketInterior3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{ModelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(wrapper.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                AssertPassive(model);

                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexUniqueTransforms(model);
                EnsureExactRendererSet(manifest, renderers);
                Transform modelRoot = RequireTransform(
                    transforms,
                    ExpectedRootName,
                    "authoring root");

                var anchorBindings =
                    new List<SupermarketInteriorAnchorBinding>();
                foreach (SupermarketInteriorManifestAnchor source in
                         manifest.anchors)
                {
                    Transform anchor = RequireTransform(
                        transforms,
                        $"ANCHOR_{source.name}",
                        $"anchor '{source.name}'");
                    AssertAnchorPosition(
                        wrapper.transform,
                        anchor,
                        source);
                    anchorBindings.Add(
                        new SupermarketInteriorAnchorBinding(
                            source.name,
                            source.role,
                            anchor));
                }

                ReparentCctvHeads(transforms, renderers);

                var partBindings =
                    new List<SupermarketInteriorPartBinding>();
                foreach (SupermarketInteriorManifestPart source in
                         manifest.parts)
                {
                    Renderer renderer = renderers[source.name];
                    bool castsShadows = ResolveCastsShadows(source);
                    renderer.sharedMaterials = new[]
                    {
                        source.emissive ? sharedEmission : sharedLit
                    };
                    renderer.shadowCastingMode = castsShadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = castsShadows;
                    renderer.enabled = true;
                    partBindings.Add(
                        new SupermarketInteriorPartBinding(
                            source.name,
                            source.role,
                            source.group,
                            source.sheet,
                            ToColor(source.base_color),
                            source.emissive,
                            castsShadows,
                            true,
                            renderer));
                }

                Bounds localBounds = CalculateLocalBounds(
                    wrapper.transform,
                    renderers.Values);
                AssertMeasuresUpToManifest(
                    localBounds,
                    manifest,
                    ModelPath);

                SupermarketInteriorAssetRegistry registry =
                    wrapper.AddComponent<
                        SupermarketInteriorAssetRegistry>();
                registry.Configure(
                    modelRoot,
                    anchorBindings.ToArray(),
                    partBindings.ToArray(),
                    localBounds,
                    new SupermarketInteriorDimensions(
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.depth,
                        manifest.dimensions_m.height,
                        manifest.wall_thickness_m,
                        manifest.entrance_opening_m.width,
                        manifest.entrance_opening_m.height),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);
                registry.ApplySurfaceAppearance();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    wrapper,
                    PrefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save '{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
        }

        private static SupermarketInteriorManifest
            LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Missing supermarket interior manifest '{ManifestPath}'.");
            }

            SupermarketInteriorManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<
                    SupermarketInteriorManifest>(
                        File.ReadAllText(ManifestPath));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest is invalid JSON.",
                    error);
            }

            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest is empty.");
            }

            ValidateManifestHeader(manifest);
            ValidateManifestAnchors(manifest);
            ValidateManifestParts(manifest);
            return manifest;
        }

        private static void ValidateManifestHeader(
            SupermarketInteriorManifest manifest)
        {
            if (!string.Equals(
                    manifest.design_id,
                    SupermarketInteriorAssetRegistry.ExpectedDesignId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest identity is invalid.");
            }

            if (manifest.dimensions_m == null ||
                manifest.entrance_opening_m == null ||
                !Near(manifest.dimensions_m.width, ExpectedWidth) ||
                !Near(manifest.dimensions_m.depth, ExpectedDepth) ||
                !Near(manifest.dimensions_m.height, ExpectedHeight) ||
                !Near(
                    manifest.wall_thickness_m,
                    ExpectedWallThickness) ||
                !Near(
                    manifest.entrance_opening_m.width,
                    ExpectedEntranceWidth) ||
                !Near(
                    manifest.entrance_opening_m.height,
                    ExpectedEntranceHeight))
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest dimensions drifted " +
                    "from the 16 x 11 x 3.6 metre layout contract.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras ||
                manifest.rigidbodies || manifest.audio_sources ||
                manifest.materials || manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The supermarket interior source must remain passive.");
            }

            if (!TryGetUnityBounds(manifest, out Bounds bounds) ||
                bounds.size.sqrMagnitude <= 0f)
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest bounds are invalid.");
            }
        }

        private static void ValidateManifestAnchors(
            SupermarketInteriorManifest manifest)
        {
            if (manifest.anchors == null || manifest.anchors.Length == 0)
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest has no anchors.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (SupermarketInteriorManifestAnchor anchor in
                     manifest.anchors)
            {
                if (anchor == null ||
                    string.IsNullOrWhiteSpace(anchor.name) ||
                    string.IsNullOrWhiteSpace(anchor.role) ||
                    !TryGetUnityPosition(anchor, out _))
                {
                    throw new InvalidOperationException(
                        "Every supermarket interior anchor needs a unique " +
                        "name, role and finite position.");
                }

                if (!names.Add(anchor.name) || !roles.Add(anchor.role))
                {
                    throw new InvalidOperationException(
                        $"Supermarket interior anchor '{anchor.name}' " +
                        "duplicates a name or role.");
                }
            }

            for (int index = 0;
                 index < RequiredAnchorRoles.Length;
                 index++)
            {
                string role = RequiredAnchorRoles[index];
                if (!roles.Contains(role))
                {
                    throw new InvalidOperationException(
                        $"Required supermarket interior anchor '{role}' " +
                        "is missing.");
                }
            }

            if (names.Count != RequiredAnchorRoles.Length ||
                !names.SetEquals(roles))
            {
                throw new InvalidOperationException(
                    "The supermarket interior anchor names and roles must " +
                    "be the exact published twenty-anchor set.");
            }
        }

        private static void ValidateManifestParts(
            SupermarketInteriorManifest manifest)
        {
            if (manifest.parts == null || manifest.parts.Length == 0 ||
                manifest.parts.Length > MaximumRenderers ||
                manifest.mesh_count != manifest.parts.Length)
            {
                throw new InvalidOperationException(
                    "The supermarket interior manifest mesh count is " +
                    "missing or outside its budget.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var roles = new HashSet<string>(StringComparer.Ordinal);
            var sheets = new HashSet<string>(StringComparer.Ordinal);
            int triangleTotal = 0;
            foreach (SupermarketInteriorManifestPart part in manifest.parts)
            {
                string sheet = part?.sheet ?? string.Empty;
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    !string.Equals(
                        part.group,
                        "fixed",
                        StringComparison.Ordinal) ||
                    !AllowedSheets.Contains(sheet) ||
                    !IsFiniteColor(part.base_color) ||
                    !IsFiniteVector(part.bounds_min) ||
                    !IsFiniteVector(part.bounds_max) ||
                    !BoundsAreOrdered(
                        part.bounds_min,
                        part.bounds_max) ||
                    part.shadows != part.casts_shadows ||
                    part.vertices <= 0 || part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        "Every supermarket interior part needs valid " +
                        "semantic, colour and mesh metadata.");
                }

                if (!names.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest repeats part '{part.name}'.");
                }

                roles.Add(part.role);
                if (!string.IsNullOrEmpty(sheet))
                {
                    sheets.Add(sheet);
                }
                triangleTotal += part.triangles;
            }


            var requiredSheets = new HashSet<string>(AllowedSheets,
                StringComparer.Ordinal);
            requiredSheets.Remove(string.Empty);
            if (!sheets.SetEquals(requiredSheets))
            {
                throw new InvalidOperationException(
                    "The supermarket interior must exercise all six " +
                    "published surface sheets.");
            }

            if (triangleTotal != manifest.triangle_count ||
                triangleTotal <= 0 || triangleTotal > MaximumTriangles)
            {
                throw new InvalidOperationException(
                    "The supermarket interior triangle budget is invalid.");
            }

            for (int index = 0; index < RequiredPartRoles.Length; index++)
            {
                string role = RequiredPartRoles[index];
                if (!roles.Contains(role))
                {
                    throw new InvalidOperationException(
                        $"Required supermarket interior part role '{role}' " +
                        "is missing.");
                }
            }

            for (int index = 1; index <= 4; index++)
            {
                string suffix = index.ToString("00");
                if (!names.Contains($"CCTV Head {suffix}") ||
                    !names.Contains($"Fluorescent Tube {suffix}"))
                {
                    throw new InvalidOperationException(
                        "The supermarket interior lacks an individually " +
                        $"addressable CCTV head or tube {suffix}.");
                }
            }
        }

        private static void ValidateModelImporterOrThrow()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                !Near(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy || importer.optimizeGameObjects ||
                importer.importCameras || importer.importLights ||
                importer.addCollider || importer.importBlendShapes ||
                importer.isReadable ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "The supermarket interior model importer contract " +
                    "drifted.");
            }
        }

        private static void ValidatePrefabOrThrow(
            SupermarketInteriorManifest manifest)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing generated prefab '{PrefabPath}'.");
            }

            var problems = new List<string>();
            SupermarketInteriorAssetRegistry[] registries =
                prefab.GetComponentsInChildren<
                    SupermarketInteriorAssetRegistry>(true);
            if (registries.Length != 1 ||
                registries[0].gameObject != prefab)
            {
                problems.Add(
                    "prefab does not carry exactly one root asset registry");
            }

            AppendForbidden<Collider>(prefab, problems, "Collider");
            AppendForbidden<Light>(prefab, problems, "Light");
            AppendForbidden<Camera>(prefab, problems, "Camera");
            AppendForbidden<AudioSource>(prefab, problems, "AudioSource");
            AppendForbidden<Rigidbody>(prefab, problems, "Rigidbody");
            AppendForbidden<Animator>(prefab, problems, "Animator");

            if (registries.Length == 1)
            {
                ValidateRegistry(
                    prefab.transform,
                    registries[0],
                    manifest,
                    problems);
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Supermarket interior prefab contract failed: " +
                    string.Join("; ", problems));
            }
        }

        private static void ValidateRegistry(
            Transform prefabRoot,
            SupermarketInteriorAssetRegistry registry,
            SupermarketInteriorManifest manifest,
            ICollection<string> problems)
        {
            if (registry.ModelRoot == null ||
                !registry.ModelRoot.IsChildOf(prefabRoot))
            {
                problems.Add("registry model root is missing");
            }

            if (!string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.SourceGeneratorVersion,
                    manifest.generator_version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != manifest.triangle_count)
            {
                problems.Add("registry source metadata differs from manifest");
            }

            if (registry.Anchors.Count != manifest.anchors.Length ||
                registry.Parts.Count != manifest.parts.Length)
            {
                problems.Add("registry binding counts differ from manifest");
            }

            AssertDimensions(registry.Dimensions, problems);
            if (TryGetUnityBounds(manifest, out Bounds expectedBounds))
            {
                AppendBoundsProblems(
                    registry.LocalBounds,
                    expectedBounds,
                    problems,
                    "registry bounds");
            }

            foreach (SupermarketInteriorManifestAnchor source in
                     manifest.anchors)
            {
                if (!registry.TryGetAnchor(
                        source.role,
                        out Transform anchor) ||
                    anchor == null)
                {
                    problems.Add(
                        $"registry cannot resolve anchor '{source.role}'");
                    continue;
                }

                if (!string.Equals(
                        anchor.name,
                        $"ANCHOR_{source.name}",
                        StringComparison.Ordinal))
                {
                    problems.Add(
                        $"anchor '{source.role}' is not the imported pivot");
                }

                if (TryGetUnityPosition(source, out Vector3 expected))
                {
                    Vector3 actual = prefabRoot.InverseTransformPoint(
                        anchor.position);
                    if (Vector3.Distance(actual, expected) >
                        MeasureTolerance)
                    {
                        problems.Add(
                            $"anchor '{source.role}' is at {actual}, " +
                            $"expected {expected}");
                    }
                }
            }

            var manifestParts = manifest.parts.ToDictionary(
                part => part.name,
                StringComparer.Ordinal);
            Material lit = AssetDatabase.LoadAssetAtPath<Material>(
                SharedLitMaterialPath);
            Material emission = AssetDatabase.LoadAssetAtPath<Material>(
                SharedEmissionMaterialPath);
            foreach (SupermarketInteriorPartBinding binding in registry.Parts)
            {
                if (binding == null || binding.Renderer == null ||
                    !manifestParts.TryGetValue(
                        binding.SourceName,
                        out SupermarketInteriorManifestPart source))
                {
                    problems.Add("registry has an invalid part binding");
                    continue;
                }

                if (!string.Equals(
                        binding.Role,
                        source.role,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.Group,
                        source.group,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.Sheet,
                        source.sheet ?? string.Empty,
                        StringComparison.Ordinal) ||
                    binding.Emissive != source.emissive ||
                    binding.CastsShadows != ResolveCastsShadows(source) ||
                    binding.Renderer.sharedMaterial !=
                        (source.emissive ? emission : lit))
                {
                    problems.Add(
                        $"part binding '{binding.SourceName}' drifted");
                }
            }

            ValidateCctvPivotChildren(registry, problems);
            for (int index = 1; index <= 4; index++)
            {
                string role = $"tube_{index:00}";
                if (!registry.TryGetRendererByRole(role, out Renderer tube) ||
                    tube == null)
                {
                    problems.Add(
                        $"registry cannot resolve renderer role '{role}'");
                }
            }
        }

        private static void ValidateCctvPivotChildren(
            SupermarketInteriorAssetRegistry registry,
            ICollection<string> problems)
        {
            for (int index = 1; index <= 4; index++)
            {
                string suffix = index.ToString("00");
                if (!registry.TryGetAnchor(
                        $"cctv_head_{suffix}",
                        out Transform pivot) ||
                    !registry.TryGetPart(
                        $"CCTV Head {suffix}",
                        out SupermarketInteriorPartBinding part) ||
                    part.Renderer == null ||
                    !part.Renderer.transform.IsChildOf(pivot))
                {
                    problems.Add(
                        $"CCTV head {suffix} is not parented to its " +
                        "imported pivot");
                }
            }
        }

        private static void ReparentCctvHeads(
            IReadOnlyDictionary<string, Transform> transforms,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            for (int index = 1; index <= 4; index++)
            {
                string suffix = index.ToString("00");
                Transform pivot = RequireTransform(
                    transforms,
                    $"ANCHOR_cctv_head_{suffix}",
                    $"CCTV head {suffix} pivot");
                Renderer renderer = renderers[$"CCTV Head {suffix}"];
                if (!renderer.transform.IsChildOf(pivot))
                {
                    renderer.transform.SetParent(pivot, true);
                }
            }
        }

        private static Dictionary<string, Renderer>
            IndexUniqueRenderers(GameObject model)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"Two imported renderers are named '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Transform>
            IndexUniqueTransforms(GameObject model)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            Transform[] transforms =
                model.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"Two imported transforms are named " +
                        $"'{transform.name}'.");
                }
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            string label)
        {
            if (!transforms.TryGetValue(name, out Transform transform) ||
                transform == null)
            {
                throw new InvalidOperationException(
                    $"The imported supermarket interior has no {label} " +
                    $"named '{name}'.");
            }

            return transform;
        }

        private static void EnsureExactRendererSet(
            SupermarketInteriorManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            var expected = new HashSet<string>(
                manifest.parts.Select(part => part.name),
                StringComparer.Ordinal);
            if (expected.Count != renderers.Count ||
                renderers.Keys.Any(name => !expected.Contains(name)))
            {
                string extras = string.Join(
                    ", ",
                    renderers.Keys.Where(name => !expected.Contains(name)));
                string missing = string.Join(
                    ", ",
                    expected.Where(name => !renderers.ContainsKey(name)));
                throw new InvalidOperationException(
                    "Imported supermarket interior renderer set differs " +
                    $"from manifest. Missing [{missing}], extra [{extras}].");
            }
        }

        private static void AssertPassive(GameObject model)
        {
            var problems = new List<string>();
            AppendForbidden<Collider>(model, problems, "Collider");
            AppendForbidden<Light>(model, problems, "Light");
            AppendForbidden<Camera>(model, problems, "Camera");
            AppendForbidden<AudioSource>(model, problems, "AudioSource");
            AppendForbidden<Rigidbody>(model, problems, "Rigidbody");
            AppendForbidden<Animator>(model, problems, "Animator");
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Imported supermarket interior is not passive: " +
                    string.Join("; ", problems));
            }
        }

        private static void AppendForbidden<TComponent>(
            GameObject root,
            ICollection<string> problems,
            string label)
            where TComponent : Component
        {
            TComponent[] found =
                root.GetComponentsInChildren<TComponent>(true);
            if (found.Length > 0)
            {
                problems.Add(
                    $"found {found.Length} {label}(s), first on " +
                    $"'{found[0].name}'");
            }
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool started = false;
            var result = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            var corner = new Vector3(
                                x == 0 ? world.min.x : world.max.x,
                                y == 0 ? world.min.y : world.max.y,
                                z == 0 ? world.min.z : world.max.z);
                            Vector3 local = root.InverseTransformPoint(corner);
                            if (!started)
                            {
                                result = new Bounds(local, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                result.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!started)
            {
                throw new InvalidOperationException(
                    "The supermarket interior has no renderer bounds.");
            }

            return result;
        }

        private static void AssertMeasuresUpToManifest(
            Bounds measured,
            SupermarketInteriorManifest manifest,
            string assetPath)
        {
            TryGetUnityBounds(manifest, out Bounds expected);
            var problems = new List<string>();
            AppendBoundsProblems(measured, expected, problems);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' imported at the wrong bounds: " +
                    string.Join(", ", problems));
            }
        }

        private static bool TryGetUnityBounds(
            SupermarketInteriorManifest manifest,
            out Bounds bounds)
        {
            float[] minimum = IsFiniteVector(manifest.unity_bounds_min)
                ? manifest.unity_bounds_min
                : null;
            float[] maximum = IsFiniteVector(manifest.unity_bounds_max)
                ? manifest.unity_bounds_max
                : null;
            Vector3 min;
            Vector3 max;
            if (minimum != null && maximum != null)
            {
                min = ToVector(minimum);
                max = ToVector(maximum);
            }
            else if (IsFiniteVector(manifest.bounds_min) &&
                     IsFiniteVector(manifest.bounds_max))
            {
                min = SourceToUnity(manifest.bounds_min);
                max = SourceToUnity(manifest.bounds_max);
            }
            else
            {
                bounds = default;
                return false;
            }

            if (min.x > max.x || min.y > max.y || min.z > max.z)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return true;
        }

        private static bool TryGetUnityPosition(
            SupermarketInteriorManifestAnchor anchor,
            out Vector3 position)
        {
            if (IsFiniteVector(anchor.unity_local_position))
            {
                position = ToVector(anchor.unity_local_position);
                return true;
            }

            if (IsFiniteVector(anchor.local_position))
            {
                position = SourceToUnity(anchor.local_position);
                return true;
            }

            position = default;
            return false;
        }

        private static void AssertAnchorPosition(
            Transform prefabRoot,
            Transform anchor,
            SupermarketInteriorManifestAnchor source)
        {
            if (!TryGetUnityPosition(source, out Vector3 expected))
            {
                throw new InvalidOperationException(
                    $"Anchor '{source.name}' has no finite position.");
            }

            Vector3 measured = prefabRoot.InverseTransformPoint(
                anchor.position);
            if (Vector3.Distance(measured, expected) > MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Anchor '{source.name}' imported at {measured}, " +
                    $"expected {expected}.");
            }
        }

        private static void AppendBoundsProblems(
            Bounds actual,
            Bounds expected,
            ICollection<string> problems,
            string label = "imported bounds")
        {
            if (Vector3.Distance(actual.center, expected.center) >
                MeasureTolerance)
            {
                problems.Add(
                    $"{label} center {actual.center} differs from " +
                    $"{expected.center}");
            }

            if (Vector3.Distance(actual.size, expected.size) >
                MeasureTolerance)
            {
                problems.Add(
                    $"{label} size {actual.size} differs from " +
                    $"{expected.size}");
            }
        }

        private static void AssertDimensions(
            SupermarketInteriorDimensions dimensions,
            ICollection<string> problems)
        {
            if (!Near(dimensions.Width, ExpectedWidth) ||
                !Near(dimensions.Depth, ExpectedDepth) ||
                !Near(dimensions.Height, ExpectedHeight) ||
                !Near(
                    dimensions.WallThickness,
                    ExpectedWallThickness) ||
                !Near(dimensions.EntranceWidth, ExpectedEntranceWidth) ||
                !Near(dimensions.EntranceHeight, ExpectedEntranceHeight))
            {
                problems.Add("registry dimensions differ from layout contract");
            }
        }

        private static bool ResolveCastsShadows(
            SupermarketInteriorManifestPart part)
        {
            return part.casts_shadows || part.shadows;
        }

        private static Color ToColor(float[] values)
        {
            return new Color(
                values[0],
                values[1],
                values[2],
                values.Length > 3 ? values[3] : 1f);
        }

        private static Vector3 ToVector(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static Vector3 SourceToUnity(float[] values)
        {
            return new Vector3(values[0], values[2], values[1]);
        }

        private static bool IsFiniteColor(float[] values)
        {
            if (values == null || (values.Length != 3 && values.Length != 4))
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

        private static bool BoundsAreOrdered(float[] minimum, float[] maximum)
        {
            return minimum[0] <= maximum[0] &&
                minimum[1] <= maximum[1] &&
                minimum[2] <= maximum[2];
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Near(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.001f;
        }

        private static bool PathsEqual(string left, string right)
        {
            return !string.IsNullOrEmpty(left) &&
                string.Equals(
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
                AssetDatabase.SaveAssets();
            }
            catch (Exception error)
            {
                Debug.LogError(error);
            }
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) ||
                AssetDatabase.IsValidFolder(folder))
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
    }

    /// <summary>
    /// The FBX has already been exported in Unity's metre-scale orientation.
    /// Preserve its semantic hierarchy and reject every active scene feature.
    /// </summary>
    public sealed class SupermarketInteriorModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!SupermarketInteriorAssetSetup.IsModelPath(assetPath) ||
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
            if (SupermarketInteriorAssetSetup.IsBuilding)
            {
                return;
            }

            if (ContainsSource(importedAssets) ||
                ContainsSource(movedAssets))
            {
                SupermarketInteriorAssetSetup.QueueBuildWhenSourcesExist();
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
                if (SupermarketInteriorAssetSetup.IsSourcePath(paths[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    internal sealed class SupermarketInteriorManifest
    {
        public string generator;
        public string generator_version;
        public string blender_version;
        public string design_id;
        public string display_name;
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
        public float[] unity_bounds_min;
        public float[] unity_bounds_max;
        public int mesh_count;
        public int triangle_count;
        public SupermarketInteriorManifestAnchor[] anchors;
        public SupermarketInteriorManifestPart[] parts;
        public string build_signature;
    }

    [Serializable]
    internal sealed class SupermarketInteriorManifestDimensions
    {
        public float width;
        public float depth;
        public float height;
    }

    [Serializable]
    internal sealed class SupermarketInteriorManifestOpening
    {
        public float width;
        public float height;
    }

    [Serializable]
    internal sealed class SupermarketInteriorManifestAnchor
    {
        public string name;
        public string role;
        public float[] local_position;
        public float[] unity_local_position;
    }

    [Serializable]
    internal sealed class SupermarketInteriorManifestPart
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
