using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Owns the four bespoke Mountain Road cafe figures as one isolated,
    /// passive staged asset set. Their source FBXs and prefabs stay outside
    /// Resources; only the provider is addressable at runtime.
    /// </summary>
    [InitializeOnLoad]
    public static class MountainRoadCafeCastAssetSetup
    {
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx";
        public const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/MountainRoadCafeCast.json";
        public const string ProviderPath =
            "Assets/Resources/MountainRoad/" +
            "MountainRoadCafeCastProvider.asset";
        public const string CoffeePotSpoutAnchorName =
            "SOCKET_CafePotSpout";

        public const string LonePatronModelPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeLonePatron3D.fbx";
        public const string LonePatronManifestPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeLonePatron3D.json";
        public const string LonePatronPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/" +
            "MountainCafeLonePatron3D.prefab";
        public const string LonePatronAtlasPath =
            "Assets/Pedestrians/Textures/" +
            "MountainCafeLonePatron3DDetailAtlas.png";

        public const string PairManModelPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeCoupleMan3D.fbx";
        public const string PairManManifestPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeCoupleMan3D.json";
        public const string PairManPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/" +
            "MountainCafeCoupleMan3D.prefab";
        public const string PairManAtlasPath =
            "Assets/Pedestrians/Textures/" +
            "MountainCafeCoupleMan3DDetailAtlas.png";

        public const string PairWomanModelPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeCoupleWoman3D.fbx";
        public const string PairWomanManifestPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeCoupleWoman3D.json";
        public const string PairWomanPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/" +
            "MountainCafeCoupleWoman3D.prefab";
        public const string PairWomanAtlasPath =
            "Assets/Pedestrians/Textures/" +
            "MountainCafeCoupleWoman3DDetailAtlas.png";

        public const string AttendantModelPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeAttendant3D.fbx";
        public const string AttendantManifestPath =
            "Assets/Pedestrians/Staged/Models/" +
            "MountainCafeAttendant3D.json";
        public const string AttendantPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/" +
            "MountainCafeAttendant3D.prefab";
        public const string AttendantAtlasPath =
            "Assets/Pedestrians/Textures/" +
            "MountainCafeAttendant3DDetailAtlas.png";

        private const string ExpectedPose = "apose";
        private const int ExpectedAtlasSize = 256;
        private const int ExpectedAtlasInset = 1;
        private const int ExpectedBoneCount = 31;
        private const int ExpectedAnimationFps = 24;
        private const float ExpectedHeight = 1.75f;
        private const float TransformPositionTolerance = 0.0001f;
        private const float TransformAngleTolerance = 0.02f;
        private const float ClipDurationTolerance = 0.002f;
        private const float ColorTolerance = 0.0001f;
        private const float UvTolerance = 0.0001f;

        private static readonly CafeCastDescriptor[] Descriptors =
        {
            new CafeCastDescriptor(
                MountainRoadCafeCastRole.LonePatron,
                "Cafe Lone Patron",
                "MountainCafeLonePatron3D",
                "cafe_lone_patron_v2",
                LonePatronModelPath,
                LonePatronManifestPath,
                LonePatronPrefabPath,
                LonePatronAtlasPath,
                MountainRoadCafeCastClipKind.Idle,
                new[]
                {
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Idle,
                        "CafeLoneSleep", 12f, true)
                },
                1800,
                3000),
            new CafeCastDescriptor(
                MountainRoadCafeCastRole.PairMan,
                "Cafe Couple Man",
                "MountainCafeCoupleMan3D",
                "cafe_couple_man_v2",
                PairManModelPath,
                PairManManifestPath,
                PairManPrefabPath,
                PairManAtlasPath,
                MountainRoadCafeCastClipKind.Idle,
                new[]
                {
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Idle,
                        "CafeManIdle", 10f, true),
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Drink,
                        "CafeManDrink", 4.75f, false)
                },
                1800,
                3000),
            new CafeCastDescriptor(
                MountainRoadCafeCastRole.PairWoman,
                "Cafe Couple Woman",
                "MountainCafeCoupleWoman3D",
                "cafe_couple_woman_v2",
                PairWomanModelPath,
                PairWomanManifestPath,
                PairWomanPrefabPath,
                PairWomanAtlasPath,
                MountainRoadCafeCastClipKind.Idle,
                new[]
                {
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Idle,
                        "CafeWomanIdle", 11f, true),
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Drink,
                        "CafeWomanDrink", 4.75f, false)
                },
                1800,
                3000),
            new CafeCastDescriptor(
                MountainRoadCafeCastRole.Attendant,
                "Cafe Attendant",
                "MountainCafeAttendant3D",
                "cafe_attendant_v2",
                AttendantModelPath,
                AttendantManifestPath,
                AttendantPrefabPath,
                AttendantAtlasPath,
                MountainRoadCafeCastClipKind.Wipe,
                new[]
                {
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Wipe,
                        "CafeAttendantWipe", 9f, true),
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Walk,
                        "CafeAttendantWalk", 1.25f, true),
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Pour,
                        "CafeAttendantPour", 3.5f, false),
                    new CafeClipDescriptor(
                        MountainRoadCafeCastClipKind.Notice,
                        "CafeAttendantNotice", 2.5f, false)
                },
                1800,
                3200)
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;
        private static int ExpectedClipCount =>
            Descriptors.Sum(descriptor => descriptor.Clips.Length);

        static MountainRoadCafeCastAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem(
            "Bar Promenade/Mountain Road Cafe Cast/Build Staged Cast")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                "Mountain Road cafe cast prefabs rebuilt and provider bound.");
        }

        [MenuItem(
            "Bar Promenade/Mountain Road Cafe Cast/" +
            "Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Mountain Road cafe cast model, animation, prefab and " +
                "provider contracts are valid.");
        }

        public static bool SourcesExist()
        {
            if (!File.Exists(PlayerModelPath) ||
                !File.Exists(SharedMaterialPath) ||
                !File.Exists(AnimationPath) ||
                !File.Exists(AnimationManifestPath))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                CafeCastDescriptor descriptor = Descriptors[index];
                if (!File.Exists(descriptor.ModelPath) ||
                    !File.Exists(descriptor.ManifestPath) ||
                    !File.Exists(descriptor.AtlasPath))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsOwnedModelPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return Descriptors.Any(descriptor =>
                string.Equals(
                    descriptor.ModelPath,
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDetailAtlasPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return Descriptors.Any(descriptor =>
                string.Equals(
                    descriptor.AtlasPath,
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsOwnedSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // The shared Hero/NPC V2 Avatar and Player3DLit material are
            // dependencies, not cafe-owned sources. Treating them as rebuild
            // triggers makes this setup and the Hero/pedestrian pipelines
            // repeatedly force-import one another. Interactive editor startup
            // still validates those dependencies through
            // ValidateDependencyStamp.
            if (string.Equals(
                    path,
                    AnimationPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    path,
                    AnimationManifestPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Descriptors.Any(descriptor =>
                string.Equals(
                    descriptor.ModelPath,
                    path,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    descriptor.ManifestPath,
                    path,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    descriptor.AtlasPath,
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryGetClipLoopFlag(
            string clipName,
            out bool loop)
        {
            for (int descriptorIndex = 0;
                 descriptorIndex < Descriptors.Length;
                 descriptorIndex++)
            {
                CafeClipDescriptor[] clips =
                    Descriptors[descriptorIndex].Clips;
                for (int clipIndex = 0;
                     clipIndex < clips.Length;
                     clipIndex++)
                {
                    if (string.Equals(
                            clips[clipIndex].Name,
                            clipName,
                            StringComparison.Ordinal))
                    {
                        loop = clips[clipIndex].Loop;
                        return true;
                    }
                }
            }

            loop = false;
            return false;
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
                    "Mountain Road cafe cast build requires all four staged " +
                    "FBX/manifest/atlas sets, its isolated animation " +
                    "FBX/manifest, the production Hero/NPC V2 model and " +
                    "Player3DLit material.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(ProviderPath);
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    EnsureFolderForAsset(Descriptors[index].PrefabPath);
                }

                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    CafeCastDescriptor descriptor = Descriptors[index];
                    AssetDatabase.ImportAsset(
                        descriptor.ModelPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(
                        descriptor.ManifestPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(
                        descriptor.AtlasPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                }

                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CafeCastAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                for (int index = 0; index < Descriptors.Length; index++)
                {
                    BuildDescriptor(
                        Descriptors[index],
                        animationManifest,
                        sharedMaterial);
                }

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
            CafeCastAnimationManifest animationManifest =
                LoadAndValidateAnimationManifest();
            for (int index = 0; index < Descriptors.Length; index++)
            {
                ValidateDescriptor(Descriptors[index], animationManifest);
            }

            ValidateProvider();
        }

        private static void BuildDescriptor(
            CafeCastDescriptor descriptor,
            CafeCastAnimationManifest animationManifest,
            Material sharedMaterial)
        {
            CafeCastModelManifest manifest =
                LoadAndValidateModelManifest(descriptor);
            Texture2D detailAtlas = LoadAndValidateAtlas(descriptor);
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import a cafe cast model from " +
                    $"'{descriptor.ModelPath}'.");
            }

            var clipBindings = new MountainRoadCafeCastClipBinding[
                descriptor.Clips.Length];
            for (int index = 0; index < descriptor.Clips.Length; index++)
            {
                CafeClipDescriptor clip = descriptor.Clips[index];
                clipBindings[index] = new MountainRoadCafeCastClipBinding(
                    clip.Kind,
                    LoadAnimationClip(
                        clip.Name,
                        clip.Duration,
                        clip.Loop,
                        animationManifest),
                    clip.Loop);
            }

            BuildPrefab(
                descriptor,
                modelAsset,
                sharedMaterial,
                clipBindings,
                manifest,
                detailAtlas);
        }

        private static void BuildPrefab(
            CafeCastDescriptor descriptor,
            GameObject modelAsset,
            Material sharedMaterial,
            MountainRoadCafeCastClipBinding[] clipBindings,
            CafeCastModelManifest manifest,
            Texture2D detailAtlas)
        {
            GameObject prefabRoot =
                new GameObject(descriptor.PrefabRootName);
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate imported cafe cast model " +
                        $"'{descriptor.ModelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName =
                    IndexUniqueRenderers(model, descriptor.DisplayName);
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"Imported {descriptor.DisplayName} renderer count " +
                        "differs from its manifest.");
                }

                var bindings =
                    new List<MountainRoadCafeCastRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    CafeCastManifestPart part = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            part.name,
                            out Renderer renderer) ||
                        renderer == null)
                    {
                        throw new InvalidOperationException(
                            $"Imported {descriptor.DisplayName} is missing " +
                            $"renderer '{part.name}'.");
                    }

                    renderer.sharedMaterials = new[] { sharedMaterial };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = false;
                    }

                    bindings.Add(
                        new MountainRoadCafeCastRendererBinding(
                            renderer,
                            ParseColor(part.base_color),
                            !string.IsNullOrEmpty(part.atlas_region)));
                }

                ValidateUvs(
                    manifest.texture_bindings[0],
                    renderersByName);

                if (descriptor.Role ==
                    MountainRoadCafeCastRole.Attendant)
                {
                    BuildCoffeePotSpoutAnchor(
                        model,
                        renderersByName,
                        descriptor.DisplayName);
                }

                Animator[] animators =
                    model.GetComponentsInChildren<Animator>(true);
                Animator animator;
                if (animators.Length == 0)
                {
                    animator = model.AddComponent<Animator>();
                }
                else if (animators.Length == 1)
                {
                    animator = animators[0];
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Imported {descriptor.DisplayName} contains more " +
                        "than one Animator.");
                }

                Avatar playerAvatar = FindPlayerAvatar();
                if (animator.avatar == null)
                {
                    animator.avatar = playerAvatar;
                }

                if (animator.avatar == null ||
                    !animator.avatar.isValid ||
                    animator.avatar != playerAvatar)
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} has no compatible " +
                        "production Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode =
                    AnimatorCullingMode.CullUpdateTransforms;

                MountainRoadCafeCastAssetRegistry registry =
                    prefabRoot.AddComponent<
                        MountainRoadCafeCastAssetRegistry>();
                registry.Configure(
                    animator,
                    descriptor.Role,
                    descriptor.DefaultClipKind,
                    clipBindings,
                    model.transform,
                    bindings.ToArray(),
                    detailAtlas);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    descriptor.PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save cafe cast prefab at " +
                        $"'{descriptor.PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static void BindProvider()
        {
            MountainRoadCafeCastProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    MountainRoadCafeCastProvider>(ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject.CreateInstance<
                    MountainRoadCafeCastProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            provider.Configure(
                LoadPrefab(Descriptors[0]),
                LoadPrefab(Descriptors[1]),
                LoadPrefab(Descriptors[2]),
                LoadPrefab(Descriptors[3]));
            EditorUtility.SetDirty(provider);
        }

        private static GameObject LoadPrefab(
            CafeCastDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Cafe cast prefab is missing at " +
                    $"'{descriptor.PrefabPath}'.");
            }

            return prefab;
        }

        private static void ValidateDescriptor(
            CafeCastDescriptor descriptor,
            CafeCastAnimationManifest animationManifest)
        {
            CafeCastModelManifest manifest =
                LoadAndValidateModelManifest(descriptor);
            Texture2D detailAtlas = LoadAndValidateAtlas(descriptor);
            ValidateImportedModel(descriptor, manifest);

            GameObject prefab = LoadPrefab(descriptor);
            MountainRoadCafeCastAssetRegistry registry =
                prefab.GetComponent<MountainRoadCafeCastAssetRegistry>();
            if (registry == null || registry.gameObject != prefab)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} prefab must carry its cafe " +
                    "cast registry on the root.");
            }

            Avatar playerAvatar = FindPlayerAvatar();
            Animator[] animators =
                prefab.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 ||
                registry.Animator != animators[0] ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                registry.Animator.avatar != playerAvatar ||
                registry.Animator.cullingMode !=
                    AnimatorCullingMode.CullUpdateTransforms)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} Animator must be the sole, " +
                    "controller-free Hero/NPC V2 Generic Animator with root " +
                    "motion disabled.");
            }

            if (registry.ModelRoot == null ||
                !registry.ModelRoot.IsChildOf(prefab.transform) ||
                registry.ModelRoot.parent != prefab.transform ||
                registry.ModelRoot.localPosition != Vector3.zero ||
                Quaternion.Angle(
                    registry.ModelRoot.localRotation,
                    Quaternion.Euler(0f, 180f, 0f)) >
                    TransformAngleTolerance ||
                registry.ModelRoot.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} model root lost its staged " +
                    "prefab transform contract.");
            }

            if (registry.Role != descriptor.Role ||
                registry.DefaultClipKind != descriptor.DefaultClipKind ||
                registry.ClipBindings.Count != descriptor.Clips.Length ||
                registry.DetailAtlas != detailAtlas)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} registry role/default/clip " +
                    "count or detail atlas differs from its authored " +
                    "contract.");
            }

            for (int index = 0; index < descriptor.Clips.Length; index++)
            {
                CafeClipDescriptor expected = descriptor.Clips[index];
                MountainRoadCafeCastClipBinding binding =
                    registry.ClipBindings[index];
                if (binding == null ||
                    binding.Kind != expected.Kind ||
                    binding.Loop != expected.Loop)
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} clip binding {index} " +
                        "differs from its authored kind/loop contract.");
                }

                ValidateAnimationClip(
                    binding.Clip,
                    expected.Name,
                    expected.Duration,
                    expected.Loop,
                    animationManifest);
            }
            ValidateRendererBindings(
                descriptor,
                prefab,
                registry,
                manifest);
            ValidateCoffeePotSpoutAnchor(descriptor, prefab);
            ValidatePassivePrefab(descriptor, prefab, registry);
        }

        private static void ValidateRendererBindings(
            CafeCastDescriptor descriptor,
            GameObject prefab,
            MountainRoadCafeCastAssetRegistry registry,
            CafeCastModelManifest manifest)
        {
            Renderer[] renderers =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} prefab renderer bindings " +
                    "differ from its deterministic manifest.");
            }

            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            var boundRenderers = new HashSet<Renderer>();
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CafeCastManifestPart part = manifest.parts[index];
                MountainRoadCafeCastRendererBinding binding =
                    registry.RendererBindings[index];
                Color expectedColor = ParseColor(part.base_color);
                if (binding == null ||
                    binding.Renderer == null ||
                    !boundRenderers.Add(binding.Renderer) ||
                    !string.Equals(
                        binding.Renderer.name,
                        part.name,
                        StringComparison.Ordinal) ||
                    !Approximately(binding.Color, expectedColor) ||
                    binding.UsesDetailAtlas !=
                        !string.IsNullOrEmpty(part.atlas_region) ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} renderer binding " +
                        $"{index} no longer matches manifest part " +
                        $"'{part.name}' or the shared Player3DLit material.");
                }
            }

            ValidateUvs(
                manifest.texture_bindings[0],
                IndexUniqueRenderers(prefab, descriptor.DisplayName));

            if (CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} prefab triangle count " +
                    "differs from its deterministic manifest.");
            }
        }

        private static void ValidatePassivePrefab(
            CafeCastDescriptor descriptor,
            GameObject prefab,
            MountainRoadCafeCastAssetRegistry registry)
        {
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} staged prefab must remain " +
                    "passive: no physics, light, audio or camera component.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length != 1 || behaviours[0] != registry)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} staged prefab may carry " +
                    "only its passive cafe cast asset registry.");
            }
        }

        private static void ValidateProvider()
        {
            MountainRoadCafeCastProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    MountainRoadCafeCastProvider>(ProviderPath);
            if (provider == null || !provider.HasCompleteCast)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe cast provider is missing or " +
                    "incomplete.");
            }

            GameObject[] expected =
            {
                LoadPrefab(Descriptors[0]),
                LoadPrefab(Descriptors[1]),
                LoadPrefab(Descriptors[2]),
                LoadPrefab(Descriptors[3])
            };
            GameObject[] actual =
            {
                provider.LonePatronPrefab,
                provider.PairManPrefab,
                provider.PairWomanPrefab,
                provider.AttendantPrefab
            };
            for (int index = 0; index < expected.Length; index++)
            {
                if (actual[index] != expected[index] ||
                    provider.GetPrefab(Descriptors[index].Role) !=
                        expected[index] ||
                    AssetDatabase.GetAssetPath(actual[index])
                        .IndexOf(
                            "/Resources/",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        "Mountain Road cafe cast provider has a stale, " +
                        "misordered or Resources-resident prefab binding.");
                }
            }

            if (actual.Distinct().Count() != Descriptors.Length)
            {
                throw new InvalidOperationException(
                    "Every Mountain Road cafe role requires its own staged " +
                    "prefab.");
            }
        }

        private static CafeCastModelManifest
            LoadAndValidateModelManifest(CafeCastDescriptor descriptor)
        {
            if (!descriptor.ModelPath.StartsWith(
                    "Assets/Pedestrians/Staged/Models/",
                    StringComparison.OrdinalIgnoreCase) ||
                !descriptor.PrefabPath.StartsWith(
                    "Assets/Pedestrians/Staged/Prefabs/",
                    StringComparison.OrdinalIgnoreCase) ||
                !descriptor.AtlasPath.StartsWith(
                    "Assets/Pedestrians/Textures/",
                    StringComparison.OrdinalIgnoreCase) ||
                descriptor.PrefabPath.IndexOf(
                    "/Resources/",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                CityPedestrianResources.TryGetArchetype(
                    descriptor.DesignId,
                    out _))
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} must remain staged outside " +
                    "Resources and absent from the ambient pedestrian " +
                    "catalogue.");
            }

            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    descriptor.ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import cafe cast manifest " +
                    $"'{descriptor.ManifestPath}'.");
            }

            CafeCastModelManifest manifest =
                JsonUtility.FromJson<CafeCastModelManifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.shared_clips == null ||
                manifest.triangle_budget == null ||
                manifest.triangle_budget.Length != 2 ||
                manifest.animations == null)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    descriptor.DesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.pose,
                    ExpectedPose,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.forward_axis,
                    "-Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.anatomical_left_axis,
                    "+X",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} design, pose or axis " +
                    "contract differs from the approved source.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.bones.Length != ExpectedBoneCount ||
                manifest.triangle_count < descriptor.MinimumTriangleCount ||
                manifest.triangle_count > descriptor.MaximumTriangleCount ||
                manifest.triangle_budget[0] !=
                    descriptor.MinimumTriangleCount ||
                manifest.triangle_budget[1] !=
                    descriptor.MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} height, skeleton, mesh or " +
                    "triangle budget contract is invalid.");
            }

            if (!manifest.staged ||
                manifest.pool_eligible ||
                manifest.emissive ||
                manifest.colliders ||
                manifest.animation_count != 0 ||
                manifest.animations.Length != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.shared_animation_source,
                    AnimationPath,
                    StringComparison.Ordinal) ||
                !manifest.shared_clips.SequenceEqual(
                    descriptor.Clips.Select(clip => clip.Name),
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} must be a non-emissive, " +
                    "collider-free, animation-free staged model using " +
                    "Player3DLit and exactly its isolated cafe clip set.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} manifest lacks " +
                    "deterministic source metadata.");
            }

            ValidateManifestHierarchy(descriptor, manifest);
            ValidateManifestTexture(descriptor, manifest);
            ValidateManifestRigAnchors(descriptor, manifest);
            return manifest;
        }

        private static void ValidateManifestRigAnchors(
            CafeCastDescriptor descriptor,
            CafeCastModelManifest manifest)
        {
            CafeCastManifestRigAnchor[] anchors = manifest.rig_anchors ??
                Array.Empty<CafeCastManifestRigAnchor>();
            int expectedCount = descriptor.Role ==
                                MountainRoadCafeCastRole.Attendant
                ? 1
                : 0;
            if (anchors.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} manifest has the wrong " +
                    "coffee-pot anchor count.");
            }

            if (expectedCount == 0)
            {
                return;
            }

            CafeCastManifestRigAnchor anchor = anchors[0];
            if (anchor == null ||
                !string.Equals(
                    anchor.name,
                    CoffeePotSpoutAnchorName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchor.bone,
                    "hand.R",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchor.kind,
                    "anchor",
                    StringComparison.Ordinal) ||
                anchor.parts == null ||
                !anchor.parts.SequenceEqual(
                    new[] { "ACC_CoffeePotSpout" },
                    StringComparer.Ordinal) ||
                !string.Equals(
                    anchor.axis_from,
                    "ACC_CoffeePotBody",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cafe attendant manifest must declare the measured " +
                    "coffee-pot spout anchor on hand.R.");
            }
        }

        private static void ValidateManifestHierarchy(
            CafeCastDescriptor descriptor,
            CafeCastModelManifest manifest)
        {
            var boneNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CafeCastManifestBone bone = manifest.bones[index];
                if (bone == null ||
                    string.IsNullOrEmpty(bone.name) ||
                    !boneNames.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} manifest contains a " +
                        "missing or duplicate bone.");
                }
            }

            var partNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CafeCastManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrEmpty(part.name) ||
                    string.IsNullOrEmpty(part.role) ||
                    string.IsNullOrEmpty(part.palette_name) ||
                    part.base_color == null ||
                    part.base_color.Length != 4 ||
                    !part.base_color.All(component =>
                        component >= 0f && component <= 1f) ||
                    !partNames.Add(part.name) ||
                    !boneNames.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} manifest contains an " +
                        "invalid renderer binding.");
                }
            }
        }

        private static void ValidateManifestTexture(
            CafeCastDescriptor descriptor,
            CafeCastModelManifest manifest)
        {
            CafeCastTextureBinding[] bindings =
                manifest.texture_bindings ??
                Array.Empty<CafeCastTextureBinding>();
            if (bindings.Length != 1 || bindings[0] == null)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} must declare exactly one " +
                    "detail atlas binding.");
            }

            CafeCastTextureBinding binding = bindings[0];
            if (!string.Equals(
                    binding.texture_asset,
                    descriptor.AtlasPath,
                    StringComparison.Ordinal) ||
                binding.width_px != ExpectedAtlasSize ||
                binding.height_px != ExpectedAtlasSize ||
                binding.materials == null ||
                binding.materials.Length != 0 ||
                !string.Equals(
                    binding.shader_property,
                    "_BaseMap",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.color_space,
                    "sRGB",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.filter_mode,
                    "Point",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.wrap_mode,
                    "Clamp",
                    StringComparison.Ordinal) ||
                binding.mipmaps ||
                !string.Equals(
                    binding.compression,
                    "Uncompressed",
                    StringComparison.Ordinal) ||
                binding.uv_channel != 0 ||
                !string.Equals(
                    binding.uv_origin,
                    "bottom_left",
                    StringComparison.Ordinal) ||
                binding.uv_safe_inset_px != ExpectedAtlasInset ||
                !string.Equals(
                    binding.material_tint_hex,
                    "FFFFFF",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.tint_source,
                    "renderer_palette",
                    StringComparison.Ordinal) ||
                binding.sha256 == null ||
                binding.sha256.Length != 64 ||
                !string.Equals(
                    binding.sha256,
                    FileSha(descriptor.AtlasPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} detail atlas manifest " +
                    "contract is invalid.");
            }

            CafeCastTextureRegion[] regions = binding.regions ??
                Array.Empty<CafeCastTextureRegion>();
            if (regions.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} detail atlas has no " +
                    "renderer regions.");
            }

            var partNames = new HashSet<string>(
                manifest.parts.Select(part => part.name),
                StringComparer.Ordinal);
            var regionNames = new HashSet<string>(StringComparer.Ordinal);
            var regionsByRenderer = new Dictionary<string, string>(
                StringComparer.Ordinal);
            for (int index = 0; index < regions.Length; index++)
            {
                CafeCastTextureRegion region = regions[index];
                if (region == null ||
                    string.IsNullOrEmpty(region.name) ||
                    string.IsNullOrEmpty(region.renderer) ||
                    !regionNames.Add(region.name) ||
                    !regionsByRenderer.TryAdd(
                        region.renderer,
                        region.name) ||
                    !partNames.Contains(region.renderer) ||
                    region.width_px <= ExpectedAtlasInset * 2 ||
                    region.height_px <= ExpectedAtlasInset * 2 ||
                    region.x_px < 0 ||
                    region.y_px < 0 ||
                    region.x_px + region.width_px > ExpectedAtlasSize ||
                    region.y_px + region.height_px > ExpectedAtlasSize ||
                    RectsOverlap(
                        region.x_px,
                        region.y_px,
                        region.width_px,
                        region.height_px,
                        0,
                        0,
                        64,
                        64))
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} has an invalid detail " +
                        $"atlas region at index {index}.");
                }

                for (int otherIndex = 0;
                     otherIndex < index;
                     otherIndex++)
                {
                    CafeCastTextureRegion other = regions[otherIndex];
                    if (RectsOverlap(
                            region.x_px,
                            region.y_px,
                            region.width_px,
                            region.height_px,
                            other.x_px,
                            other.y_px,
                            other.width_px,
                            other.height_px))
                    {
                        throw new InvalidOperationException(
                            $"{descriptor.DisplayName} detail atlas " +
                            $"regions '{region.name}' and '{other.name}' " +
                            "overlap.");
                    }
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CafeCastManifestPart part = manifest.parts[index];
                regionsByRenderer.TryGetValue(
                    part.name,
                    out string expectedRegion);
                if (!string.Equals(
                        part.atlas_region ?? string.Empty,
                        expectedRegion ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} renderer " +
                        $"'{part.name}' has a stale atlas region mapping.");
                }
            }
        }

        private static CafeCastAnimationManifest
            LoadAndValidateAnimationManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    AnimationManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import cafe cast animation manifest " +
                    $"'{AnimationManifestPath}'.");
            }

            CafeCastAnimationManifest manifest = JsonUtility.FromJson<
                CafeCastAnimationManifest>(source.text);
            if (manifest == null ||
                manifest.clips == null ||
                manifest.fps != ExpectedAnimationFps ||
                manifest.bone_count != ExpectedBoneCount ||
                manifest.mesh_count != 0 ||
                manifest.clip_count != ExpectedClipCount ||
                manifest.clips.Length != ExpectedClipCount ||
                manifest.root_motion ||
                string.IsNullOrWhiteSpace(manifest.skeleton_source) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Mountain Road cafe animation manifest has an invalid " +
                    "skeleton, clip count, root-motion or source contract.");
            }

            var expected = new Dictionary<string, ClipExpectation>(
                StringComparer.Ordinal);
            for (int index = 0; index < Descriptors.Length; index++)
            {
                CafeCastDescriptor descriptor = Descriptors[index];
                for (int clipIndex = 0;
                     clipIndex < descriptor.Clips.Length;
                     clipIndex++)
                {
                    CafeClipDescriptor clip =
                        descriptor.Clips[clipIndex];
                    expected.Add(
                        clip.Name,
                        new ClipExpectation(
                            descriptor.DesignId,
                            clip.Duration,
                            clip.Loop));
                }
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.clips.Length; index++)
            {
                CafeCastAnimationClipManifest clip = manifest.clips[index];
                if (clip == null ||
                    string.IsNullOrWhiteSpace(clip.name) ||
                    !names.Add(clip.name) ||
                    !expected.TryGetValue(
                        clip.name,
                        out ClipExpectation expectation) ||
                    !string.Equals(
                        clip.archetype,
                        expectation.DesignId,
                        StringComparison.Ordinal) ||
                    Mathf.Abs(
                        clip.duration_seconds - expectation.Duration) >
                        ClipDurationTolerance ||
                    clip.frame_start != 0 ||
                    clip.frame_end != Mathf.RoundToInt(
                        expectation.Duration * ExpectedAnimationFps) ||
                    clip.loop != expectation.Loop ||
                    clip.one_shot == expectation.Loop ||
                    !clip.in_place ||
                    clip.keyed_bone_count != ExpectedBoneCount ||
                    string.IsNullOrWhiteSpace(clip.authored_posture) ||
                    string.IsNullOrWhiteSpace(clip.gait) ||
                    Mathf.Abs(clip.loop_max_error) > 0.0001f ||
                    clip.root_translation_range_m == null ||
                    clip.root_translation_range_m.Length != 3 ||
                    clip.root_translation_range_m.Any(component =>
                        Mathf.Abs(component) > 0.0001f))
                {
                    throw new InvalidOperationException(
                        $"Cafe animation manifest clip {index} violates " +
                        "the approved per-clip loop, in-place Generic " +
                        "contract.");
                }
            }

            if (names.Count != expected.Count ||
                expected.Keys.Any(name => !names.Contains(name)))
            {
                throw new InvalidOperationException(
                    "Cafe animation manifest must contain exactly the " +
                    "approved nine cafe tableau clips.");
            }

            ValidateImportedAnimationAsset(manifest);
            return manifest;
        }

        private static void ValidateImportedAnimationAsset(
            CafeCastAnimationManifest manifest)
        {
            Avatar playerAvatar = FindPlayerAvatar();
            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            if (playerAvatar == null ||
                importer == null ||
                !importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != playerAvatar ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "Cafe animation FBX must import as Generic, copy the " +
                    "production Hero/NPC V2 Avatar and import no materials.");
            }

            AnimationClip[] clips = GetImportedAnimationClips();
            if (clips.Length != ExpectedClipCount)
            {
                throw new InvalidOperationException(
                    $"Unity imported {clips.Length} cafe clips; expected " +
                    $"{ExpectedClipCount}.");
            }

            for (int index = 0; index < manifest.clips.Length; index++)
            {
                CafeCastAnimationClipManifest expected = manifest.clips[index];
                AnimationClip clip = clips.FirstOrDefault(candidate =>
                    string.Equals(
                        NormalizeClipName(candidate.name),
                        expected.name,
                        StringComparison.Ordinal));
                ValidateAnimationClip(
                    clip,
                    expected.name,
                    expected.duration_seconds,
                    expected.loop,
                    manifest);
            }

            GameObject animationAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            if (animationAsset == null ||
                animationAsset
                    .GetComponentsInChildren<Renderer>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Cafe animation FBX must remain animation-only.");
            }

            Dictionary<string, Transform> transforms =
                IndexUniqueTransforms(animationAsset, "cafe animation");
            Transform boneRoot =
                RequireTransform(transforms, "root", "cafe animation");
            if (boneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                ExpectedBoneCount)
            {
                throw new InvalidOperationException(
                    "Cafe animation FBX has added or missing Generic bones.");
            }
        }

        private static void ValidateImportedModel(
            CafeCastDescriptor descriptor,
            CafeCastModelManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.ModelPath);
            GameObject playerModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null || playerModel == null)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} or Hero/NPC V2 source model " +
                    "failed to import.");
            }

            Dictionary<string, Transform> modelTransforms =
                IndexUniqueTransforms(model, descriptor.DisplayName);
            Dictionary<string, Transform> playerTransforms =
                IndexUniqueTransforms(playerModel, "Player");
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CafeCastManifestBone source = manifest.bones[index];
                Transform modelBone = RequireTransform(
                    modelTransforms,
                    source.name,
                    descriptor.DisplayName);
                Transform playerBone = RequireTransform(
                    playerTransforms,
                    source.name,
                    "Player");
                string expectedParent = string.IsNullOrEmpty(source.parent)
                    ? "RIG_Player"
                    : source.parent;
                if (modelBone.parent == null ||
                    playerBone.parent == null ||
                    !string.Equals(
                        modelBone.parent.name,
                        expectedParent,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        playerBone.parent.name,
                        expectedParent,
                        StringComparison.Ordinal) ||
                    Vector3.Distance(
                        modelBone.localPosition,
                        playerBone.localPosition) >
                        TransformPositionTolerance ||
                    Quaternion.Angle(
                        modelBone.localRotation,
                        playerBone.localRotation) >
                        TransformAngleTolerance ||
                    Vector3.Distance(
                        modelBone.localScale,
                        playerBone.localScale) >
                        TransformPositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} bone '{source.name}' " +
                        "differs from the production Hero/NPC V2 Generic " +
                        "rig.");
                }
            }

            Transform boneRoot = RequireTransform(
                modelTransforms,
                "root",
                descriptor.DisplayName);
            int expectedRigTransformCount = ExpectedBoneCount +
                (descriptor.Role == MountainRoadCafeCastRole.Attendant
                    ? 1
                    : 0);
            if (boneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                expectedRigTransformCount)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} has added or missing " +
                    "Generic bones or authored rig anchors.");
            }

            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count ||
                CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} imported mesh count or " +
                    "triangle count differs from its manifest.");
            }

            if (AssetDatabase.LoadAllAssetsAtPath(descriptor.ModelPath)
                .OfType<AnimationClip>()
                .Any(clip => !clip.name.StartsWith(
                    "__preview__",
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} model FBX unexpectedly " +
                    "imports animation.");
            }

            Avatar playerAvatar = FindPlayerAvatar();
            ModelImporter importer = AssetImporter.GetAtPath(
                descriptor.ModelPath) as ModelImporter;
            if (playerAvatar == null ||
                !playerAvatar.isValid ||
                importer == null ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != playerAvatar ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} FBX must copy the valid " +
                    "production Generic Avatar and import neither animation " +
                    "nor materials.");
            }
        }

        private static Texture2D LoadAndValidateAtlas(
            CafeCastDescriptor descriptor)
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                descriptor.AtlasPath);
            TextureImporter importer = AssetImporter.GetAtPath(
                descriptor.AtlasPath) as TextureImporter;
            if (atlas == null ||
                atlas.width != ExpectedAtlasSize ||
                atlas.height != ExpectedAtlasSize ||
                atlas.filterMode != FilterMode.Point ||
                atlas.wrapMode != TextureWrapMode.Clamp ||
                atlas.mipmapCount != 1 ||
                importer == null ||
                importer.textureType != TextureImporterType.Default ||
                importer.textureShape != TextureImporterShape.Texture2D ||
                !importer.sRGBTexture ||
                importer.isReadable ||
                importer.npotScale != TextureImporterNPOTScale.None ||
                importer.filterMode != FilterMode.Point ||
                importer.wrapMode != TextureWrapMode.Clamp ||
                importer.anisoLevel != 1 ||
                importer.maxTextureSize != ExpectedAtlasSize ||
                importer.alphaSource !=
                    TextureImporterAlphaSource.FromInput ||
                importer.alphaIsTransparency ||
                importer.mipmapEnabled ||
                importer.streamingMipmaps ||
                importer.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                importer.compressionQuality != 100)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} detail atlas import " +
                    "contract is invalid.");
            }

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden ||
                standalone.maxTextureSize != ExpectedAtlasSize ||
                standalone.format != TextureImporterFormat.Automatic ||
                standalone.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                standalone.compressionQuality != 100 ||
                standalone.crunchedCompression)
            {
                throw new InvalidOperationException(
                    $"{descriptor.DisplayName} detail atlas Standalone " +
                    "import contract is invalid.");
            }

            return atlas;
        }

        private static void ValidateUvs(
            CafeCastTextureBinding binding,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            for (int index = 0; index < binding.regions.Length; index++)
            {
                CafeCastTextureRegion region = binding.regions[index];
                if (!renderers.TryGetValue(
                        region.renderer,
                        out Renderer renderer))
                {
                    throw new InvalidOperationException(
                        $"Missing cafe detail-atlas renderer " +
                        $"'{region.renderer}'.");
                }

                Mesh mesh = RendererMesh(renderer);
                Vector2[] uv = mesh?.uv;
                if (mesh == null ||
                    uv == null ||
                    uv.Length == 0 ||
                    uv.Length != mesh.vertexCount)
                {
                    throw new InvalidOperationException(
                        $"Cafe renderer '{region.renderer}' has no valid " +
                        "detail-atlas UV0.");
                }

                Vector2 minimum = new Vector2(
                    (float)(region.x_px + binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 maximum = new Vector2(
                    (float)(region.x_px + region.width_px -
                            binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + region.height_px -
                            binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 observedMinimum = uv[0];
                Vector2 observedMaximum = uv[0];
                for (int uvIndex = 0; uvIndex < uv.Length; uvIndex++)
                {
                    Vector2 point = uv[uvIndex];
                    if (point.x < minimum.x - UvTolerance ||
                        point.x > maximum.x + UvTolerance ||
                        point.y < minimum.y - UvTolerance ||
                        point.y > maximum.y + UvTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Cafe renderer '{region.renderer}' UV0 leaves " +
                            $"detail-atlas region '{region.name}'.");
                    }

                    observedMinimum = Vector2.Min(
                        observedMinimum,
                        point);
                    observedMaximum = Vector2.Max(
                        observedMaximum,
                        point);
                }

                if (observedMaximum.x - observedMinimum.x <= UvTolerance ||
                    observedMaximum.y - observedMinimum.y <= UvTolerance)
                {
                    throw new InvalidOperationException(
                        $"Cafe renderer '{region.renderer}' has degenerate " +
                        "detail-atlas UV0.");
                }
            }
        }

        private static AnimationClip LoadAnimationClip(
            string name,
            float duration,
            bool loop,
            CafeCastAnimationManifest manifest)
        {
            AnimationClip clip = GetImportedAnimationClips()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        NormalizeClipName(candidate.name),
                        name,
                        StringComparison.Ordinal));
            ValidateAnimationClip(clip, name, duration, loop, manifest);
            return clip;
        }

        private static void ValidateAnimationClip(
            AnimationClip clip,
            string expectedName,
            float expectedDuration,
            bool expectedLoop,
            CafeCastAnimationManifest manifest)
        {
            CafeCastAnimationClipManifest source = manifest.clips
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.name,
                        expectedName,
                        StringComparison.Ordinal));
            if (clip == null ||
                source == null ||
                !string.Equals(
                    NormalizeClipName(clip.name),
                    expectedName,
                    StringComparison.Ordinal) ||
                Mathf.Abs(clip.length - expectedDuration) >
                    ClipDurationTolerance)
            {
                throw new InvalidOperationException(
                    $"Cafe cast clip '{expectedName}' is missing, has an " +
                    "unexpected name or duration.");
            }

            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime != expectedLoop ||
                settings.loopBlend != expectedLoop ||
                AnimationUtility.GetAnimationEvents(clip).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Cafe cast clip '{expectedName}' did not import with " +
                    "its authored loop flag or contains Animation Events.");
            }
        }

        private static AnimationClip[] GetImportedAnimationClips()
        {
            return AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith(
                    "__preview__",
                    StringComparison.Ordinal))
                .ToArray();
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                ValidateOrThrow();
            }
            catch (InvalidOperationException)
            {
                QueueBuildWhenSourcesExist();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect Mountain Road cafe cast assets: " +
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
                    "Could not build Mountain Road cafe cast assets: " +
                    exception);
            }
        }

        private static Avatar FindPlayerAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static Dictionary<string, Transform>
            IndexUniqueTransforms(GameObject root, string label)
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
                        $"Imported {label} hierarchy contains duplicate " +
                        $"transform name '{transform.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Renderer>
            IndexUniqueRenderers(GameObject root, string label)
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
                        $"Imported {label} hierarchy contains duplicate " +
                        $"renderer name '{renderer.name}'.");
                }
            }

            return result;
        }

        private static void BuildCoffeePotSpoutAnchor(
            GameObject model,
            IReadOnlyDictionary<string, Renderer> renderers,
            string label)
        {
            Dictionary<string, Transform> transforms =
                IndexUniqueTransforms(model, label);
            Transform hand = RequireTransform(
                transforms,
                "hand.R",
                label);
            if (!renderers.TryGetValue(
                    "ACC_CoffeePotBody",
                    out Renderer body) ||
                !renderers.TryGetValue(
                    "ACC_CoffeePotSpout",
                    out Renderer spout) ||
                body == null ||
                spout == null)
            {
                throw new InvalidOperationException(
                    "Cafe attendant is missing authored coffee-pot parts.");
            }

            Transform authoredAnchor = RequireTransform(
                transforms,
                CoffeePotSpoutAnchorName,
                label);
            if (authoredAnchor.parent != hand)
            {
                throw new InvalidOperationException(
                    "Cafe attendant coffee-pot spout anchor must be " +
                    "exported directly below hand.R.");
            }
        }

        private static void ValidateCoffeePotSpoutAnchor(
            CafeCastDescriptor descriptor,
            GameObject prefab)
        {
            Transform[] matches = prefab
                .GetComponentsInChildren<Transform>(true)
                .Where(transform => string.Equals(
                    transform.name,
                    CoffeePotSpoutAnchorName,
                    StringComparison.Ordinal))
                .ToArray();
            if (descriptor.Role != MountainRoadCafeCastRole.Attendant)
            {
                if (matches.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"{descriptor.DisplayName} must not own a coffee-pot " +
                        "spout anchor.");
                }

                return;
            }

            if (matches.Length != 1 ||
                matches[0].parent == null ||
                !string.Equals(
                    matches[0].parent.name,
                    "hand.R",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cafe attendant requires one measured " +
                    $"'{CoffeePotSpoutAnchorName}' child of hand.R.");
            }
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            string label)
        {
            if (transforms.TryGetValue(name, out Transform result) &&
                result != null)
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Imported {label} hierarchy is missing transform " +
                $"'{name}'.");
        }

        private static Mesh RendererMesh(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }

        private static int CountTriangles(
            IReadOnlyList<Renderer> renderers)
        {
            int count = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no shared mesh.");
                }

                count += (int)(mesh.GetIndexCount(0) / 3);
            }

            return count;
        }

        private static Color ParseColor(float[] components)
        {
            return new Color(
                components[0],
                components[1],
                components[2],
                components[3]);
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) <= ColorTolerance &&
                   Mathf.Abs(left.g - right.g) <= ColorTolerance &&
                   Mathf.Abs(left.b - right.b) <= ColorTolerance &&
                   Mathf.Abs(left.a - right.a) <= ColorTolerance;
        }

        private static string FileSha(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static bool RectsOverlap(
            int leftX,
            int leftY,
            int leftWidth,
            int leftHeight,
            int rightX,
            int rightY,
            int rightWidth,
            int rightHeight)
        {
            return leftX < rightX + rightWidth &&
                   rightX < leftX + leftWidth &&
                   leftY < rightY + rightHeight &&
                   rightY < leftY + leftHeight;
        }

        private static string NormalizeClipName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return sourceName;
            }

            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
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

        private sealed class CafeCastDescriptor
        {
            public CafeCastDescriptor(
                MountainRoadCafeCastRole role,
                string displayName,
                string prefabRootName,
                string designId,
                string modelPath,
                string manifestPath,
                string prefabPath,
                string atlasPath,
                MountainRoadCafeCastClipKind defaultClipKind,
                CafeClipDescriptor[] clips,
                int minimumTriangleCount,
                int maximumTriangleCount)
            {
                Role = role;
                DisplayName = displayName;
                PrefabRootName = prefabRootName;
                DesignId = designId;
                ModelPath = modelPath;
                ManifestPath = manifestPath;
                PrefabPath = prefabPath;
                AtlasPath = atlasPath;
                DefaultClipKind = defaultClipKind;
                Clips = clips ??
                    throw new ArgumentNullException(nameof(clips));
                MinimumTriangleCount = minimumTriangleCount;
                MaximumTriangleCount = maximumTriangleCount;
            }

            public MountainRoadCafeCastRole Role { get; }
            public string DisplayName { get; }
            public string PrefabRootName { get; }
            public string DesignId { get; }
            public string ModelPath { get; }
            public string ManifestPath { get; }
            public string PrefabPath { get; }
            public string AtlasPath { get; }
            public MountainRoadCafeCastClipKind DefaultClipKind { get; }
            public CafeClipDescriptor[] Clips { get; }
            public int MinimumTriangleCount { get; }
            public int MaximumTriangleCount { get; }
        }

        private sealed class CafeClipDescriptor
        {
            public CafeClipDescriptor(
                MountainRoadCafeCastClipKind kind,
                string name,
                float duration,
                bool loop)
            {
                Kind = kind;
                Name = name;
                Duration = duration;
                Loop = loop;
            }

            public MountainRoadCafeCastClipKind Kind { get; }
            public string Name { get; }
            public float Duration { get; }
            public bool Loop { get; }
        }

        private sealed class ClipExpectation
        {
            public ClipExpectation(
                string designId,
                float duration,
                bool loop)
            {
                DesignId = designId;
                Duration = duration;
                Loop = loop;
            }

            public string DesignId { get; }
            public float Duration { get; }
            public bool Loop { get; }
        }

        [Serializable]
        private sealed class CafeCastModelManifest
        {
            public string generator_version;
            public string design_id;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int[] triangle_budget;
            public bool staged;
            public bool pool_eligible;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public int animation_count;
            public string[] animations;
            public string shared_animation_source;
            public string[] shared_clips;
            public string build_signature;
            public CafeCastManifestBone[] bones;
            public CafeCastManifestPart[] parts;
            public CafeCastManifestRigAnchor[] rig_anchors;
            public CafeCastTextureBinding[] texture_bindings;
        }

        [Serializable]
        private sealed class CafeCastManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class CafeCastManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public string atlas_region;
            public float[] base_color;
        }

        [Serializable]
        private sealed class CafeCastTextureBinding
        {
            public string texture_asset;
            public int width_px;
            public int height_px;
            public string[] materials;
            public string shader_property;
            public string color_space;
            public string filter_mode;
            public string wrap_mode;
            public bool mipmaps;
            public string compression;
            public int uv_channel;
            public string uv_origin;
            public int uv_safe_inset_px;
            public string material_tint_hex;
            public string tint_source;
            public string sha256;
            public CafeCastTextureRegion[] regions;
        }

        [Serializable]
        private sealed class CafeCastTextureRegion
        {
            public string name;
            public string renderer;
            public int x_px;
            public int y_px;
            public int width_px;
            public int height_px;
        }

        [Serializable]
        private sealed class CafeCastManifestRigAnchor
        {
            public string name;
            public string bone;
            public string kind;
            public string[] parts;
            public string axis_from;
        }

        [Serializable]
        private sealed class CafeCastAnimationManifest
        {
            public string generator_version;
            public string skeleton_source;
            public int bone_count;
            public int fps;
            public bool root_motion;
            public int mesh_count;
            public int clip_count;
            public CafeCastAnimationClipManifest[] clips;
            public string build_signature;
        }

        [Serializable]
        private sealed class CafeCastAnimationClipManifest
        {
            public string name;
            public string archetype;
            public float duration_seconds;
            public int frame_start;
            public int frame_end;
            public bool loop;
            public bool one_shot;
            public bool in_place;
            public string authored_posture;
            public string gait;
            public int keyed_bone_count;
            public float loop_max_error;
            public float[] root_translation_range_m;
        }
    }

    /// <summary>
    /// Imports only the dedicated cafe cast sources. It deliberately does
    /// not extend the ordinary pedestrian model path list or animation
    /// manifest, keeping the tableau structurally outside that population.
    /// </summary>
    public sealed class MountainRoadCafeCastModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            bool isModel =
                MountainRoadCafeCastAssetSetup.IsOwnedModelPath(assetPath);
            bool isAnimation = string.Equals(
                assetPath,
                MountainRoadCafeCastAssetSetup.AnimationPath,
                StringComparison.OrdinalIgnoreCase);
            if (!isModel && !isAnimation)
            {
                return;
            }

            Avatar playerAvatar = FindPlayerAvatar();
            if (playerAvatar != null)
            {
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = playerAvatar;
            }
            else
            {
                // A clean Library may discover the cafe cast before the
                // Hero V2 FBX. The queued setup imports Hero V2 first, then
                // reimports this source onto that canonical Generic Avatar.
                importer.avatarSetup =
                    ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = isAnimation;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
            if (isAnimation)
            {
                ConfigureAnimationClips(importer);
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    MountainRoadCafeCastAssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ConfigureAnimationClips(importer);
        }

        private static void ConfigureAnimationClips(
            ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips =
                importer.defaultClipAnimations;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < clips.Length; index++)
            {
                ModelImporterClipAnimation clip = clips[index];
                clip.name = NormalizeClipName(clip.name);
                if (!MountainRoadCafeCastAssetSetup.TryGetClipLoopFlag(
                        clip.name,
                        out bool loop))
                {
                    throw new InvalidOperationException(
                        "Cafe animation FBX contains undeclared clip " +
                        $"'{clip.name}'.");
                }

                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                if (!names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        "Cafe animation FBX contains duplicate clip " +
                        $"'{clip.name}' after name normalization.");
                }
            }

            importer.clipAnimations = clips;
        }

        private static Avatar FindPlayerAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(
                    MountainRoadCafeCastAssetSetup.PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static string NormalizeClipName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return sourceName;
            }

            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (NpcHumanV2AssetSetup.IsAnyPipelineBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (!MountainRoadCafeCastAssetSetup.IsOwnedSourcePath(
                        importedAssets[index]))
                {
                    continue;
                }

                MountainRoadCafeCastAssetSetup
                    .QueueBuildWhenSourcesExist();
                return;
            }
        }
    }

    /// <summary>
    /// Keeps the four cafe detail sheets on the shared Hero V2 atlas import
    /// contract without broadening the ambient pedestrian texture pipeline.
    /// </summary>
    public sealed class MountainRoadCafeCastTextureImporter :
        AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetImporter is TextureImporter importer &&
                MountainRoadCafeCastAssetSetup.IsDetailAtlasPath(assetPath))
            {
                Player3DV2TextureImporter.ConfigureAtlas(importer);
            }
        }
    }
}
