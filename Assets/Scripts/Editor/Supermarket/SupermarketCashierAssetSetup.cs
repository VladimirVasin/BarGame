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
    /// Imports the Watcher Cashier FBX against the shared Player
    /// Generic Avatar and builds the passive runtime prefab outside
    /// Resources, binding it to the one addressable provider asset.
    /// </summary>
    [InitializeOnLoad]
    public static class SupermarketCashierAssetSetup
    {
        public const string ModelPath =
            "Assets/Supermarket/Cashier/Models/SupermarketCashier3D.fbx";
        public const string ManifestPath =
            "Assets/Supermarket/Cashier/Models/SupermarketCashier3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Supermarket/Cashier/Prefabs/SupermarketCashier.prefab";
        public const string ProviderPath =
            "Assets/Resources/Supermarket/SupermarketCashierProvider.asset";

        private const string ExpectedDesignId = "watcher_cashier_v1";
        private const string ExpectedPose = "apose";
        private const string ExpectedNeckDesign =
            "segmented_periscope_v1";
        private const string ExpectedEyeDesign =
            "wide_watcher_asymmetric";
        private const int ExpectedNeckSegmentCount = 5;
        private const float ExpectedHeight = 2.05f;
        private const int MinimumTriangleCount = 1100;
        private const int MaximumTriangleCount = 2200;

        private static readonly string[] ExpectedPivotNames =
        {
            "PIVOT_Neck.01",
            "PIVOT_Neck.02",
            "PIVOT_Neck.03",
            "PIVOT_Neck.04",
            "PIVOT_Neck.05"
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static SupermarketCashierAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Supermarket Cashier 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Supermarket cashier prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/Supermarket Cashier 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Supermarket cashier passive prefab contract is " +
                "valid. (The imported model itself is not diffed - " +
                "only the built prefab is checked.)");
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
                    "Supermarket cashier build requires its FBX/manifest, " +
                    "the production Player model and the shared " +
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

                SupermarketCashierManifest manifest =
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
            SupermarketCashierManifest manifest =
                LoadAndValidateManifest();

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Supermarket cashier prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            SupermarketCashierAssetRegistry registry =
                prefab.GetComponent<SupermarketCashierAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier prefab has no asset registry.");
            }

            Avatar playerAvatar = FindModelAvatar();
            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                registry.Animator.avatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier Animator must be " +
                    "controller-free, use the Player Generic Avatar " +
                    "and disable root motion.");
            }

            ValidateRegistryBindings(registry);
            if (registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier renderer counts differ from " +
                    "the deterministic manifest.");
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
                    "Supermarket cashier registry source metadata is " +
                    "stale.");
            }

            if (Mathf.Abs(
                    registry.LocalBounds.size.y - ExpectedHeight) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier prefab bounds lost the " +
                    "authored resting height or grounding.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true)
                    .Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true)
                    .Length != 0)
            {
                throw new InvalidOperationException(
                    "The passive cashier prefab must contain no " +
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
                        "Every cashier renderer must reference the one " +
                        "shared Player3DLit material.");
                }
            }

            ValidateEyeBindings(registry.RendererBindings);

            SupermarketCashierProvider provider =
                AssetDatabase.LoadAssetAtPath<SupermarketCashierProvider>(
                    ProviderPath);
            if (provider == null || provider.CashierPrefab != prefab)
            {
                throw new InvalidOperationException(
                    "The cashier provider asset must reference the " +
                    "built prefab.");
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            SupermarketCashierManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not validate Supermarket cashier source " +
                    $"manifest: {exception}");
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            SupermarketCashierAssetRegistry registry = prefab != null
                ? prefab.GetComponent<SupermarketCashierAssetRegistry>()
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
                    "Could not build Supermarket cashier prefab: " +
                    $"{exception}");
            }
        }

        private static SupermarketCashierManifest
            LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            SupermarketCashierManifest manifest =
                JsonUtility.FromJson<SupermarketCashierManifest>(
                    source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.pivot_names == null)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier manifest is malformed.");
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
                    manifest.neck_design,
                    ExpectedNeckDesign,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.eye_design,
                    ExpectedEyeDesign,
                    StringComparison.Ordinal) ||
                manifest.neck_segment_count != ExpectedNeckSegmentCount)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier design, neck or eye contract " +
                    "differs from the approved source.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) >
                    0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 24 ||
                manifest.mesh_count > 56 ||
                manifest.bones.Length != 31 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount ||
                manifest.pool_eligible)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier manifest height, skeleton, " +
                    "mesh, triangle budget or pool flag is invalid.");
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
                    "Supermarket cashier must be non-emissive, " +
                    "collider/light/Rigidbody-free, animation-free and " +
                    "reuse Player3DLit.");
            }

            if (!manifest.pivot_names.SequenceEqual(
                    ExpectedPivotNames,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Supermarket cashier neck pivots diverge from the " +
                    "five authored PIVOT_Neck anchors.");
            }

            if (string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier manifest lacks deterministic " +
                    "source metadata.");
            }

            ValidateManifestDesignParts(manifest.parts);
            return manifest;
        }

        private static void ValidateManifestDesignParts(
            IReadOnlyList<SupermarketCashierManifestPart> parts)
        {
            Dictionary<string, SupermarketCashierManifestPart> byName =
                parts.ToDictionary(
                    part => part.name,
                    StringComparer.Ordinal);
            RequirePart(
                byName,
                "GEO_Head",
                "undersized_watcher_head",
                "head");
            RequirePart(
                byName,
                "FACE_EyeWhite.L",
                "wide_watcher_eye",
                "head");
            RequirePart(
                byName,
                "FACE_EyeWhite.R",
                "wide_watcher_eye",
                "head");
            RequirePart(
                byName,
                "FACE_Pupil.L",
                "visible_eye_pupil",
                "face.eye.L");
            RequirePart(
                byName,
                "FACE_Pupil.R",
                "visible_eye_pupil",
                "face.eye.R");
            RequirePart(
                byName,
                "CLO_TightCollar",
                "strangling_collar",
                "chest");
            RequirePart(
                byName,
                "CLO_NameTag",
                "uniform_detail",
                "chest");
            for (int index = 1;
                 index <= ExpectedNeckSegmentCount;
                 index++)
            {
                RequirePart(
                    byName,
                    $"NECK_Segment.{index:00}",
                    "stretch_neck_segment",
                    "root");
            }
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, SupermarketCashierManifestPart>
                parts,
            string name,
            string role,
            string bone)
        {
            if (!parts.TryGetValue(
                    name,
                    out SupermarketCashierManifestPart part) ||
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
                    $"Supermarket cashier manifest lost required part " +
                    $"'{name}' with role '{role}' on '{bone}'.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            SupermarketCashierManifest manifest)
        {
            GameObject prefabRoot =
                new GameObject("SupermarketCashier");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset)
                        as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the imported cashier " +
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
                    IndexUniqueTransforms(model, "cashier prefab");
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported cashier renderer count differs from " +
                        "the manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<SupermarketCashierRendererBinding> bindings =
                    new List<SupermarketCashierRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    SupermarketCashierManifestPart source =
                        manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Imported cashier is missing renderer " +
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
                        skinned.updateWhenOffscreen = false;
                    }

                    bindings.Add(
                        new SupermarketCashierRendererBinding(
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
                        "Cashier model has no valid Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;

                Transform[] neckPivots =
                    new Transform[ExpectedPivotNames.Length];
                for (int index = 0;
                     index < ExpectedPivotNames.Length;
                     index++)
                {
                    neckPivots[index] = RequireTransform(
                        transformsByName,
                        ExpectedPivotNames[index],
                        "cashier prefab");
                }

                Renderer[] renderers = rendererList.ToArray();
                SupermarketCashierAssetRegistry registry =
                    prefabRoot.AddComponent<
                        SupermarketCashierAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(
                        transformsByName, "pelvis", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "spine", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "chest", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "neck", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "head", "cashier prefab"),
                    RequireTransform(
                        transformsByName,
                        "face.eye.L",
                        "cashier prefab"),
                    RequireTransform(
                        transformsByName,
                        "face.eye.R",
                        "cashier prefab"),
                    RequireTransform(
                        transformsByName,
                        "upper_arm.L",
                        "cashier prefab"),
                    RequireTransform(
                        transformsByName, "forearm.L", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "hand.L", "cashier prefab"),
                    RequireTransform(
                        transformsByName,
                        "upper_arm.R",
                        "cashier prefab"),
                    RequireTransform(
                        transformsByName, "forearm.R", "cashier prefab"),
                    RequireTransform(
                        transformsByName, "hand.R", "cashier prefab"),
                    neckPivots,
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
                        "Could not save Supermarket cashier prefab at " +
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
            SupermarketCashierProvider provider =
                AssetDatabase.LoadAssetAtPath<SupermarketCashierProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<SupermarketCashierProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            SerializedObject serialized =
                new SerializedObject(provider);
            serialized.FindProperty("cashierPrefab").objectReferenceValue =
                prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static void ValidateRegistryBindings(
            SupermarketCashierAssetRegistry registry)
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
                    "Supermarket cashier registry is missing a " +
                    "procedural torso, eye or arm binding.");
            }

            if (registry.NeckPivots.Count !=
                ExpectedNeckSegmentCount ||
                registry.NeckPivots.Any(pivot => pivot == null))
            {
                throw new InvalidOperationException(
                    "Supermarket cashier registry must bind exactly " +
                    "five neck pivots.");
            }

            for (int index = 0;
                 index < registry.NeckPivots.Count;
                 index++)
            {
                if (!registry.NeckPivots[index]
                        .IsChildOf(registry.ModelRoot))
                {
                    throw new InvalidOperationException(
                        "A cashier neck pivot escaped the model root.");
                }
            }

            if (registry.FaceEyeLeft.parent != registry.Head ||
                registry.FaceEyeRight.parent != registry.Head)
            {
                throw new InvalidOperationException(
                    "Supermarket cashier eye bones lost their " +
                    "canonical head parent.");
            }
        }

        private static void ValidateEyeBindings(
            IReadOnlyList<SupermarketCashierRendererBinding> bindings)
        {
            SupermarketCashierRendererBinding leftEye =
                bindings.FirstOrDefault(binding =>
                    binding.RendererName == "FACE_EyeWhite.L");
            SupermarketCashierRendererBinding rightEye =
                bindings.FirstOrDefault(binding =>
                    binding.RendererName == "FACE_EyeWhite.R");
            SupermarketCashierRendererBinding leftPupil =
                bindings.FirstOrDefault(binding =>
                    binding.RendererName == "FACE_Pupil.L");
            SupermarketCashierRendererBinding rightPupil =
                bindings.FirstOrDefault(binding =>
                    binding.RendererName == "FACE_Pupil.R");
            if (leftEye == null ||
                rightEye == null ||
                leftPupil == null ||
                rightPupil == null ||
                leftEye.Role != "wide_watcher_eye" ||
                rightEye.Role != "wide_watcher_eye" ||
                leftPupil.Role != "visible_eye_pupil" ||
                rightPupil.Role != "visible_eye_pupil" ||
                leftPupil.BoneName != "face.eye.L" ||
                rightPupil.BoneName != "face.eye.R")
            {
                throw new InvalidOperationException(
                    "Cashier watcher-eye whites or poseable pupil " +
                    "bindings are invalid.");
            }

            if (leftPupil.BaseColor.maxColorComponent >= 0.08f ||
                rightPupil.BaseColor.maxColorComponent >= 0.08f)
            {
                throw new InvalidOperationException(
                    "Cashier pupils must stay pinprick dark.");
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
                        "Imported cashier hierarchy contains " +
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
                    "Supermarket cashier model contains no renderers.");
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
        private sealed class SupermarketCashierManifest
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
            public string neck_design;
            public int neck_segment_count;
            public float neck_rest_length_m;
            public float neck_max_stretch_ratio;
            public string eye_design;
            public SupermarketCashierManifestBone[] bones;
            public SupermarketCashierManifestPart[] parts;
        }

        [Serializable]
        private sealed class SupermarketCashierManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class SupermarketCashierManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;
        }
    }
}
