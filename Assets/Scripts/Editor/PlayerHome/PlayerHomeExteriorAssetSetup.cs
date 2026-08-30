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
    /// Builds the deterministic player-home exterior FBX into the passive
    /// Resources prefab shared by City and the bounded Home reconstruction.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerHomeExteriorAssetSetup
    {
        public const string ModelPath =
            "Assets/PlayerHome/Models/PlayerHomeExterior3D.fbx";
        public const string ManifestPath =
            "Assets/PlayerHome/Models/PlayerHomeExterior3D.json";
        public const string PrefabPath =
            "Assets/Resources/PlayerHome/PlayerHomeExterior3D.prefab";

        public const string TextureFolder =
            "Assets/Resources/PlayerHome/ExteriorTextures";
        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedDesignId =
            "player_home_exterior_v1";
        private const float ExpectedWidth = 13f;
        private const float ExpectedDepth = 12f;
        private const float ExpectedHeight = 8.8f;
        private const float MeasureTolerance = 0.05f;
        private const int MaximumRenderers = 160;
        private const int MaximumTriangles = 18000;

        private static readonly string[] TexturePaths =
        {
            TexturePath("StuccoPrimary"),
            TexturePath("StuccoRepair"),
            TexturePath("BrickPlinth"),
            TexturePath("RoofSlate"),
            TexturePath("PaintedWood"),
            TexturePath("PaintedMetal"),
            TexturePath("WindowFrame"),
            TexturePath("WindowGlass"),
            TexturePath("Concrete")
        };

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

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static PlayerHomeExteriorAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem(
            "Bar Promenade/Player Home/Build Exterior Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem(
            "Bar Promenade/Player Home/Validate Exterior Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Player-home exterior model contract is valid.");
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

        public static bool IsTexturePath(string path)
        {
            for (int index = 0; index < TexturePaths.Length; index++)
            {
                if (PathsEqual(path, TexturePaths[index]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsSourcePath(string path)
        {
            return IsModelPath(path) ||
                IsManifestPath(path) ||
                IsTexturePath(path);
        }

        public static bool SourcesExist()
        {
            if (!File.Exists(ModelPath) || !File.Exists(ManifestPath))
            {
                return false;
            }

            for (int index = 0; index < TexturePaths.Length; index++)
            {
                if (!File.Exists(TexturePaths[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (buildQueued || !SourcesExist())
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
                    "Player-home exterior sources are missing. Run the " +
                    "deterministic Blender and texture generators first.");
            }

            IsBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                ImportSources();
                PlayerHomeExteriorManifest manifest =
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
            PlayerHomeExteriorManifest manifest =
                LoadAndValidateManifest();
            ValidateTextureImportContracts();
            ValidatePrefabOrThrow(manifest);
        }

        private static string TexturePath(string surface)
        {
            return $"{TextureFolder}/PlayerHomeExterior{surface}Albedo.png";
        }

        private static void ImportSources()
        {
            for (int index = 0; index < TexturePaths.Length; index++)
            {
                AssetDatabase.ImportAsset(
                    TexturePaths[index],
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
            }

            foreach (string path in new[] { ModelPath, ManifestPath })
            {
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void BuildPrefab(
            PlayerHomeExteriorManifest manifest)
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
                    "Player-home exterior shared materials failed to load.");
            }

            var root = new GameObject("PlayerHomeExterior3D");
            try
            {
                var model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{ModelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(
                    0f,
                    manifest.runtime_wrapper_yaw_degrees,
                    0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexTransforms(model);
                EnsureExactRendererSet(manifest, renderers);

                var parts = new List<PlayerHomeExteriorPartBinding>();
                foreach (PlayerHomeExteriorManifestPart part in
                         manifest.parts)
                {
                    Renderer renderer = renderers[part.name];
                    renderer.sharedMaterial =
                        part.emissive ? sharedEmission : sharedLit;
                    renderer.shadowCastingMode = part.shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = part.shadows;
                    parts.Add(new PlayerHomeExteriorPartBinding(
                        part.name,
                        part.role,
                        part.group,
                        part.sheet,
                        part.emissive,
                        part.shadows,
                        renderer));
                }

                var anchors = new List<PlayerHomeExteriorAnchorBinding>();
                foreach (PlayerHomeExteriorManifestAnchor anchor in
                         manifest.anchors)
                {
                    string transformName = $"ANCHOR_{anchor.name}";
                    if (!transforms.TryGetValue(
                            transformName,
                            out Transform transform))
                    {
                        throw new InvalidOperationException(
                            $"Player-home exterior anchor " +
                            $"'{transformName}' is missing from the model.");
                    }

                    AssertAnchorPosition(
                        root.transform,
                        transform,
                        anchor,
                        manifest.runtime_wrapper_yaw_degrees);
                    anchors.Add(new PlayerHomeExteriorAnchorBinding(
                        anchor.name,
                        anchor.role,
                        transform));
                }

                Bounds measured = CalculateLocalBounds(
                    root.transform,
                    renderers.Values);
                AssertMeasuresUpToManifest(measured, manifest, PrefabPath);

                PlayerHomeExteriorAssetRegistry registry =
                    root.AddComponent<PlayerHomeExteriorAssetRegistry>();
                registry.Configure(
                    ResolveAuthoringRoot(model.transform),
                    anchors
                        .OrderBy(
                            binding => binding.AnchorName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    parts
                        .OrderBy(
                            binding => binding.SourceName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    measured,
                    new PlayerHomeExteriorDimensions(
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.depth,
                        manifest.dimensions_m.height),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PlayerHomeExteriorManifest
            LoadAndValidateManifest()
        {
            string json = File.ReadAllText(ManifestPath);
            PlayerHomeExteriorManifest manifest =
                JsonUtility.FromJson<PlayerHomeExteriorManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    $"'{ManifestPath}' is not a player-home exterior " +
                    "manifest.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Player-home design id '{manifest.design_id}' is not " +
                    $"'{ExpectedDesignId}'.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras ||
                manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The player-home exterior must be a passive asset.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The player-home manifest has no generator version or " +
                    "build signature.");
            }

            ValidateDimensions(manifest.dimensions_m);
            ValidateBounds(manifest);
            ValidateParts(manifest);
            ValidateAnchors(manifest);
            ValidateClearanceContract(manifest.surface_clearance_contract);
            return manifest;
        }

        private static void ValidateDimensions(
            PlayerHomeExteriorManifestDimensions dimensions)
        {
            if (dimensions == null ||
                Mathf.Abs(dimensions.width - ExpectedWidth) > 0.0001f ||
                Mathf.Abs(dimensions.depth - ExpectedDepth) > 0.0001f ||
                Mathf.Abs(dimensions.height - ExpectedHeight) > 0.0001f)
            {
                throw new InvalidOperationException(
                    "The player-home exterior must remain exactly " +
                    $"{ExpectedWidth} x {ExpectedDepth} x " +
                    $"{ExpectedHeight} metres.");
            }
        }

        private static void ValidateBounds(
            PlayerHomeExteriorManifest manifest)
        {
            if (!IsFiniteVector(manifest.bounds_min) ||
                !IsFiniteVector(manifest.bounds_max))
            {
                throw new InvalidOperationException(
                    "The player-home manifest has invalid bounds.");
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (manifest.bounds_max[axis] <= manifest.bounds_min[axis])
                {
                    throw new InvalidOperationException(
                        "The player-home manifest bounds are empty.");
                }
            }

            float[] expectedMinimum =
                { -ExpectedWidth * 0.5f, -ExpectedDepth * 0.5f, 0f };
            float[] expectedMaximum =
            {
                ExpectedWidth * 0.5f,
                ExpectedDepth * 0.5f + 2.3f,
                ExpectedHeight
            };
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(
                        manifest.bounds_min[axis] -
                        expectedMinimum[axis]) > 0.001f ||
                    Mathf.Abs(
                        manifest.bounds_max[axis] -
                        expectedMaximum[axis]) > 0.001f)
                {
                    throw new InvalidOperationException(
                        "The player-home authored bounds drifted from its " +
                        "13 x 12 x 8.8 metre body plus the required 2.3 " +
                        "metre outward balcony.");
                }
            }
        }

        private static void ValidateParts(
            PlayerHomeExteriorManifest manifest)
        {
            if (manifest.parts == null || manifest.parts.Length == 0)
            {
                throw new InvalidOperationException(
                    "The player-home manifest has no semantic parts.");
            }

            if (manifest.parts.Length > MaximumRenderers ||
                manifest.triangle_count <= 0 ||
                manifest.triangle_count > MaximumTriangles)
            {
                throw new InvalidOperationException(
                    "The player-home exterior exceeds its geometry budget.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var sheets = new HashSet<string>(StringComparer.Ordinal);
            int triangleSum = 0;
            foreach (PlayerHomeExteriorManifestPart part in manifest.parts)
            {
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    string.IsNullOrWhiteSpace(part.group) ||
                    string.IsNullOrWhiteSpace(part.sheet) ||
                    !names.Add(part.name) ||
                    part.vertices <= 0 ||
                    part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        "The player-home manifest has an invalid part.");
                }

                if (!PlayerHomeExteriorSurfaceAppearance.TryResolveSheet(
                        part.sheet,
                        out _))
                {
                    throw new InvalidOperationException(
                        $"No runtime surface resolves '{part.sheet}'.");
                }

                sheets.Add(part.sheet);
                triangleSum += part.triangles;
            }

            if (!sheets.SetEquals(RequiredSheets))
            {
                throw new InvalidOperationException(
                    "The player-home semantic sheet set is incomplete.");
            }

            if (triangleSum != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Part triangles total {triangleSum}, but the manifest " +
                    $"declares {manifest.triangle_count}.");
            }

            PlayerHomeExteriorManifestPart[] emissiveParts = manifest.parts
                .Where(part => part.emissive)
                .ToArray();
            if (emissiveParts.Length != 1 ||
                !string.Equals(
                    emissiveParts[0].name,
                    "Front Lit Window Glass",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    emissiveParts[0].sheet,
                    "WindowGlass",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    emissiveParts[0].role,
                    "exterior_glass",
                    StringComparison.Ordinal) ||
                manifest.parts.Any(part =>
                    part != emissiveParts[0] &&
                    string.Equals(
                        part.sheet,
                        "WindowGlass",
                        StringComparison.Ordinal) &&
                    part.emissive))
            {
                throw new InvalidOperationException(
                    "Exactly 'Front Lit Window Glass' may be emissive; " +
                    "every other WindowGlass part must remain dark.");
            }
        }

        private static void ValidateAnchors(
            PlayerHomeExteriorManifest manifest)
        {
            if (manifest.anchors == null || manifest.anchors.Length == 0)
            {
                throw new InvalidOperationException(
                    "The player-home manifest has no anchors.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int exteriorDoors = 0;
            foreach (PlayerHomeExteriorManifestAnchor anchor in
                     manifest.anchors)
            {
                if (anchor == null ||
                    string.IsNullOrWhiteSpace(anchor.name) ||
                    string.IsNullOrWhiteSpace(anchor.role) ||
                    !names.Add(anchor.name) ||
                    !IsFiniteVector(anchor.local_position))
                {
                    throw new InvalidOperationException(
                        "The player-home manifest has an invalid anchor.");
                }

                if (anchor.unity_local_position != null &&
                    !IsFiniteVector(anchor.unity_local_position))
                {
                    throw new InvalidOperationException(
                        $"Anchor '{anchor.name}' has an invalid Unity pose.");
                }

                if (string.Equals(
                        anchor.role,
                        "exterior_door",
                        StringComparison.Ordinal))
                {
                    exteriorDoors++;
                    Vector3 expected = ResolveAnchorPosition(
                        anchor,
                        manifest.runtime_wrapper_yaw_degrees);
                    if (Vector3.Distance(
                            expected,
                            new Vector3(0f, 0f, 6f)) > 0.0001f)
                    {
                        throw new InvalidOperationException(
                            "The player-home exterior door moved from " +
                            "Unity local [0, 0, 6].");
                    }
                }
            }

            if (exteriorDoors != 1)
            {
                throw new InvalidOperationException(
                    "The player-home manifest must declare exactly one " +
                    "exterior_door anchor.");
            }
        }

        private static void ValidateClearanceContract(
            PlayerHomeSurfaceClearanceContract contract)
        {
            if (contract == null ||
                Mathf.Abs(contract.opaque_overlay_min_clearance_m - 0.03f) >
                0.0001f ||
                Mathf.Abs(contract.runtime_foundation_inset_m - 0.08f) >
                0.0001f ||
                !string.Equals(
                    contract.facade_uv,
                    "authored_per_elevation_no_whole_building_stretch",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    contract.openings,
                    "separate_geometry_not_baked",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The player-home no-overlap/no-stretch surface contract " +
                    "is missing or invalid.");
            }
        }

        private static void ValidatePrefabOrThrow(
            PlayerHomeExteriorManifest manifest)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"The player-home exterior prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            PlayerHomeExteriorAssetRegistry registry =
                prefab.GetComponent<PlayerHomeExteriorAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The player-home prefab has no exterior registry.");
            }

            var problems = new List<string>();
            AppendIfDifferent(
                registry.DesignId,
                manifest.design_id,
                "design id",
                problems);
            AppendIfDifferent(
                registry.BuildSignature,
                manifest.build_signature,
                "build signature",
                problems);
            AppendIfDifferent(
                registry.SourceGeneratorVersion,
                manifest.generator_version,
                "generator version",
                problems);
            AppendDimensions(registry.Dimensions, problems);
            if (registry.SourceTriangleCount != manifest.triangle_count)
            {
                problems.Add("registry triangle count differs from manifest");
            }

            AppendForbidden<Collider>(prefab, problems, "collider");
            AppendForbidden<Light>(prefab, problems, "light");
            AppendForbidden<Camera>(prefab, problems, "camera");
            AppendForbidden<Rigidbody>(prefab, problems, "rigidbody");
            AppendForbidden<Animator>(prefab, problems, "animator");

            Renderer[] rendererArray =
                prefab.GetComponentsInChildren<Renderer>(true);
            Dictionary<string, Renderer> renderers;
            try
            {
                renderers = IndexUniqueRenderers(prefab);
                EnsureExactRendererSet(manifest, renderers);
            }
            catch (InvalidOperationException error)
            {
                problems.Add(error.Message);
                renderers = new Dictionary<string, Renderer>(
                    StringComparer.Ordinal);
            }

            ValidatePartBindings(manifest, registry, renderers, problems);
            ValidateAnchorBindings(manifest, registry, problems);
            Bounds measured = CalculateLocalBounds(
                prefab.transform,
                rendererArray);
            AppendBoundsProblems(
                measured,
                ExpectedImportedBounds(manifest),
                problems);
            AppendBoundsProblems(
                registry.LocalBounds,
                measured,
                problems,
                "registry bounds");
            if (registry.ModelRoot == null ||
                !registry.ModelRoot.IsChildOf(prefab.transform))
            {
                problems.Add(
                    "registry model root is missing or outside the prefab");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{PrefabPath}' failed validation:" +
                    Environment.NewLine + "  " +
                    string.Join(Environment.NewLine + "  ", problems));
            }
        }

        private static void ValidatePartBindings(
            PlayerHomeExteriorManifest manifest,
            PlayerHomeExteriorAssetRegistry registry,
            IReadOnlyDictionary<string, Renderer> renderers,
            List<string> problems)
        {
            if (registry.Parts.Count != manifest.parts.Length)
            {
                problems.Add("registry part count differs from manifest");
            }

            var expected = manifest.parts.ToDictionary(
                part => part.name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerHomeExteriorPartBinding binding in registry.Parts)
            {
                if (binding == null ||
                    !expected.TryGetValue(
                        binding.SourceName,
                        out PlayerHomeExteriorManifestPart part) ||
                    !seen.Add(binding.SourceName))
                {
                    problems.Add("registry contains an unknown/repeated part");
                    continue;
                }

                if (binding.Renderer == null ||
                    !renderers.TryGetValue(
                        binding.SourceName,
                        out Renderer renderer) ||
                    binding.Renderer != renderer ||
                    !string.Equals(binding.Role, part.role) ||
                    !string.Equals(binding.Group, part.group) ||
                    !string.Equals(binding.Sheet, part.sheet) ||
                    binding.Emissive != part.emissive ||
                    binding.CastsShadows != part.shadows)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' binding drifted");
                }
            }
        }

        private static void ValidateAnchorBindings(
            PlayerHomeExteriorManifest manifest,
            PlayerHomeExteriorAssetRegistry registry,
            List<string> problems)
        {
            if (registry.Anchors.Count != manifest.anchors.Length)
            {
                problems.Add("registry anchor count differs from manifest");
            }

            var expected = manifest.anchors.ToDictionary(
                anchor => anchor.name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerHomeExteriorAnchorBinding binding in
                     registry.Anchors)
            {
                if (binding == null ||
                    !expected.TryGetValue(
                        binding.AnchorName,
                        out PlayerHomeExteriorManifestAnchor anchor) ||
                    !seen.Add(binding.AnchorName) ||
                    binding.Anchor == null ||
                    !string.Equals(binding.Role, anchor.role) ||
                    !string.Equals(
                        binding.Anchor.name,
                        $"ANCHOR_{binding.AnchorName}"))
                {
                    problems.Add("registry contains an invalid anchor");
                }
            }

            if (!registry.TryGetAnchor(
                    "exterior_door",
                    out Transform door) ||
                door == null)
            {
                problems.Add("registry does not resolve exterior_door");
            }
        }

        private static void ValidateTextureImportContracts()
        {
            for (int index = 0; index < TexturePaths.Length; index++)
            {
                string path = TexturePaths[index];
                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null ||
                    importer.textureType != TextureImporterType.Default ||
                    importer.textureShape != TextureImporterShape.Texture2D ||
                    !importer.sRGBTexture ||
                    importer.alphaSource != TextureImporterAlphaSource.None ||
                    !importer.mipmapEnabled ||
                    importer.streamingMipmaps ||
                    importer.isReadable ||
                    importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.wrapMode != TextureWrapMode.Repeat ||
                    importer.filterMode != FilterMode.Bilinear ||
                    importer.anisoLevel != 4 ||
                    importer.textureCompression !=
                        TextureImporterCompression.Uncompressed ||
                    importer.maxTextureSize != 1024)
                {
                    throw new InvalidOperationException(
                        $"Player-home texture importer drifted for " +
                        $"'{path}'.");
                }
            }
        }

        private static void EnsureExactRendererSet(
            PlayerHomeExteriorManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            var expected = new HashSet<string>(
                manifest.parts.Select(part => part.name),
                StringComparer.Ordinal);
            if (!expected.SetEquals(renderers.Keys))
            {
                throw new InvalidOperationException(
                    "Imported player-home renderer names differ from the " +
                    "manifest.");
            }
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject root)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"The player-home model repeats renderer " +
                        $"'{renderer.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexTransforms(
            GameObject root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"The player-home model repeats transform " +
                        $"'{transform.name}'.");
                }
            }

            return result;
        }

        private static void AssertAnchorPosition(
            Transform prefabRoot,
            Transform anchorTransform,
            PlayerHomeExteriorManifestAnchor anchor,
            float wrapperYaw)
        {
            Vector3 expected = ResolveAnchorPosition(anchor, wrapperYaw);
            Vector3 measured = prefabRoot.InverseTransformPoint(
                anchorTransform.position);
            if (Vector3.Distance(measured, expected) > MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Player-home anchor '{anchor.name}' imported at " +
                    $"{measured} against {expected}.");
            }
        }

        private static Vector3 ResolveAnchorPosition(
            PlayerHomeExteriorManifestAnchor anchor,
            float wrapperYaw)
        {
            if (IsFiniteVector(anchor.unity_local_position))
            {
                return ToVector3(anchor.unity_local_position);
            }

            return Quaternion.Euler(0f, wrapperYaw, 0f) *
                BlenderToUnity(anchor.local_position);
        }

        private static void AssertMeasuresUpToManifest(
            Bounds measured,
            PlayerHomeExteriorManifest manifest,
            string assetPath)
        {
            var problems = new List<string>();
            AppendBoundsProblems(
                measured,
                ExpectedImportedBounds(manifest),
                problems);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' imported at the wrong bounds: " +
                    string.Join(", ", problems));
            }
        }

        private static Bounds ExpectedImportedBounds(
            PlayerHomeExteriorManifest manifest)
        {
            Vector3 minimum = BlenderToUnity(manifest.bounds_min);
            Vector3 maximum = BlenderToUnity(manifest.bounds_max);
            var source = new Bounds();
            source.SetMinMax(
                Vector3.Min(minimum, maximum),
                Vector3.Max(minimum, maximum));
            Quaternion rotation = Quaternion.Euler(
                0f,
                manifest.runtime_wrapper_yaw_degrees,
                0f);

            bool started = false;
            var result = new Bounds();
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(
                            x == 0 ? source.min.x : source.max.x,
                            y == 0 ? source.min.y : source.max.y,
                            z == 0 ? source.min.z : source.max.z);
                        Vector3 point = rotation * corner;
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

            return result;
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
                            Vector3 local =
                                root.InverseTransformPoint(corner);
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
                    "The player-home exterior has no renderer bounds.");
            }

            return result;
        }

        private static Transform ResolveAuthoringRoot(Transform wrapper)
        {
            Transform result = null;
            foreach (Transform child in wrapper)
            {
                if (!string.Equals(
                        child.name,
                        ExpectedDesignId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        "The player-home model has more than one authored " +
                        $"'{ExpectedDesignId}' root node.");
                }

                result = child;
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "The player-home model has no authored " +
                    $"'{ExpectedDesignId}' root node.");
            }

            return result;
        }

        private static void AppendDimensions(
            PlayerHomeExteriorDimensions dimensions,
            List<string> problems)
        {
            AppendIfFar(dimensions.Width, ExpectedWidth, "width", problems);
            AppendIfFar(dimensions.Depth, ExpectedDepth, "depth", problems);
            AppendIfFar(dimensions.Height, ExpectedHeight, "height", problems);
        }

        private static void AppendIfFar(
            float actual,
            float expected,
            string label,
            List<string> problems)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
            {
                problems.Add(
                    $"{label} is {actual:0.###} m, expected " +
                    $"{expected:0.###} m");
            }
        }

        private static void AppendIfDifferent(
            string actual,
            string expected,
            string label,
            List<string> problems)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{label} '{actual}' differs from '{expected}'");
            }
        }

        private static void AppendForbidden<TComponent>(
            GameObject prefab,
            List<string> problems,
            string label)
            where TComponent : Component
        {
            TComponent[] found =
                prefab.GetComponentsInChildren<TComponent>(true);
            if (found.Length > 0)
            {
                problems.Add(
                    $"the prefab carries {found.Length} {label}(s)");
            }
        }

        private static void AppendBoundsProblems(
            Bounds actual,
            Bounds expected,
            List<string> problems,
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

        private static Vector3 BlenderToUnity(float[] values)
        {
            return new Vector3(values[0], values[2], values[1]);
        }

        private static Vector3 ToVector3(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool IsFiniteVector(float[] values)
        {
            return values != null && values.Length == 3 &&
                IsFinite(values[0]) &&
                IsFinite(values[1]) &&
                IsFinite(values[2]);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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

    [Serializable]
    internal sealed class PlayerHomeExteriorManifest
    {
        public string generator_version;
        public string design_id;
        public float[] bounds_min;
        public float[] bounds_max;
        public PlayerHomeExteriorManifestDimensions dimensions_m;
        public float runtime_wrapper_yaw_degrees;
        public bool colliders;
        public bool lights;
        public bool cameras;
        public int animation_count;
        public int triangle_count;
        public PlayerHomeExteriorManifestAnchor[] anchors;
        public PlayerHomeExteriorManifestPart[] parts;
        public PlayerHomeSurfaceClearanceContract surface_clearance_contract;
        public string build_signature;
    }

    [Serializable]
    internal sealed class PlayerHomeExteriorManifestDimensions
    {
        public float width;
        public float depth;
        public float height;
    }

    [Serializable]
    internal sealed class PlayerHomeExteriorManifestAnchor
    {
        public string name;
        public string role;
        public float[] local_position;
        public float[] unity_local_position;
    }

    [Serializable]
    internal sealed class PlayerHomeExteriorManifestPart
    {
        public string name;
        public string role;
        public string group;
        public string sheet;
        public bool emissive;
        public bool shadows;
        public int vertices;
        public int triangles;
        public float[] bounds_min;
        public float[] bounds_max;
    }

    [Serializable]
    internal sealed class PlayerHomeSurfaceClearanceContract
    {
        public float opaque_overlay_min_clearance_m;
        public float runtime_foundation_inset_m;
        public string facade_uv;
        public string openings;
    }
}
