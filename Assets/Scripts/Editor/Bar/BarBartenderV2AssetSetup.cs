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
    /// <summary>
    /// Builds the active ordinary bartender beside, never over, the retained
    /// six-armed prefab. The model remains animation-free; four bone-only
    /// cafe-attendant clips are referenced from their shared authored FBX.
    /// </summary>
    [InitializeOnLoad]
    public static class BarBartenderV2AssetSetup
    {
        public const string ModelPath =
            "Assets/Bar/Bartender/Models/" +
            "BarBartenderOrdinary3D.fbx";
        public const string ManifestPath =
            "Assets/Bar/Bartender/Models/" +
            "BarBartenderOrdinary3D.json";
        public const string PrefabPath =
            "Assets/Bar/Bartender/Prefabs/" +
            "BarBartenderOrdinary.prefab";
        public const string LegacyPrefabPath =
            "Assets/Bar/Bartender/Prefabs/BarBartender.prefab";
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx";
        public const string ProviderPath =
            "Assets/Resources/Bar/BarBartenderProvider.asset";

        public const string VesselGripAnchorName =
            "ANCHOR_BartenderVesselGrip";
        public const string BottleGripAnchorName =
            "ANCHOR_BartenderBottleGrip";

        private const string ExpectedDesignId = "bar_bartender_v2";
        private const string ExpectedPose = "apose";
        private const string ExpectedArmDesign =
            "ordinary_two_armed_v2";
        private const float ExpectedHeight = 1.75f;
        private const int MinimumTriangleCount = 900;
        private const int MaximumTriangleCount = 2600;

        private static readonly string[] ExpectedAnchors =
        {
            VesselGripAnchorName,
            BottleGripAnchorName
        };

        private static readonly string[] ExpectedSockets =
        {
            "SOCKET_Grip.L",
            "SOCKET_Vessel.L",
            "SOCKET_Grip.R",
            "SOCKET_Bottle.R"
        };

        private static readonly ClipDescriptor[] Clips =
        {
            new ClipDescriptor(
                BarBartenderClipKind.Wipe,
                "CafeAttendantWipe",
                9f,
                true),
            new ClipDescriptor(
                BarBartenderClipKind.Walk,
                "CafeAttendantWalk",
                1.25f,
                true),
            new ClipDescriptor(
                BarBartenderClipKind.Pour,
                "CafeAttendantPour",
                3.5f,
                false),
            new ClipDescriptor(
                BarBartenderClipKind.Notice,
                "CafeAttendantNotice",
                2.5f,
                false)
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static BarBartenderV2AssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem(
            "Bar Promenade/Bar Bartender 3D/" +
            "Build Active Ordinary Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Active ordinary bartender prefab rebuilt at " +
                $"'{PrefabPath}'.");
        }

        [MenuItem(
            "Bar Promenade/Bar Bartender 3D/" +
            "Validate Active Ordinary Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Active ordinary bartender model, shared clips, prefab " +
                "and legacy-preserving provider are valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(LegacyPrefabPath) &&
                File.Exists(PlayerModelPath) &&
                File.Exists(SharedMaterialPath) &&
                File.Exists(AnimationPath);
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
                    "Ordinary bartender build requires its FBX/manifest, " +
                    "the retained legacy prefab, Hero V2, Player3DLit and " +
                    "the shared cafe-attendant animation FBX.");
            }

            isBuilding = true;
            try
            {
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                Manifest manifest = LoadAndValidateManifest();
                GameObject model =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (model == null || material == null)
                {
                    throw new InvalidOperationException(
                        "Unity did not import the ordinary bartender " +
                        "model or shared material.");
                }

                BuildPrefab(model, material, manifest);
                BindProvider();
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
            Manifest manifest = LoadAndValidateManifest();
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Ordinary bartender prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            BarBartenderAssetRegistry registry =
                prefab.GetComponent<BarBartenderAssetRegistry>();
            if (registry == null || !registry.UsesAuthoredServiceClips)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender prefab has no authored-service " +
                    "registry.");
            }

            Avatar playerAvatar = FindPlayerAvatar();
            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender Animator must be controller-free, " +
                    "in-place and use the Hero/NPC V2 Generic Avatar.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != manifest.triangle_count ||
                registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count ||
                registry.ExtraArmChains.Count != 0)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender registry source metadata, " +
                    "renderer count or two-arm contract is stale.");
            }

            ValidateServiceBindings(registry);
            if (Mathf.Abs(registry.LocalBounds.size.y - ExpectedHeight) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender bounds lost authored height or " +
                    "grounding.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Passive ordinary bartender prefab contains a " +
                    "Collider, Light or Rigidbody.");
            }

            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            for (int index = 0;
                 index < registry.Renderers.Count;
                 index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        "Every ordinary bartender renderer must reuse " +
                        "Player3DLit.");
                }
            }

            GameObject legacy =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    LegacyPrefabPath);
            BarBartenderProvider provider =
                AssetDatabase.LoadAssetAtPath<BarBartenderProvider>(
                    ProviderPath);
            if (provider == null ||
                provider.BartenderPrefab != prefab ||
                provider.LegacyBartenderPrefab != legacy ||
                provider.BartenderPrefab == provider.LegacyBartenderPrefab)
            {
                throw new InvalidOperationException(
                    "Bartender provider must select the ordinary prefab " +
                    "and retain the distinct six-armed legacy prefab.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            Manifest manifest)
        {
            GameObject prefabRoot =
                new GameObject("BarBartenderOrdinary");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset)
                        as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the imported ordinary " +
                        "bartender model.");
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
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported ordinary bartender renderer count " +
                        "differs from its manifest.");
                }

                var rendererList =
                    new List<Renderer>(manifest.parts.Length);
                var bindings =
                    new List<BarBartenderRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    ManifestPart part = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            part.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Imported ordinary bartender is missing " +
                            $"renderer '{part.name}'.");
                    }

                    renderer.sharedMaterials =
                        new[] { sharedMaterial };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = true;
                    }

                    rendererList.Add(renderer);
                    bindings.Add(
                        new BarBartenderRendererBinding(
                            part.name,
                            part.role,
                            part.bone,
                            part.palette_name,
                            renderer,
                            ParseColor(part.base_color)));
                }

                Animator animator =
                    model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }

                animator.avatar = FindPlayerAvatar();
                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Ordinary bartender model has no valid Hero V2 " +
                        "Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Renderer[] renderers = rendererList.ToArray();
                BarBartenderAssetRegistry registry =
                    prefabRoot.AddComponent<
                        BarBartenderAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(transformsByName, "pelvis"),
                    RequireTransform(transformsByName, "spine"),
                    RequireTransform(transformsByName, "chest"),
                    RequireTransform(transformsByName, "neck"),
                    RequireTransform(transformsByName, "head"),
                    RequireTransform(transformsByName, "face.eye.L"),
                    RequireTransform(transformsByName, "face.eye.R"),
                    RequireTransform(transformsByName, "upper_arm.L"),
                    RequireTransform(transformsByName, "forearm.L"),
                    RequireTransform(transformsByName, "hand.L"),
                    RequireTransform(transformsByName, "upper_arm.R"),
                    RequireTransform(transformsByName, "forearm.R"),
                    RequireTransform(transformsByName, "hand.R"),
                    Array.Empty<BarBartenderArmChain>(),
                    CalculateLocalBounds(
                        prefabRoot.transform,
                        renderers),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);
                registry.ConfigureOrdinaryService(
                    BuildClipBindings(),
                    RequireTransform(transformsByName, "SOCKET_Grip.L"),
                    RequireTransform(transformsByName, "SOCKET_Vessel.L"),
                    RequireTransform(transformsByName, "SOCKET_Grip.R"),
                    RequireTransform(transformsByName, "SOCKET_Bottle.R"),
                    RequireTransform(
                        transformsByName,
                        VesselGripAnchorName),
                    RequireTransform(
                        transformsByName,
                        BottleGripAnchorName));

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save ordinary bartender prefab at " +
                        $"'{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static BarBartenderClipBinding[] BuildClipBindings()
        {
            var result = new BarBartenderClipBinding[Clips.Length];
            for (int index = 0; index < Clips.Length; index++)
            {
                ClipDescriptor descriptor = Clips[index];
                AnimationClip clip = LoadClip(descriptor);
                result[index] = new BarBartenderClipBinding(
                    descriptor.Kind,
                    clip,
                    descriptor.Loop);
            }

            return result;
        }

        private static void BindProvider()
        {
            BarBartenderProvider provider =
                AssetDatabase.LoadAssetAtPath<BarBartenderProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<BarBartenderProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            GameObject active =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject legacy =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    LegacyPrefabPath);
            provider.ConfigureActive(active, legacy);
            EditorUtility.SetDirty(provider);
        }

        private static void ValidateServiceBindings(
            BarBartenderAssetRegistry registry)
        {
            if (registry.ClipBindings.Count != Clips.Length ||
                registry.LeftGripSocket == null ||
                registry.LeftVesselSocket == null ||
                registry.RightGripSocket == null ||
                registry.RightBottleSocket == null ||
                registry.VesselGripAnchor == null ||
                registry.BottleGripAnchor == null)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender lost a clip or service socket.");
            }

            for (int index = 0; index < Clips.Length; index++)
            {
                ClipDescriptor expected = Clips[index];
                BarBartenderClipBinding binding =
                    registry.ClipBindings[index];
                if (binding == null ||
                    binding.Kind != expected.Kind ||
                    binding.Loop != expected.Loop ||
                    binding.Clip == null ||
                    !string.Equals(
                        NormalizeClipName(binding.Clip.name),
                        expected.Name,
                        StringComparison.Ordinal) ||
                    Mathf.Abs(binding.Clip.length - expected.Duration) >
                        0.002f)
                {
                    throw new InvalidOperationException(
                        $"Ordinary bartender clip binding {index} " +
                        "differs from the authored waiter contract.");
                }
            }
        }

        private static Manifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            Manifest manifest =
                JsonUtility.FromJson<Manifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.pivot_names == null ||
                manifest.anchor_names == null ||
                manifest.socket_names == null ||
                manifest.shared_clips == null)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.pose,
                    ExpectedPose,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.arm_design,
                    ExpectedArmDesign,
                    StringComparison.Ordinal) ||
                manifest.extra_arm_pairs != 0 ||
                manifest.pivot_names.Length != 0)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender design still declares abnormal " +
                    "anatomy or an unexpected source pose.");
            }

            if (!manifest.anchor_names.SequenceEqual(
                    ExpectedAnchors,
                    StringComparer.Ordinal) ||
                !manifest.socket_names.SequenceEqual(
                    ExpectedSockets,
                    StringComparer.Ordinal) ||
                !string.Equals(
                    manifest.shared_animation_asset,
                    AnimationPath,
                    StringComparison.Ordinal) ||
                !manifest.shared_clips.SequenceEqual(
                    Clips.Select(clip => clip.Name),
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Ordinary bartender service sockets, anchors or " +
                    "shared waiter clips changed.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 28 ||
                manifest.mesh_count > 58 ||
                manifest.bones.Length != 31 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount ||
                manifest.pool_eligible ||
                manifest.animation_count != 0 ||
                manifest.emissive ||
                manifest.colliders ||
                manifest.lights ||
                manifest.rigidbodies ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender height, skeleton, budget, passive " +
                    "prefab or deterministic metadata contract is invalid.");
            }

            var parts = manifest.parts.ToDictionary(
                part => part.name,
                StringComparer.Ordinal);
            RequirePart(parts, "GEO_Head", "publican_head", "head");
            RequirePart(parts, "GEO_Hand.L", "hand_palm", "hand.L");
            RequirePart(parts, "GEO_Hand.R", "hand_palm", "hand.R");
            RequirePart(
                parts,
                "CLO_WaistcoatFront",
                "uniform",
                "chest");
            RequirePart(parts, "CLO_Apron", "uniform", "pelvis");
            RequirePart(
                parts,
                "ACC_ServiceTowel",
                "held_prop",
                "hand.L");
            if (manifest.parts.Any(part =>
                    part.name.StartsWith(
                        "ARM2_",
                        StringComparison.Ordinal) ||
                    part.name.StartsWith(
                        "ARM3_",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Ordinary bartender manifest contains a legacy " +
                    "extra-arm mesh.");
            }

            return manifest;
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, ManifestPart> parts,
            string name,
            string role,
            string bone)
        {
            if (!parts.TryGetValue(name, out ManifestPart part) ||
                !string.Equals(part.role, role, StringComparison.Ordinal) ||
                !string.Equals(part.bone, bone, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Ordinary bartender manifest lost '{name}' on " +
                    $"'{bone}' with role '{role}'.");
            }
        }

        private static AnimationClip LoadClip(ClipDescriptor descriptor)
        {
            AnimationClip clip = AssetDatabase
                .LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate =>
                    !candidate.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        NormalizeClipName(candidate.name),
                        descriptor.Name,
                        StringComparison.Ordinal));
            if (clip == null ||
                Mathf.Abs(clip.length - descriptor.Duration) > 0.002f)
            {
                throw new InvalidOperationException(
                    $"Shared waiter clip '{descriptor.Name}' is missing " +
                    "or has the wrong duration.");
            }

            return clip;
        }

        private static string NormalizeClipName(string name)
        {
            int separator = name.LastIndexOf('|');
            return separator >= 0 ? name.Substring(separator + 1) : name;
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                Manifest manifest = LoadAndValidateManifest();
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                BarBartenderAssetRegistry registry = prefab != null
                    ? prefab.GetComponent<BarBartenderAssetRegistry>()
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
                    "Could not validate ordinary bartender source: " +
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
                    "Could not build ordinary bartender prefab: " +
                    exception);
            }
        }

        private static Avatar FindPlayerAvatar()
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static Dictionary<string, Transform>
            IndexUniqueTransforms(GameObject root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (Transform transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        "Imported ordinary bartender contains duplicate " +
                        $"transform '{transform.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Renderer>
            IndexUniqueRenderers(GameObject root)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        "Imported ordinary bartender contains duplicate " +
                        $"renderer '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name)
        {
            if (transforms.TryGetValue(name, out Transform result))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Imported ordinary bartender is missing transform " +
                $"'{name}'.");
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            Bounds result = default;
            bool initialized = false;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }

                Matrix4x4 toRoot =
                    root.worldToLocalMatrix *
                    renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(
                        new Vector3(
                            (corner & 1) == 0
                                ? mesh.bounds.min.x
                                : mesh.bounds.max.x,
                            (corner & 2) == 0
                                ? mesh.bounds.min.y
                                : mesh.bounds.max.y,
                            (corner & 4) == 0
                                ? mesh.bounds.min.z
                                : mesh.bounds.max.z));
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

            if (!initialized)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender model contains no renderers.");
            }

            return result;
        }

        private static Color ParseColor(float[] components)
        {
            if (components == null || components.Length != 4)
            {
                throw new InvalidOperationException(
                    "Ordinary bartender part has malformed base colour.");
            }

            return new Color(
                components[0],
                components[1],
                components[2],
                components[3]);
        }

        private readonly struct ClipDescriptor
        {
            public ClipDescriptor(
                BarBartenderClipKind kind,
                string name,
                float duration,
                bool loop)
            {
                Kind = kind;
                Name = name;
                Duration = duration;
                Loop = loop;
            }

            public BarBartenderClipKind Kind { get; }
            public string Name { get; }
            public float Duration { get; }
            public bool Loop { get; }
        }

        [Serializable]
        private sealed class Manifest
        {
            public string generator_version;
            public string design_id;
            public float height_m;
            public string pose;
            public int mesh_count;
            public int triangle_count;
            public bool pool_eligible;
            public string[] pivot_names;
            public string[] anchor_names;
            public string[] socket_names;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public bool lights;
            public bool rigidbodies;
            public int animation_count;
            public string shared_animation_asset;
            public string[] shared_clips;
            public string build_signature;
            public string arm_design;
            public int extra_arm_pairs;
            public ManifestBone[] bones;
            public ManifestPart[] parts;
        }

        [Serializable]
        private sealed class ManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class ManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;
        }
    }
}
