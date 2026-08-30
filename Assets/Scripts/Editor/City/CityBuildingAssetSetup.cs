using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class CityBuildingAssetSetup
    {
        public const string ModelPath =
            "Assets/City/Models/CityBuildings3D.fbx";
        public const string ManifestPath =
            "Assets/City/Models/CityBuildings3D.json";
        public const string ProviderPath =
            "Assets/Resources/City/CityBuildingAssetProvider.asset";
        public const string PrefabFolder =
            "Assets/Resources/City/Buildings";

        private const string CatalogRootName = "ROOT_CityBuildings3D";
        private const string ExpectedGeneratorVersion = "2.0.0";
        private const float ContractTolerance = 0.003f;
        private const float BoundsTolerance = 0.02f;

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityBuildingAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Buildings 3D/Build Prototype Catalog")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                "City building prototype prefabs and provider rebuilt.");
        }

        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("CITY BUILDING UNITY ASSET BUILD OK");
        }

        [MenuItem("Bar Promenade/City Buildings 3D/Validate Catalog")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("City building prototype catalog is valid.");
        }

        public static bool IsModelPath(string path)
        {
            return string.Equals(
                path,
                ModelPath,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOwnedSourcePath(string path)
        {
            return IsModelPath(path) || string.Equals(
                path,
                ManifestPath,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
        }

        public static string GetPrefabPath(string stableId)
        {
            switch (stableId)
            {
                case "old-town-prototype-01":
                    return PrefabFolder + "/OldTownPrototype01.prefab";
                case "residential-prototype-01":
                    return PrefabFolder +
                        "/ResidentialPrototype01.prefab";
                case "industrial-prototype-01":
                    return PrefabFolder +
                        "/IndustrialPrototype01.prefab";
                case "nightlife-prototype-01":
                    return PrefabFolder +
                        "/NightlifePrototype01.prefab";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(stableId),
                        stableId,
                        "Unknown City building prototype ID.");
            }
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
                    "City building setup requires CityBuildings3D.fbx and " +
                    "CityBuildings3D.json.");
            }

            isBuilding = true;
            try
            {
                ImportSources();
                BuildingManifest manifest = LoadAndValidateManifest();
                ValidateImportedModel(manifest);
                EnsureAssetFolder(PrefabFolder);

                var entries = new CityBuildingPrefabEntry[
                    CityBuildingAssetProvider.ExpectedPrototypeCount];
                for (int index = 0; index < manifest.prototypes.Length;
                     index++)
                {
                    BuildingPrototype prototype =
                        manifest.prototypes[index];
                    GameObject prefab = BuildPrefab(prototype, manifest);
                    entries[index] = new CityBuildingPrefabEntry(
                        prototype.stable_id,
                        ParseDistrict(prototype.district),
                        prefab);
                }

                CityBuildingAssetProvider provider =
                    LoadOrCreateProvider();
                provider.Configure(
                    entries,
                    manifest.design_id,
                    manifest.build_signature);
                EditorUtility.SetDirty(provider);
                AssetDatabase.SaveAssets();
                ValidateOrThrow();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void ValidateOrThrow()
        {
            BuildingManifest manifest = LoadAndValidateManifest();
            ValidateImporter();
            ValidateImportedModel(manifest);

            for (int index = 0; index < manifest.prototypes.Length;
                 index++)
            {
                ValidatePrefab(manifest.prototypes[index], manifest);
            }

            CityBuildingAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityBuildingAssetProvider>(
                    ProviderPath);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"City building provider is missing at '{ProviderPath}'.");
            }

            provider.ValidateOrThrow();
            if (!string.Equals(
                    provider.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City building provider was built from another manifest.");
            }

            for (int index = 0; index < manifest.prototypes.Length;
                 index++)
            {
                BuildingPrototype prototype = manifest.prototypes[index];
                GameObject expectedPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        GetPrefabPath(prototype.stable_id));
                GameObject actualPrefab = provider.GetPrefabOrThrow(
                    ParseDistrict(prototype.district));
                if (actualPrefab != expectedPrefab)
                {
                    throw new InvalidOperationException(
                        $"Provider binding for '{prototype.stable_id}' " +
                        "does not point at its wrapper prefab.");
                }
            }
        }

        private static void ImportSources()
        {
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static GameObject BuildPrefab(
            BuildingPrototype prototype,
            BuildingManifest manifest)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}' as a model.");
            }

            GameObject wrapper = new GameObject(
                "CityBuilding_" + prototype.district + "_Prototype01");
            GameObject sourceInstance = null;
            try
            {
                sourceInstance = PrefabUtility.InstantiatePrefab(
                    modelAsset) as GameObject;
                if (sourceInstance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the City building catalog.");
                }

                PrefabUtility.UnpackPrefabInstance(
                    sourceInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                Transform catalogRoot = FindUniqueTransform(
                    sourceInstance.transform,
                    CatalogRootName);
                Transform prototypeRoot = FindDirectChild(
                    catalogRoot,
                    prototype.root_name);
                Matrix4x4 sourceWorldMatrix = prototypeRoot.localToWorldMatrix;
                prototypeRoot.SetParent(wrapper.transform, true);
                AssertMatrixNear(
                    prototypeRoot.localToWorldMatrix,
                    sourceWorldMatrix,
                    prototype.stable_id + " preserved imported transform");
                Object.DestroyImmediate(sourceInstance);
                sourceInstance = null;

                CityBuildingPartBinding[] bindings =
                    BindParts(prototypeRoot, prototype);
                Renderer[] renderers = bindings
                    .Select(binding => binding.Renderer)
                    .ToArray();
                Bounds calculatedBounds = CalculateLocalBounds(
                    wrapper.transform,
                    renderers);
                Bounds expectedBounds = BoundsFromArrays(
                    prototype.bounds_min_unity,
                    prototype.bounds_max_unity);
                AssertBoundsNear(
                    calculatedBounds,
                    expectedBounds,
                    prototype.stable_id + " imported bounds");

                GameObject anchorObject = new GameObject("ANCHOR_Front");
                Transform frontAnchor = anchorObject.transform;
                frontAnchor.SetParent(wrapper.transform, false);
                frontAnchor.localPosition = Vector3FromArray(
                    prototype.front_anchor.position_unity,
                    prototype.stable_id + " front anchor position");
                Vector3 forward = Vector3FromArray(
                    prototype.front_anchor.forward_unity,
                    prototype.stable_id + " front anchor forward");
                frontAnchor.localRotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);

                CityBuildingAssetRegistry registry =
                    wrapper.AddComponent<CityBuildingAssetRegistry>();
                registry.Configure(
                    prototype.stable_id,
                    ParseDistrict(prototype.district),
                    prototype.grammar,
                    prototypeRoot,
                    frontAnchor,
                    bindings,
                    calculatedBounds,
                    ConvertSourceBoundsToUnity(
                        prototype.roof_attachment_bounds_min_source,
                        prototype.roof_attachment_bounds_max_source),
                    BuildFacadeAttachments(prototype),
                    BuildWindowSlots(prototype),
                    prototype.frontage_width_m,
                    prototype.depth_m,
                    prototype.height_m,
                    manifest.unit_factor,
                    prototype.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);
                registry.ValidateOrThrow();

                string prefabPath = GetPrefabPath(prototype.stable_id);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    wrapper,
                    prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save '{prefabPath}'.");
                }

                return prefab;
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

        private static CityBuildingPartBinding[] BindParts(
            Transform prototypeRoot,
            BuildingPrototype prototype)
        {
            var sourceParts = prototype.parts.ToDictionary(
                part => ParseRole(part.role),
                part => part);
            var bindings = new CityBuildingPartBinding[
                CityBuildingAssetRegistry.ExpectedRoleCount];

            for (int index = 0; index < bindings.Length; index++)
            {
                CityBuildingMeshRole role =
                    CityBuildingAssetRegistry.GetExpectedRole(index);
                BuildingPart sourcePart = sourceParts[role];
                Transform child = FindDirectChild(
                    prototypeRoot,
                    sourcePart.object_name);
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (renderer == null || filter == null ||
                    filter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"Imported part '{sourcePart.object_name}' is not a " +
                        "static mesh.");
                }

                bindings[index] = new CityBuildingPartBinding(
                    sourcePart.object_name,
                    role,
                    sourcePart.surface_kind,
                    sourcePart.uv_scheme,
                    sourcePart.meters_per_tile,
                    renderer);
            }

            return bindings;
        }

        private static CityBuildingFacadeAttachment[]
            BuildFacadeAttachments(BuildingPrototype prototype)
        {
            return prototype.facade_attachment_bounds
                .Select(attachment => new CityBuildingFacadeAttachment(
                    attachment.side,
                    ConvertSourceBoundsToUnity(
                        attachment.bounds_min_source,
                        attachment.bounds_max_source)))
                .ToArray();
        }

        private static CityBuildingWindowSlot[] BuildWindowSlots(
            BuildingPrototype prototype)
        {
            return prototype.window_slots
                .Select(slot => new CityBuildingWindowSlot(
                    slot.slot_id,
                    slot.side,
                    slot.floor,
                    slot.bay,
                    ConvertSourceVectorToUnity(slot.center_source),
                    Vector2FromArray(
                        slot.size_m,
                        slot.slot_id + " window size"),
                    slot.uv2_slot_id))
                .ToArray();
        }

        private static void ValidatePrefab(
            BuildingPrototype prototype,
            BuildingManifest manifest)
        {
            string path = GetPrefabPath(prototype.stable_id);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City building prefab is missing at '{path}'.");
            }

            CityBuildingAssetRegistry registry =
                prefab.GetComponent<CityBuildingAssetRegistry>();
            if (registry == null ||
                !string.Equals(
                    registry.StableId,
                    prototype.stable_id,
                    StringComparison.Ordinal) ||
                registry.District != ParseDistrict(prototype.district) ||
                !string.Equals(
                    registry.Grammar,
                    prototype.grammar,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != prototype.triangle_count ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"City building prefab '{prototype.stable_id}' is stale.");
            }

            registry.ValidateOrThrow();
            Bounds expectedBounds = BoundsFromArrays(
                prototype.bounds_min_unity,
                prototype.bounds_max_unity);
            AssertBoundsNear(
                registry.LocalBounds,
                expectedBounds,
                prototype.stable_id + " registry bounds");
            AssertBoundsNear(
                CalculateLocalBounds(
                    prefab.transform,
                    registry.Parts.Select(part => part.Renderer)),
                expectedBounds,
                prototype.stable_id + " prefab renderer bounds");

            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Transform sourceCatalog = FindUniqueTransform(
                modelAsset.transform,
                CatalogRootName);
            Transform sourcePrototype = FindDirectChild(
                sourceCatalog,
                prototype.root_name);
            AssertMatrixNear(
                registry.ModelRoot.localToWorldMatrix,
                sourcePrototype.localToWorldMatrix,
                prototype.stable_id + " wrapper transform");
        }

        private static BuildingManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Missing City building manifest at '{ManifestPath}'.");
            }

            BuildingManifest manifest =
                JsonUtility.FromJson<BuildingManifest>(source.text);
            ValidateManifestHeader(manifest);
            ValidateManifestPrototypes(manifest);
            return manifest;
        }

        private static void ValidateManifestHeader(BuildingManifest manifest)
        {
            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.generator) ||
                !string.Equals(
                    manifest.generator_version,
                    ExpectedGeneratorVersion,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.blender_version) ||
                !string.Equals(
                    manifest.design_id,
                    CityBuildingAssetProvider.ExpectedDesignId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.display_name) ||
                !string.Equals(
                    manifest.fbx_asset_path,
                    ModelPath,
                    StringComparison.Ordinal) ||
                Mathf.Abs(manifest.unit_factor - 1f) > ContractTolerance ||
                manifest.uv0_encoding == null ||
                !string.Equals(
                    manifest.uv0_encoding.window_glass_scheme,
                    "per_window_face_projected_0_1",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.uv0_encoding.building_side_atlas_scheme,
                    "building_side_atlas_0_1",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.uv0_encoding.full_face_surface_scheme,
                    "full_face_projected_0_1",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.uv0_encoding.metric_surface_scheme,
                    "world_metre_projected",
                    StringComparison.Ordinal) ||
                manifest.uv2_encoding == null ||
                manifest.uv2_encoding.channel_index != 1 ||
                !string.Equals(
                    manifest.uv2_encoding.scheme,
                    "u_centered_uint8",
                    StringComparison.Ordinal) ||
                Mathf.Abs(
                    manifest.uv2_encoding.divisor -
                    CityBuildingAssetRegistry.WindowSlotUv2Divisor) >
                    ContractTolerance ||
                !string.Equals(
                    manifest.uv2_encoding.zero_means,
                    "non_window_geometry",
                    StringComparison.Ordinal) ||
                manifest.prototype_count !=
                    CityBuildingAssetProvider.ExpectedPrototypeCount ||
                manifest.mesh_count !=
                    CityBuildingAssetProvider.ExpectedPrototypeCount *
                    CityBuildingAssetRegistry.ExpectedRoleCount ||
                manifest.triangle_count <= 0 ||
                !CityBuildingAssetProvider.IsSha256(
                    manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "City building manifest header is malformed.");
            }

            if (manifest.source_axes == null ||
                !string.Equals(manifest.source_axes.right, "+X",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.source_axes.forward, "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.source_axes.up, "+Z",
                    StringComparison.Ordinal) ||
                manifest.unity_axes == null ||
                !string.Equals(manifest.unity_axes.right, "+X",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.unity_axes.forward, "+Z",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.unity_axes.up, "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.unity_axes.fbx_axis_forward, "-Z",
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.unity_axes.fbx_axis_up, "+Y",
                    StringComparison.Ordinal) ||
                manifest.unity_axes.bake_space_transform)
            {
                throw new InvalidOperationException(
                    "City building manifest axis contract changed.");
            }

            BuildingRootContract root = manifest.root_contract;
            BuildingPassiveContract passive = manifest.passive;
            if (root == null ||
                !string.Equals(root.catalog_root, CatalogRootName,
                    StringComparison.Ordinal) ||
                !string.Equals(root.origin, "footprint_center_ground",
                    StringComparison.Ordinal) ||
                !string.Equals(root.scale_mode, "fixed_meters",
                    StringComparison.Ordinal) ||
                !string.Equals(root.source_ground_axis, "Z",
                    StringComparison.Ordinal) ||
                !string.Equals(root.unity_ground_axis, "Y",
                    StringComparison.Ordinal) ||
                !string.Equals(root.source_forward_axis, "+Y",
                    StringComparison.Ordinal) ||
                !string.Equals(root.unity_forward_axis, "+Z",
                    StringComparison.Ordinal) ||
                Mathf.Abs(root.source_ground_value) > ContractTolerance ||
                Mathf.Abs(root.unity_ground_value) > ContractTolerance ||
                passive == null || passive.colliders || passive.lights ||
                passive.cameras || passive.materials ||
                passive.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "City building root or passive contract changed.");
            }
        }

        private static void ValidateManifestPrototypes(
            BuildingManifest manifest)
        {
            if (manifest.prototypes == null ||
                manifest.prototypes.Length != manifest.prototype_count)
            {
                throw new InvalidOperationException(
                    "City building manifest prototype table is malformed.");
            }

            int triangleTotal = 0;
            for (int index = 0; index < manifest.prototypes.Length;
                 index++)
            {
                BuildingPrototype prototype = manifest.prototypes[index];
                ValidatePrototype(prototype, index);
                triangleTotal += prototype.triangle_count;
            }

            if (triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "City building manifest triangle total is stale.");
            }
        }

        private static void ValidatePrototype(
            BuildingPrototype prototype,
            int expectedIndex)
        {
            if (prototype == null ||
                !string.Equals(
                    prototype.stable_id,
                    CityBuildingAssetProvider.GetExpectedStableId(
                        expectedIndex),
                    StringComparison.Ordinal) ||
                ParseDistrict(prototype.district) !=
                    CityBuildingAssetProvider.GetExpectedDistrict(
                        expectedIndex) ||
                string.IsNullOrWhiteSpace(prototype.grammar) ||
                !string.Equals(
                    prototype.root_name,
                    "ROOT_" + prototype.stable_id,
                    StringComparison.Ordinal) ||
                prototype.triangle_count <= 0 ||
                prototype.triangle_count >
                    CityBuildingAssetRegistry.MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    $"City building prototype {expectedIndex} is malformed.");
            }

            Vector3 envelope =
                CityBuildingAssetProvider.GetExpectedEnvelope(expectedIndex);
            if (Mathf.Abs(prototype.frontage_width_m - envelope.x) >
                    ContractTolerance ||
                Mathf.Abs(prototype.depth_m - envelope.z) >
                    ContractTolerance ||
                Mathf.Abs(prototype.height_m - envelope.y) >
                    ContractTolerance)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' envelope changed.");
            }

            ValidatePrototypeBounds(prototype, envelope);
            ValidateFrontAnchor(prototype);
            ValidateAttachments(prototype);
            ValidateParts(prototype);
        }

        private static void ValidatePrototypeBounds(
            BuildingPrototype prototype,
            Vector3 envelope)
        {
            Bounds sourceBounds = BoundsFromArrays(
                prototype.bounds_min_source,
                prototype.bounds_max_source);
            Bounds unityBounds = BoundsFromArrays(
                prototype.bounds_min_unity,
                prototype.bounds_max_unity);
            AssertBoundsNear(
                unityBounds,
                ConvertSourceBoundsToUnity(
                    prototype.bounds_min_source,
                    prototype.bounds_max_source),
                prototype.stable_id + " source/Unity bounds");

            if (Mathf.Abs(sourceBounds.min.z) > ContractTolerance ||
                Mathf.Abs(unityBounds.min.y) > ContractTolerance ||
                unityBounds.size.x < envelope.x - BoundsTolerance ||
                unityBounds.size.x > envelope.x + 0.16f ||
                Mathf.Abs(unityBounds.size.y - envelope.y) >
                    BoundsTolerance ||
                unityBounds.size.z < envelope.z - BoundsTolerance ||
                unityBounds.size.z > envelope.z + 0.16f)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' is not grounded " +
                    "inside its fit-safe envelope.");
            }
        }

        private static void ValidateFrontAnchor(BuildingPrototype prototype)
        {
            if (prototype.front_anchor == null)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' needs a front " +
                    "anchor.");
            }

            Vector3 sourcePosition = Vector3FromArray(
                prototype.front_anchor.position_source,
                prototype.stable_id + " source front position");
            Vector3 unityPosition = Vector3FromArray(
                prototype.front_anchor.position_unity,
                prototype.stable_id + " Unity front position");
            Vector3 sourceForward = Vector3FromArray(
                prototype.front_anchor.forward_source,
                prototype.stable_id + " source front forward");
            Vector3 unityForward = Vector3FromArray(
                prototype.front_anchor.forward_unity,
                prototype.stable_id + " Unity front forward");
            if (Vector3.Distance(
                    ConvertSourceVectorToUnity(
                        prototype.front_anchor.position_source),
                    unityPosition) > ContractTolerance ||
                Vector3.Distance(
                    ConvertSourceVectorToUnity(
                        prototype.front_anchor.forward_source),
                    unityForward) > ContractTolerance ||
                Vector3.Distance(sourceForward, Vector3.up) >
                    ContractTolerance ||
                Vector3.Distance(unityForward, Vector3.forward) >
                    ContractTolerance ||
                Mathf.Abs(sourcePosition.z) > ContractTolerance ||
                Mathf.Abs(unityPosition.y) > ContractTolerance ||
                Mathf.Abs(
                    unityPosition.z -
                    prototype.depth_m * 0.5f) > ContractTolerance)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' front axis or " +
                    "anchor changed.");
            }
        }

        private static void ValidateAttachments(
            BuildingPrototype prototype)
        {
            Bounds roof = BoundsFromArrays(
                prototype.roof_attachment_bounds_min_source,
                prototype.roof_attachment_bounds_max_source);
            if (roof.size.x <= 0f || roof.size.y <= 0f ||
                prototype.facade_attachment_bounds == null ||
                prototype.facade_attachment_bounds.Length == 0 ||
                prototype.window_slots == null ||
                prototype.window_slots.Length == 0)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' attachment " +
                    "metadata is incomplete.");
            }

            var facadeSides = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuildingFacadeAttachment attachment in
                     prototype.facade_attachment_bounds)
            {
                if (attachment == null ||
                    string.IsNullOrWhiteSpace(attachment.side) ||
                    !facadeSides.Add(attachment.side))
                {
                    throw new InvalidOperationException(
                        $"City building '{prototype.stable_id}' has invalid " +
                        "facade attachment bounds.");
                }

                BoundsFromArrays(
                    attachment.bounds_min_source,
                    attachment.bounds_max_source);
            }

            var slotIds = new HashSet<int>();
            var uv2Ids = new HashSet<int>();
            foreach (BuildingWindowSlot slot in prototype.window_slots)
            {
                Vector2 size = Vector2FromArray(
                    slot?.size_m,
                    prototype.stable_id + " window size");
                if (slot == null ||
                    slot.slot_id <= 0 ||
                    string.IsNullOrWhiteSpace(slot.side) ||
                    slot.floor < 0 || slot.bay < 0 ||
                    size.x <= 0f || size.y <= 0f ||
                    slot.uv2_slot_id <= 0 ||
                    slot.uv2_slot_id >
                        CityBuildingAssetRegistry.MaximumWindowSlotId ||
                    slot.uv2_slot_id != slot.slot_id ||
                    !slotIds.Add(slot.slot_id) ||
                    !uv2Ids.Add(slot.uv2_slot_id))
                {
                    throw new InvalidOperationException(
                        $"City building '{prototype.stable_id}' has invalid " +
                        "window-slot metadata.");
                }

                Vector3FromArray(
                    slot.center_source,
                    slot.slot_id + " source center");
            }
        }

        private static void ValidateParts(BuildingPrototype prototype)
        {
            if (prototype.parts == null ||
                prototype.parts.Length !=
                    CityBuildingAssetRegistry.ExpectedRoleCount)
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' needs seven " +
                    "semantic surface parts.");
            }

            int triangles = 0;
            var roles = new HashSet<CityBuildingMeshRole>();
            var objectNames = new HashSet<string>(StringComparer.Ordinal);
            var uv2Ids = new HashSet<int>();
            for (int index = 0; index < prototype.parts.Length; index++)
            {
                BuildingPart part = prototype.parts[index];
                CityBuildingMeshRole role = ParseRole(part?.role);
                string expectedName = prototype.stable_id + "__" + role;
                if (part == null ||
                    !string.Equals(
                        part.object_name,
                        expectedName,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        part.surface_kind,
                        role.ToString(),
                        StringComparison.Ordinal) ||
                    !HasExpectedUvContract(part, role) ||
                    part.vertices <= 0 || part.triangles <= 0 ||
                    !roles.Add(role) ||
                    !objectNames.Add(part.object_name))
                {
                    throw new InvalidOperationException(
                        $"City building '{prototype.stable_id}' has invalid " +
                        "role parts.");
                }

                AssertBoundsNear(
                    BoundsFromArrays(
                        part.bounds_min_unity,
                        part.bounds_max_unity),
                    ConvertSourceBoundsToUnity(
                        part.bounds_min_source,
                        part.bounds_max_source),
                    part.object_name + " source/Unity bounds");
                Vector2FromArray(part.uv0_min, part.object_name + " UV min");
                Vector2FromArray(part.uv0_max, part.object_name + " UV max");
                if (part.uv2_slot_ids == null)
                {
                    throw new InvalidOperationException(
                        $"Part '{part.object_name}' has no UV2 slot table.");
                }

                foreach (int uv2SlotId in part.uv2_slot_ids)
                {
                    if (uv2SlotId < 0)
                    {
                        throw new InvalidOperationException(
                            $"Part '{part.object_name}' has a negative UV2 " +
                            "slot ID.");
                    }

                    uv2Ids.Add(uv2SlotId);
                }

                triangles += part.triangles;
            }

            HashSet<int> expectedUv2Ids = prototype.window_slots
                .Select(slot => slot.uv2_slot_id)
                .ToHashSet();
            uv2Ids.Remove(0);
            if (triangles != prototype.triangle_count ||
                roles.Count !=
                    CityBuildingAssetRegistry.ExpectedRoleCount ||
                !uv2Ids.SetEquals(expectedUv2Ids))
            {
                throw new InvalidOperationException(
                    $"City building '{prototype.stable_id}' part totals or " +
                    "UV2 slot ownership changed.");
            }
        }

        private static void ValidateImportedModel(BuildingManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}' as a model.");
            }

            ValidatePassiveHierarchy(model);
            Transform catalogRoot = FindUniqueTransform(
                model.transform,
                CatalogRootName);
            if (catalogRoot.childCount != manifest.prototype_count)
            {
                throw new InvalidOperationException(
                    "Imported City building catalog root count changed.");
            }

            int importedTriangles = 0;
            for (int index = 0; index < manifest.prototypes.Length;
                 index++)
            {
                BuildingPrototype prototype = manifest.prototypes[index];
                Transform root = FindDirectChild(
                    catalogRoot,
                    prototype.root_name);
                if (root.childCount !=
                    CityBuildingAssetRegistry.ExpectedRoleCount)
                {
                    throw new InvalidOperationException(
                        $"Imported '{prototype.root_name}' must have seven " +
                        "direct semantic mesh children.");
                }

                var renderers = new List<Renderer>();
                foreach (BuildingPart part in prototype.parts)
                {
                    Transform child = FindDirectChild(
                        root,
                        part.object_name);
                    MeshFilter filter = child.GetComponent<MeshFilter>();
                    MeshRenderer renderer =
                        child.GetComponent<MeshRenderer>();
                    if (filter == null || filter.sharedMesh == null ||
                        renderer == null || child.childCount != 0)
                    {
                        throw new InvalidOperationException(
                            $"Imported part '{part.object_name}' is not one " +
                            "direct passive mesh.");
                    }

                    int triangles = CountTriangles(filter.sharedMesh);
                    if (filter.sharedMesh.isReadable ||
                        triangles != part.triangles)
                    {
                        throw new InvalidOperationException(
                            $"Imported mesh '{part.object_name}' read/write " +
                            "or triangle contract changed.");
                    }

                    importedTriangles += triangles;
                    renderers.Add(renderer);
                }

                AssertBoundsNear(
                    CalculateLocalBounds(model.transform, renderers),
                    BoundsFromArrays(
                        prototype.bounds_min_unity,
                        prototype.bounds_max_unity),
                    prototype.stable_id + " source-model bounds");
            }

            Object[] imported = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            if (imported.OfType<Mesh>().Count() != manifest.mesh_count ||
                imported.OfType<Material>().Any() ||
                imported.OfType<AnimationClip>().Any() ||
                importedTriangles != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Imported City building sub-assets differ from the " +
                    "passive manifest.");
            }
        }

        private static void ValidateImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                Mathf.Abs(importer.globalScale - 1f) > ContractTolerance ||
                !importer.useFileScale ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy ||
                importer.optimizeGameObjects ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation || importer.importCameras ||
                importer.importLights || importer.importBlendShapes ||
                importer.addCollider || importer.isReadable ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "City building FBX import contract changed.");
            }
        }

        private static CityBuildingAssetProvider LoadOrCreateProvider()
        {
            CityBuildingAssetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityBuildingAssetProvider>(
                    ProviderPath);
            if (provider != null)
            {
                return provider;
            }

            EnsureAssetFolder("Assets/Resources/City");
            provider =
                ScriptableObject.CreateInstance<CityBuildingAssetProvider>();
            AssetDatabase.CreateAsset(provider, ProviderPath);
            return provider;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid Unity asset folder '{path}'.");
            }

            string parent = path.Substring(0, separator);
            string name = path.Substring(separator + 1);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Transform FindUniqueTransform(
            Transform root,
            string name)
        {
            Transform match = null;
            foreach (Transform candidate in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(
                        candidate.name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Transform '{name}' is duplicated.");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Transform '{name}' is missing.");
            }

            return match;
        }

        private static Transform FindDirectChild(
            Transform parent,
            string name)
        {
            Transform match = null;
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (!string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Direct child '{name}' is duplicated.");
                }

                match = child;
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Direct child '{name}' is missing below " +
                    $"'{parent.name}'.");
            }

            return match;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool hasPoint = false;
            Vector3 minimum = Vector3.zero;
            Vector3 maximum = Vector3.zero;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = root.InverseTransformPoint(new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z));
                    if (!hasPoint)
                    {
                        minimum = maximum = point;
                        hasPoint = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, point);
                        maximum = Vector3.Max(maximum, point);
                    }
                }
            }

            if (!hasPoint)
            {
                throw new InvalidOperationException(
                    "Cannot calculate bounds without renderers.");
            }

            return new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
        }

        private static Bounds ConvertSourceBoundsToUnity(
            float[] minimum,
            float[] maximum)
        {
            Vector3 sourceMinimum = Vector3FromArray(
                minimum,
                "source bounds minimum");
            Vector3 sourceMaximum = Vector3FromArray(
                maximum,
                "source bounds maximum");
            Vector3[] corners = new Vector3[8];
            for (int corner = 0; corner < corners.Length; corner++)
            {
                corners[corner] = ConvertSourceVectorToUnity(new[]
                {
                    (corner & 1) == 0
                        ? sourceMinimum.x
                        : sourceMaximum.x,
                    (corner & 2) == 0
                        ? sourceMinimum.y
                        : sourceMaximum.y,
                    (corner & 4) == 0
                        ? sourceMinimum.z
                        : sourceMaximum.z
                });
            }

            Vector3 unityMinimum = corners[0];
            Vector3 unityMaximum = corners[0];
            for (int index = 1; index < corners.Length; index++)
            {
                unityMinimum = Vector3.Min(unityMinimum, corners[index]);
                unityMaximum = Vector3.Max(unityMaximum, corners[index]);
            }

            return new Bounds(
                (unityMinimum + unityMaximum) * 0.5f,
                unityMaximum - unityMinimum);
        }

        private static Vector3 ConvertSourceVectorToUnity(float[] value)
        {
            Vector3 source = Vector3FromArray(value, "source vector");
            return new Vector3(source.x, source.z, source.y);
        }

        private static Bounds BoundsFromArrays(
            float[] minimum,
            float[] maximum)
        {
            Vector3 min = Vector3FromArray(minimum, "bounds minimum");
            Vector3 max = Vector3FromArray(maximum, "bounds maximum");
            if (max.x < min.x || max.y < min.y || max.z < min.z)
            {
                throw new InvalidOperationException(
                    "Manifest bounds minimum exceeds its maximum.");
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static Vector3 Vector3FromArray(
            float[] value,
            string label)
        {
            if (value == null || value.Length != 3 ||
                value.Any(component =>
                    float.IsNaN(component) || float.IsInfinity(component)))
            {
                throw new InvalidOperationException(
                    $"{label} must contain three finite values.");
            }

            return new Vector3(value[0], value[1], value[2]);
        }

        private static Vector2 Vector2FromArray(
            float[] value,
            string label)
        {
            if (value == null || value.Length != 2 ||
                value.Any(component =>
                    float.IsNaN(component) || float.IsInfinity(component)))
            {
                throw new InvalidOperationException(
                    $"{label} must contain two finite values.");
            }

            return new Vector2(value[0], value[1]);
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Bounds expected,
            string label)
        {
            if (Vector3.Distance(actual.min, expected.min) > BoundsTolerance ||
                Vector3.Distance(actual.max, expected.max) > BoundsTolerance)
            {
                throw new InvalidOperationException(
                    $"{label} differs: actual {actual}, expected {expected}.");
            }
        }

        private static void AssertMatrixNear(
            Matrix4x4 actual,
            Matrix4x4 expected,
            string label)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(actual[index] - expected[index]) >
                    ContractTolerance)
                {
                    throw new InvalidOperationException(
                        $"{label} changed at matrix index {index}.");
                }
            }
        }

        private static int CountTriangles(Mesh mesh)
        {
            long indexCount = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                indexCount += (long)mesh.GetIndexCount(subMesh);
            }

            if (indexCount % 3L != 0L || indexCount / 3L > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Mesh '{mesh.name}' does not contain triangle indices.");
            }

            return (int)(indexCount / 3L);
        }

        private static void ValidatePassiveHierarchy(GameObject root)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                root.GetComponentsInChildren<Light>(true).Length != 0 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<Animator>(true).Length != 0 ||
                root.GetComponentsInChildren<Animation>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                root.GetComponentsInChildren<ParticleSystem>(true).Length !=
                    0 ||
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Length != 0)
            {
                throw new InvalidOperationException(
                    "City building source hierarchy must remain passive.");
            }
        }

        private static CityDistrictKind ParseDistrict(string value)
        {
            if (!Enum.TryParse(value, false, out CityDistrictKind district) ||
                district != CityDistrictKind.OldTown &&
                district != CityDistrictKind.Residential &&
                district != CityDistrictKind.Industrial &&
                district != CityDistrictKind.Nightlife)
            {
                throw new InvalidOperationException(
                    $"Unknown City building district '{value}'.");
            }

            return district;
        }

        private static CityBuildingMeshRole ParseRole(string value)
        {
            if (!Enum.TryParse(
                    value,
                    false,
                    out CityBuildingMeshRole role))
            {
                throw new InvalidOperationException(
                    $"Unknown City building mesh role '{value}'.");
            }

            return role;
        }

        private static bool HasExpectedUvContract(
            BuildingPart part,
            CityBuildingMeshRole role)
        {
            string expectedScheme;
            bool metric;
            switch (role)
            {
                case CityBuildingMeshRole.FacadePrimary:
                case CityBuildingMeshRole.FacadeSecondary:
                    expectedScheme = "building_side_atlas_0_1";
                    metric = false;
                    break;
                case CityBuildingMeshRole.Plinth:
                    expectedScheme = "full_face_projected_0_1";
                    metric = false;
                    break;
                case CityBuildingMeshRole.Roof:
                case CityBuildingMeshRole.Metal:
                case CityBuildingMeshRole.WindowFrame:
                    expectedScheme = "world_metre_projected";
                    metric = true;
                    break;
                case CityBuildingMeshRole.WindowGlass:
                    expectedScheme = "per_window_face_projected_0_1";
                    metric = false;
                    break;
                default:
                    return false;
            }

            if (!string.Equals(
                    part.uv_scheme,
                    expectedScheme,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return metric
                ? part.meters_per_tile > 0f &&
                  !float.IsNaN(part.meters_per_tile) &&
                  !float.IsInfinity(part.meters_per_tile)
                : Mathf.Abs(part.meters_per_tile) <= ContractTolerance;
        }

        private static void ValidateDependencyStamp()
        {
            if (isBuilding || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !SourcesExist())
            {
                return;
            }

            try
            {
                BuildingManifest manifest = LoadAndValidateManifest();
                CityBuildingAssetProvider provider =
                    AssetDatabase.LoadAssetAtPath<
                        CityBuildingAssetProvider>(ProviderPath);
                if (provider == null || !provider.HasCompletePrefabs ||
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
                    "Could not inspect City building assets: " + exception);
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
                    "Could not bind City building assets: " + exception);
            }
        }

        [Serializable]
        private sealed class BuildingManifest
        {
            public string generator;
            public string generator_version;
            public string blender_version;
            public string design_id;
            public string display_name;
            public string fbx_asset_path;
            public BuildingAxes source_axes;
            public BuildingUnityAxes unity_axes;
            public float unit_factor;
            public BuildingRootContract root_contract;
            public BuildingPassiveContract passive;
            public BuildingUv0Encoding uv0_encoding;
            public BuildingUv2Encoding uv2_encoding;
            public int prototype_count;
            public int mesh_count;
            public int triangle_count;
            public BuildingPrototype[] prototypes;
            public string build_signature;
        }

        [Serializable]
        private sealed class BuildingAxes
        {
            public string right;
            public string forward;
            public string up;
        }

        [Serializable]
        private sealed class BuildingUnityAxes
        {
            public string right;
            public string forward;
            public string up;
            public string fbx_axis_forward;
            public string fbx_axis_up;
            public bool bake_space_transform;
        }

        [Serializable]
        private sealed class BuildingRootContract
        {
            public string catalog_root;
            public string origin;
            public string scale_mode;
            public string source_ground_axis;
            public float source_ground_value;
            public string unity_ground_axis;
            public float unity_ground_value;
            public string source_forward_axis;
            public string unity_forward_axis;
        }

        [Serializable]
        private sealed class BuildingPassiveContract
        {
            public bool colliders;
            public bool lights;
            public bool cameras;
            public bool materials;
            public int animation_count;
        }

        [Serializable]
        private sealed class BuildingUv2Encoding
        {
            public int channel_index;
            public string scheme;
            public float divisor;
            public string zero_means;
        }

        [Serializable]
        private sealed class BuildingUv0Encoding
        {
            public string window_glass_scheme;
            public string building_side_atlas_scheme;
            public string full_face_surface_scheme;
            public string metric_surface_scheme;
        }

        [Serializable]
        private sealed class BuildingPrototype
        {
            public string stable_id;
            public string district;
            public string grammar;
            public string root_name;
            public float frontage_width_m;
            public float depth_m;
            public float height_m;
            public int triangle_count;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public BuildingFrontAnchor front_anchor;
            public float[] roof_attachment_bounds_min_source;
            public float[] roof_attachment_bounds_max_source;
            public BuildingFacadeAttachment[] facade_attachment_bounds;
            public BuildingWindowSlot[] window_slots;
            public BuildingPart[] parts;
        }

        [Serializable]
        private sealed class BuildingFrontAnchor
        {
            public float[] position_source;
            public float[] forward_source;
            public float[] position_unity;
            public float[] forward_unity;
        }

        [Serializable]
        private sealed class BuildingFacadeAttachment
        {
            public string side;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
        }

        [Serializable]
        private sealed class BuildingWindowSlot
        {
            public int slot_id;
            public string side;
            public int floor;
            public int bay;
            public float[] center_source;
            public float[] size_m;
            public int uv2_slot_id;
        }

        [Serializable]
        private sealed class BuildingPart
        {
            public string object_name;
            public string role;
            public string surface_kind;
            public string uv_scheme;
            public float meters_per_tile;
            public int vertices;
            public int triangles;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public float[] uv0_min;
            public float[] uv0_max;
            public int[] uv2_slot_ids;
        }
    }
}
