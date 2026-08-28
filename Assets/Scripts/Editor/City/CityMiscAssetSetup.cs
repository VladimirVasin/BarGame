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
    /// Imports the deterministic City misc kit and binds its passive readable
    /// mesh sub-assets into one Resources provider. Placement, collision,
    /// materials, light and interaction remain owned by City runtime plans.
    /// </summary>
    [InitializeOnLoad]
    public static class CityMiscAssetSetup
    {
        public const string ModelPath =
            "Assets/City/Models/CityMisc3D.fbx";
        public const string ManifestPath =
            "Assets/City/Models/CityMisc3D.json";
        public const string ProviderPath =
            "Assets/Resources/" +
            CityMiscAssetProvider.ResourcePath + ".asset";

        private const float BoundsTolerance = 0.003f;
        private const float UvTolerance = 0.0001f;
        private const float ContractTolerance = 0.0001f;
        private const string Wave1CompatibilitySignature =
            "dd2e814d906fd2c7a7855c6d75ee54fe912ebb90f7cd02633c95c558d752f9f6";
        private const string V2CompatibilitySignature =
            "8ec3ffe04ffbcfba94cbf708d9c8263afbe853aeea4ffdeabfe638857a043193";

        private static readonly ExpectedPart[] ExpectedParts =
            CreateExpectedParts();

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityMiscAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Misc/Bind Provider")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log($"City misc provider rebuilt at '{ProviderPath}'.");
        }

        /// <summary>Headless entrypoint used after Blender export.</summary>
        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("CITY MISC UNITY ASSET BUILD OK");
        }

        [MenuItem("Bar Promenade/City Misc/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("City misc model and provider contracts are valid.");
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
                    "City misc binding requires its generated FBX and JSON " +
                    "manifest. Run the deterministic Blender generator " +
                    "first.");
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

                CityMiscAssetProvider provider = LoadOrCreateProvider();
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
        /// Read-only validation seam for the menu and focused test runner.
        /// </summary>
        public static void ValidateOrThrow()
        {
            MiscManifest manifest = LoadAndValidateManifest();
            Dictionary<string, Mesh> meshes = LoadExactMeshes();
            ValidateImportedModel(manifest, meshes);
            CityMiscAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityMiscAssetProvider>(
                    ProviderPath);
            ValidateProvider(provider, manifest, meshes);
        }

        private static ExpectedPart[] CreateExpectedParts()
        {
            var result = new List<ExpectedPart>(
                CityMiscAssetProvider.ExpectedMeshCount);
            for (int kindIndex = 0;
                 kindIndex < CityMiscAssetProvider.SupportedKindCount;
                 kindIndex++)
            {
                CityMiscKind kind =
                    CityMiscAssetProvider.GetSupportedKind(kindIndex);
                for (int variant = 0;
                     variant < CityMiscAssetProvider.GetVariantCount(kind);
                     variant++)
                {
                    for (int partIndex = 0;
                         partIndex < CityMiscAssetProvider.GetPartCount(kind);
                         partIndex++)
                    {
                        CityMiscMeshRole role =
                            CityMiscAssetProvider.GetExpectedRole(
                                kind,
                                variant,
                                partIndex);
                        string component =
                            CityMiscAssetProvider.GetExpectedComponent(
                                kind,
                                variant,
                                partIndex);
                        CityMiscSurfaceKind surface =
                            CityMiscAssetProvider.GetExpectedSurface(
                                kind,
                                variant,
                                partIndex);
                        result.Add(new ExpectedPart(
                            CityMiscAssetProvider.GetExpectedMeshName(
                                kind,
                                variant,
                                partIndex),
                            kind,
                            variant,
                            component,
                            role,
                            surface,
                            ExpectedSurfaceKind(role, surface)));
                    }
                }
            }

            if (result.Count != CityMiscAssetProvider.ExpectedMeshCount)
            {
                throw new InvalidOperationException(
                    "City misc provider catalog count is stale.");
            }

            return result.ToArray();
        }

        private static string ExpectedSurfaceKind(
            CityMiscMeshRole role,
            CityMiscSurfaceKind surface)
        {
            if (surface != CityMiscSurfaceKind.Default)
            {
                return surface.ToString();
            }

            switch (role)
            {
                case CityMiscMeshRole.Industrial:
                    return "IndustrialMetal";
                case CityMiscMeshRole.Street:
                    return "StreetMetal";
                case CityMiscMeshRole.Masonry:
                    return "Masonry";
                case CityMiscMeshRole.Neon:
                    return "Neon";
                case CityMiscMeshRole.Bark:
                    return "Bark";
                case CityMiscMeshRole.Foliage:
                    return "Foliage";
                case CityMiscMeshRole.Timber:
                    return "Timber";
                case CityMiscMeshRole.Residential:
                    return "ResidentialGlass";
                case CityMiscMeshRole.BacklitSign:
                    return "BacklitSign";
                case CityMiscMeshRole.Fixture:
                    return "FixtureMetal";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        "Unsupported City misc mesh role.");
            }
        }

        private static void BindProvider(
            CityMiscAssetProvider provider,
            IReadOnlyDictionary<string, Mesh> meshes,
            string buildSignature)
        {
            var serialized = new SerializedObject(provider);
            SerializedProperty entries = RequireProperty(serialized, "entries");
            if (!entries.isArray)
            {
                throw new InvalidOperationException(
                    "CityMiscAssetProvider.entries is not an array.");
            }

            entries.arraySize = ExpectedParts.Length;
            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                RequireRelative(entry, "kind").intValue =
                    (int)expected.Kind;
                RequireRelative(entry, "variant").intValue =
                    expected.Variant;
                RequireRelative(entry, "component").stringValue =
                    expected.Component;
                RequireRelative(entry, "role").intValue =
                    (int)expected.Role;
                RequireRelative(entry, "surface").intValue =
                    (int)expected.Surface;
                RequireRelative(entry, "mesh").objectReferenceValue =
                    meshes[expected.MeshName];
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
                $"CityMiscAssetProvider has no '{field}' field.");
        }

        private static SerializedProperty RequireRelative(
            SerializedProperty parent,
            string field)
        {
            SerializedProperty property =
                parent.FindPropertyRelative(field);
            if (property != null)
            {
                return property;
            }

            throw new InvalidOperationException(
                $"CityMiscMeshEntry has no '{field}' field.");
        }

        private static MiscManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import City misc manifest " +
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
                    "City misc manifest is missing or malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    CityMiscAssetProvider.DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"City misc design '{manifest.design_id}' does not " +
                    $"match provider design " +
                    $"'{CityMiscAssetProvider.DesignId}'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator) ||
                !string.Equals(
                    manifest.generator_version,
                    CityMiscAssetProvider.GeneratorVersion,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.blender_version) ||
                string.IsNullOrWhiteSpace(manifest.display_name) ||
                !IsSha256(manifest.build_signature) ||
                !string.Equals(manifest.wave1_compatibility_signature,
                    Wave1CompatibilitySignature, StringComparison.Ordinal) ||
                !string.Equals(manifest.v2_compatibility_signature,
                    V2CompatibilitySignature, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City misc generator metadata or build signature is " +
                    "invalid.");
            }

            if (manifest.colliders ||
                manifest.lights ||
                manifest.cameras ||
                manifest.animation_count != 0 ||
                manifest.mesh_count != ExpectedParts.Length ||
                manifest.assembly_count !=
                    CityMiscAssetProvider.ExpectedAssemblyCount ||
                manifest.parts.Length != ExpectedParts.Length ||
                manifest.assemblies.Length !=
                    CityMiscAssetProvider.ExpectedAssemblyCount ||
                manifest.triangle_count <= 0)
            {
                throw new InvalidOperationException(
                    $"City misc manifest must describe exactly " +
                    $"{CityMiscAssetProvider.ExpectedMeshCount} passive " +
                    $"meshes in " +
                    $"{CityMiscAssetProvider.ExpectedAssemblyCount} " +
                    "fixed-metre assemblies.");
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
                    "City misc source-to-Unity axis contract changed.");
            }

            MiscRootContract root = manifest.root_contract;
            if (!string.Equals(
                    root.origin,
                    "per_assembly_root_derivation",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.scale_mode,
                    "fixed_meters",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.source_ground_axis,
                    "Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.unity_ground_axis,
                    "Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.source_forward_axis,
                    "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.unity_forward_axis,
                    "+Z",
                    StringComparison.Ordinal) ||
                Mathf.Abs(root.source_ground_value) > ContractTolerance ||
                Mathf.Abs(root.unity_ground_value) > ContractTolerance ||
                Mathf.Abs(
                    root.legacy_recipe_x_to_unity_local_x + 1f) >
                    ContractTolerance)
            {
                throw new InvalidOperationException(
                    "City misc fixed-metre ground/forward placement " +
                    "contract changed.");
            }
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
                        "City misc manifest contains an invalid or duplicate " +
                        "mesh part.");
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
                    "City misc manifest mesh-name set changed. Missing: " +
                    string.Join(", ", expectedNames.Except(partsByName.Keys)) +
                    "; unexpected: " +
                    string.Join(", ", partsByName.Keys.Except(expectedNames)));
            }

            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                MiscManifestPart actual = partsByName[expected.MeshName];
                string role = expected.ManifestRole;
                if (!string.Equals(
                        actual.kind,
                        expected.Kind.ToString(),
                        StringComparison.Ordinal) ||
                    actual.variant != expected.Variant ||
                    !string.Equals(
                        actual.part_role,
                        role,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.tint_role,
                        role,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.surface_kind,
                        expected.SurfaceKind,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Manifest mesh '{expected.MeshName}' has the wrong " +
                        "kind, variant, role or surface contract.");
                }
            }

            if (triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "City misc manifest triangle total is stale.");
            }

            var assembliesByKey =
                new Dictionary<string, MiscManifestAssembly>(
                    StringComparer.Ordinal);
            for (int index = 0;
                 index < manifest.assemblies.Length;
                 index++)
            {
                MiscManifestAssembly assembly = manifest.assemblies[index];
                string key = AssemblyKey(
                    assembly?.kind,
                    assembly?.variant ?? -1);
                if (assembly == null ||
                    string.IsNullOrWhiteSpace(assembly.kind) ||
                    assembly.variant < 0 ||
                    assembly.part_meshes == null ||
                    assembly.scale_parameters == null ||
                    assembly.unity_owned_parts == null ||
                    assembly.part_meshes.Length == 0 ||
                    !assembliesByKey.TryAdd(key, assembly))
                {
                    throw new InvalidOperationException(
                        "City misc manifest contains an invalid or duplicate " +
                        "assembly.");
                }

                if (!Enum.TryParse(
                        assembly.kind,
                        out CityMiscKind parsedKind) ||
                    !CityMiscAssetProvider.Supports(parsedKind) ||
                    assembly.variant >=
                    CityMiscAssetProvider.GetVariantCount(parsedKind))
                {
                    throw new InvalidOperationException(
                        $"Assembly '{key}' has an unsupported kind or " +
                        "variant.");
                }

                bool legacyRecipe =
                    (int)parsedKind <=
                    (int)CityMiscKind.ParkPlayground;
                string expectedPlacement =
                    parsedKind == CityMiscKind.YardSpotlightWallMount ||
                    parsedKind == CityMiscKind.YardSpotlightHeadShell
                        ? "anchor_forward_frame"
                        : "ground_forward_frame";
                string expectedCoordinateProfile = legacyRecipe
                    ? "legacy_recipe_reflected_x"
                    : "root_local_direct";

                if (!string.Equals(
                        assembly.scale_mode,
                        "fixed_meters",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        assembly.placement_contract,
                        expectedPlacement,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        assembly.coordinate_profile,
                        expectedCoordinateProfile,
                        StringComparison.Ordinal) ||
                    (legacyRecipe
                        ? !string.IsNullOrEmpty(assembly.root_derivation)
                        : string.IsNullOrWhiteSpace(
                            assembly.root_derivation)))
                {
                    throw new InvalidOperationException(
                        $"Assembly '{key}' has an invalid placement " +
                        "contract.");
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
                float expectedMinimumY =
                    CityMiscAssetProvider.GetExpectedAssemblyMinimumY(
                        parsedKind,
                        assembly.variant);
                if (Mathf.Abs(
                        assembly.bounds_min_unity[1] -
                        expectedMinimumY) > ContractTolerance)
                {
                    throw new InvalidOperationException(
                        $"Assembly '{key}' violates its local-Y origin contract.");
                }

                ValidateScaleParameters(assembly, key);
            }

            foreach (IGrouping<string, ExpectedPart> expectedAssembly in
                     ExpectedParts.GroupBy(part =>
                         AssemblyKey(
                             part.Kind.ToString(),
                             part.Variant)))
            {
                if (!assembliesByKey.TryGetValue(
                        expectedAssembly.Key,
                        out MiscManifestAssembly actual))
                {
                    throw new InvalidOperationException(
                        $"City misc manifest is missing assembly " +
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

        private static void ValidateScaleParameters(
            MiscManifestAssembly assembly,
            string key)
        {
            ExpectedScaleParameter[] expected =
                ExpectedScaleParameters(assembly.kind);
            if (assembly.scale_parameters.Length != expected.Length)
            {
                throw new InvalidOperationException(
                    $"Assembly '{key}' has stale scale parameters.");
            }

            for (int index = 0; index < expected.Length; index++)
            {
                MiscScaleParameter actual = assembly.scale_parameters[index];
                ExpectedScaleParameter wanted = expected[index];
                if (actual == null || actual.source_axes == null ||
                    !string.Equals(actual.name, wanted.Name,
                        StringComparison.Ordinal) ||
                    !actual.source_axes.SequenceEqual(wanted.SourceAxes,
                        StringComparer.Ordinal) ||
                    Mathf.Abs(actual.reference - wanted.Reference) >
                        ContractTolerance ||
                    Mathf.Abs(actual.min - wanted.Minimum) >
                        ContractTolerance ||
                    Mathf.Abs(actual.max - wanted.Maximum) >
                        ContractTolerance)
                {
                    throw new InvalidOperationException(
                        $"Assembly '{key}' scale parameter {index} changed.");
                }
            }
        }

        private static ExpectedScaleParameter[] ExpectedScaleParameters(
            string kind)
        {
            if (!Enum.TryParse(kind, out CityMiscKind parsed))
            {
                return Array.Empty<ExpectedScaleParameter>();
            }

            switch (parsed)
            {
                case CityMiscKind.OldTownChimneysAndDormers:
                    return new[] { S("chimney_spread", 2.4f, 1.1f, 2.4f, "X"), S("dormer_offset", 1.8f, 0f, 1.8f, "X") };
                case CityMiscKind.OldTownScaffolding:
                    return new[] { S("resolved_width", 7.2f, 4.2f, 7.2f, "X"), S("resolved_height", 7.2f, 4.8f, 7.2f, "Z") };
                case CityMiscKind.OldTownStreetMarket:
                    return new[] { S("resolved_width", 5.2f, 3.4f, 5.2f, "X") };
                case CityMiscKind.OldTownClockTower:
                    return new[] { S("resolved_width", 4f, 2.8f, 4f, "X", "Y") };
                case CityMiscKind.ResidentialBalconies:
                    return new[] { S("resolved_width", 4.8f, 2.8f, 4.8f, "X") };
                case CityMiscKind.ResidentialRooftopGreenhouse:
                    return new[] { S("resolved_width", 5.4f, 3.4f, 5.4f, "X"), S("resolved_depth", 4f, 2.6f, 4f, "Y") };
                case CityMiscKind.IndustrialPipeRack:
                    return new[] { S("resolved_width", 7f, 4.5f, 7f, "X") };
                case CityMiscKind.IndustrialGantry:
                    return new[] { S("resolved_width", 9f, 6f, 9f, "X"), S("resolved_depth", 5f, 3.2f, 5f, "Y") };
                case CityMiscKind.NightlifeBillboard:
                    return new[] { S("resolved_width", 7f, 4.2f, 7f, "X") };
                case CityMiscKind.NightlifeFireEscape:
                    return new[] { S("resolved_width", 4.4f, 3f, 4.4f, "X"), S("resolved_height", 7.2f, 5.2f, 7.2f, "Z") };
                case CityMiscKind.NightlifeCinema:
                    return new[] { S("resolved_width", 9.5f, 6f, 9.5f, "X") };
                case CityMiscKind.SeacoastDriftwood:
                    return new[] { S("resolved_length", 2.8f, 1.5f, 2.8f, "X") };
                case CityMiscKind.PoiOldTownWaterworksShell:
                case CityMiscKind.PoiResidentialDryingYardShell:
                case CityMiscKind.PoiIndustrialWeighbridgeShell:
                case CityMiscKind.PoiNightlifeLastRouteIslandShell:
                    return new[] { S("public_width", 15f, 10.8f, 16.2f, "X", "Y") };
                case CityMiscKind.BarBuildingShell:
                    return SpecialBuildingScaleParameters(
                        12.2645f, 13.5237f, 9.3435f);
                case CityMiscKind.SupermarketBuildingShell:
                    return SpecialBuildingScaleParameters(
                        15.5f, 15.5f, 6.4f);
                case CityMiscKind.PlayerHomeBuildingShell:
                    return SpecialBuildingScaleParameters(
                        13f, 12f, 8.8f);
                case CityMiscKind.ChurchCourtyardSurface:
                    return new[]
                    {
                        S("resolved_width", 1f, 0.25f, 64f, "X"),
                        S("resolved_depth", 1f, 0.25f, 64f, "Y")
                    };
                case CityMiscKind.CemeteryFenceRail:
                    return new[]
                    {
                        S("resolved_length", 1f, 0.1f, 48f, "X")
                    };
                default:
                    return Array.Empty<ExpectedScaleParameter>();
            }
        }

        private static ExpectedScaleParameter[]
            SpecialBuildingScaleParameters(
                float frontageWidth,
                float depth,
                float height)
        {
            return new[]
            {
                S("lot_frontage_width", frontageWidth, 6f, 24f, "X"),
                S("lot_depth", depth, 6f, 24f, "Y"),
                S("lot_height", height, 4.5f, 14f, "Z")
            };
        }

        private static ExpectedScaleParameter S(
            string name, float reference, float minimum, float maximum,
            params string[] sourceAxes)
        {
            return new ExpectedScaleParameter(
                name, sourceAxes, reference, minimum, maximum);
        }

        private static Dictionary<string, Mesh> LoadExactMeshes()
        {
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
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
                        $"City misc FBX contains two meshes named " +
                        $"'{mesh.name}'.");
                }
            }

            HashSet<string> expectedNames = ExpectedParts
                .Select(part => part.MeshName)
                .ToHashSet(StringComparer.Ordinal);
            if (meshes.Count != CityMiscAssetProvider.ExpectedMeshCount ||
                !expectedNames.SetEquals(meshes.Keys))
            {
                throw new InvalidOperationException(
                    $"City misc FBX does not contain the exact " +
                    $"{CityMiscAssetProvider.ExpectedMeshCount} authored " +
                    "mesh sub-assets.");
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
                    "City misc FBX import settings are not the readable " +
                    "passive-geometry contract.");
            }

            UnityEngine.Object[] importedAssets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            if (importedAssets.OfType<Material>().Any() ||
                importedAssets.OfType<AnimationClip>().Any())
            {
                throw new InvalidOperationException(
                    "City misc FBX unexpectedly imported materials or " +
                    "animation clips.");
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
                    "City misc model is not passive render-only geometry.");
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
                        $"Imported City misc mesh '{name}' must be readable " +
                        "and non-empty for runtime combining.");
                }

                Vector2[] uv = mesh.uv;
                if (uv == null || uv.Length != mesh.vertexCount)
                {
                    throw new InvalidOperationException(
                        $"Imported City misc mesh '{name}' has missing UV0.");
                }

                int triangles = CountTriangles(mesh, name);
                if (triangles != source.triangles)
                {
                    throw new InvalidOperationException(
                        $"Imported City misc mesh '{name}' has {triangles} " +
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
                    "City misc imported triangle total differs from the " +
                    "manifest.");
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
            CityMiscAssetProvider provider,
            MiscManifest manifest,
            IReadOnlyDictionary<string, Mesh> meshes)
        {
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"City misc provider is missing at '{ProviderPath}'.");
            }

            provider.ValidateOrThrow();
            if (!provider.HasCompleteMeshes ||
                !string.Equals(
                    provider.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City misc provider is incomplete or was bound against " +
                    "another art build.");
            }

            for (int index = 0; index < ExpectedParts.Length; index++)
            {
                ExpectedPart expected = ExpectedParts[index];
                int partIndex = GetPartIndex(expected);
                CityMiscMeshPart part = provider.GetPartOrThrow(
                    expected.Kind,
                    expected.Variant,
                    partIndex);
                if (!string.Equals(part.Component, expected.Component,
                        StringComparison.Ordinal) ||
                    part.Role != expected.Role ||
                    part.Surface != expected.Surface ||
                    part.Mesh != meshes[expected.MeshName] ||
                    !string.Equals(
                        CityMiscAssetProvider.GetExpectedMeshName(
                            expected.Kind,
                            expected.Variant,
                            partIndex),
                        expected.MeshName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Provider binding disagrees on " +
                        $"'{expected.MeshName}'.");
                }
            }
        }

        private static int GetPartIndex(ExpectedPart expected)
        {
            for (int partIndex = 0;
                 partIndex < CityMiscAssetProvider.GetPartCount(expected.Kind);
                 partIndex++)
            {
                if (string.Equals(
                        CityMiscAssetProvider.GetExpectedComponent(
                            expected.Kind,
                            expected.Variant,
                            partIndex),
                        expected.Component,
                        StringComparison.Ordinal))
                {
                    return partIndex;
                }
            }

            throw new InvalidOperationException(
                $"No provider part index for {expected.Kind}/" +
                $"{expected.Component}.");
        }

        private static CityMiscAssetProvider LoadOrCreateProvider()
        {
            CityMiscAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityMiscAssetProvider>(
                    ProviderPath);
            if (provider != null)
            {
                return provider;
            }

            provider = ScriptableObject.CreateInstance<CityMiscAssetProvider>();
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
                CityMiscAssetProvider provider =
                    AssetDatabase.LoadAssetAtPath<CityMiscAssetProvider>(
                        ProviderPath);
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
                    "Could not inspect City misc assets: " + exception);
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
                    "Could not bind City misc assets: " + exception);
            }
        }

        private static int CountTriangles(Mesh mesh, string name)
        {
            long indices = 0;
            for (int subMesh = 0;
                 subMesh < mesh.subMeshCount;
                 subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles ||
                    mesh.GetIndexCount(subMesh) % 3 != 0)
                {
                    throw new InvalidOperationException(
                        $"Imported City misc mesh '{name}' is not " +
                        "triangulated.");
                }

                indices += (long)mesh.GetIndexCount(subMesh);
            }

            if (indices / 3 > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Imported City misc mesh '{name}' exceeds triangle " +
                    "limits.");
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

        private static Bounds BoundsFromManifestParts(
            IEnumerable<MiscManifestPart> parts)
        {
            using (IEnumerator<MiscManifestPart> enumerator =
                   parts.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException(
                        "Cannot union an empty City misc assembly.");
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
                        "Cannot union an empty imported City misc assembly.");
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
                        $"Imported City misc mesh '{name}' has non-finite " +
                        "UV0.");
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
                    $"Imported City misc mesh '{name}' UV0 bounds differ " +
                    "from the manifest.");
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
            var result = new Bounds();
            result.SetMinMax(
                Vector3From(minimum),
                Vector3From(maximum));
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
                string meshName,
                CityMiscKind kind,
                int variant,
                string component,
                CityMiscMeshRole role,
                CityMiscSurfaceKind surface,
                string surfaceKind)
            {
                MeshName = meshName;
                Kind = kind;
                Variant = variant;
                Component = component;
                Role = role;
                Surface = surface;
                SurfaceKind = surfaceKind;
            }

            public string MeshName { get; }
            public CityMiscKind Kind { get; }
            public int Variant { get; }
            public string Component { get; }
            public CityMiscMeshRole Role { get; }
            public CityMiscSurfaceKind Surface { get; }
            public string ManifestRole =>
                Surface == CityMiscSurfaceKind.Default
                    ? Role.ToString()
                    : $"{Role}_{Surface}";
            public string SurfaceKind { get; }
        }

        private sealed class ExpectedScaleParameter
        {
            public ExpectedScaleParameter(
                string name, string[] sourceAxes, float reference,
                float minimum, float maximum)
            {
                Name = name;
                SourceAxes = sourceAxes;
                Reference = reference;
                Minimum = minimum;
                Maximum = maximum;
            }

            public string Name { get; }
            public string[] SourceAxes { get; }
            public float Reference { get; }
            public float Minimum { get; }
            public float Maximum { get; }
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
            public string wave1_compatibility_signature;
            public string v2_compatibility_signature;
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
            public string scale_mode;
            public string source_ground_axis;
            public float source_ground_value;
            public string unity_ground_axis;
            public float unity_ground_value;
            public string source_forward_axis;
            public string unity_forward_axis;
            public float legacy_recipe_x_to_unity_local_x;
        }

        [Serializable]
        private sealed class MiscManifestAssembly
        {
            public string kind;
            public int variant;
            public string scale_mode;
            public string placement_contract;
            public string root_derivation;
            public string coordinate_profile;
            public MiscScaleParameter[] scale_parameters;
            public string[] unity_owned_parts;
            public string[] part_meshes;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
        }

        [Serializable]
        private sealed class MiscScaleParameter
        {
            public string name;
            public string[] source_axes;
            public float reference;
            public float min;
            public float max;
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
            public string placement_mode;
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
    /// City misc meshes are combined at runtime, so readability is
    /// intentional. Everything else stays passive and material-free.
    /// </summary>
    public sealed class CityMiscModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    CityMiscAssetSetup.ModelPath,
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
            if (CityMiscAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (!CityMiscAssetSetup.IsOwnedSourcePath(
                        importedAssets[index]))
                {
                    continue;
                }

                CityMiscAssetSetup.QueueBuildWhenSourcesExist();
                return;
            }
        }
    }
}
