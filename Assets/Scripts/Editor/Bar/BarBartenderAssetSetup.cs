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
    /// Imports the Six-Armed Bartender FBX against the production Hero/NPC V2
    /// Generic Avatar and builds the passive runtime prefab outside
    /// Resources, binding it to the one addressable provider asset.
    /// </summary>
    [InitializeOnLoad]
    public static class BarBartenderAssetSetup
    {
        public const string ModelPath =
            "Assets/Bar/Bartender/Models/BarBartender3D.fbx";
        public const string ManifestPath =
            "Assets/Bar/Bartender/Models/BarBartender3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Bar/Bartender/Prefabs/BarBartender.prefab";
        public const string ProviderPath =
            "Assets/Resources/Bar/BarBartenderProvider.asset";

        private const string ExpectedDesignId =
            "six_armed_bartender_v1";
        private const string ExpectedPose = "apose";
        private const string ExpectedArmDesign =
            "stacked_pivot_pairs_v1";
        private const int ExpectedExtraArmPairs = 2;
        // The counter he works behind stands 1.4 m; the publican
        // looms a full cashier-class two metres so his head and
        // shoulders read across it from the hall.
        private const float ExpectedHeight = 1.75f;
        private const int MinimumTriangleCount = 1400;
        private const int MaximumTriangleCount = 3400;

        private static readonly string[] ChainIds =
        {
            "Arm2.L",
            "Arm2.R",
            "Arm3.L",
            "Arm3.R"
        };

        private static readonly string[] PivotSuffixes =
        {
            "Shoulder",
            "Elbow",
            "Wrist",
            "Grip"
        };

        private static readonly string[] ExpectedPivotNames =
            ChainIds
                .SelectMany(chain => PivotSuffixes.Select(
                    suffix => $"PIVOT_{chain}.{suffix}"))
                .ToArray();

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static BarBartenderAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Bar Bartender 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Bar bartender prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/Bar Bartender 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Bar bartender passive prefab contract is valid. " +
                "(The imported model itself is not diffed - only the " +
                "built prefab is checked.)");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(PlayerModelPath) &&
                File.Exists(SharedMaterialPath);
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
                    "Bar bartender build requires its FBX/manifest, " +
                    "the production Hero/NPC V2 model and the shared " +
                    "Player3DLit material.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                EnsureFolderForAsset(ProviderPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
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

                BarBartenderManifest manifest =
                    LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from " +
                        $"'{ModelPath}'.");
                }

                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildPrefab(modelAsset, sharedMaterial, manifest);
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
            BarBartenderManifest manifest = LoadAndValidateManifest();

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Bar bartender prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            BarBartenderAssetRegistry registry =
                prefab.GetComponent<BarBartenderAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "Bar bartender prefab has no asset registry.");
            }

            Avatar playerAvatar = FindModelAvatar();
            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                registry.Animator.avatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Bar bartender Animator must be controller-free, " +
                    "use the Hero/NPC V2 Generic Avatar and disable root " +
                    "motion.");
            }

            ValidateRegistryBindings(registry);
            if (registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "Bar bartender renderer counts differ from the " +
                    "deterministic manifest.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Bar bartender registry source metadata is stale.");
            }

            if (Mathf.Abs(
                    registry.LocalBounds.size.y - ExpectedHeight) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "Bar bartender prefab bounds lost the authored " +
                    "resting height or grounding.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true)
                    .Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true)
                    .Length != 0)
            {
                throw new InvalidOperationException(
                    "The passive bartender prefab must contain no " +
                    "Collider, Light or Rigidbody component.");
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
                        "Every bartender renderer must reference the " +
                        "one shared Player3DLit material.");
                }
            }

            BarBartenderProvider provider =
                AssetDatabase.LoadAssetAtPath<BarBartenderProvider>(
                    ProviderPath);
            if (provider == null ||
                provider.LegacyBartenderPrefab != prefab)
            {
                throw new InvalidOperationException(
                    "The bartender provider asset must retain the built " +
                    "six-armed prefab as its inactive legacy reference.");
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            BarBartenderManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not validate Bar bartender source " +
                    $"manifest: {exception}");
                return;
            }

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
                    "Could not build Bar bartender prefab: " +
                    $"{exception}");
            }
        }

        private static BarBartenderManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            BarBartenderManifest manifest =
                JsonUtility.FromJson<BarBartenderManifest>(
                    source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.pivot_names == null)
            {
                throw new InvalidOperationException(
                    "Bar bartender manifest is malformed.");
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
                manifest.extra_arm_pairs != ExpectedExtraArmPairs)
            {
                throw new InvalidOperationException(
                    "Bar bartender design or arm contract differs " +
                    "from the approved source.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) >
                    0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 30 ||
                manifest.mesh_count > 72 ||
                manifest.bones.Length != 31 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount ||
                manifest.pool_eligible)
            {
                throw new InvalidOperationException(
                    "Bar bartender manifest height, skeleton, mesh, " +
                    "triangle budget or pool flag is invalid.");
            }

            if (manifest.emissive ||
                manifest.colliders ||
                manifest.lights ||
                manifest.rigidbodies ||
                manifest.animation_count != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Bar bartender must be non-emissive, " +
                    "collider/light/Rigidbody-free, animation-free " +
                    "and reuse Player3DLit.");
            }

            if (!manifest.pivot_names.SequenceEqual(
                    ExpectedPivotNames,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Bar bartender arm pivots diverge from the " +
                    "sixteen authored PIVOT_Arm anchors.");
            }

            if (string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Bar bartender manifest lacks deterministic " +
                    "source metadata.");
            }

            ValidateManifestDesignParts(manifest.parts);
            return manifest;
        }

        private static void ValidateManifestDesignParts(
            IReadOnlyList<BarBartenderManifestPart> parts)
        {
            Dictionary<string, BarBartenderManifestPart> byName =
                parts.ToDictionary(
                    part => part.name,
                    StringComparer.Ordinal);
            RequirePart(byName, "GEO_Head", "publican_head", "head");
            RequirePart(
                byName,
                "FACE_Moustache",
                "human_face",
                "head");
            RequirePart(
                byName,
                "FACE_Pupil.L",
                "human_face",
                "face.eye.L");
            RequirePart(
                byName,
                "FACE_Pupil.R",
                "human_face",
                "face.eye.R");
            RequirePart(
                byName,
                "CLO_WaistcoatFront",
                "uniform",
                "chest");
            foreach (string chain in ChainIds)
            {
                string pair = chain.Substring(3, 1);
                string side = chain.Substring(5, 1);
                RequirePart(
                    byName,
                    $"ARM{pair}_Upper.{side}",
                    "extra_arm_upper",
                    "root");
                RequirePart(
                    byName,
                    $"ARM{pair}_Fore.{side}",
                    "extra_arm_fore",
                    "root");
                RequirePart(
                    byName,
                    $"ARM{pair}_Hand.{side}",
                    "extra_arm_hand",
                    "root");
            }
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, BarBartenderManifestPart>
                parts,
            string name,
            string role,
            string bone)
        {
            if (!parts.TryGetValue(
                    name,
                    out BarBartenderManifestPart part) ||
                !string.Equals(
                    part.role,
                    role,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    part.bone,
                    bone,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Bar bartender manifest lost required part " +
                    $"'{name}' with role '{role}' on '{bone}'.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            BarBartenderManifest manifest)
        {
            GameObject prefabRoot = new GameObject("BarBartender");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset)
                        as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the imported bartender " +
                        "model.");
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
                    IndexUniqueTransforms(model, "bartender prefab");
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported bartender renderer count differs " +
                        "from the manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<BarBartenderRendererBinding> bindings =
                    new List<BarBartenderRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    BarBartenderManifestPart source =
                        manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Imported bartender is missing renderer " +
                            $"'{source.name}'.");
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

                    bindings.Add(
                        new BarBartenderRendererBinding(
                            source.name,
                            source.role,
                            source.bone,
                            source.palette_name,
                            renderer,
                            ParseColor(source.base_color)));
                    rendererList.Add(renderer);
                }

                Animator animator =
                    model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }

                if (animator.avatar == null)
                {
                    animator.avatar = FindModelAvatar();
                }

                if (animator.avatar == null ||
                    !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Bartender model has no valid Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;

                BarBartenderArmChain[] chains =
                    new BarBartenderArmChain[ChainIds.Length];
                for (int index = 0; index < ChainIds.Length; index++)
                {
                    string chain = ChainIds[index];
                    chains[index] = new BarBartenderArmChain(
                        chain,
                        RequireTransform(
                            transformsByName,
                            $"PIVOT_{chain}.Shoulder",
                            "bartender prefab"),
                        RequireTransform(
                            transformsByName,
                            $"PIVOT_{chain}.Elbow",
                            "bartender prefab"),
                        RequireTransform(
                            transformsByName,
                            $"PIVOT_{chain}.Wrist",
                            "bartender prefab"),
                        RequireTransform(
                            transformsByName,
                            $"PIVOT_{chain}.Grip",
                            "bartender prefab"));
                }

                Renderer[] renderers = rendererList.ToArray();
                BarBartenderAssetRegistry registry =
                    prefabRoot.AddComponent<BarBartenderAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(
                        transformsByName, "pelvis", "bartender prefab"),
                    RequireTransform(
                        transformsByName, "spine", "bartender prefab"),
                    RequireTransform(
                        transformsByName, "chest", "bartender prefab"),
                    RequireTransform(
                        transformsByName, "neck", "bartender prefab"),
                    RequireTransform(
                        transformsByName, "head", "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "face.eye.L",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "face.eye.R",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "upper_arm.L",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "forearm.L",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName, "hand.L", "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "upper_arm.R",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName,
                        "forearm.R",
                        "bartender prefab"),
                    RequireTransform(
                        transformsByName, "hand.R", "bartender prefab"),
                    chains,
                    CalculateLocalBounds(
                        prefabRoot.transform,
                        renderers),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Could not save Bar bartender prefab at " +
                        $"'{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
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

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            provider.ConfigureLegacy(prefab);
            EditorUtility.SetDirty(provider);
        }

        private static void ValidateRegistryBindings(
            BarBartenderAssetRegistry registry)
        {
            Transform[] required =
            {
                registry.ModelRoot,
                registry.Pelvis,
                registry.Spine,
                registry.Chest,
                registry.Neck,
                registry.Head,
                registry.FaceEyeLeft,
                registry.FaceEyeRight,
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand
            };
            if (required.Any(target => target == null))
            {
                throw new InvalidOperationException(
                    "Bar bartender registry is missing a procedural " +
                    "torso, eye or arm binding.");
            }

            if (registry.ExtraArmChains.Count !=
                BarBartenderAssetRegistry.ExtraArmChainCount)
            {
                throw new InvalidOperationException(
                    "Bar bartender registry must bind exactly four " +
                    "extra arm chains.");
            }

            for (int index = 0;
                 index < registry.ExtraArmChains.Count;
                 index++)
            {
                BarBartenderArmChain chain =
                    registry.ExtraArmChains[index];
                if (chain == null ||
                    !string.Equals(
                        chain.ChainId,
                        ChainIds[index],
                        StringComparison.Ordinal) ||
                    chain.ShoulderPivot == null ||
                    chain.ElbowPivot == null ||
                    chain.WristPivot == null ||
                    chain.GripPivot == null)
                {
                    throw new InvalidOperationException(
                        "A bartender extra arm chain lost one of its " +
                        "four authored pivots.");
                }

                if (!chain.ShoulderPivot.IsChildOf(registry.ModelRoot) ||
                    !chain.ElbowPivot.IsChildOf(registry.ModelRoot) ||
                    !chain.WristPivot.IsChildOf(registry.ModelRoot) ||
                    !chain.GripPivot.IsChildOf(registry.ModelRoot))
                {
                    throw new InvalidOperationException(
                        "A bartender arm pivot escaped the model root.");
                }
            }

            if (registry.FaceEyeLeft.parent != registry.Head ||
                registry.FaceEyeRight.parent != registry.Head)
            {
                throw new InvalidOperationException(
                    "Bar bartender eye bones lost their canonical " +
                    "head parent.");
            }
        }

        private static Avatar FindModelAvatar()
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static Dictionary<string, Transform>
            IndexUniqueTransforms(GameObject root, string label)
        {
            Dictionary<string, Transform> result =
                new Dictionary<string, Transform>(
                    StringComparer.Ordinal);
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"Imported {label} hierarchy contains " +
                        $"duplicate transform name '{transform.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Renderer>
            IndexUniqueRenderers(GameObject root)
        {
            Dictionary<string, Renderer> result =
                new Dictionary<string, Renderer>(
                    StringComparer.Ordinal);
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        "Imported bartender hierarchy contains " +
                        $"duplicate renderer name '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            string label)
        {
            if (transforms.TryGetValue(name, out Transform result))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Imported {label} hierarchy is missing transform " +
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
                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }

                Bounds bounds = mesh.bounds;
                Matrix4x4 rendererToRoot =
                    root.worldToLocalMatrix *
                    renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = rendererToRoot.MultiplyPoint3x4(
                        new Vector3(
                            (corner & 1) == 0
                                ? bounds.min.x
                                : bounds.max.x,
                            (corner & 2) == 0
                                ? bounds.min.y
                                : bounds.max.y,
                            (corner & 4) == 0
                                ? bounds.min.z
                                : bounds.max.z));
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
                    "Bar bartender model contains no renderers.");
            }

            return result;
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static Color ParseColor(float[] components)
        {
            return new Color(
                components[0],
                components[1],
                components[2],
                components[3]);
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

        [Serializable]
        private sealed class BarBartenderManifest
        {
            public string generator_version;
            public string design_id;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public bool pool_eligible;
            public string[] pivot_names;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public bool lights;
            public bool rigidbodies;
            public int animation_count;
            public string build_signature;
            public string arm_design;
            public int extra_arm_pairs;
            public BarBartenderManifestBone[] bones;
            public BarBartenderManifestPart[] parts;
        }

        [Serializable]
        private sealed class BarBartenderManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class BarBartenderManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;
        }
    }
}
