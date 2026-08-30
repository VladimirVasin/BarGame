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
    /// Builds the deterministic supermarket exterior FBX into the passive
    /// Resources prefab consumed by the city and player-home views.
    /// </summary>
    [InitializeOnLoad]
    public static class SupermarketExteriorAssetSetup
    {
        public const string ModelPath =
            "Assets/Supermarket/Models/SupermarketExterior3D.fbx";
        public const string ManifestPath =
            "Assets/Supermarket/Models/SupermarketExterior3D.json";
        public const string PrefabPath =
            "Assets/Resources/Supermarket/SupermarketExterior3D.prefab";

        public const string WallAtlasTexturePath =
            "Assets/Resources/Supermarket/ExteriorTextures/" +
            "SupermarketExteriorWallAtlas.png";
        public const string FasciaAtlasTexturePath =
            "Assets/Resources/Supermarket/ExteriorTextures/" +
            "SupermarketExteriorFasciaAtlas.png";
        public const string BrickTexturePath =
            "Assets/Resources/Supermarket/ExteriorTextures/" +
            "SupermarketExteriorBrickAlbedo.png";
        public const string MetalTexturePath =
            "Assets/Resources/Supermarket/ExteriorTextures/" +
            "SupermarketExteriorMetalAlbedo.png";

        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedDesignId =
            "supermarket_exterior_v1";
        private const float ExpectedWidth =
            SupermarketEntranceGeometry.ExteriorWidth;
        private const float ExpectedDepth =
            SupermarketEntranceGeometry.ExteriorDepth;
        private const float ExpectedHeight =
            SupermarketEntranceGeometry.ExteriorHeight;
        private const float MeasureTolerance = 0.05f;
        private const int MaximumRenderers = 120;
        private const int MaximumTriangles = 12000;

        private static readonly string[] TexturePaths =
        {
            WallAtlasTexturePath,
            FasciaAtlasTexturePath,
            BrickTexturePath,
            MetalTexturePath,
        };

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static SupermarketExteriorAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Build Exterior Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem(
            "Bar Promenade/Supermarket/Validate Exterior Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Supermarket exterior model contract is valid.");
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

        public static bool IsAtlasTexturePath(string path)
        {
            return PathsEqual(path, WallAtlasTexturePath) ||
                PathsEqual(path, FasciaAtlasTexturePath);
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
                    "Supermarket exterior sources are missing. Run " +
                    "tools/build-supermarket-exterior-3d-model.py through " +
                    "Blender first.");
            }

            IsBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                ImportSources();
                SupermarketExteriorManifest manifest =
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
            SupermarketExteriorManifest manifest =
                LoadAndValidateManifest();
            ValidateTextureImportContracts();
            ValidatePrefabOrThrow(manifest);
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
            SupermarketExteriorManifest manifest)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}'.");
            }

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "Supermarket exterior shared materials failed to load.");
            }

            var root = new GameObject("SupermarketExterior3D");
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

                var parts = new List<SupermarketExteriorPartBinding>();
                foreach (SupermarketExteriorManifestPart part in
                         manifest.parts)
                {
                    Renderer renderer = renderers[part.name];
                    renderer.sharedMaterial =
                        part.emissive ? sharedEmission : sharedLit;
                    renderer.shadowCastingMode = part.shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = part.shadows;

                    parts.Add(new SupermarketExteriorPartBinding(
                        part.name,
                        part.role,
                        part.group,
                        part.sheet,
                        part.emissive,
                        part.shadows,
                        renderer));
                }

                var anchors = new List<SupermarketExteriorAnchorBinding>();
                foreach (SupermarketExteriorManifestAnchor anchor in
                         manifest.anchors)
                {
                    string transformName = $"ANCHOR_{anchor.name}";
                    if (!transforms.TryGetValue(
                            transformName,
                            out Transform transform))
                    {
                        throw new InvalidOperationException(
                            $"Supermarket exterior anchor " +
                            $"'{transformName}' is in the manifest but " +
                            "not in the model.");
                    }

                    AssertAnchorPosition(
                        root.transform,
                        transform,
                        anchor,
                        manifest.runtime_wrapper_yaw_degrees);
                    anchors.Add(new SupermarketExteriorAnchorBinding(
                        anchor.name,
                        anchor.role,
                        transform));
                }

                Bounds measured = CalculateLocalBounds(
                    root.transform,
                    renderers.Values);
                AssertMeasuresUpToManifest(measured, manifest, PrefabPath);

                SupermarketExteriorAssetRegistry registry =
                    root.AddComponent<SupermarketExteriorAssetRegistry>();
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
                    new SupermarketExteriorDimensions(
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

        private static void ValidatePrefabOrThrow(
            SupermarketExteriorManifest manifest)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"The supermarket exterior prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            var problems = new List<string>();
            SupermarketExteriorAssetRegistry registry =
                prefab.GetComponent<SupermarketExteriorAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior prefab has no " +
                    "SupermarketExteriorAssetRegistry.");
            }

            AppendIfDifferent(
                registry.BuildSignature,
                manifest.build_signature,
                "build signature",
                problems);
            AppendIfDifferent(
                registry.DesignId,
                manifest.design_id,
                "design id",
                problems);
            AppendIfDifferent(
                registry.SourceGeneratorVersion,
                manifest.generator_version,
                "generator version",
                problems);

            AppendDimensionProblems(registry.Dimensions, problems);
            if (registry.SourceTriangleCount != manifest.triangle_count)
            {
                problems.Add(
                    $"registry triangle count " +
                    $"{registry.SourceTriangleCount} differs from the " +
                    $"manifest's {manifest.triangle_count}");
            }

            AppendForbidden<Collider>(prefab, problems, "collider");
            AppendForbidden<Light>(prefab, problems, "light");
            AppendForbidden<Camera>(prefab, problems, "camera");
            AppendForbidden<Rigidbody>(prefab, problems, "rigidbody");
            AppendForbidden<Animator>(prefab, problems, "animator");

            Renderer[] rendererArray =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (rendererArray.Length > MaximumRenderers)
            {
                problems.Add(
                    $"{rendererArray.Length} renderers exceed the " +
                    $"{MaximumRenderers} allowed");
            }

            if (registry.SourceTriangleCount > MaximumTriangles)
            {
                problems.Add(
                    $"{registry.SourceTriangleCount} triangles exceed " +
                    $"the {MaximumTriangles} allowed");
            }

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

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            ValidatePartBindings(
                manifest,
                registry,
                renderers,
                sharedLit,
                sharedEmission,
                problems);
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

        private static SupermarketExteriorManifest LoadAndValidateManifest()
        {
            string json = File.ReadAllText(ManifestPath);
            SupermarketExteriorManifest manifest =
                JsonUtility.FromJson<SupermarketExteriorManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    $"'{ManifestPath}' is not a supermarket exterior " +
                    "manifest.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Supermarket exterior design id " +
                    $"'{manifest.design_id}' is not " +
                    $"'{ExpectedDesignId}'.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior declares colliders, lights " +
                    "or cameras; city plans own all three.");
            }

            if (manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior declares animation; the " +
                    "authored building must be passive.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The supermarket exterior manifest has no generator " +
                    "version or build signature.");
            }

            ValidateDimensions(manifest.dimensions_m);
            ValidateBounds(manifest);
            ValidateParts(manifest);
            ValidateAnchors(manifest);
            return manifest;
        }

        private static void ValidateDimensions(
            SupermarketExteriorManifestDimensions dimensions)
        {
            if (dimensions == null)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior manifest has no dimensions.");
            }

            var problems = new List<string>();
            AppendIfFar(
                dimensions.width,
                ExpectedWidth,
                "width",
                problems);
            AppendIfFar(
                dimensions.depth,
                ExpectedDepth,
                "depth",
                problems);
            AppendIfFar(
                dimensions.height,
                ExpectedHeight,
                "height",
                problems);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Supermarket exterior dimensions do not match the " +
                    "city lot contract: " + string.Join(", ", problems));
            }
        }

        private static void ValidateBounds(
            SupermarketExteriorManifest manifest)
        {
            if (!IsFiniteVector(manifest.bounds_min) ||
                !IsFiniteVector(manifest.bounds_max))
            {
                throw new InvalidOperationException(
                    "The supermarket exterior manifest has invalid bounds.");
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (manifest.bounds_min[axis] > manifest.bounds_max[axis])
                {
                    throw new InvalidOperationException(
                        "The supermarket exterior manifest bounds are " +
                        "inverted.");
                }
            }

            if (!IsFinite(manifest.runtime_wrapper_yaw_degrees))
            {
                throw new InvalidOperationException(
                    "The supermarket exterior wrapper yaw is invalid.");
            }
        }

        private static void ValidateParts(
            SupermarketExteriorManifest manifest)
        {
            if (manifest.parts == null || manifest.parts.Length == 0)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior manifest has no parts.");
            }

            if (manifest.parts.Length > MaximumRenderers)
            {
                throw new InvalidOperationException(
                    $"The supermarket exterior declares " +
                    $"{manifest.parts.Length} parts; at most " +
                    $"{MaximumRenderers} are allowed.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int triangleTotal = 0;
            foreach (SupermarketExteriorManifestPart part in manifest.parts)
            {
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    string.IsNullOrWhiteSpace(part.group) ||
                    string.IsNullOrWhiteSpace(part.sheet))
                {
                    throw new InvalidOperationException(
                        "Every supermarket exterior part needs a name, " +
                        "role, group and sheet.");
                }

                if (!names.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"The supermarket exterior manifest repeats part " +
                        $"'{part.name}'.");
                }

                if (part.vertices <= 0 || part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        $"Supermarket exterior part '{part.name}' has " +
                        "no usable geometry counts.");
                }

                checked
                {
                    triangleTotal += part.triangles;
                }
            }

            if (manifest.triangle_count != triangleTotal)
            {
                throw new InvalidOperationException(
                    $"The supermarket exterior manifest declares " +
                    $"{manifest.triangle_count} triangles but its parts " +
                    $"sum to {triangleTotal}.");
            }

            if (manifest.triangle_count <= 0 ||
                manifest.triangle_count > MaximumTriangles)
            {
                throw new InvalidOperationException(
                    $"The supermarket exterior triangle count " +
                    $"{manifest.triangle_count} is outside 1.." +
                    $"{MaximumTriangles}.");
            }
        }

        private static void ValidateAnchors(
            SupermarketExteriorManifest manifest)
        {
            if (manifest.anchors == null || manifest.anchors.Length == 0)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior manifest has no anchors.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int exteriorDoorCount = 0;
            foreach (SupermarketExteriorManifestAnchor anchor in
                     manifest.anchors)
            {
                if (anchor == null ||
                    string.IsNullOrWhiteSpace(anchor.name) ||
                    string.IsNullOrWhiteSpace(anchor.role) ||
                    !IsFiniteVector(anchor.local_position))
                {
                    throw new InvalidOperationException(
                        "Every supermarket exterior anchor needs a name, " +
                        "role and three-component local position.");
                }

                if (!names.Add(anchor.name))
                {
                    throw new InvalidOperationException(
                        $"The supermarket exterior manifest repeats " +
                        $"anchor '{anchor.name}'.");
                }

                if (string.Equals(
                        anchor.role,
                        "exterior_door",
                        StringComparison.Ordinal))
                {
                    exteriorDoorCount++;
                }
            }

            if (exteriorDoorCount != 1)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior must declare exactly one " +
                    "anchor with role 'exterior_door'.");
            }
        }

        private static void EnsureExactRendererSet(
            SupermarketExteriorManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            if (renderers.Count != manifest.parts.Length)
            {
                throw new InvalidOperationException(
                    $"The supermarket exterior has {renderers.Count} " +
                    $"renderers against {manifest.parts.Length} manifest " +
                    "parts.");
            }

            foreach (SupermarketExteriorManifestPart part in manifest.parts)
            {
                if (!renderers.ContainsKey(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest part '{part.name}' has no renderer in " +
                        "the supermarket exterior model.");
                }
            }
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject model)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            foreach (Renderer renderer in
                     model.GetComponentsInChildren<Renderer>(true))
            {
                if (result.ContainsKey(renderer.name))
                {
                    throw new InvalidOperationException(
                        $"The supermarket exterior has two renderers named " +
                        $"'{renderer.name}'; renderer names are the bridge " +
                        "to the manifest.");
                }

                result.Add(renderer.name, renderer);
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexTransforms(
            GameObject model)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (Transform transform in
                     model.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(transform.name, transform) &&
                    transform.name.StartsWith(
                        "ANCHOR_",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The supermarket exterior has two transforms " +
                        $"named '{transform.name}'.");
                }
            }

            return result;
        }

        private static void ValidatePartBindings(
            SupermarketExteriorManifest manifest,
            SupermarketExteriorAssetRegistry registry,
            IReadOnlyDictionary<string, Renderer> renderers,
            Material sharedLit,
            Material sharedEmission,
            List<string> problems)
        {
            if (sharedLit == null || sharedEmission == null)
            {
                problems.Add("shared material dependencies are missing");
                return;
            }

            if (registry.Parts.Count != manifest.parts.Length)
            {
                problems.Add(
                    $"registry has {registry.Parts.Count} parts against " +
                    $"the manifest's {manifest.parts.Length}");
            }

            var manifestByName = manifest.parts.ToDictionary(
                part => part.name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SupermarketExteriorPartBinding binding in registry.Parts)
            {
                if (binding == null ||
                    !manifestByName.TryGetValue(
                        binding.SourceName,
                        out SupermarketExteriorManifestPart part))
                {
                    problems.Add("registry contains an unknown part binding");
                    continue;
                }

                if (!seen.Add(binding.SourceName))
                {
                    problems.Add(
                        $"registry repeats part '{binding.SourceName}'");
                }

                if (binding.Renderer == null ||
                    !renderers.TryGetValue(
                        binding.SourceName,
                        out Renderer renderer) ||
                    binding.Renderer != renderer)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' has the wrong " +
                        "renderer binding");
                    continue;
                }

                if (!string.Equals(
                        binding.Role,
                        part.role,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.Group,
                        part.group,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        binding.Sheet,
                        part.sheet,
                        StringComparison.Ordinal) ||
                    binding.Emissive != part.emissive ||
                    binding.CastsShadows != part.shadows)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' metadata differs " +
                        "from the manifest");
                }

                Material expectedMaterial =
                    part.emissive ? sharedEmission : sharedLit;
                if (renderer.sharedMaterial != expectedMaterial)
                {
                    problems.Add(
                        $"renderer '{renderer.name}' has the wrong shared " +
                        "material");
                }

                ShadowCastingMode expectedShadowMode = part.shadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                if (renderer.shadowCastingMode != expectedShadowMode ||
                    renderer.receiveShadows != part.shadows)
                {
                    problems.Add(
                        $"renderer '{renderer.name}' has the wrong shadow " +
                        "contract");
                }
            }
        }

        private static void ValidateAnchorBindings(
            SupermarketExteriorManifest manifest,
            SupermarketExteriorAssetRegistry registry,
            List<string> problems)
        {
            if (registry.Anchors.Count != manifest.anchors.Length)
            {
                problems.Add(
                    $"registry has {registry.Anchors.Count} anchors against " +
                    $"the manifest's {manifest.anchors.Length}");
            }

            var manifestByName = manifest.anchors.ToDictionary(
                anchor => anchor.name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int exteriorDoorCount = 0;
            foreach (SupermarketExteriorAnchorBinding binding in
                     registry.Anchors)
            {
                if (binding == null ||
                    !manifestByName.TryGetValue(
                        binding.AnchorName,
                        out SupermarketExteriorManifestAnchor anchor))
                {
                    problems.Add(
                        "registry contains an unknown anchor binding");
                    continue;
                }

                if (!seen.Add(binding.AnchorName))
                {
                    problems.Add(
                        $"registry repeats anchor '{binding.AnchorName}'");
                }

                if (!string.Equals(
                        binding.Role,
                        anchor.role,
                        StringComparison.Ordinal) ||
                    binding.Anchor == null ||
                    !string.Equals(
                        binding.Anchor.name,
                        $"ANCHOR_{binding.AnchorName}",
                        StringComparison.Ordinal))
                {
                    problems.Add(
                        $"anchor '{binding.AnchorName}' has the wrong " +
                        "role or transform binding");
                }

                if (string.Equals(
                        binding.Role,
                        "exterior_door",
                        StringComparison.Ordinal))
                {
                    exteriorDoorCount++;
                }
            }

            if (exteriorDoorCount != 1 ||
                !registry.TryGetAnchor("exterior_door", out Transform door) ||
                door == null)
            {
                problems.Add(
                    "registry does not resolve exactly one exterior_door " +
                    "anchor");
            }
        }

        private static void ValidateTextureImportContracts()
        {
            for (int index = 0; index < TexturePaths.Length; index++)
            {
                string path = TexturePaths[index];
                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                TextureWrapMode expectedWrap = IsAtlasTexturePath(path)
                    ? TextureWrapMode.Clamp
                    : TextureWrapMode.Repeat;
                if (importer == null ||
                    importer.textureType != TextureImporterType.Default ||
                    importer.textureShape != TextureImporterShape.Texture2D ||
                    !importer.sRGBTexture ||
                    importer.alphaSource != TextureImporterAlphaSource.None ||
                    !importer.mipmapEnabled ||
                    importer.streamingMipmaps ||
                    importer.isReadable ||
                    importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.wrapMode != expectedWrap ||
                    importer.filterMode != FilterMode.Bilinear ||
                    importer.anisoLevel != 4 ||
                    importer.textureCompression !=
                        TextureImporterCompression.Uncompressed ||
                    importer.maxTextureSize != 1024)
                {
                    throw new InvalidOperationException(
                        $"Supermarket exterior texture importer contract " +
                        $"drifted for '{path}'.");
                }
            }
        }

        private static void AppendDimensionProblems(
            SupermarketExteriorDimensions dimensions,
            List<string> problems)
        {
            AppendIfFar(
                dimensions.Width,
                ExpectedWidth,
                "width",
                problems);
            AppendIfFar(
                dimensions.Depth,
                ExpectedDepth,
                "depth",
                problems);
            AppendIfFar(
                dimensions.Height,
                ExpectedHeight,
                "height",
                problems);
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
                    $"{label} reads {actual:0.###} m against the " +
                    $"expected {expected:0.###} m");
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
                    $"the model carries {found.Length} {label}(s), first " +
                    $"on '{found[0].name}'");
            }
        }

        private static void AssertAnchorPosition(
            Transform prefabRoot,
            Transform anchorTransform,
            SupermarketExteriorManifestAnchor anchor,
            float wrapperYaw)
        {
            Vector3 expected = Quaternion.Euler(0f, wrapperYaw, 0f) *
                BlenderToUnity(anchor.local_position);
            Vector3 measured = prefabRoot.InverseTransformPoint(
                anchorTransform.position);
            if (Vector3.Distance(measured, expected) > MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Supermarket exterior anchor '{anchor.name}' imported " +
                    $"at {measured} against the manifest's {expected}.");
            }
        }

        private static void AssertMeasuresUpToManifest(
            Bounds measured,
            SupermarketExteriorManifest manifest,
            string assetPath)
        {
            Bounds expected = ExpectedImportedBounds(manifest);
            var problems = new List<string>();
            AppendBoundsProblems(measured, expected, problems);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' imported at the wrong bounds: " +
                    string.Join(", ", problems));
            }
        }

        private static Bounds ExpectedImportedBounds(
            SupermarketExteriorManifest manifest)
        {
            Vector3 minimum = BlenderToUnity(manifest.bounds_min);
            Vector3 maximum = BlenderToUnity(manifest.bounds_max);
            var source = new Bounds();
            source.SetMinMax(minimum, maximum);

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

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool started = false;
            var bounds = new Bounds();
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
                                bounds = new Bounds(local, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                bounds.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!started)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior has no renderer bounds.");
            }

            return bounds;
        }

        private static Transform ResolveAuthoringRoot(Transform wrapper)
        {
            Transform result = null;
            foreach (Transform child in wrapper)
            {
                if (!child.name.StartsWith(
                        "ROOT_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        "The supermarket exterior model has more than one " +
                        "ROOT_ node.");
                }

                result = child;
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "The supermarket exterior model has no ROOT_ node.");
            }

            return result;
        }

        private static Vector3 BlenderToUnity(float[] values)
        {
            return new Vector3(values[0], values[2], values[1]);
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
                string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
    internal sealed class SupermarketExteriorManifest
    {
        public string generator_version;
        public string design_id;
        public float[] bounds_min;
        public float[] bounds_max;
        public SupermarketExteriorManifestDimensions dimensions_m;
        public float runtime_wrapper_yaw_degrees;
        public bool colliders;
        public bool lights;
        public bool cameras;
        public int animation_count;
        public int triangle_count;
        public SupermarketExteriorManifestAnchor[] anchors;
        public SupermarketExteriorManifestPart[] parts;
        public string build_signature;
    }

    [Serializable]
    internal sealed class SupermarketExteriorManifestDimensions
    {
        public float width;
        public float depth;
        public float height;
    }

    [Serializable]
    internal sealed class SupermarketExteriorManifestAnchor
    {
        public string name;
        public string role;
        public float[] local_position;
    }

    [Serializable]
    internal sealed class SupermarketExteriorManifestPart
    {
        public string name;
        public string role;
        public string group;
        public string sheet;
        public bool emissive;
        public bool shadows;
        public int vertices;
        public int triangles;
    }
}
