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
    public static class CityPedestrianAssetSetup
    {
        public const string ModelPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.fbx";
        public const string ManifestPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        public const string PlayerAnimationPath =
            "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Pedestrians/CityPedestrian3D.prefab";

        private const string ExpectedDesignId = "lampshade_walker_v1";
        private const string ExpectedPose = "apose";
        private const float ExpectedHeight = 1.75f;
        private const int MinimumTriangleCount = 800;
        private const int MaximumTriangleCount = 1200;
        private const float TransformPositionTolerance = 0.0001f;
        private const float TransformAngleTolerance = 0.02f;

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityPedestrianAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"City pedestrian prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "City pedestrian imported model, shared clips and prefab " +
                "contract are valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(PlayerModelPath) &&
                File.Exists(PlayerAnimationPath) &&
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
                    "City pedestrian build requires its FBX/manifest plus " +
                    "the production player model, animation FBX and shared " +
                    "Player3DLit material.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                // Import the Avatar dependency first so a clean Library and
                // later Player-rig changes both rebuild this model against
                // the canonical external Generic Avatar.
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
                AssetDatabase.ImportAsset(
                    PlayerAnimationPath,
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianManifest manifest =
                    LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from '{ModelPath}'.");
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

                AnimationClip idle = LoadSharedClip("Idle");
                AnimationClip walk = LoadSharedClip("Walk");
                BuildPrefab(
                    modelAsset,
                    sharedMaterial,
                    idle,
                    walk,
                    manifest);
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
            CityPedestrianManifest manifest = LoadAndValidateManifest();
            ValidateImportedModel(manifest);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City pedestrian prefab is missing at '{PrefabPath}'.");
            }

            CityPedestrianAssetRegistry registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab has no asset registry.");
            }

            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(registry.Animator.avatar),
                    PlayerModelPath,
                    StringComparison.Ordinal) ||
                registry.Animator.cullingMode !=
                    AnimatorCullingMode.CullUpdateTransforms)
            {
                throw new InvalidOperationException(
                    "City pedestrian Animator must be controller-free, " +
                    "culled and have root motion disabled.");
            }

            if (registry.ModelRoot == null ||
                registry.HeadAnchor == null ||
                registry.LeftFootAnchor == null ||
                registry.RightFootAnchor == null)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab is missing a model/head/foot " +
                    "anchor binding.");
            }

            ValidateSharedClip(registry.IdleClip, "Idle", 4f);
            ValidateSharedClip(registry.WalkClip, "Walk", 1f);
            if (registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "City pedestrian registry renderer counts differ from " +
                    "the deterministic manifest.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.SourceGeneratorVersion,
                    manifest.generator_version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "City pedestrian registry source metadata is stale.");
            }

            if (Mathf.Abs(registry.LocalBounds.size.y - ExpectedHeight) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab bounds lost canonical height or " +
                    "grounding.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Atmospheric pedestrians must contain no colliders or " +
                    "lights.");
            }

            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        "Every pedestrian renderer must reference the one " +
                        "shared Player3DLit material.");
                }
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            CityPedestrianManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate City pedestrian source manifest: " +
                    $"{exception}");
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            CityPedestrianAssetRegistry registry = prefab != null
                ? prefab.GetComponent<CityPedestrianAssetRegistry>()
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
                    $"Could not build City pedestrian prefab: {exception}");
            }
        }

        private static CityPedestrianManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            CityPedestrianManifest manifest =
                JsonUtility.FromJson<CityPedestrianManifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.shared_clips == null)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest is malformed.");
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
                    manifest.forward_axis,
                    "-Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.anatomical_left_axis,
                    "+X",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City pedestrian design/pose/axis contract differs from " +
                    "the approved Lampshade Walker.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.bones.Length != 31 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest height, skeleton, mesh or " +
                    "triangle budget is invalid.");
            }

            if (manifest.emissive ||
                manifest.colliders ||
                manifest.animation_count != 0 ||
                manifest.animations == null ||
                manifest.animations.Length != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.shared_animation_source,
                    PlayerAnimationPath,
                    StringComparison.Ordinal) ||
                !manifest.shared_clips.SequenceEqual(
                    new[] { "Idle", "Walk" }))
            {
                throw new InvalidOperationException(
                    "City pedestrian must be non-emissive, collider-free, " +
                    "animation-free and reuse Player3DLit plus Idle/Walk.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest lacks deterministic source " +
                    "metadata.");
            }

            HashSet<string> partNames =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> boneNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityPedestrianManifestBone bone = manifest.bones[index];
                if (bone == null ||
                    string.IsNullOrEmpty(bone.name) ||
                    !boneNames.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        "City pedestrian manifest contains a missing or " +
                        "duplicate bone.");
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityPedestrianManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrEmpty(part.name) ||
                    string.IsNullOrEmpty(part.role) ||
                    string.IsNullOrEmpty(part.palette_name) ||
                    part.base_color == null ||
                    part.base_color.Length != 4 ||
                    !partNames.Add(part.name) ||
                    !boneNames.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        "City pedestrian manifest contains an invalid part " +
                        "binding.");
                }
            }

            return manifest;
        }

        private static void ValidateImportedModel(
            CityPedestrianManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject playerModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null || playerModel == null)
            {
                throw new InvalidOperationException(
                    "Pedestrian or Player source model failed to import.");
            }

            Dictionary<string, Transform> pedestrianTransforms =
                IndexUniqueTransforms(model, "pedestrian");
            Dictionary<string, Transform> playerTransforms =
                IndexUniqueTransforms(playerModel, "player");
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityPedestrianManifestBone source = manifest.bones[index];
                Transform pedestrian = RequireTransform(
                    pedestrianTransforms,
                    source.name,
                    "pedestrian");
                Transform player = RequireTransform(
                    playerTransforms,
                    source.name,
                    "player");
                string expectedParent = string.IsNullOrEmpty(source.parent)
                    ? "RIG_Player"
                    : source.parent;
                if (pedestrian.parent == null ||
                    !string.Equals(
                        pedestrian.parent.name,
                        expectedParent,
                        StringComparison.Ordinal) ||
                    player.parent == null ||
                    !string.Equals(
                        player.parent.name,
                        expectedParent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' lost the exact Player parent " +
                        $"'{expectedParent}'.");
                }

                if (Vector3.Distance(
                        pedestrian.localPosition,
                        player.localPosition) > TransformPositionTolerance ||
                    Quaternion.Angle(
                        pedestrian.localRotation,
                        player.localRotation) > TransformAngleTolerance ||
                    Vector3.Distance(
                        pedestrian.localScale,
                        player.localScale) > TransformPositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' rest transform differs from " +
                        "PlayerCharacter3D.");
                }
            }

            Transform pedestrianBoneRoot = RequireTransform(
                pedestrianTransforms,
                "root",
                "pedestrian");
            if (pedestrianBoneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                manifest.bones.Length)
            {
                throw new InvalidOperationException(
                    "Pedestrian armature has added or missing Generic bones.");
            }

            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {renderers.Length} pedestrian meshes; " +
                    $"manifest declares {manifest.mesh_count}.");
            }

            int triangles = CountTriangles(renderers);
            if (triangles != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {triangles} pedestrian triangles; " +
                    $"manifest declares {manifest.triangle_count}.");
            }

            UnityEngine.Object[] sourceAssets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            if (sourceAssets.Any(asset =>
                    asset is AnimationClip clip &&
                    !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Pedestrian FBX unexpectedly imported its own animation.");
            }

            Avatar playerAvatar = FindModelAvatar();
            ModelImporter modelImporter =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (playerAvatar == null ||
                !playerAvatar.isValid ||
                modelImporter == null ||
                modelImporter.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                modelImporter.sourceAvatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Pedestrian FBX must copy the valid production Player " +
                    "Generic Avatar.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            AnimationClip idle,
            AnimationClip walk,
            CityPedestrianManifest manifest)
        {
            GameObject prefabRoot = new GameObject("CityPedestrian3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported pedestrian model.");
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
                    IndexUniqueTransforms(model, "pedestrian prefab");
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported pedestrian renderer count differs from " +
                        "the manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<CityPedestrianRendererBinding> bindings =
                    new List<CityPedestrianRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    CityPedestrianManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Imported pedestrian is missing renderer " +
                            $"'{source.name}'.");
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

                    Color baseColor = ParseColor(source.base_color);
                    bindings.Add(
                        new CityPedestrianRendererBinding(
                            source.name,
                            source.role,
                            source.palette_name,
                            renderer,
                            baseColor,
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                1),
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                2),
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                3)));
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

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Pedestrian model has no valid Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode =
                    AnimatorCullingMode.CullUpdateTransforms;

                Transform head = RequireTransform(
                    transformsByName,
                    "head",
                    "pedestrian prefab");
                Transform leftFoot = RequireTransform(
                    transformsByName,
                    "foot.L",
                    "pedestrian prefab");
                Transform rightFoot = RequireTransform(
                    transformsByName,
                    "foot.R",
                    "pedestrian prefab");
                Renderer[] renderers = rendererList.ToArray();
                Bounds localBounds = CalculateLocalBounds(
                    prefabRoot.transform,
                    renderers);
                if (Mathf.Abs(localBounds.size.y - manifest.height_m) >
                        0.035f ||
                    Mathf.Abs(localBounds.min.y) > 0.025f ||
                    CountTriangles(renderers) != manifest.triangle_count)
                {
                    throw new InvalidOperationException(
                        "Imported pedestrian geometry lost source bounds or " +
                        "triangle count.");
                }

                CityPedestrianAssetRegistry registry =
                    prefabRoot.AddComponent<CityPedestrianAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    head,
                    leftFoot,
                    rightFoot,
                    idle,
                    walk,
                    localBounds,
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
                        $"Could not save City pedestrian prefab at " +
                        $"'{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static AnimationClip LoadSharedClip(string clipName)
        {
            AnimationClip clip = AssetDatabase
                .LoadAllAssetsAtPath(PlayerAnimationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate =>
                    !candidate.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        NormalizeClipName(candidate.name),
                        clipName,
                        StringComparison.Ordinal));
            ValidateSharedClip(
                clip,
                clipName,
                string.Equals(clipName, "Idle", StringComparison.Ordinal)
                    ? 4f
                    : 1f);
            return clip;
        }

        private static void ValidateSharedClip(
            AnimationClip clip,
            string expectedName,
            float expectedDuration)
        {
            if (clip == null ||
                !string.Equals(
                    NormalizeClipName(clip.name),
                    expectedName,
                    StringComparison.Ordinal) ||
                !clip.isLooping ||
                Mathf.Abs(clip.length - expectedDuration) > 1f / 24f ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(clip),
                    PlayerAnimationPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pedestrian '{expectedName}' must directly reference " +
                    $"the looping Player animation clip at " +
                    $"'{PlayerAnimationPath}'.");
            }
        }

        private static Color BuildPaletteVariant(
            string paletteName,
            Color baseColor,
            int variant)
        {
            if (string.Equals(paletteName, "void", StringComparison.Ordinal) ||
                string.Equals(paletteName, "amber", StringComparison.Ordinal) ||
                string.Equals(paletteName, "sole", StringComparison.Ordinal))
            {
                return baseColor;
            }

            Vector3 multiplier;
            if (variant == 1)
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(0.74f, 0.86f, 1.08f)
                    : new Vector3(0.84f, 0.92f, 1.03f);
            }
            else if (variant == 2)
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(1.12f, 0.84f, 0.72f)
                    : new Vector3(1.05f, 0.88f, 0.78f);
            }
            else
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(0.92f, 1.02f, 0.76f)
                    : new Vector3(0.94f, 1.00f, 0.86f);
            }

            return new Color(
                Mathf.Clamp01(baseColor.r * multiplier.x),
                Mathf.Clamp01(baseColor.g * multiplier.y),
                Mathf.Clamp01(baseColor.b * multiplier.z),
                baseColor.a);
        }

        private static Color ParseColor(float[] components)
        {
            return new Color(
                components[0],
                components[1],
                components[2],
                components[3]);
        }

        private static Avatar FindModelAvatar()
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            GameObject root,
            string label)
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
                        $"Imported {label} hierarchy contains duplicate " +
                        $"transform name '{transform.name}'.");
                }
            }

            return result;
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
                        "Imported pedestrian hierarchy contains duplicate " +
                        $"renderer name '{renderer.name}'.");
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
                    root.worldToLocalMatrix * renderer.localToWorldMatrix;
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
                    "City pedestrian model contains no renderers.");
            }

            return result;
        }

        private static int CountTriangles(
            IReadOnlyList<Renderer> renderers)
        {
            int triangleCount = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Mesh mesh = GetRendererMesh(renderers[index]);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderers[index].name}' has no mesh.");
                }

                for (int subMesh = 0;
                     subMesh < mesh.subMeshCount;
                     subMesh++)
                {
                    triangleCount +=
                        (int)(mesh.GetIndexCount(subMesh) / 3);
                }
            }

            return triangleCount;
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

        private static string NormalizeClipName(string sourceName)
        {
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

        [Serializable]
        private sealed class CityPedestrianManifest
        {
            public string generator_version;
            public string design_id;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public int animation_count;
            public string[] animations;
            public string shared_animation_source;
            public string[] shared_clips;
            public string build_signature;
            public CityPedestrianManifestBone[] bones;
            public CityPedestrianManifestPart[] parts;
        }

        [Serializable]
        private sealed class CityPedestrianManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class CityPedestrianManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;
        }
    }
}
