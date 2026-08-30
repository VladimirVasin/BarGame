using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the deterministic cafe FBX and builds its passive Resources
    /// prefab. The registry preserves authored sockets and pure collider
    /// descriptors without adding physical or causal components to the asset.
    /// </summary>
    [InitializeOnLoad]
    public static class MountainRoadCafeAssetSetup
    {
        public const string ModelPath =
            "Assets/MountainRoad/Cafe/Models/MountainRoadCafe3D.fbx";
        public const string ManifestPath =
            "Assets/MountainRoad/Cafe/Models/MountainRoadCafe3D.json";
        public const string PrefabPath =
            "Assets/Resources/MountainRoad/Cafe/MountainRoadCafe3D.prefab";
        public const string PreviewPath =
            "ArtSource/MountainRoad/Cafe/Preview/MountainRoadCafe3D.png";
        public const string BlendPath =
            "ArtSource/MountainRoad/Cafe/Blender/MountainRoadCafe3D.blend";

        public const string ExteriorTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafeExteriorDetail.png";
        public const string InteriorTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafeInteriorDetail.png";
        public const string CounterTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafeCounterDetail.png";
        public const string MetalTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafeMetalDetail.png";
        public const string PropsTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafePropsDetail.png";
        public const string GlassTexturePath =
            "Assets/Resources/MountainRoad/Cafe/Textures/" +
            "MountainRoadCafeGlassDetail.png";

        private const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        private const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";
        private const string ExpectedDesignId =
            "mountain_road_cafe_nighthawks_v1";
        private const int ExpectedMeshCount = 51;
        private const int ExpectedStoolCount = 7;
        private const int ExpectedCupCount = 3;
        private const int ExpectedColliderCount = 17;
        private const int MaximumTriangles = 45000;
        private const int MaximumRenderers = 90;
        private const float MeasureTolerance = 0.035f;

        private static readonly string[] TexturePaths =
        {
            ExteriorTexturePath,
            InteriorTexturePath,
            CounterTexturePath,
            MetalTexturePath,
            PropsTexturePath,
            GlassTexturePath,
        };

        private static readonly string[] RequiredAnchorNames =
        {
            "Origin",
            "DoorThreshold",
            "DoorApproach",
            "InteriorCenter",
            "CanonicalCameraTarget",
            "GlassCorner",
            "CounterStart",
            "CounterCorner",
            "CounterEnd",
            "HeroSeat",
            "Cast.Lone",
            "Cast.PairMan",
            "Cast.PairWoman",
            "Cast.Attendant",
            "Cup.Lone",
            "Cup.PairMan",
            "Cup.PairWoman",
            "PourTarget.Lone",
            "PourTarget.PairMan",
            "PourTarget.PairWoman",
            "Grip.Lone",
            "Grip.PairMan",
            "Grip.PairWoman",
            "PotDock",
            "PotSpout",
            "WipePatch.00",
            "WipePatch.01",
            "WipePatch.02",
            "ServiceRail.00",
            "ServiceRail.01",
            "ServiceRail.02",
            "ServiceRail.03",
            "Light.WarmCounter",
            "Light.ColdService",
            "Light.ExteriorWash",
            "Audio.Fridge",
            "Audio.Fixture",
            "Audio.Boiler",
        };

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static MountainRoadCafeAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem("Bar Promenade/Mountain Road/Build Cafe Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Bar Promenade/Mountain Road/Validate Cafe Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Mountain Road cafe authored model contract is valid.");
        }

        public static void RunBatch()
        {
            try
            {
                BuildOrThrow();
                MountainRoadCafeCastAssetSetup.BuildOrThrow();
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

        public static bool IsTexturePath(string path)
        {
            return TexturePaths.Any(candidate => PathsEqual(path, candidate));
        }

        public static bool IsGlassTexturePath(string path)
        {
            return PathsEqual(path, GlassTexturePath);
        }

        public static bool IsClampTexturePath(string path)
        {
            return PathsEqual(path, PropsTexturePath) ||
                PathsEqual(path, GlassTexturePath);
        }

        public static bool IsSourcePath(string path)
        {
            return IsModelPath(path) ||
                PathsEqual(path, ManifestPath) ||
                IsTexturePath(path);
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(PreviewPath) &&
                File.Exists(BlendPath) &&
                TexturePaths.All(File.Exists);
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
                    "Mountain Road cafe sources are incomplete. Run " +
                    "tools/build-mountain-road-cafe-3d-model.py through " +
                    "Blender first and retain its blend, FBX, manifest, " +
                    "preview and six textures.");
            }

            IsBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                ImportSources();
                CafeManifest manifest = LoadAndValidateManifest();
                ValidateModelImporter();
                ValidateTextureImporters(manifest);
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
            CafeManifest manifest = LoadAndValidateManifest();
            ValidateModelImporter();
            ValidateTextureImporters(manifest);
            ValidatePrefab(manifest);
            ValidateReviewArtifacts();
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (IsBuilding || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !SourcesExist())
            {
                QueueBuildWhenSourcesExist();
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

        private static void ImportSources()
        {
            foreach (string path in TexturePaths)
            {
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static void BuildPrefab(CafeManifest manifest)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}'.");
            }

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe shared materials failed to load.");
            }

            var root = new GameObject("MountainRoadCafe3D");
            try
            {
                var model = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{ModelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderers = IndexRenderers(model);
                Dictionary<string, Transform> transforms = IndexTransforms(model);
                EnsureExactRendererSet(manifest, renderers);

                var partBindings = new List<MountainRoadCafePartBinding>();
                foreach (CafePart part in manifest.parts)
                {
                    Renderer renderer = renderers[part.name];
                    renderer.sharedMaterial = part.emissive
                        ? sharedEmission
                        : sharedLit;
                    renderer.shadowCastingMode = part.shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = part.shadows;
                    renderer.enabled = part.initially_visible;
                    partBindings.Add(new MountainRoadCafePartBinding(
                        part.name,
                        part.role,
                        part.group,
                        part.sheet,
                        part.base_surface,
                        part.emissive,
                        part.shadows,
                        part.initially_visible,
                        renderer));
                }

                var anchorBindings = new List<MountainRoadCafeAnchorBinding>();
                foreach (CafeAnchor anchor in manifest.anchors)
                {
                    Transform transform = RequireTransform(
                        transforms,
                        $"ANCHOR_{anchor.name}");
                    AssertAnchorPosition(root.transform, transform, anchor);
                    anchorBindings.Add(new MountainRoadCafeAnchorBinding(
                        anchor.name,
                        anchor.role,
                        ToVector(anchor.unity_local_forward),
                        ToVector(anchor.unity_local_up),
                        transform));
                }

                var propBindings = new List<MountainRoadCafeDynamicPropBinding>();
                foreach (CafeDynamicProp prop in manifest.dynamic_props)
                {
                    Transform propRoot = RequireTransform(transforms, prop.root_name);
                    Transform liftRoot = string.IsNullOrEmpty(prop.lift_root_name)
                        ? null
                        : RequireTransform(transforms, prop.lift_root_name);
                    Renderer[] propRenderers = prop.part_names
                        .Select(name => renderers[name])
                        .ToArray();
                    Renderer liquidRenderer = string.IsNullOrEmpty(prop.liquid_part)
                        ? null
                        : renderers[prop.liquid_part];
                    Transform liquidTransform = liquidRenderer != null
                        ? liquidRenderer.transform
                        : null;
                    Transform grip = TryResolveAnchorTransform(
                        transforms,
                        $"Grip.{prop.owner}");
                    Transform pourTarget = TryResolveAnchorTransform(
                        transforms,
                        $"PourTarget.{prop.owner}");

                    Vector3 emptyLocalPosition = Vector3.zero;
                    Vector3 fullLocalPosition = Vector3.zero;
                    if (liquidTransform != null)
                    {
                        emptyLocalPosition = ConvertRootHeightToParentLocalPosition(
                            root.transform,
                            liquidTransform,
                            prop.empty_local_y);
                        fullLocalPosition = ConvertRootHeightToParentLocalPosition(
                            root.transform,
                            liquidTransform,
                            prop.full_local_y);
                        liquidTransform.localPosition = fullLocalPosition;
                    }

                    propBindings.Add(new MountainRoadCafeDynamicPropBinding(
                        prop.name,
                        prop.role,
                        prop.owner,
                        propRoot,
                        liftRoot,
                        grip,
                        pourTarget,
                        liquidTransform,
                        liquidRenderer,
                        propRenderers,
                        emptyLocalPosition,
                        fullLocalPosition));
                }

                MountainRoadCafeColliderDescriptor[] colliderBindings =
                    manifest.collider_descriptors
                        .Select(CreateColliderDescriptor)
                        .ToArray();
                IEnumerable<Renderer> staticRenderers = manifest.parts
                    .Where(part => !string.Equals(
                        part.group,
                        "dynamic_prop",
                        StringComparison.Ordinal))
                    .Select(part => renderers[part.name]);
                Bounds measured = CalculateLocalBounds(
                    root.transform,
                    staticRenderers);
                AssertBoundsMatchManifest(measured, manifest);

                var registry = root.AddComponent<MountainRoadCafeAssetRegistry>();
                registry.Configure(
                    ResolveAuthoringRoot(model.transform),
                    anchorBindings
                        .OrderBy(binding => binding.AnchorName, StringComparer.Ordinal)
                        .ToArray(),
                    partBindings
                        .OrderBy(binding => binding.SourceName, StringComparer.Ordinal)
                        .ToArray(),
                    propBindings
                        .OrderBy(binding => binding.PropName, StringComparer.Ordinal)
                        .ToArray(),
                    colliderBindings,
                    measured,
                    new MountainRoadCafeDimensions(
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

        private static void ValidatePrefab(CafeManifest manifest)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"The Mountain Road cafe prefab is missing at '{PrefabPath}'.");
            }

            MountainRoadCafeAssetRegistry registry =
                prefab.GetComponent<MountainRoadCafeAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The Mountain Road cafe prefab has no asset registry.");
            }

            var problems = new List<string>();
            if (!string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                problems.Add("registry build signature differs from manifest");
            }

            if (registry.Parts.Count != manifest.parts.Length ||
                registry.Anchors.Count != manifest.anchors.Length ||
                registry.Props.Count != manifest.dynamic_props.Length ||
                registry.Colliders.Count != ExpectedColliderCount)
            {
                problems.Add("registry collection counts differ from manifest");
            }

            if (registry.SourceTriangleCount != manifest.triangle_count)
            {
                problems.Add("registry triangle count differs from manifest");
            }

            AppendForbidden<Collider>(prefab, problems, "collider");
            AppendForbidden<Light>(prefab, problems, "light");
            AppendForbidden<Camera>(prefab, problems, "camera");
            AppendForbidden<AudioSource>(prefab, problems, "audio source");
            AppendForbidden<AudioListener>(prefab, problems, "audio listener");
            AppendForbidden<Rigidbody>(prefab, problems, "rigidbody");
            AppendForbidden<Animator>(prefab, problems, "animator");

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != ExpectedMeshCount ||
                renderers.Length > MaximumRenderers)
            {
                problems.Add(
                    $"prefab has {renderers.Length} renderers, expected " +
                    $"{ExpectedMeshCount} within cap {MaximumRenderers}");
            }

            foreach (string anchorName in RequiredAnchorNames)
            {
                if (!registry.TryGetAnchor(anchorName, out Transform anchor) ||
                    anchor == null)
                {
                    problems.Add($"registry cannot resolve anchor '{anchorName}'");
                }
            }

            foreach (string owner in new[] { "Lone", "PairMan", "PairWoman" })
            {
                if (!registry.TryGetProp(
                        $"Cup.{owner}",
                        out MountainRoadCafeDynamicPropBinding cup) ||
                    cup.LiftRoot == null ||
                    cup.GripAnchor == null ||
                    cup.PourTarget == null ||
                    cup.LiquidTransform == null ||
                    cup.LiquidRenderer == null ||
                    !HasUpwardFillTravel(registry.transform, cup))
                {
                    problems.Add($"cup prop '{owner}' has an incomplete lift/fill binding");
                }
            }

            foreach (MountainRoadCafePartBinding part in registry.Parts)
            {
                if (part == null || part.Renderer == null)
                {
                    problems.Add("registry contains a null part binding");
                    continue;
                }

                if (part.Renderer.enabled != part.InitiallyVisible)
                {
                    problems.Add(
                        $"part '{part.SourceName}' visibility differs from manifest");
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe prefab validation failed: " +
                    string.Join("; ", problems));
            }
        }

        private static CafeManifest LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Could not load Mountain Road cafe manifest '{ManifestPath}'.");
            }

            CafeManifest manifest = JsonUtility.FromJson<CafeManifest>(
                File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.dimensions_m == null ||
                manifest.door_opening_m == null || manifest.parts == null ||
                manifest.anchors == null || manifest.dynamic_props == null ||
                manifest.collider_descriptors == null || manifest.textures == null)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                !IsSha256(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe manifest identity is invalid.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras ||
                manifest.materials || manifest.animation_count != 0 ||
                manifest.mesh_count != ExpectedMeshCount ||
                manifest.parts.Length != ExpectedMeshCount ||
                manifest.stool_count != ExpectedStoolCount ||
                manifest.cup_assembly_count != ExpectedCupCount ||
                manifest.collider_descriptors.Length != ExpectedColliderCount ||
                manifest.overlap_count != 0 || manifest.textures.Length != 6 ||
                manifest.triangle_count <= 0 ||
                manifest.triangle_count > MaximumTriangles)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe manifest passive/budget contract drifted.");
            }

            AssertNear(manifest.dimensions_m.width, 9.8f, "width");
            AssertNear(manifest.dimensions_m.depth, 10f, "depth");
            AssertNear(manifest.dimensions_m.height, 4.4f, "height");
            AssertNear(manifest.door_opening_m.width, 1.6f, "door width");
            AssertNear(manifest.door_opening_m.height, 2.28f, "door height");
            ValidateManifestNames(manifest);
            ValidateTextureFiles(manifest);
            ValidateDynamicProps(manifest);
            ValidateColliderDescriptors(manifest);
            return manifest;
        }

        private static void ValidateManifestNames(CafeManifest manifest)
        {
            string[] partNames = manifest.parts.Select(part => part.name).ToArray();
            if (partNames.Any(string.IsNullOrWhiteSpace) ||
                partNames.Distinct(StringComparer.Ordinal).Count() != partNames.Length)
            {
                throw new InvalidOperationException(
                    "Cafe manifest part names are empty or duplicated.");
            }

            var anchorNames = new HashSet<string>(
                manifest.anchors.Select(anchor => anchor.name),
                StringComparer.Ordinal);
            if (anchorNames.Count != manifest.anchors.Length ||
                RequiredAnchorNames.Any(name => !anchorNames.Contains(name)))
            {
                throw new InvalidOperationException(
                    "Cafe manifest anchor set is incomplete or duplicated.");
            }

            foreach (CafePart part in manifest.parts)
            {
                if (string.IsNullOrWhiteSpace(part.role) ||
                    string.IsNullOrWhiteSpace(part.group) ||
                    string.IsNullOrWhiteSpace(part.sheet) ||
                    string.IsNullOrWhiteSpace(part.base_surface))
                {
                    throw new InvalidOperationException(
                        $"Cafe manifest part '{part.name}' lacks semantic metadata.");
                }
            }
        }

        private static void ValidateTextureFiles(CafeManifest manifest)
        {
            var expectedPaths = new HashSet<string>(
                TexturePaths.Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);
            foreach (CafeTexture texture in manifest.textures)
            {
                string path = NormalizePath(texture.file);
                if (!expectedPaths.Remove(path) || texture.width != 512 ||
                    texture.height != 512 || !IsSha256(texture.sha256) ||
                    !File.Exists(path) ||
                    !string.Equals(
                        ComputeSha256(path),
                        texture.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cafe texture contract failed for '{texture.file}'.");
                }
            }

            if (expectedPaths.Count != 0)
            {
                throw new InvalidOperationException(
                    "Cafe manifest does not cover all six detail textures.");
            }
        }

        private static void ValidateDynamicProps(CafeManifest manifest)
        {
            var propNames = new HashSet<string>(
                manifest.dynamic_props.Select(prop => prop.name),
                StringComparer.Ordinal);
            foreach (string required in new[]
            {
                "Cup.Lone", "Cup.PairMan", "Cup.PairWoman",
                "PourStream", "ServicePot", "ServiceTowel"
            })
            {
                if (!propNames.Contains(required))
                {
                    throw new InvalidOperationException(
                        $"Cafe manifest lacks dynamic prop '{required}'.");
                }
            }

            foreach (CafeDynamicProp prop in manifest.dynamic_props)
            {
                bool cup = prop.name.StartsWith("Cup.", StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(prop.root_name) ||
                    prop.part_names == null || prop.part_names.Length == 0 ||
                    (cup && (string.IsNullOrWhiteSpace(prop.lift_root_name) ||
                             string.IsNullOrWhiteSpace(prop.liquid_part) ||
                             !(prop.empty_local_y < prop.full_local_y))))
                {
                    throw new InvalidOperationException(
                        $"Cafe dynamic prop '{prop.name}' is malformed.");
                }
            }
        }

        private static void ValidateColliderDescriptors(CafeManifest manifest)
        {
            int boxes = 0;
            int capsules = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CafeCollider descriptor in manifest.collider_descriptors)
            {
                if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.id) ||
                    !ids.Add(descriptor.id) || !IsFiniteVector(descriptor.center))
                {
                    throw new InvalidOperationException(
                        "Cafe collider descriptor is invalid or duplicated.");
                }

                if (string.Equals(descriptor.shape, "box", StringComparison.Ordinal))
                {
                    boxes++;
                    if (!IsPositiveVector(descriptor.size))
                    {
                        throw new InvalidOperationException(
                            $"Cafe box '{descriptor.id}' has invalid size.");
                    }
                }
                else if (string.Equals(
                             descriptor.shape,
                             "capsule",
                             StringComparison.Ordinal))
                {
                    capsules++;
                    if (descriptor.radius <= 0f ||
                        descriptor.height < descriptor.radius * 2f)
                    {
                        throw new InvalidOperationException(
                            $"Cafe capsule '{descriptor.id}' has invalid measures.");
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Cafe collider '{descriptor.id}' has unknown shape.");
                }
            }

            if (boxes != 10 || capsules != 7)
            {
                throw new InvalidOperationException(
                    $"Cafe collider recipe has {boxes} boxes and {capsules} " +
                    "capsules; expected 10 and 7.");
            }
        }

        private static MountainRoadCafeColliderDescriptor CreateColliderDescriptor(
            CafeCollider source)
        {
            bool capsule = string.Equals(
                source.shape,
                "capsule",
                StringComparison.Ordinal);
            return new MountainRoadCafeColliderDescriptor(
                source.id,
                capsule
                    ? MountainRoadCafeColliderShape.Capsule
                    : MountainRoadCafeColliderShape.Box,
                ToVector(source.center),
                source.size != null && source.size.Length == 3
                    ? ToVector(source.size)
                    : Vector3.zero,
                source.yaw,
                source.radius,
                source.height);
        }

        private static void ValidateModelImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation || importer.globalScale != 1f ||
                !importer.bakeAxisConversion || !importer.preserveHierarchy ||
                importer.optimizeGameObjects || importer.importCameras ||
                importer.importLights || importer.addCollider ||
                importer.importBlendShapes ||
                importer.importNormals != ModelImporterNormals.Import ||
                importer.importTangents != ModelImporterTangents.CalculateMikk ||
                importer.meshCompression != ModelImporterMeshCompression.Off ||
                importer.isReadable || !importer.weldVertices ||
                importer.keepQuads || importer.generateSecondaryUV ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe model importer contract drifted.");
            }
        }

        private static void ValidateTextureImporters(CafeManifest manifest)
        {
            foreach (string path in TexturePaths)
            {
                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                bool glass = IsGlassTexturePath(path);
                TextureWrapMode expectedWrap = IsClampTexturePath(path)
                    ? TextureWrapMode.Clamp
                    : TextureWrapMode.Repeat;
                if (importer == null ||
                    importer.textureType != TextureImporterType.Default ||
                    importer.textureShape != TextureImporterShape.Texture2D ||
                    !importer.sRGBTexture ||
                    importer.alphaSource != (glass
                        ? TextureImporterAlphaSource.FromInput
                        : TextureImporterAlphaSource.None) ||
                    importer.alphaIsTransparency != glass ||
                    !importer.mipmapEnabled || importer.streamingMipmaps ||
                    importer.isReadable ||
                    importer.npotScale != TextureImporterNPOTScale.None ||
                    importer.wrapMode != expectedWrap ||
                    importer.filterMode != FilterMode.Bilinear ||
                    importer.anisoLevel != 4 ||
                    importer.textureCompression !=
                        TextureImporterCompression.Uncompressed ||
                    importer.maxTextureSize != 512)
                {
                    throw new InvalidOperationException(
                        $"Cafe texture importer contract drifted for '{path}'.");
                }
            }
        }

        private static Dictionary<string, Renderer> IndexRenderers(GameObject model)
        {
            var result = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"Cafe model repeats renderer name '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexTransforms(GameObject model)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"Cafe model repeats transform name '{transform.name}'.");
                }
            }

            return result;
        }

        private static void EnsureExactRendererSet(
            CafeManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            var expected = new HashSet<string>(
                manifest.parts.Select(part => part.name),
                StringComparer.Ordinal);
            if (renderers.Count != expected.Count ||
                renderers.Keys.Any(name => !expected.Contains(name)))
            {
                throw new InvalidOperationException(
                    "Cafe FBX renderer set differs from its manifest.");
            }
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name)
        {
            if (!transforms.TryGetValue(name, out Transform transform))
            {
                throw new InvalidOperationException(
                    $"Cafe FBX lacks transform '{name}'.");
            }

            return transform;
        }

        private static Transform TryResolveAnchorTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string anchorName)
        {
            transforms.TryGetValue($"ANCHOR_{anchorName}", out Transform transform);
            return transform;
        }

        private static Transform ResolveAuthoringRoot(Transform model)
        {
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(
                        transform.name,
                        "ROOT_MountainRoadCafe3D",
                        StringComparison.Ordinal))
                {
                    return transform;
                }
            }

            throw new InvalidOperationException(
                "Cafe FBX lacks ROOT_MountainRoadCafe3D.");
        }

        private static void AssertAnchorPosition(
            Transform prefabRoot,
            Transform anchorTransform,
            CafeAnchor anchor)
        {
            Vector3 expectedMeters = ToVector(anchor.unity_local_position);
            bool parentLocal = anchor.name.StartsWith("Grip.", StringComparison.Ordinal) ||
                string.Equals(anchor.name, "PotSpout", StringComparison.Ordinal);
            Vector3 expectedWorld = parentLocal
                ? anchorTransform.parent.position +
                  prefabRoot.TransformVector(expectedMeters)
                : prefabRoot.TransformPoint(expectedMeters);
            if (Vector3.Distance(anchorTransform.position, expectedWorld) >
                MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Cafe anchor '{anchor.name}' imported at " +
                    $"{anchorTransform.position}, expected {expectedWorld}.");
            }
        }

        private static Vector3 ConvertRootHeightToParentLocalPosition(
            Transform prefabRoot,
            Transform liquidTransform,
            float authoredMeters)
        {
            Transform parent = liquidTransform != null
                ? liquidTransform.parent
                : null;
            if (parent == null)
            {
                throw new InvalidOperationException(
                    "Cafe liquid mesh has no parent transform.");
            }

            Vector3 rootLocalPosition = prefabRoot.InverseTransformPoint(
                liquidTransform.position);
            rootLocalPosition.y = authoredMeters;
            return parent.InverseTransformPoint(
                prefabRoot.TransformPoint(rootLocalPosition));
        }

        private static bool HasUpwardFillTravel(
            Transform prefabRoot,
            MountainRoadCafeDynamicPropBinding cup)
        {
            if (cup == null || cup.LiquidTransform == null ||
                cup.LiquidTransform.parent == null)
            {
                return false;
            }

            Transform parent = cup.LiquidTransform.parent;
            Vector3 emptyWorld = parent.TransformPoint(cup.EmptyLocalPosition);
            Vector3 fullWorld = parent.TransformPoint(cup.FullLocalPosition);
            return Vector3.Dot(fullWorld - emptyWorld, prefabRoot.up) >
                MeasureTolerance;
        }

        private static void AssertBoundsMatchManifest(
            Bounds actual,
            CafeManifest manifest)
        {
            Vector3 minimum = BlenderToUnity(manifest.bounds_min);
            Vector3 maximum = BlenderToUnity(manifest.bounds_max);
            var expected = new Bounds();
            expected.SetMinMax(Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
            if (Vector3.Distance(actual.center, expected.center) > MeasureTolerance ||
                Vector3.Distance(actual.size, expected.size) > MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Cafe imported bounds {actual} differ from manifest {expected}.");
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
                Bounds bounds = renderer.bounds;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 world = new Vector3(
                                x == 0 ? bounds.min.x : bounds.max.x,
                                y == 0 ? bounds.min.y : bounds.max.y,
                                z == 0 ? bounds.min.z : bounds.max.z);
                            Vector3 local = root.InverseTransformPoint(world);
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
                    "Cafe authored model has no static renderer bounds.");
            }

            return result;
        }

        private static void ValidateReviewArtifacts()
        {
            if (!File.Exists(PreviewPath) || new FileInfo(PreviewPath).Length < 1024 ||
                !File.Exists(BlendPath) || new FileInfo(BlendPath).Length < 1024)
            {
                throw new InvalidOperationException(
                    "Cafe review render or Blender source is missing/empty.");
            }
        }

        private static void AppendForbidden<TComponent>(
            GameObject root,
            List<string> problems,
            string label)
            where TComponent : Component
        {
            TComponent[] found = root.GetComponentsInChildren<TComponent>(true);
            if (found.Length > 0)
            {
                problems.Add(
                    $"prefab contains {found.Length} forbidden {label}(s)");
            }
        }

        private static Vector3 BlenderToUnity(float[] source)
        {
            if (!IsFiniteVector(source))
            {
                throw new InvalidOperationException(
                    "Cafe manifest carries an invalid Blender vector.");
            }

            return new Vector3(source[0], source[2], source[1]);
        }

        private static Vector3 ToVector(float[] values)
        {
            if (!IsFiniteVector(values))
            {
                throw new InvalidOperationException(
                    "Cafe manifest carries an invalid Unity vector.");
            }

            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool IsFiniteVector(float[] values)
        {
            return values != null && values.Length == 3 &&
                values.All(value => !float.IsNaN(value) && !float.IsInfinity(value));
        }

        private static bool IsPositiveVector(float[] values)
        {
            return IsFiniteVector(values) && values.All(value => value > 0f);
        }

        private static void AssertNear(float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"Cafe {label} is {actual:0.###}, expected {expected:0.###}.");
            }
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length == 64 &&
                value.All(character =>
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'));
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                NormalizePath(first),
                NormalizePath(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        [Serializable]
        private sealed class CafeManifest
        {
            public string generator_version;
            public string design_id;
            public string build_signature;
            public CafeDimensions dimensions_m;
            public CafeDoor door_opening_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public bool materials;
            public int animation_count;
            public int mesh_count;
            public int triangle_count;
            public int overlap_count;
            public int stool_count;
            public int cup_assembly_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public CafeTexture[] textures;
            public CafePart[] parts;
            public CafeAnchor[] anchors;
            public CafeDynamicProp[] dynamic_props;
            public CafeCollider[] collider_descriptors;
        }

        [Serializable]
        private sealed class CafeDimensions
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class CafeDoor
        {
            public float width;
            public float height;
        }

        [Serializable]
        private sealed class CafeTexture
        {
            public string sheet;
            public string file;
            public string resource_path;
            public int width;
            public int height;
            public string wrap;
            public string sha256;
            public string base_surface;
        }

        [Serializable]
        private sealed class CafePart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public string base_surface;
            public bool emissive;
            public bool shadows;
            public bool initially_visible;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class CafeAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
            public float[] unity_local_position;
            public float[] unity_local_forward;
            public float[] unity_local_up;
        }

        [Serializable]
        private sealed class CafeDynamicProp
        {
            public string name;
            public string root_name;
            public string lift_root_name;
            public string role;
            public string owner;
            public string[] part_names;
            public string liquid_part;
            public float empty_local_y;
            public float full_local_y;
        }

        [Serializable]
        private sealed class CafeCollider
        {
            public string id;
            public string shape;
            public float[] center;
            public float[] size;
            public float yaw;
            public float radius;
            public float height;
        }
    }
}
