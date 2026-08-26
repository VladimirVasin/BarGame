using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the first deterministic Mountain Road misc kit and binds all
    /// nineteen readable mesh sub-assets into one Resources provider. Runtime
    /// plans remain authoritative for placement, scale, collision and tint;
    /// the FBX is deliberately passive geometry only.
    /// </summary>
    [InitializeOnLoad]
    public static class MountainRoadMiscAssetSetup
    {
        public const string ModelPath =
            "Assets/MountainRoad/Models/MountainRoadMisc3D.fbx";
        public const string ManifestPath =
            "Assets/MountainRoad/Models/MountainRoadMisc3D.json";
        public const string ProviderPath =
            "Assets/Resources/" +
            MountainRoadMiscAssetProvider.ResourcePath + ".asset";

        private const int ExpectedAssemblyCount = 15;
        private const float BoundsTolerance = 0.003f;
        private const float UvTolerance = 0.0001f;
        private const float ContractTolerance = 0.0001f;

        private static readonly ExpectedPart[] ExpectedParts =
        {
            new ExpectedPart(
                "snowPoleBody",
                -1,
                "GEO_MRM_SnowPole_Body",
                MountainRoadMiscKind.SnowPole,
                0,
                "Body"),
            new ExpectedPart(
                "snowPoleBand",
                -1,
                "GEO_MRM_SnowPole_Band",
                MountainRoadMiscKind.SnowPole,
                0,
                "Band"),
            new ExpectedPart(
                "fallenLogVariants",
                0,
                "GEO_MRM_FallenLog_Variant01_Wood",
                MountainRoadMiscKind.FallenLog,
                0,
                "Wood"),
            new ExpectedPart(
                "fallenLogVariants",
                1,
                "GEO_MRM_FallenLog_Variant02_Wood",
                MountainRoadMiscKind.FallenLog,
                1,
                "Wood"),
            new ExpectedPart(
                "fallenLogVariants",
                2,
                "GEO_MRM_FallenLog_Variant03_Wood",
                MountainRoadMiscKind.FallenLog,
                2,
                "Wood"),
            new ExpectedPart(
                "stumpVariants",
                0,
                "GEO_MRM_Stump_Variant01_Wood",
                MountainRoadMiscKind.Stump,
                0,
                "Wood"),
            new ExpectedPart(
                "stumpVariants",
                1,
                "GEO_MRM_Stump_Variant02_Wood",
                MountainRoadMiscKind.Stump,
                1,
                "Wood"),
            new ExpectedPart(
                "stumpVariants",
                2,
                "GEO_MRM_Stump_Variant03_Wood",
                MountainRoadMiscKind.Stump,
                2,
                "Wood"),
            new ExpectedPart(
                "stumpVariants",
                3,
                "GEO_MRM_Stump_Variant04_Wood",
                MountainRoadMiscKind.Stump,
                3,
                "Wood"),
            new ExpectedPart(
                "deadTreeVariants",
                0,
                "GEO_MRM_DeadTree_Variant01_Wood",
                MountainRoadMiscKind.DeadTree,
                0,
                "Wood"),
            new ExpectedPart(
                "deadTreeVariants",
                1,
                "GEO_MRM_DeadTree_Variant02_Wood",
                MountainRoadMiscKind.DeadTree,
                1,
                "Wood"),
            new ExpectedPart(
                "deadTreeVariants",
                2,
                "GEO_MRM_DeadTree_Variant03_Wood",
                MountainRoadMiscKind.DeadTree,
                2,
                "Wood"),
            new ExpectedPart(
                "guardRailIron",
                -1,
                "GEO_MRM_GuardRail_Iron",
                MountainRoadMiscKind.GuardRail,
                0,
                "Iron"),
            new ExpectedPart(
                "convexMirrorPole",
                -1,
                "GEO_MRM_ConvexMirror_Pole",
                MountainRoadMiscKind.ConvexMirror,
                0,
                "Pole"),
            new ExpectedPart(
                "convexMirrorFrame",
                -1,
                "GEO_MRM_ConvexMirror_Frame",
                MountainRoadMiscKind.ConvexMirror,
                0,
                "Frame"),
            new ExpectedPart(
                "convexMirrorFace",
                -1,
                "GEO_MRM_ConvexMirror_Face",
                MountainRoadMiscKind.ConvexMirror,
                0,
                "Face"),
            new ExpectedPart(
                "utilityCabinetBody",
                -1,
                "GEO_MRM_UtilityCabinet_Body",
                MountainRoadMiscKind.UtilityCabinet,
                0,
                "Body"),
            new ExpectedPart(
                "utilityCabinetTrim",
                -1,
                "GEO_MRM_UtilityCabinet_Trim",
                MountainRoadMiscKind.UtilityCabinet,
                0,
                "Trim"),
            new ExpectedPart(
                "abandonedChairWood",
                -1,
                "GEO_MRM_AbandonedChair_Wood",
                MountainRoadMiscKind.AbandonedChair,
                0,
                "Wood")
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static MountainRoadMiscAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Mountain Road Misc/Bind Provider")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Mountain Road misc provider rebuilt at '{ProviderPath}'.");
        }

        /// <summary>Headless entrypoint used after the Blender export.</summary>
        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("MOUNTAIN ROAD MISC UNITY ASSET BUILD OK");
        }

        [MenuItem(
            "Bar Promenade/Mountain Road Misc/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Mountain Road misc model and provider contracts are valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
        }

        public static bool IsOwnedSourcePath(string path)
        {
            return string.Equals(
                       path,
                       ModelPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       path,
                       ManifestPath,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding || buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Mountain Road misc binding requires its generated FBX " +
                    "and JSON manifest. Run the deterministic Blender " +
                    "generator first.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(ProviderPath);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                MiscManifest manifest = LoadAndValidateManifest();
                Dictionary<string, Mesh> meshes = LoadExactMeshes();
                ValidateImportedModel(manifest, meshes);

                MountainRoadMiscAssetProvider provider =
                    LoadOrCreateProvider();
                BindProvider(provider, meshes, manifest.build_signature);
                AssetDatabase.SaveAssets();

                ValidateProvider(provider, manifest, meshes);
            }
            finally
            {
                isBuilding = false;
            }
        }

        /// <summary>
        /// Public validation seam for both the menu and a headless Unity
        /// invocation. It performs no writes.
        /// </summary>
        public static void ValidateOrThrow()
        {
            MiscManifest manifest = LoadAndValidateManifest();
            Dictionary<string, Mesh> meshes = LoadExactMeshes();
            ValidateImportedModel(manifest, meshes);
            MountainRoadMiscAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    MountainRoadMiscAssetProvider>(ProviderPath);
            ValidateProvider(provider, manifest, meshes);
        }

        private static void BindProvider(
            MountainRoadMiscAssetProvider provider,
            IReadOnlyDictionary<string, Mesh> meshes,
            string buildSignature)
        {
            var serialized = new SerializedObject(provider);
            foreach (IGrouping<string, ExpectedPart> arrayBinding in
                     ExpectedParts
                         .Where(part => part.ProviderArrayIndex >= 0)
                         .GroupBy(part => part.ProviderField))
            {
                SerializedProperty property = RequireProperty(
                    serialized,
                    arrayBinding.Key);
                if (!property.isArray)
                {
                    throw new InvalidOperationException(
                        $"MountainRoadMiscAssetProvider field " +
                        $"'{arrayBinding.Key}' is not an array.");
                }

                property.arraySize = arrayBinding.Max(
                    part => part.ProviderArrayIndex) + 1;
            }

            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                Mesh mesh = meshes[expected.MeshName];
                SerializedProperty property = RequireProperty(
                    serialized,
                    expected.ProviderField);
                if (expected.ProviderArrayIndex >= 0)
                {
                    property = property.GetArrayElementAtIndex(
                        expected.ProviderArrayIndex);
                }

                property.objectReferenceValue = mesh;
            }

            RequireProperty(serialized, "buildSignature").stringValue =
                buildSignature;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string field)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null)
            {
                return property;
            }

            throw new InvalidOperationException(
                $"MountainRoadMiscAssetProvider has no '{field}' field.");
        }

        private static MiscManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import Mountain Road misc manifest " +
                    $"'{ManifestPath}'.");
            }

            MiscManifest manifest =
                JsonUtility.FromJson<MiscManifest>(source.text);
            if (manifest == null ||
                manifest.source_axes == null ||
                manifest.unity_axes == null ||
                manifest.root_contract == null ||
                manifest.assemblies == null ||
                manifest.parts == null)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc manifest is missing or malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    MountainRoadMiscAssetProvider.DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Mountain Road misc design '{manifest.design_id}' does " +
                    $"not match provider design " +
                    $"'{MountainRoadMiscAssetProvider.DesignId}'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.blender_version) ||
                string.IsNullOrWhiteSpace(manifest.display_name) ||
                !IsSha256(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "Mountain Road misc generator metadata or build " +
                    "signature is invalid.");
            }

            if (manifest.colliders ||
                manifest.lights ||
                manifest.cameras ||
                manifest.animation_count != 0 ||
                manifest.mesh_count != ExpectedParts.Length ||
                manifest.assembly_count != ExpectedAssemblyCount ||
                manifest.parts.Length != ExpectedParts.Length ||
                manifest.assemblies.Length != ExpectedAssemblyCount ||
                manifest.triangle_count <= 0)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc manifest must describe exactly 19 " +
                    "passive meshes in 15 assemblies.");
            }

            ValidateAxisAndRootContract(manifest);
            ValidateManifestPartsAndAssemblies(manifest);
            return manifest;
        }

        private static void ValidateAxisAndRootContract(
            MiscManifest manifest)
        {
            if (!string.Equals(
                    manifest.source_axes.right,
                    "+X",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.source_axes.forward,
                    "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.source_axes.up,
                    "+Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_axes.right,
                    "+X",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_axes.forward,
                    "+Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_axes.up,
                    "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_axes.fbx_axis_forward,
                    "-Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_axes.fbx_axis_up,
                    "+Y",
                    StringComparison.Ordinal) ||
                !manifest.unity_axes.bake_space_transform)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc source-to-Unity axis contract " +
                    "changed.");
            }

            MiscRootContract root = manifest.root_contract;
            if (!string.Equals(
                    root.origin,
                    "descriptor_center",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.source_ground_axis,
                    "Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.unity_ground_axis,
                    "Y",
                    StringComparison.Ordinal) ||
                Mathf.Abs(root.source_ground_value + 0.5f) >
                    ContractTolerance ||
                Mathf.Abs(root.unity_ground_value + 0.5f) >
                    ContractTolerance)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc descriptor-center or ground " +
                    "contract changed.");
            }

            AssertArrayNear(
                root.normalized_descriptor_min,
                new Vector3(-0.5f, -0.5f, -0.5f),
                ContractTolerance,
                "normalized descriptor minimum");
            AssertArrayNear(
                root.normalized_descriptor_max,
                new Vector3(0.5f, 0.5f, 0.5f),
                ContractTolerance,
                "normalized descriptor maximum");
        }

        private static void ValidateManifestPartsAndAssemblies(
            MiscManifest manifest)
        {
            var partsByName = new Dictionary<string, MiscManifestPart>(
                StringComparer.Ordinal);
            int triangleTotal = 0;
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                MiscManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.mesh) ||
                    string.IsNullOrWhiteSpace(part.kind) ||
                    string.IsNullOrWhiteSpace(part.part_role) ||
                    string.IsNullOrWhiteSpace(part.surface_kind) ||
                    string.IsNullOrWhiteSpace(part.tint_role) ||
                    part.variant < 0 ||
                    part.vertices <= 0 ||
                    part.triangles <= 0 ||
                    !partsByName.TryAdd(part.mesh, part))
                {
                    throw new InvalidOperationException(
                        "Mountain Road misc manifest contains an invalid or " +
                        "duplicate mesh part.");
                }

                ValidateBoundsArrays(
                    part.bounds_min_source,
                    part.bounds_max_source,
                    part.mesh + " source bounds");
                ValidateBoundsArrays(
                    part.bounds_min_unity,
                    part.bounds_max_unity,
                    part.mesh + " Unity bounds");
                ValidateUvArrays(part);
                AssertSourceBoundsSwap(part);
                triangleTotal += part.triangles;
            }

            HashSet<string> expectedNames = ExpectedParts
                .Select(part => part.MeshName)
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedNames.SetEquals(partsByName.Keys))
            {
                throw new InvalidOperationException(
                    "Mountain Road misc manifest mesh-name set changed.");
            }

            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                MiscManifestPart actual = partsByName[expected.MeshName];
                if (!string.Equals(
                        actual.kind,
                        expected.Kind.ToString(),
                        StringComparison.Ordinal) ||
                    actual.variant != expected.Variant ||
                    !string.Equals(
                        actual.part_role,
                        expected.Role,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Manifest mesh '{expected.MeshName}' has the wrong " +
                        "kind, variant or part role.");
                }
            }

            if (triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc manifest triangle total is stale.");
            }

            var assembliesByKey =
                new Dictionary<string, MiscManifestAssembly>(
                    StringComparer.Ordinal);
            for (int index = 0; index < manifest.assemblies.Length; index++)
            {
                MiscManifestAssembly assembly = manifest.assemblies[index];
                string key = AssemblyKey(assembly?.kind, assembly?.variant ?? -1);
                if (assembly == null ||
                    string.IsNullOrWhiteSpace(assembly.kind) ||
                    assembly.variant < 0 ||
                    assembly.part_meshes == null ||
                    assembly.part_meshes.Length == 0 ||
                    !assembliesByKey.TryAdd(key, assembly))
                {
                    throw new InvalidOperationException(
                        "Mountain Road misc manifest contains an invalid or " +
                        "duplicate assembly.");
                }

                string expectedScale = string.Equals(
                    assembly.kind,
                    MountainRoadMiscKind.DeadTree.ToString(),
                    StringComparison.Ordinal)
                    ? "uniform_by_height"
                    : "normalized_to_descriptor";
                if (!string.Equals(
                        assembly.scale_mode,
                        expectedScale,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Assembly '{key}' has an invalid scale mode.");
                }

                ValidateBoundsArrays(
                    assembly.bounds_min_source,
                    assembly.bounds_max_source,
                    key + " source bounds");
                ValidateBoundsArrays(
                    assembly.bounds_min_unity,
                    assembly.bounds_max_unity,
                    key + " Unity bounds");
                AssertSourceBoundsSwap(assembly, key);
                AssertNormalizedAssemblyBounds(assembly, key);
            }

            foreach (IGrouping<string, ExpectedPart> expectedAssembly in
                     ExpectedParts.GroupBy(part =>
                         AssemblyKey(part.Kind.ToString(), part.Variant)))
            {
                if (!assembliesByKey.TryGetValue(
                        expectedAssembly.Key,
                        out MiscManifestAssembly actual))
                {
                    throw new InvalidOperationException(
                        $"Mountain Road misc manifest is missing assembly " +
                        $"'{expectedAssembly.Key}'.");
                }

                string[] expectedMeshes = expectedAssembly
                    .Select(part => part.MeshName)
                    .ToArray();
                if (!actual.part_meshes.SequenceEqual(
                        expectedMeshes,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Assembly '{expectedAssembly.Key}' has a stale " +
                        "ordered part list.");
                }

                Bounds union = BoundsFromManifestParts(
                    expectedMeshes.Select(name => partsByName[name]));
                AssertBoundsNear(
                    union,
                    actual.bounds_min_unity,
                    actual.bounds_max_unity,
                    ContractTolerance,
                    expectedAssembly.Key + " manifest part union");
            }
        }

        private static Dictionary<string, Mesh> LoadExactMeshes()
        {
            var meshes = new Dictionary<string, Mesh>(
                StringComparer.Ordinal);
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (!(assets[index] is Mesh mesh))
                {
                    continue;
                }

                if (!meshes.TryAdd(mesh.name, mesh))
                {
                    throw new InvalidOperationException(
                        $"Mountain Road misc FBX contains two meshes named " +
                        $"'{mesh.name}'.");
                }
            }

            HashSet<string> expectedNames = ExpectedParts
                .Select(part => part.MeshName)
                .ToHashSet(StringComparer.Ordinal);
            if (meshes.Count != MountainRoadMiscAssetProvider
                    .ExpectedMeshCount ||
                !expectedNames.SetEquals(meshes.Keys))
            {
                throw new InvalidOperationException(
                    "Mountain Road misc FBX does not contain the exact 19 " +
                    "authored mesh sub-assets.");
            }

            return meshes;
        }

        private static void ValidateImportedModel(
            MiscManifest manifest,
            IReadOnlyDictionary<string, Mesh> meshes)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                !Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy ||
                importer.optimizeGameObjects ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation ||
                importer.importCameras ||
                importer.importLights ||
                importer.importBlendShapes ||
                importer.addCollider ||
                importer.importNormals != ModelImporterNormals.Import ||
                importer.importTangents != ModelImporterTangents.None ||
                importer.meshCompression != ModelImporterMeshCompression.Off ||
                !importer.weldVertices ||
                importer.keepQuads ||
                importer.generateSecondaryUV ||
                !importer.isReadable ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc FBX import settings are not the " +
                    "readable passive-geometry contract.");
            }

            UnityEngine.Object[] importedAssets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            if (importedAssets.OfType<Material>().Any() ||
                importedAssets.OfType<AnimationClip>().Any())
            {
                throw new InvalidOperationException(
                    "Mountain Road misc FBX unexpectedly imported materials " +
                    "or animation clips.");
            }

            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null ||
                model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0 ||
                model.GetComponentsInChildren<Camera>(true).Length != 0 ||
                model.GetComponentsInChildren<Animator>(true).Length != 0 ||
                model.GetComponentsInChildren<Animation>(true).Length != 0 ||
                model.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc model is not passive render-only " +
                    "geometry.");
            }

            var manifestParts = manifest.parts.ToDictionary(
                part => part.mesh,
                StringComparer.Ordinal);
            int importedTriangles = 0;
            foreach (KeyValuePair<string, Mesh> pair in meshes)
            {
                string name = pair.Key;
                Mesh mesh = pair.Value;
                MiscManifestPart source = manifestParts[name];
                if (!mesh.isReadable || mesh.vertexCount <= 0)
                {
                    throw new InvalidOperationException(
                        $"Imported misc mesh '{name}' must be readable and " +
                        "non-empty for runtime combining.");
                }

                Vector2[] uv = mesh.uv;
                if (uv == null || uv.Length != mesh.vertexCount)
                {
                    throw new InvalidOperationException(
                        $"Imported misc mesh '{name}' has missing UV0.");
                }

                int triangles = CountTriangles(mesh, name);
                if (triangles != source.triangles)
                {
                    throw new InvalidOperationException(
                        $"Imported misc mesh '{name}' has {triangles} " +
                        $"triangles, not manifest {source.triangles}.");
                }

                AssertBoundsNear(
                    mesh.bounds,
                    source.bounds_min_unity,
                    source.bounds_max_unity,
                    BoundsTolerance,
                    name + " imported bounds");
                AssertUvBoundsNear(uv, source, name);
                importedTriangles += triangles;
            }

            if (importedTriangles != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Mountain Road misc imported triangle total differs " +
                    "from the manifest.");
            }

            foreach (MiscManifestAssembly assembly in manifest.assemblies)
            {
                Bounds union = BoundsFromMeshes(
                    assembly.part_meshes.Select(name => meshes[name]));
                AssertBoundsNear(
                    union,
                    assembly.bounds_min_unity,
                    assembly.bounds_max_unity,
                    BoundsTolerance,
                    AssemblyKey(assembly.kind, assembly.variant) +
                    " imported union");
            }
        }

        private static void ValidateProvider(
            MountainRoadMiscAssetProvider provider,
            MiscManifest manifest,
            IReadOnlyDictionary<string, Mesh> meshes)
        {
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Mountain Road misc provider is missing at " +
                    $"'{ProviderPath}'.");
            }

            provider.ValidateOrThrow();
            if (!provider.HasCompleteMeshes ||
                !string.Equals(
                    provider.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Mountain Road misc provider is incomplete or was bound " +
                    "against another art build.");
            }

            var serialized = new SerializedObject(provider);
            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                SerializedProperty property = RequireProperty(
                    serialized,
                    expected.ProviderField);
                if (expected.ProviderArrayIndex >= 0)
                {
                    if (!property.isArray ||
                        property.arraySize <= expected.ProviderArrayIndex)
                    {
                        throw new InvalidOperationException(
                            $"Provider array '{expected.ProviderField}' is " +
                            "shorter than its authored variant set.");
                    }

                    property = property.GetArrayElementAtIndex(
                        expected.ProviderArrayIndex);
                }

                if (property.objectReferenceValue !=
                    meshes[expected.MeshName])
                {
                    throw new InvalidOperationException(
                        $"Provider binding '{expected.ProviderField}' does " +
                        $"not point to '{expected.MeshName}'.");
                }

                string apiName =
                    MountainRoadMiscAssetProvider.GetExpectedMeshName(
                        expected.Kind,
                        expected.Variant,
                        PartIndex(expected));
                if (!string.Equals(
                        apiName,
                        expected.MeshName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Provider mesh-name API disagrees on " +
                        $"'{expected.MeshName}'.");
                }
            }
        }

        private static int PartIndex(ExpectedPart expected)
        {
            return ExpectedParts
                .Where(part =>
                    part.Kind == expected.Kind &&
                    part.Variant == expected.Variant)
                .TakeWhile(part => !ReferenceEquals(part, expected))
                .Count();
        }

        private static MountainRoadMiscAssetProvider LoadOrCreateProvider()
        {
            MountainRoadMiscAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    MountainRoadMiscAssetProvider>(ProviderPath);
            if (provider != null)
            {
                return provider;
            }

            provider = ScriptableObject.CreateInstance<
                MountainRoadMiscAssetProvider>();
            AssetDatabase.CreateAsset(provider, ProviderPath);
            return provider;
        }

        private static void ValidateDependencyStamp()
        {
            if (isBuilding ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                !SourcesExist())
            {
                return;
            }

            try
            {
                MiscManifest manifest = LoadAndValidateManifest();
                MountainRoadMiscAssetProvider provider =
                    AssetDatabase.LoadAssetAtPath<
                        MountainRoadMiscAssetProvider>(ProviderPath);
                if (provider == null ||
                    !provider.HasCompleteMeshes ||
                    !string.Equals(
                        provider.BuildSignature,
                        manifest.build_signature,
                        StringComparison.Ordinal))
                {
                    QueueBuildWhenSourcesExist();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect Mountain Road misc assets: " +
                    exception);
            }
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not bind Mountain Road misc assets: " +
                    exception);
            }
        }

        private static int CountTriangles(Mesh mesh, string name)
        {
            long indices = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles ||
                    mesh.GetIndexCount(subMesh) % 3 != 0)
                {
                    throw new InvalidOperationException(
                        $"Imported misc mesh '{name}' is not triangulated.");
                }

                indices += (long)mesh.GetIndexCount(subMesh);
            }

            if (indices / 3 > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Imported misc mesh '{name}' exceeds triangle limits.");
            }

            return (int)(indices / 3);
        }

        private static void ValidateBoundsArrays(
            float[] minimum,
            float[] maximum,
            string label)
        {
            if (!IsFiniteArray(minimum, 3) ||
                !IsFiniteArray(maximum, 3))
            {
                throw new InvalidOperationException(
                    $"{label} is missing or non-finite.");
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (minimum[axis] > maximum[axis])
                {
                    throw new InvalidOperationException(
                        $"{label} is inverted on axis {axis}.");
                }
            }
        }

        private static void ValidateUvArrays(MiscManifestPart part)
        {
            if (!IsFiniteArray(part.uv_min, 2) ||
                !IsFiniteArray(part.uv_max, 2) ||
                part.uv_min[0] > part.uv_max[0] ||
                part.uv_min[1] > part.uv_max[1] ||
                Mathf.Abs(part.uv_max[0] - part.uv_min[0]) <=
                    ContractTolerance ||
                Mathf.Abs(part.uv_max[1] - part.uv_min[1]) <=
                    ContractTolerance)
            {
                throw new InvalidOperationException(
                    $"Manifest mesh '{part.mesh}' has invalid UV0 bounds.");
            }
        }

        private static void AssertSourceBoundsSwap(MiscManifestPart part)
        {
            AssertArrayNear(
                part.bounds_min_unity,
                new Vector3(
                    part.bounds_min_source[0],
                    part.bounds_min_source[2],
                    part.bounds_min_source[1]),
                ContractTolerance,
                part.mesh + " source-to-Unity minimum");
            AssertArrayNear(
                part.bounds_max_unity,
                new Vector3(
                    part.bounds_max_source[0],
                    part.bounds_max_source[2],
                    part.bounds_max_source[1]),
                ContractTolerance,
                part.mesh + " source-to-Unity maximum");
        }

        private static void AssertSourceBoundsSwap(
            MiscManifestAssembly assembly,
            string label)
        {
            AssertArrayNear(
                assembly.bounds_min_unity,
                new Vector3(
                    assembly.bounds_min_source[0],
                    assembly.bounds_min_source[2],
                    assembly.bounds_min_source[1]),
                ContractTolerance,
                label + " source-to-Unity minimum");
            AssertArrayNear(
                assembly.bounds_max_unity,
                new Vector3(
                    assembly.bounds_max_source[0],
                    assembly.bounds_max_source[2],
                    assembly.bounds_max_source[1]),
                ContractTolerance,
                label + " source-to-Unity maximum");
        }

        private static void AssertNormalizedAssemblyBounds(
            MiscManifestAssembly assembly,
            string label)
        {
            Vector3 minimum = Vector3From(assembly.bounds_min_unity);
            Vector3 maximum = Vector3From(assembly.bounds_max_unity);
            if (minimum.x < -0.5f - ContractTolerance ||
                minimum.y < -0.5f - ContractTolerance ||
                minimum.z < -0.5f - ContractTolerance ||
                maximum.x > 0.5f + ContractTolerance ||
                maximum.y > 0.5f + ContractTolerance ||
                maximum.z > 0.5f + ContractTolerance ||
                Mathf.Abs(minimum.y + 0.5f) > ContractTolerance)
            {
                throw new InvalidOperationException(
                    $"Assembly '{label}' leaves its normalized descriptor " +
                    "envelope or does not stand on local Y=-0.5.");
            }
        }

        private static Bounds BoundsFromManifestParts(
            IEnumerable<MiscManifestPart> parts)
        {
            using (IEnumerator<MiscManifestPart> enumerator =
                   parts.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException(
                        "Cannot union an empty manifest assembly.");
                }

                Bounds result = BoundsFrom(
                    enumerator.Current.bounds_min_unity,
                    enumerator.Current.bounds_max_unity);
                while (enumerator.MoveNext())
                {
                    result.Encapsulate(Vector3From(
                        enumerator.Current.bounds_min_unity));
                    result.Encapsulate(Vector3From(
                        enumerator.Current.bounds_max_unity));
                }

                return result;
            }
        }

        private static Bounds BoundsFromMeshes(IEnumerable<Mesh> meshes)
        {
            using (IEnumerator<Mesh> enumerator = meshes.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException(
                        "Cannot union an empty imported assembly.");
                }

                Bounds result = enumerator.Current.bounds;
                while (enumerator.MoveNext())
                {
                    result.Encapsulate(enumerator.Current.bounds.min);
                    result.Encapsulate(enumerator.Current.bounds.max);
                }

                return result;
            }
        }

        private static void AssertUvBoundsNear(
            IReadOnlyList<Vector2> uv,
            MiscManifestPart source,
            string name)
        {
            Vector2 minimum = uv[0];
            Vector2 maximum = uv[0];
            for (int index = 0; index < uv.Count; index++)
            {
                Vector2 value = uv[index];
                if (!IsFinite(value.x) || !IsFinite(value.y))
                {
                    throw new InvalidOperationException(
                        $"Imported misc mesh '{name}' has non-finite UV0.");
                }

                minimum = Vector2.Min(minimum, value);
                maximum = Vector2.Max(maximum, value);
            }

            if (Mathf.Abs(minimum.x - source.uv_min[0]) > UvTolerance ||
                Mathf.Abs(minimum.y - source.uv_min[1]) > UvTolerance ||
                Mathf.Abs(maximum.x - source.uv_max[0]) > UvTolerance ||
                Mathf.Abs(maximum.y - source.uv_max[1]) > UvTolerance)
            {
                throw new InvalidOperationException(
                    $"Imported misc mesh '{name}' UV0 bounds differ from " +
                    "the manifest.");
            }
        }

        private static void AssertBoundsNear(
            Bounds actual,
            float[] expectedMinimum,
            float[] expectedMaximum,
            float tolerance,
            string label)
        {
            AssertVectorNear(
                actual.min,
                Vector3From(expectedMinimum),
                tolerance,
                label + " minimum");
            AssertVectorNear(
                actual.max,
                Vector3From(expectedMaximum),
                tolerance,
                label + " maximum");
        }

        private static void AssertArrayNear(
            float[] actual,
            Vector3 expected,
            float tolerance,
            string label)
        {
            if (!IsFiniteArray(actual, 3))
            {
                throw new InvalidOperationException(
                    $"{label} is missing or non-finite.");
            }

            AssertVectorNear(
                Vector3From(actual),
                expected,
                tolerance,
                label);
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected,
            float tolerance,
            string label)
        {
            if (Mathf.Abs(actual.x - expected.x) > tolerance ||
                Mathf.Abs(actual.y - expected.y) > tolerance ||
                Mathf.Abs(actual.z - expected.z) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label} is {actual}, expected {expected} within " +
                    $"{tolerance:0.####}.");
            }
        }

        private static Bounds BoundsFrom(float[] minimum, float[] maximum)
        {
            Vector3 min = Vector3From(minimum);
            Vector3 max = Vector3From(maximum);
            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        private static Vector3 Vector3From(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static string AssemblyKey(string kind, int variant)
        {
            return $"{kind}#{variant}";
        }

        private static bool IsFiniteArray(float[] values, int length)
        {
            if (values == null || values.Length != length)
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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.Length == 64 &&
                   value.All(Uri.IsHexDigit);
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string[] segments = directory.Split('/');
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

        private sealed class ExpectedPart
        {
            public ExpectedPart(
                string providerField,
                int providerArrayIndex,
                string meshName,
                MountainRoadMiscKind kind,
                int variant,
                string role)
            {
                ProviderField = providerField;
                ProviderArrayIndex = providerArrayIndex;
                MeshName = meshName;
                Kind = kind;
                Variant = variant;
                Role = role;
            }

            public string ProviderField { get; }
            public int ProviderArrayIndex { get; }
            public string MeshName { get; }
            public MountainRoadMiscKind Kind { get; }
            public int Variant { get; }
            public string Role { get; }
        }

        [Serializable]
        private sealed class MiscManifest
        {
            public string generator;
            public string generator_version;
            public string blender_version;
            public string design_id;
            public string display_name;
            public MiscSourceAxes source_axes;
            public MiscUnityAxes unity_axes;
            public MiscRootContract root_contract;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int mesh_count;
            public int assembly_count;
            public int triangle_count;
            public MiscManifestAssembly[] assemblies;
            public MiscManifestPart[] parts;
            public string build_signature;
        }

        [Serializable]
        private sealed class MiscSourceAxes
        {
            public string right;
            public string forward;
            public string up;
        }

        [Serializable]
        private sealed class MiscUnityAxes
        {
            public string right;
            public string forward;
            public string up;
            public string fbx_axis_forward;
            public string fbx_axis_up;
            public bool bake_space_transform;
        }

        [Serializable]
        private sealed class MiscRootContract
        {
            public string origin;
            public string source_ground_axis;
            public float source_ground_value;
            public string unity_ground_axis;
            public float unity_ground_value;
            public float[] normalized_descriptor_min;
            public float[] normalized_descriptor_max;
        }

        [Serializable]
        private sealed class MiscManifestAssembly
        {
            public string kind;
            public int variant;
            public string scale_mode;
            public string[] part_meshes;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
        }

        [Serializable]
        private sealed class MiscManifestPart
        {
            public string mesh;
            public string kind;
            public int variant;
            public string part_role;
            public string surface_kind;
            public string tint_role;
            public int vertices;
            public int triangles;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public float[] uv_min;
            public float[] uv_max;
        }
    }

    /// <summary>
    /// The runtime combines these meshes, so readability is intentional.
    /// Everything else is a passive, material-free static model import.
    /// </summary>
    public sealed class MountainRoadMiscModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    MountainRoadMiscAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = true;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (MountainRoadMiscAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (!MountainRoadMiscAssetSetup.IsOwnedSourcePath(
                        importedAssets[index]))
                {
                    continue;
                }

                MountainRoadMiscAssetSetup.QueueBuildWhenSourcesExist();
                return;
            }
        }
    }
}
