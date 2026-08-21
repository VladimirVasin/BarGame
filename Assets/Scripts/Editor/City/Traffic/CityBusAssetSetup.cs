using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class CityBusAssetSetup
    {
        public const string ModelPath =
            "Assets/Vehicles/Models/CityBus3D.fbx";
        public const string ManifestPath =
            "Assets/Vehicles/Models/CityBus3D.json";
        public const string PrefabPath =
            "Assets/Resources/Vehicles/CityBus3D.prefab";
        public const string MaterialFolder =
            "Assets/Vehicles/Materials";
        public const string TextureFolder =
            "Assets/Vehicles/Textures";

        private const string ExpectedDesignId = "road_v2_midibus_v1";
        private const string ExpectedForwardAxis = "-Y";
        private const string ExpectedRuntimeForwardAxis = "+Z";
        private const float ExpectedLength = 8.25f;
        private const float ExpectedWidth = 2.38f;
        private const float ExpectedHeight = 2.95f;
        private const float ExpectedWheelbase = 4.50f;
        private const float ExpectedWheelRadius = 0.43f;
        private const float ExpectedDoorButtonTravel = 0.012f;
        private const int MinimumTriangleCount = 900;
        private const int MaximumTriangleCount = 12000;

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityBusAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Bus 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log($"City bus prefab rebuilt at '{PrefabPath}'.");
        }

        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("CITY BUS UNITY ASSET BUILD OK");
        }

        [MenuItem("Bar Promenade/City Bus 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("City bus imported asset and prefab contract are valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
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
                    "City bus build requires its generated FBX and manifest.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                EnsureFolderForAsset($"{MaterialFolder}/placeholder.mat");
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityBusManifest manifest = LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from '{ModelPath}'.");
                }

                Dictionary<CityBusMaterialSlot, Material> materials =
                    BuildMaterials();
                BuildPrefab(modelAsset, manifest, materials);
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
            CityBusManifest manifest = LoadAndValidateManifest();
            ValidateImportedModel(manifest);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City bus prefab is missing at '{PrefabPath}'.");
            }

            CityBusAssetRegistry registry =
                prefab.GetComponent<CityBusAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "City bus prefab has no CityBusAssetRegistry.");
            }

            ValidateRegistryBindings(registry, manifest);
            ValidateRegistryMetadata(registry, manifest);
            ValidatePrefabPresentation(prefab, registry, manifest);
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                CityBusManifest manifest = LoadAndValidateManifest();
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                CityBusAssetRegistry registry = prefab != null
                    ? prefab.GetComponent<CityBusAssetRegistry>()
                    : null;
                if (registry == null ||
                    !string.Equals(
                        registry.BuildSignature,
                        manifest.build_signature,
                        StringComparison.Ordinal))
                {
                    QueueBuildWhenSourcesExist();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate City bus source manifest: {exception}");
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
                Debug.LogError($"Could not build City bus prefab: {exception}");
            }
        }

        private static CityBusManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import City bus manifest '{ManifestPath}'.");
            }

            CityBusManifest manifest =
                JsonUtility.FromJson<CityBusManifest>(source.text);
            if (manifest == null ||
                manifest.dimensions_m == null ||
                manifest.parts == null ||
                manifest.pivots == null ||
                manifest.bounds_min == null ||
                manifest.bounds_max == null)
            {
                throw new InvalidOperationException(
                    "City bus manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.forward_axis,
                    ExpectedForwardAxis,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_runtime_forward_axis,
                    ExpectedRuntimeForwardAxis,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City bus design or source/runtime axis contract changed.");
            }

            AssertNear(manifest.dimensions_m.length, ExpectedLength, "length");
            AssertNear(manifest.dimensions_m.width, ExpectedWidth, "width");
            AssertNear(manifest.dimensions_m.height, ExpectedHeight, "height");
            AssertNear(
                manifest.dimensions_m.wheelbase,
                ExpectedWheelbase,
                "wheelbase");
            AssertNear(
                manifest.dimensions_m.wheel_radius,
                ExpectedWheelRadius,
                "wheel radius");

            if (!manifest.visible_interior ||
                manifest.colliders ||
                manifest.animation_count != 0 ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount ||
                manifest.bounds_min.Length != 3 ||
                manifest.bounds_max.Length != 3 ||
                manifest.passenger_seat_count < 10 ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City bus source count, interior, collider, animation, " +
                    "bounds or build-stamp contract is invalid.");
            }

            ValidateManifestParts(manifest);
            ValidateManifestPivots(manifest);
            return manifest;
        }

        private static void ValidateManifestParts(CityBusManifest manifest)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<CityBusMaterialSlot> slots =
                new HashSet<CityBusMaterialSlot>();
            int triangles = 0;
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityBusManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    string.IsNullOrWhiteSpace(part.parent) ||
                    !names.Add(part.name) ||
                    !Enum.TryParse(
                        part.material_slot,
                        false,
                        out CityBusMaterialSlot slot) ||
                    part.vertices <= 0 ||
                    part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        "City bus manifest contains an invalid mesh part.");
                }

                slots.Add(slot);
                triangles += part.triangles;
            }

            if (triangles != manifest.triangle_count ||
                slots.Count != Enum.GetValues(typeof(CityBusMaterialSlot)).Length)
            {
                throw new InvalidOperationException(
                    "City bus manifest triangle total or material coverage is invalid.");
            }
        }

        private static void ValidateManifestPivots(CityBusManifest manifest)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> roles = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, CityBusManifestPivot> pivotsByName =
                new Dictionary<string, CityBusManifestPivot>(
                    StringComparer.Ordinal);
            int passengerSeats = 0;
            for (int index = 0; index < manifest.pivots.Length; index++)
            {
                CityBusManifestPivot pivot = manifest.pivots[index];
                if (pivot == null ||
                    string.IsNullOrWhiteSpace(pivot.name) ||
                    string.IsNullOrWhiteSpace(pivot.role) ||
                    string.IsNullOrWhiteSpace(pivot.parent) ||
                    pivot.local_position == null ||
                    pivot.local_position.Length != 3 ||
                    pivot.local_rotation_degrees == null ||
                    pivot.local_rotation_degrees.Length != 3 ||
                    !names.Add(pivot.name))
                {
                    throw new InvalidOperationException(
                        "City bus manifest contains an invalid pivot.");
                }

                roles.Add(pivot.role);
                pivotsByName.Add(pivot.name, pivot);
                if (pivot.role == "passenger_seat_anchor")
                {
                    passengerSeats++;
                }
            }

            foreach (string required in RequiredPivotRoles)
            {
                if (!roles.Contains(required))
                {
                    throw new InvalidOperationException(
                        $"City bus manifest is missing pivot role '{required}'.");
                }
            }

            if (passengerSeats != manifest.passenger_seat_count)
            {
                throw new InvalidOperationException(
                    "City bus passenger seat anchor count is stale.");
            }

            CityBusManifestPivot steering = RequireManifestPivot(
                pivotsByName,
                "PIVOT_SteeringWheel",
                "steering_wheel",
                "ROOT_Body");
            AssertVectorNear(
                steering.local_position,
                new Vector3(0.60f, -3.32f, 1.57f),
                "steering-wheel source position");
            AssertVectorNear(
                steering.local_rotation_degrees,
                new Vector3(-90f, 0f, 0f),
                "steering-wheel source rotation");
            if (!string.Equals(
                    steering.runtime_axis_local,
                    "+Z",
                    StringComparison.Ordinal) ||
                Mathf.Abs(steering.travel_m) > 0.0001f)
            {
                throw new InvalidOperationException(
                    "City bus steering-wheel local-axis contract is invalid.");
            }

            float gripTop = Mathf.Sqrt(0.18f * 0.18f - 0.14f * 0.14f);
            CityBusManifestPivot leftGrip = RequireManifestPivot(
                pivotsByName,
                "ANCHOR_SteeringGrip.L",
                "left_steering_grip",
                "PIVOT_SteeringWheel");
            CityBusManifestPivot rightGrip = RequireManifestPivot(
                pivotsByName,
                "ANCHOR_SteeringGrip.R",
                "right_steering_grip",
                "PIVOT_SteeringWheel");
            AssertVectorNear(
                leftGrip.local_position,
                new Vector3(0.14f, -gripTop, 0.020f),
                "left steering-grip source position");
            AssertVectorNear(
                rightGrip.local_position,
                new Vector3(-0.14f, -gripTop, 0.020f),
                "right steering-grip source position");

            CityBusManifestPivot doorButton = RequireManifestPivot(
                pivotsByName,
                "PIVOT_DoorButton",
                "door_button",
                "ROOT_Body");
            AssertVectorNear(
                doorButton.local_position,
                new Vector3(0.30f, -3.335f, 1.50f),
                "door-button source position");
            AssertVectorNear(
                doorButton.local_rotation_degrees,
                new Vector3(0f, 0f, 180f),
                "door-button source rotation");
            if (!string.Equals(
                    doorButton.runtime_axis_local,
                    "+Y",
                    StringComparison.Ordinal) ||
                Mathf.Abs(doorButton.travel_m - ExpectedDoorButtonTravel) >
                    0.0001f)
            {
                throw new InvalidOperationException(
                    "City bus door-button local-travel contract is invalid.");
            }

            CityBusManifestPivot pressAnchor = RequireManifestPivot(
                pivotsByName,
                "ANCHOR_DoorButtonPress",
                "door_button_press",
                "PIVOT_DoorButton");
            AssertVectorNear(
                pressAnchor.local_position,
                new Vector3(0f, -0.0265f, 0f),
                "door-button press-anchor source position");

            CityBusManifestPivot doorLook = RequireManifestPivot(
                pivotsByName,
                "ANCHOR_DriverDoorLook",
                "driver_door_look",
                "ROOT_Body");
            AssertVectorNear(
                doorLook.local_position,
                new Vector3(-0.90f, -3.05f, 2.12f),
                "driver door-look source position");
        }

        private static Dictionary<CityBusMaterialSlot, Material> BuildMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP/Lit shader is unavailable for City bus materials.");
            }

            foreach (string texturePath in Enum
                .GetValues(typeof(CityBusMaterialSlot))
                .Cast<CityBusMaterialSlot>()
                .Select(TexturePath)
                .Where(path => path != null)
                .Distinct(StringComparer.Ordinal))
            {
                AssetDatabase.ImportAsset(
                    texturePath,
                    ImportAssetOptions.ForceSynchronousImport);
            }

            Dictionary<CityBusMaterialSlot, Material> result =
                new Dictionary<CityBusMaterialSlot, Material>();
            foreach (CityBusMaterialSlot slot in
                Enum.GetValues(typeof(CityBusMaterialSlot)))
            {
                string path = MaterialPath(slot);
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader)
                    {
                        name = $"CityBus{slot}"
                    };
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                }

                ConfigureMaterial(material, slot);
                EditorUtility.SetDirty(material);
                result.Add(slot, material);
            }

            return result;
        }

        private static void ConfigureMaterial(
            Material material,
            CityBusMaterialSlot slot)
        {
            Color color = GetMaterialColor(slot);
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetFloatIfPresent(material, "_Metallic", GetMetallic(slot));
            SetFloatIfPresent(material, "_Smoothness", GetSmoothness(slot));
            Texture2D albedo = LoadAlbedoTexture(slot);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
            }

            material.enableInstancing = true;

            bool transparent = slot == CityBusMaterialSlot.Glass;
            SetFloatIfPresent(material, "_Surface", transparent ? 1f : 0f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(
                material,
                "_SrcBlend",
                transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            SetFloatIfPresent(
                material,
                "_DstBlend",
                transparent
                    ? (float)BlendMode.OneMinusSrcAlpha
                    : (float)BlendMode.Zero);
            SetFloatIfPresent(material, "_ZWrite", transparent ? 0f : 1f);
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
                material.SetShaderPassEnabled("ShadowCaster", true);
            }

            float emission = GetEmissionStrength(slot);
            if (emission > 0f)
            {
                SetColorIfPresent(material, "_EmissionColor", color * emission);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                SetColorIfPresent(material, "_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            CityBusManifest manifest,
            IReadOnlyDictionary<CityBusMaterialSlot, Material> materials)
        {
            GameObject prefabRoot = new GameObject("CityBus3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported City bus model.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transformsByName =
                    IndexUniqueTransforms(model);
                List<Renderer> renderers =
                    new List<Renderer>(manifest.parts.Length);
                List<CityBusRendererBinding> bindings =
                    new List<CityBusRendererBinding>(manifest.parts.Length);
                List<Renderer> headlights = new List<Renderer>();
                List<Renderer> tailLights = new List<Renderer>();
                List<Renderer> cabinLights = new List<Renderer>();

                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    CityBusManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer) ||
                        !Enum.TryParse(
                            source.material_slot,
                            false,
                            out CityBusMaterialSlot slot))
                    {
                        throw new InvalidOperationException(
                            $"Imported City bus is missing '{source.name}'.");
                    }

                    renderer.sharedMaterials = new[] { materials[slot] };
                    renderer.shadowCastingMode = slot == CityBusMaterialSlot.Glass
                        ? ShadowCastingMode.Off
                        : ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    renderers.Add(renderer);
                    bindings.Add(
                        new CityBusRendererBinding(
                            source.name,
                            source.role,
                            slot,
                            renderer));
                    if (slot == CityBusMaterialSlot.Headlight)
                    {
                        headlights.Add(renderer);
                    }
                    else if (slot == CityBusMaterialSlot.TailLight)
                    {
                        tailLights.Add(renderer);
                    }
                    else if (slot == CityBusMaterialSlot.CabinLight ||
                        slot == CityBusMaterialSlot.Destination)
                    {
                        cabinLights.Add(renderer);
                    }
                }

                Transform[] passengerSeats = manifest.pivots
                    .Where(pivot => pivot.role == "passenger_seat_anchor")
                    .OrderBy(pivot => pivot.name, StringComparer.Ordinal)
                    .Select(pivot => RequireTransform(transformsByName, pivot.name))
                    .ToArray();
                Renderer[] rendererArray = renderers.ToArray();
                CityBusAssetRegistry registry =
                    prefabRoot.AddComponent<CityBusAssetRegistry>();
                registry.Configure(
                    model.transform,
                    RequireTransform(transformsByName, "ROOT_Body"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_FrontDoorForwardLeaf"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_FrontDoorRearwardLeaf"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_RearDoorForwardLeaf"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_RearDoorRearwardLeaf"),
                    RequireTransform(transformsByName, "PIVOT_WheelFLRoll"),
                    RequireTransform(transformsByName, "PIVOT_WheelFRRoll"),
                    RequireTransform(transformsByName, "PIVOT_WheelRLRoll"),
                    RequireTransform(transformsByName, "PIVOT_WheelRRRoll"),
                    RequireTransform(transformsByName, "PIVOT_WheelFLSteer"),
                    RequireTransform(transformsByName, "PIVOT_WheelFRSteer"),
                    RequireTransform(transformsByName, "ANCHOR_DriverSeat"),
                    RequireTransform(transformsByName, "ANCHOR_FrontDoorEntry"),
                    RequireTransform(transformsByName, "ANCHOR_RearDoorEntry"),
                    passengerSeats,
                    rendererArray,
                    bindings.ToArray(),
                    headlights.ToArray(),
                    tailLights.ToArray(),
                    cabinLights.ToArray(),
                    CalculateLocalBounds(prefabRoot.transform, rendererArray),
                    new CityBusDimensions(
                        manifest.dimensions_m.length,
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.height,
                        manifest.dimensions_m.wheelbase,
                        manifest.dimensions_m.wheel_radius),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature,
                    RequireTransform(
                        transformsByName,
                        "PIVOT_SteeringWheel"),
                    RequireTransform(
                        transformsByName,
                        "ANCHOR_SteeringGrip.L"),
                    RequireTransform(
                        transformsByName,
                        "ANCHOR_SteeringGrip.R"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_DoorButton"),
                    RequireTransform(
                        transformsByName,
                        "ANCHOR_DoorButtonPress"),
                    RequireTransform(
                        transformsByName,
                        "ANCHOR_DriverDoorLook"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_WiperL"),
                    RequireTransform(
                        transformsByName,
                        "PIVOT_WiperR"));
                registry.ResetArticulation();

                PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    PrefabPath,
                    out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException(
                        $"Could not save the city bus prefab at " +
                        $"'{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static void ValidateImportedModel(CityBusManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    "City bus FBX did not import as a GameObject.");
            }

            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count ||
                CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Imported City bus mesh/triangle totals differ from manifest.");
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation ||
                importer.globalScale != 1f ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy ||
                importer.optimizeGameObjects ||
                importer.importCameras ||
                importer.importLights ||
                importer.addCollider ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "City bus ModelImporter contract is invalid.");
            }

            Dictionary<string, Transform> transforms =
                IndexUniqueTransforms(model);
            for (int index = 0; index < manifest.pivots.Length; index++)
            {
                CityBusManifestPivot source = manifest.pivots[index];
                Transform pivot = RequireTransform(transforms, source.name);
                if (pivot.parent == null ||
                    !string.Equals(
                        pivot.parent.name,
                        source.parent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"City bus pivot '{source.name}' lost parent " +
                        $"'{source.parent}'.");
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityBusManifestPart source = manifest.parts[index];
                Transform part = RequireTransform(transforms, source.name);
                if (part.parent == null ||
                    !string.Equals(
                        part.parent.name,
                        source.parent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"City bus mesh '{source.name}' lost parent " +
                        $"'{source.parent}'.");
                }
            }

            if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Animator>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Imported presentation FBX must be collider, rig and light free.");
            }
        }

        private static void ValidateRegistryBindings(
            CityBusAssetRegistry registry,
            CityBusManifest manifest)
        {
            Transform[] required =
            {
                registry.ModelRoot,
                registry.Body,
                registry.FrontDoorForwardLeaf,
                registry.FrontDoorRearwardLeaf,
                registry.RearDoorForwardLeaf,
                registry.RearDoorRearwardLeaf,
                registry.FrontLeftWheel,
                registry.FrontRightWheel,
                registry.RearLeftWheel,
                registry.RearRightWheel,
                registry.FrontLeftSteeringPivot,
                registry.FrontRightSteeringPivot,
                registry.SteeringWheelPivot,
                registry.LeftSteeringGrip,
                registry.RightSteeringGrip,
                registry.DoorButtonPivot,
                registry.DoorButtonPressAnchor,
                registry.DriverDoorLookAnchor,
                registry.DriverSeatAnchor,
                registry.FrontDoorEntryAnchor,
                registry.RearDoorEntryAnchor,
                registry.LeftWiperPivot,
                registry.RightWiperPivot
            };
            if (required.Any(item => item == null) ||
                registry.PassengerSeatAnchors.Count !=
                    manifest.passenger_seat_count ||
                registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count ||
                registry.Headlights.Count == 0 ||
                registry.TailLights.Count == 0 ||
                registry.CabinLights.Count == 0)
            {
                throw new InvalidOperationException(
                    "City bus prefab is missing an articulation, seat, light " +
                    "or renderer binding.");
            }

            if (registry.FrontDoorForwardLeaf.parent != registry.Body ||
                registry.FrontDoorRearwardLeaf.parent != registry.Body ||
                registry.RearDoorForwardLeaf.parent != registry.Body ||
                registry.RearDoorRearwardLeaf.parent != registry.Body)
            {
                throw new InvalidOperationException(
                    "Door leaf pivots must remain direct children of the " +
                    "bus body.");
            }

            if (registry.FrontLeftWheel.parent !=
                    registry.FrontLeftSteeringPivot ||
                registry.FrontRightWheel.parent !=
                    registry.FrontRightSteeringPivot)
            {
                throw new InvalidOperationException(
                    "Front wheel roll pivots must remain under steering pivots.");
            }

            if (registry.SteeringWheelPivot.parent != registry.Body ||
                registry.LeftSteeringGrip.parent !=
                    registry.SteeringWheelPivot ||
                registry.RightSteeringGrip.parent !=
                    registry.SteeringWheelPivot ||
                registry.DoorButtonPivot.parent != registry.Body ||
                registry.DoorButtonPressAnchor.parent !=
                    registry.DoorButtonPivot ||
                registry.DriverDoorLookAnchor.parent != registry.Body ||
                registry.LeftWiperPivot.parent != registry.Body ||
                registry.RightWiperPivot.parent != registry.Body)
            {
                throw new InvalidOperationException(
                    "Driver control pivots and anchors lost their authored " +
                    "hierarchy.");
            }

            if (Vector3.Distance(
                    registry.SteeringWheelAxisLocal,
                    Vector3.forward) > 0.0001f)
            {
                throw new InvalidOperationException(
                    "Driver control local-axis bindings are stale.");
            }

            Vector3 steeringAxis = registry.SteeringWheelPivot
                .TransformDirection(registry.SteeringWheelAxisLocal)
                .normalized;
            Vector3 leftGripOffset =
                registry.LeftSteeringGrip.position -
                registry.SteeringWheelPivot.position;
            Vector3 rightGripOffset =
                registry.RightSteeringGrip.position -
                registry.SteeringWheelPivot.position;
            float leftGripRadius = (
                leftGripOffset - Vector3.Project(
                    leftGripOffset,
                    steeringAxis)).magnitude;
            float rightGripRadius = (
                rightGripOffset - Vector3.Project(
                    rightGripOffset,
                    steeringAxis)).magnitude;
            Vector3 buttonTravelWorld = registry.DoorButtonPivot.parent
                .TransformVector(registry.DoorButtonTravelLocal);
            Vector3 buttonFaceOffset =
                registry.DoorButtonPressAnchor.position -
                registry.DoorButtonPivot.position;
            float buttonTravelAlignment = buttonTravelWorld.sqrMagnitude >
                    0.000001f && buttonFaceOffset.sqrMagnitude > 0.000001f
                ? Vector3.Dot(
                    buttonTravelWorld.normalized,
                    -buttonFaceOffset.normalized)
                : -1f;
            if (Mathf.Abs(leftGripRadius - 0.18f) > 0.001f ||
                Mathf.Abs(rightGripRadius - 0.18f) > 0.001f ||
                Mathf.Abs(buttonFaceOffset.magnitude - 0.0265f) > 0.001f ||
                Mathf.Abs(
                    buttonTravelWorld.magnitude -
                    ExpectedDoorButtonTravel) > 0.0001f ||
                buttonTravelAlignment < 0.999f)
            {
                throw new InvalidOperationException(
                    "Driver hand-contact anchors lost the wheel rim or " +
                    $"button face (left radius {leftGripRadius:F6}, " +
                    $"right radius {rightGripRadius:F6}, button face " +
                    $"{buttonFaceOffset.magnitude:F6}, travel " +
                    $"{buttonTravelWorld.magnitude:F6}, alignment " +
                    $"{buttonTravelAlignment:F6}).");
            }

            CityBusRendererBinding steeringWheel = registry.RendererBindings
                .SingleOrDefault(binding =>
                    binding != null &&
                    string.Equals(
                        binding.SourceName,
                        "INT_SteeringWheel",
                        StringComparison.Ordinal));
            CityBusRendererBinding doorButton = registry.RendererBindings
                .SingleOrDefault(binding =>
                    binding != null &&
                    string.Equals(
                        binding.SourceName,
                        "INT_DoorButton",
                        StringComparison.Ordinal));
            if (steeringWheel?.Renderer == null ||
                steeringWheel.Renderer.transform.parent !=
                    registry.SteeringWheelPivot ||
                doorButton?.Renderer == null ||
                doorButton.Renderer.transform.parent !=
                    registry.DoorButtonPivot)
            {
                throw new InvalidOperationException(
                    "Visible steering-wheel or door-button mesh lost its pivot.");
            }
        }

        private static void ValidateRegistryMetadata(
            CityBusAssetRegistry registry,
            CityBusManifest manifest)
        {
            CityBusDimensions dimensions = registry.Dimensions;
            AssertNear(dimensions.Length, ExpectedLength, "prefab length");
            AssertNear(dimensions.Width, ExpectedWidth, "prefab width");
            AssertNear(dimensions.Height, ExpectedHeight, "prefab height");
            AssertNear(
                dimensions.Wheelbase,
                ExpectedWheelbase,
                "prefab wheelbase");
            AssertNear(
                dimensions.WheelRadius,
                ExpectedWheelRadius,
                "prefab wheel radius");
            if (registry.SourceTriangleCount != manifest.triangle_count ||
                !string.Equals(
                    registry.SourceGeneratorVersion,
                    manifest.generator_version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City bus prefab source metadata is stale.");
            }
        }

        private static void ValidatePrefabPresentation(
            GameObject prefab,
            CityBusAssetRegistry registry,
            CityBusManifest manifest)
        {
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "City bus art prefab must remain collider, rig and Light free.");
            }

            if (Quaternion.Angle(
                    registry.ModelRoot.localRotation,
                    Quaternion.Euler(0f, 180f, 0f)) > 0.01f)
            {
                throw new InvalidOperationException(
                    "City bus model no longer faces runtime local +Z.");
            }

            Bounds bounds = registry.LocalBounds;
            if (Mathf.Abs(bounds.min.y) > 0.03f ||
                Mathf.Abs(bounds.size.y - ExpectedHeight) > 0.04f ||
                Mathf.Abs(bounds.size.z - ExpectedLength) > 0.16f ||
                bounds.size.x < ExpectedWidth ||
                bounds.size.x > ExpectedWidth + 0.40f)
            {
                throw new InvalidOperationException(
                    $"City bus prefab bounds {bounds} lost meter scale/grounding.");
            }

            for (int index = 0; index < registry.RendererBindings.Count; index++)
            {
                CityBusRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    string.IsNullOrWhiteSpace(binding.SourceName) ||
                    string.IsNullOrWhiteSpace(binding.Role) ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial !=
                        AssetDatabase.LoadAssetAtPath<Material>(
                            MaterialPath(binding.MaterialSlot)))
                {
                    throw new InvalidOperationException(
                        "City bus renderer/material registry binding is invalid.");
                }

                string texturePath = TexturePath(binding.MaterialSlot);
                if (texturePath != null &&
                    binding.Renderer.sharedMaterial.GetTexture("_BaseMap") !=
                        AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath))
                {
                    throw new InvalidOperationException(
                        $"City bus material '{binding.MaterialSlot}' lost " +
                        $"its albedo '{texturePath}'.");
                }
            }
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject root)
        {
            Dictionary<string, Renderer> result =
                new Dictionary<string, Renderer>(StringComparer.Ordinal);
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"City bus contains duplicate renderer '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            GameObject root)
        {
            Dictionary<string, Transform> result =
                new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"City bus contains duplicate transform '{transform.name}'.");
                }
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name)
        {
            if (!transforms.TryGetValue(name, out Transform result) ||
                result == null)
            {
                throw new InvalidOperationException(
                    $"City bus hierarchy is missing transform '{name}'.");
            }

            return result;
        }

        private static int CountTriangles(IEnumerable<Renderer> renderers)
        {
            int total = 0;
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"City bus renderer '{renderer.name}' has no mesh.");
                }

                for (int subMesh = 0;
                     subMesh < filter.sharedMesh.subMeshCount;
                     subMesh++)
                {
                    total += (int)filter.sharedMesh.GetIndexCount(subMesh) / 3;
                }
            }

            return total;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool initialized = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 center = world.center;
                Vector3 extents = world.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = root.InverseTransformPoint(
                                center + Vector3.Scale(
                                    extents,
                                    new Vector3(x, y, z)));
                            if (!initialized)
                            {
                                result = new Bounds(point, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            if (!initialized)
            {
                throw new InvalidOperationException(
                    "City bus has no renderers for bounds calculation.");
            }

            return result;
        }

        private static string MaterialPath(CityBusMaterialSlot slot)
        {
            return $"{MaterialFolder}/CityBus{slot}.mat";
        }

        private static string TexturePath(CityBusMaterialSlot slot)
        {
            switch (slot)
            {
                case CityBusMaterialSlot.Body:
                case CityBusMaterialSlot.Accent:
                    return $"{TextureFolder}/CityBusPaintAlbedo.png";
                case CityBusMaterialSlot.Metal:
                case CityBusMaterialSlot.Rail:
                    return $"{TextureFolder}/CityBusMetalAlbedo.png";
                case CityBusMaterialSlot.Interior:
                case CityBusMaterialSlot.Dashboard:
                    return $"{TextureFolder}/CityBusInteriorAlbedo.png";
                case CityBusMaterialSlot.Seat:
                    return $"{TextureFolder}/CityBusSeatAlbedo.png";
                default:
                    return null;
            }
        }

        private static Texture2D LoadAlbedoTexture(CityBusMaterialSlot slot)
        {
            string path = TexturePath(slot);
            if (path == null)
            {
                return null;
            }

            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"City bus albedo '{path}' is missing; run " +
                    "tools/build-city-bus-textures.py.");
            }

            return texture;
        }

        private static Color GetMaterialColor(CityBusMaterialSlot slot)
        {
            switch (slot)
            {
                case CityBusMaterialSlot.Body:
                    return Hex("#6E3035FF");
                case CityBusMaterialSlot.Accent:
                    return Hex("#A56D25FF");
                case CityBusMaterialSlot.Trim:
                    return Hex("#121716FF");
                case CityBusMaterialSlot.Rubber:
                    return Hex("#070908FF");
                case CityBusMaterialSlot.Metal:
                    return Hex("#606B68FF");
                case CityBusMaterialSlot.Glass:
                    return Hex("#3160645C");
                case CityBusMaterialSlot.Interior:
                    return Hex("#303532FF");
                case CityBusMaterialSlot.Seat:
                    return Hex("#24554AFF");
                case CityBusMaterialSlot.Rail:
                    return Hex("#C27A19FF");
                case CityBusMaterialSlot.Dashboard:
                    return Hex("#171C1AFF");
                case CityBusMaterialSlot.Headlight:
                    return Hex("#DDECB8FF");
                case CityBusMaterialSlot.TailLight:
                    return Hex("#B5140CFF");
                case CityBusMaterialSlot.CabinLight:
                    return Hex("#EDA357FF");
                case CityBusMaterialSlot.Destination:
                    return Hex("#F06D13FF");
                default:
                    return Color.magenta;
            }
        }

        private static float GetMetallic(CityBusMaterialSlot slot)
        {
            switch (slot)
            {
                case CityBusMaterialSlot.Metal:
                    return 0.58f;
                case CityBusMaterialSlot.Rail:
                    return 0.31f;
                default:
                    return 0.03f;
            }
        }

        private static float GetSmoothness(CityBusMaterialSlot slot)
        {
            switch (slot)
            {
                case CityBusMaterialSlot.Glass:
                    return 0.78f;
                case CityBusMaterialSlot.Metal:
                case CityBusMaterialSlot.Rail:
                    return 0.65f;
                case CityBusMaterialSlot.Headlight:
                case CityBusMaterialSlot.TailLight:
                    return 0.72f;
                case CityBusMaterialSlot.Rubber:
                    return 0.08f;
                default:
                    return 0.34f;
            }
        }

        private static float GetEmissionStrength(CityBusMaterialSlot slot)
        {
            switch (slot)
            {
                case CityBusMaterialSlot.Headlight:
                    return 4.5f;
                case CityBusMaterialSlot.TailLight:
                    return 3.2f;
                case CityBusMaterialSlot.CabinLight:
                    return 2.1f;
                case CityBusMaterialSlot.Destination:
                    return 3.6f;
                default:
                    return 0f;
            }
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
            {
                throw new InvalidOperationException(
                    $"Invalid City bus material color '{value}'.");
            }

            return color;
        }

        private static void SetColorIfPresent(
            Material material,
            string propertyName,
            Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void AssertNear(
            float actual,
            float expected,
            string label)
        {
            if (Mathf.Abs(actual - expected) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"City bus {label} is {actual}, expected {expected}.");
            }
        }

        private static CityBusManifestPivot RequireManifestPivot(
            IReadOnlyDictionary<string, CityBusManifestPivot> pivots,
            string name,
            string role,
            string parent)
        {
            if (!pivots.TryGetValue(name, out CityBusManifestPivot pivot) ||
                pivot == null ||
                !string.Equals(pivot.role, role, StringComparison.Ordinal) ||
                !string.Equals(
                    pivot.parent,
                    parent,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"City bus pivot '{name}' lost role '{role}' or " +
                    $"parent '{parent}'.");
            }

            return pivot;
        }

        private static void AssertVectorNear(
            float[] actual,
            Vector3 expected,
            string label)
        {
            if (actual == null ||
                actual.Length != 3 ||
                Mathf.Abs(actual[0] - expected.x) > 0.0001f ||
                Mathf.Abs(actual[1] - expected.y) > 0.0001f ||
                Mathf.Abs(actual[2] - expected.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"City bus {label} is invalid.");
            }
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

        private static readonly string[] RequiredPivotRoles =
        {
            "front_door_forward_leaf",
            "front_door_rearward_leaf",
            "rear_door_forward_leaf",
            "rear_door_rearward_leaf",
            "front_left_steering",
            "front_right_steering",
            "front_left_wheel",
            "front_right_wheel",
            "rear_left_wheel",
            "rear_right_wheel",
            "steering_wheel",
            "left_steering_grip",
            "right_steering_grip",
            "door_button",
            "door_button_press",
            "driver_door_look",
            "driver_seat_anchor",
            "front_door_entry",
            "rear_door_entry",
            "passenger_seat_anchor",
            "left_wiper",
            "right_wiper"
        };

        [Serializable]
        private sealed class CityBusManifest
        {
            public string generator_version;
            public string design_id;
            public string forward_axis;
            public string unity_runtime_forward_axis;
            public CityBusManifestDimensions dimensions_m;
            public int mesh_count;
            public int triangle_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public bool colliders;
            public int animation_count;
            public bool visible_interior;
            public int passenger_seat_count;
            public string build_signature;
            public CityBusManifestPart[] parts;
            public CityBusManifestPivot[] pivots;
        }

        [Serializable]
        private sealed class CityBusManifestDimensions
        {
            public float length;
            public float width;
            public float height;
            public float wheelbase;
            public float wheel_radius;
        }

        [Serializable]
        private sealed class CityBusManifestPart
        {
            public string name;
            public string role;
            public string material_slot;
            public string parent;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class CityBusManifestPivot
        {
            public string name;
            public string role;
            public string parent;
            public float[] local_position;
            public float[] local_rotation_degrees;
            public string runtime_axis_local;
            public float travel_m;
        }
    }
}
